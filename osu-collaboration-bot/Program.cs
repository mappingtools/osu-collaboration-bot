using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using CollaborationBot.Resources;

namespace CollaborationBot {
    public class Program {
        private DiscordSocketClient _client;
        private CommandHandlerService _commandHandler;
        private AppSettings _appSettings;
        private FileHandlingService _fileHandler;
        private OsuCollabContext _context;

        private readonly List<SocketGuild> guildList = new();
        private readonly Timer checkupTimer = new();

        // TODO: Add help text
        // TODO: Add guild permission config
        // TODO: Add auto project channel and role setup

        public static void Main(string[] args) {
            new Program().MainAsync().GetAwaiter().GetResult();
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
            // Check deadlines and give reminders
            var remindingTime = TimeSpan.FromDays(2);

            var assignmentsToRemind = await _context.Assignments.AsQueryable().Where(
                o => o.Deadline.HasValue && o.Deadline - remindingTime < DateTime.UtcNow &&
                     (!o.LastReminder.HasValue || o.LastReminder + remindingTime < DateTime.UtcNow) &&
                     o.Part.Project.DoReminders && o.Part.Project.MainChannelId.HasValue)
                .Include(o => o.Part).ThenInclude(p => p.Project)
                .Include(o => o.Member).ToListAsync();

            foreach (var assignment in assignmentsToRemind) {
                var channel = _client.GetChannel((ulong) assignment.Part.Project.MainChannelId!.Value);
                var user = _client.GetUser((ulong) assignment.Member.UniqueMemberId);
                
                if (channel is not ITextChannel textChannel) continue;

                await textChannel.SendMessageAsync(string.Format(Strings.DeadlineReminder, user.Mention,
                    assignment.Part, assignment.Part.Project.Name));

                // TODO: update last reminder time
            }

        }

        private async Task Connected() {
            await _client.DownloadUsersAsync(guildList);
            guildList.Clear();
        }

        private Task GuildAvailable(SocketGuild arg) {
            guildList.Add(arg);
            return Task.CompletedTask;
        }

        private Task Log(LogMessage msg) {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private ServiceProvider ConfigureServices() {
            var services = new ServiceCollection();
            services.AddSingleton(
                JsonConvert.DeserializeObject<AppSettings>(
                    File.ReadAllText("appsettings.Development.json")));
            services.AddSingleton<ResourceService>();
            services.AddDbContext<OsuCollabContext>();
            services.AddSingleton<FileHandlingService>();
            services.AddSingleton<DiscordSocketClient>();
            services.AddSingleton<CommandService>();
            services.AddSingleton<CommandHandlerService>();

            return services.BuildServiceProvider();
        }
    }
}