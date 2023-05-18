using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using CollaborationBot.Commands;
using Fergun.Interactive;

namespace CollaborationBot {
    public class Program {
        private static IConfigurationRoot config;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private DiscordSocketClient _client;
        private InteractionService _interactionService;
        private InteractionHandlerService _interactionHandler;
        private AppSettings _appSettings;
        private FileHandlingService _fileHandler;
        private OsuCollabContext _context;

        private readonly List<SocketGuild> _guildList = new();
        private readonly Timer _checkupTimer = new();

        public static Task Main(string[] args) {
            config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables().Build();
            LogManager.Configuration = new NLogLoggingConfiguration(config.GetSection("NLog"));

            logger.Info("Starting program");

            return new Program().MainAsync().ContinueWith(task => {
                if (task.IsFaulted) {
                    //NLog: catch setup errors
                    logger.Error(task.Exception, "Stopped program because of exception");
                }

                // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
                LogManager.Shutdown();
            });
        }

        public async Task MainAsync() {
            _appSettings = GetAppSettings();

            var discordSocketConfig = new DiscordSocketConfig {
                GatewayIntents = GatewayIntents.DirectMessages | GatewayIntents.GuildMessages | GatewayIntents.Guilds,
                DefaultRetryMode = RetryMode.AlwaysRetry,
                LogLevel = LogSeverity.Debug,
            };
            _client = new DiscordSocketClient(discordSocketConfig);
            _client.Log += Log;
            _client.GuildAvailable += GuildAvailable;
            _client.Connected += Connected;
            _client.Ready += Ready;
            _client.Disconnected += Suicide;

            var interactionServiceConfig = new InteractionServiceConfig() {
                DefaultRunMode = RunMode.Sync,
                UseCompiledLambda = true,
                EnableAutocompleteHandlers = true,
                LogLevel = LogSeverity.Debug,
            };
            _interactionService = new InteractionService(_client, interactionServiceConfig);

            await using var services = ConfigureServices();

            _context = services.GetRequiredService<OsuCollabContext>();

            _interactionHandler = services.GetRequiredService<InteractionHandlerService>();
            await _interactionHandler.AddModulesAsync();

            _fileHandler = services.GetRequiredService<FileHandlingService>();
            _fileHandler.Initialize(_appSettings.Path);

            await _client.LoginAsync(TokenType.Bot, _appSettings.Token);
            await _client.StartAsync();

            //checkupTimer.Interval = TimeSpan.FromMinutes(30).TotalMilliseconds;
            //checkupTimer.Elapsed += (s, e) => _= CheckupTimerOnElapsed(s, e);
            //checkupTimer.AutoReset = true;
            //checkupTimer.Start();

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private static Task Suicide(Exception arg) {
            Environment.Exit(1);
            return Task.CompletedTask;
        }

        private async Task CheckupTimerOnElapsed(object sender, ElapsedEventArgs e) {
            try {
                logger.Info("Checking for late deadlines...");

                // Check assignments and give reminders
                var remindingTime = TimeSpan.FromDays(2);
                var assignmentsToRemind = await _context.Assignments.AsQueryable().Where(
                    o => o.Deadline.HasValue && o.Deadline - remindingTime < DateTime.UtcNow &&
                         (!o.LastReminder.HasValue || o.LastReminder + remindingTime < DateTime.UtcNow) &&
                         o.Part.Project.DoReminders)
                    .Include(o => o.Part).ThenInclude(p => p.Project)
                    .Include(o => o.Member)
                    .ToListAsync();

                logger.Debug("Found {count} assignments to remind.", assignmentsToRemind.Count);

                foreach (var assignment in assignmentsToRemind) {
                    try {
                        var user = _client.GetUser((ulong)assignment.Member.UniqueMemberId);

                        if (user != null) {
                            var dmChannel = await user.CreateDMChannelAsync();
                            await dmChannel.SendMessageAsync(string.Format(Strings.DeadlineReminder, user.Mention,
                                assignment.Part.Name, assignment.Part.Project.Name));
                        }
                    } catch (Exception ex) {
                        logger.Error(ex, "Failed to send reminder for assignment {assignment}", assignment.Id);
                    }

                    assignment.LastReminder = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();

                // Check passed deadlines
                var deadAssignments = await _context.Assignments.AsQueryable().Where(
                        o => o.Deadline.HasValue && o.Deadline < DateTime.UtcNow)
                    .Include(o => o.Part).ThenInclude(p => p.Project)
                    .Include(o => o.Member).ToListAsync();

                logger.Debug("Found {count} assignments overdue.", deadAssignments.Count);

                foreach (var assignment in deadAssignments) {
                    try {
                        if (assignment.Part.Project.MainChannelId.HasValue) {
                            // Show deadline passed message
                            var channel = _client.GetChannel((ulong)assignment.Part.Project.MainChannelId!.Value);
                            var user = _client.GetUser((ulong)assignment.Member.UniqueMemberId);

                            if (channel is ITextChannel textChannel && user != null) {
                                await textChannel.SendMessageAsync(string.Format(Strings.AssignmentDeadlinePassed,
                                    user.Mention,
                                    assignment.Part.Name, assignment.Part.Project.Name));
                            }
                            else if (user != null) {
                                var dmChannel = await user.CreateDMChannelAsync();
                                await dmChannel.SendMessageAsync(string.Format(Strings.AssignmentDeadlinePassed,
                                    user.Mention,
                                    assignment.Part.Name, assignment.Part.Project.Name));
                            }
                        }
                        else {
                            var user = _client.GetUser((ulong)assignment.Member.UniqueMemberId);
                            if (user != null) {
                                var dmChannel = await user.CreateDMChannelAsync();
                                await dmChannel.SendMessageAsync(string.Format(Strings.AssignmentDeadlinePassed,
                                    user.Mention,
                                    assignment.Part.Name, assignment.Part.Project.Name));
                            }
                        }
                    } catch (Exception ex) {
                        logger.Error(ex, "Failed to send message for overdue assignment {assignment}", assignment.Id);
                    }

                    // Remove the assignment
                    _context.Assignments.Remove(assignment);
                }
                await _context.SaveChangesAsync();

                // Check inactive projects
                var inactiveProjects = await _context.Projects.AsQueryable().Where(
                        o => o.LastActivity.HasValue && o.Guild.InactivityTimer.HasValue && o.CleanupOnDeletion &&
                             o.LastActivity + o.Guild.InactivityTimer < DateTime.UtcNow)
                    .Include(o => o.Guild)
                    .ToListAsync();

                logger.Debug("Found {count} inactive projects.", inactiveProjects.Count);

                foreach (var project in inactiveProjects) {
                    var guild = _client.GetGuild((ulong)project.Guild.UniqueGuildId);

                    if (guild is null) {
                        logger.Error("Failed to fetch guild for inactive project {project}", project.Id);
                        continue;
                    }

                    try {
                        var ownerMember = await _context.Members.Where(
                            o => o.Project.Id == project.Id && o.ProjectRole == ProjectRole.Owner).SingleOrDefaultAsync();
                        var user = _client.GetUser((ulong)ownerMember.UniqueMemberId);

                        if (user != null) {
                            var dmChannel = await user.CreateDMChannelAsync();
                            await dmChannel.SendMessageAsync(string.Format(Strings.InactiveProjectDeletionNotice, project.Name));
                        }
                    } catch (Exception ex) {
                        logger.Error(ex, "Failed to send notice for removal inactive project {project}", project.Id);
                    }

                    try {
                        await ProjectModule.DeleteProjectAsync(project, guild, _context, _fileHandler);
                    } catch (Exception ex) {
                        logger.Error(ex, "Failed to delete project {project}", project.Id);
                    }
                }
                await _context.SaveChangesAsync();
            } catch (Exception exception) {
                logger.Error(exception);
            }
        }

        private async Task Ready() {
            await _interactionHandler.RegisterModulesAsync(_guildList);
        }

        private async Task Connected() {
            //await _client.DownloadUsersAsync(guildList);
            //guildList.Clear();
            await _client.SetGameAsync(_appSettings.Prefix + "help");
        }

        private Task GuildAvailable(SocketGuild arg) {
            _guildList.Add(arg);
            return Task.CompletedTask;
        }

        private Task Log(LogMessage msg) {
            logger.Log(LogLevel.FromOrdinal(5 - (int)msg.Severity), msg.Message);
            if (msg.Exception is not null)
                logger.Log(LogLevel.FromOrdinal(5 - (int)msg.Severity), msg.Exception);
            return Task.CompletedTask;
        }

        private ServiceProvider ConfigureServices() {
            var services = new ServiceCollection()
                .AddSingleton(_appSettings)
                .AddSingleton<CommonService>()
                .AddSingleton<ResourceService>()
                .AddDbContext<OsuCollabContext>()
                .AddSingleton<FileHandlingService>()
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton(_interactionService)
                .AddSingleton(_client)
                .AddSingleton<InteractionHandlerService>()
                .AddSingleton<UserHelpService>()
                .AddSingleton<InputSanitizingService>()
                .AddSingleton(new InteractiveConfig { DefaultTimeout = TimeSpan.FromMinutes(10) })
                .AddSingleton<InteractiveService>();

            return services.BuildServiceProvider();
        }

        private AppSettings GetAppSettings() {
            return config.GetSection("App").Get<AppSettings>();
        }
    }
}