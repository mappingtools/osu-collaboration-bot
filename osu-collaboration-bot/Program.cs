using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;
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

namespace CollaborationBot {
    public class Program {
        private static IConfigurationRoot config;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private DiscordSocketClient _client;
        private CommandHandlerService _commandHandler;
        private AppSettings _appSettings;
        private FileHandlingService _fileHandler;
        private OsuCollabContext _context;

        private readonly List<SocketGuild> guildList = new();
        private readonly Timer checkupTimer = new();

        public static void Main(string[] args) {
            config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables().Build();
            LogManager.Configuration = new NLogLoggingConfiguration(config.GetSection("NLog"));

            try {
                logger.Info("Starting program");
                new Program().MainAsync().GetAwaiter().GetResult();
            } catch (Exception exception) {
                //NLog: catch setup errors
                logger.Error(exception, "Stopped program because of exception");
                throw;
            } finally {
                // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
                LogManager.Shutdown();
            }
        }

        public async Task MainAsync() {
            await using var services = ConfigureServices();

            _appSettings = services.GetRequiredService<AppSettings>();
            _context = services.GetRequiredService<OsuCollabContext>();

            _client = services.GetRequiredService<DiscordSocketClient>();
            _client.Log += Log;
            _client.GuildAvailable += GuildAvailable;
            _client.Connected += Connected;

            _commandHandler = services.GetRequiredService<CommandHandlerService>();
            await _commandHandler.InstallCommandsAsync();

            _fileHandler = services.GetRequiredService<FileHandlingService>();
            _fileHandler.Initialize(_appSettings.Path);

            await _client.LoginAsync(TokenType.Bot, _appSettings.Token);
            await _client.StartAsync();

            checkupTimer.Interval = TimeSpan.FromMinutes(10).TotalMilliseconds;
            checkupTimer.Elapsed += CheckupTimerOnElapsed;
            checkupTimer.AutoReset = true;
            checkupTimer.Start();

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private async void CheckupTimerOnElapsed(object sender, ElapsedEventArgs e) {
            logger.Info("Checking for late deadlines...");

            logger.Debug("Current connection state: {state}", _client.ConnectionState);
            logger.Debug("Current login state: {state}", _client.LoginState);
            logger.Debug("Current latency: {state}", _client.Latency);
            logger.Debug("Shard ID: {state}", _client.ShardId);
            logger.Debug("Token type: {state}", _client.TokenType);

            // Check assignments and give reminders
            var remindingTime = TimeSpan.FromDays(2);

            // Query is grouped by project so multiple reminders in one channel can be combined to one message
            var assignmentsToRemind = (await _context.Assignments.AsQueryable().Where(
                o => o.Deadline.HasValue && o.Deadline - remindingTime < DateTime.UtcNow &&
                     (!o.LastReminder.HasValue || o.LastReminder + remindingTime < DateTime.UtcNow) &&
                     o.Part.Project.DoReminders && o.Part.Project.MainChannelId.HasValue)
                .Include(o => o.Part).ThenInclude(p => p.Project)
                .Include(o => o.Member)
                .ToListAsync()).GroupBy(o => o.Part.Project).ToList();

            logger.Debug("Found {count} assignments to remind.", assignmentsToRemind.SelectMany(o => o).Count());

            foreach (var assignmentGroup in assignmentsToRemind) {
                ulong channelId = (ulong) assignmentGroup.Key.MainChannelId!.Value;
                var channel = _client.GetChannel(channelId);
                var users = assignmentGroup
                    .Select(o => _client.GetUser((ulong) o.Member.UniqueMemberId))
                    .Where(o => o is not null).Distinct().ToList();
                
                if (channel is not ITextChannel textChannel || users.Count == 0) continue;

                var mentions = string.Join(' ', users.Select(o => o.Mention));
                var parts = assignmentGroup.Select(o => o.Part).Distinct().ToList();
                var projectName = assignmentGroup.Key.Name;

                if (parts.Count != 1) {
                    await textChannel.SendMessageAsync(string.Format(Strings.DeadlineReminderCombined, mentions, projectName));
                } else {
                    await textChannel.SendMessageAsync(string.Format(Strings.DeadlineReminder, mentions,
                        parts.First().Name, projectName));
                }

                foreach (var assignment in assignmentGroup) {
                    assignment.LastReminder = DateTime.UtcNow;
                }
            }

            // Check passed deadlines
            var deadAssignments = await _context.Assignments.AsQueryable().Where(
                    o => o.Deadline.HasValue && o.Deadline < DateTime.UtcNow)
                .Include(o => o.Part).ThenInclude(p => p.Project)
                .Include(o => o.Member).ToListAsync();
            
            logger.Debug("Found {count} assignments overdue.", deadAssignments.Count);

            foreach (var assignment in deadAssignments) {
                if (assignment.Part.Project.MainChannelId.HasValue) {
                    // Show deadline passed message
                    var channel = _client.GetChannel((ulong) assignment.Part.Project.MainChannelId!.Value);
                    var user = _client.GetUser((ulong) assignment.Member.UniqueMemberId);
                
                    if (channel is ITextChannel textChannel && user != null) {
                        await textChannel.SendMessageAsync(string.Format(Strings.AssignmentDeadlinePassed, user.Mention,
                            assignment.Part.Name, assignment.Part.Project.Name));
                    }
                }
                
                // Remove the assignment
                _context.Assignments.Remove(assignment);
            }

            await _context.SaveChangesAsync();
        }

        private async Task Connected() {
            await _client.DownloadUsersAsync(guildList);
            guildList.Clear();
            await _client.SetGameAsync(_appSettings.Prefix + "help");
        }

        private Task GuildAvailable(SocketGuild arg) {
            guildList.Add(arg);
            return Task.CompletedTask;
        }

        private Task Log(LogMessage msg) {
            logger.Info(msg.ToString());
            return Task.CompletedTask;
        }

        private ServiceProvider ConfigureServices() {
            var services = new ServiceCollection();
            services.AddSingleton(GetAppSettings());
            services.AddSingleton<ResourceService>();
            services.AddDbContext<OsuCollabContext>();
            services.AddSingleton<FileHandlingService>();
            services.AddSingleton<DiscordSocketClient>();
            services.AddSingleton<CommandService>();
            services.AddSingleton<CommandHandlerService>();
            services.AddSingleton<UserHelpService>();
            services.AddSingleton<InputSanitizingService>();

            return services.BuildServiceProvider();
        }

        private AppSettings GetAppSettings() {
            return config.GetSection("App").Get<AppSettings>();
        }
    }
}