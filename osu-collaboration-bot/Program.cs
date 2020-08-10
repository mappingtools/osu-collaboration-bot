using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using CollaborationBot.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using CollaborationBot.Database;

namespace CollaborationBot {

    public class Program {
        private DiscordSocketClient _client;
        private DiscordSettings _discordSettings;
        private DatabaseSettings _databaseSettings;
        private FileHandlerSettings _fileHandlerSettings;
        private CommandHandlerService _commandHandler;
        private CollaborationContext _context;

        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync() {
            using var services = ConfigureServices();

            _discordSettings = JsonConvert.DeserializeObject<DiscordSettings>(File.ReadAllText("discord_settings.json"));
            _databaseSettings = JsonConvert.DeserializeObject<DatabaseSettings>(File.ReadAllText("database_settings.json"));
            _fileHandlerSettings = JsonConvert.DeserializeObject<FileHandlerSettings>(File.ReadAllText("filehandler_settings.json"));

            _client = services.GetRequiredService<DiscordSocketClient>();
            _client.Log += Log;

            await _client.LoginAsync(TokenType.Bot, _discordSettings.Token);
            await _client.StartAsync();

            _context = services.GetRequiredService<CollaborationContext>();
            _context.Initialize(_databaseSettings.ConnectionString);

            _commandHandler = services.GetRequiredService<CommandHandlerService>();
            await _commandHandler.InstallCommandsAsync();

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private Task Log(LogMessage msg) {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private ServiceProvider ConfigureServices() {
            var services = new ServiceCollection();
            services.AddSingleton<ResourceService>();
            services.AddSingleton<CollaborationContext>();
            services.AddSingleton<FileHandlingService>();
            services.AddSingleton<DiscordSocketClient>();
            services.AddSingleton<CommandService>();
            services.AddSingleton<CommandHandlerService>();

            return services.BuildServiceProvider();
        }
    }
}