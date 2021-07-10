using System;
using System.IO;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace CollaborationBot {
    public class Program {
        private DiscordSocketClient _client;
        private CommandHandlerService _commandHandler;
        private AppSettings _appSettings;
        private FileHandlingService _fileHandler;

        public static void Main(string[] args) {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync() {
            await using var services = ConfigureServices();

            _appSettings = services.GetRequiredService<AppSettings>();

            _client = services.GetRequiredService<DiscordSocketClient>();
            _client.Log += Log;

            await _client.LoginAsync(TokenType.Bot, _appSettings.Token);
            await _client.StartAsync();

            _commandHandler = services.GetRequiredService<CommandHandlerService>();
            await _commandHandler.InstallCommandsAsync();

            _fileHandler = services.GetRequiredService<FileHandlingService>();
            _fileHandler.Initialize(_appSettings.Path);

            // Block this task until the program is closed.
            await Task.Delay(-1);
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