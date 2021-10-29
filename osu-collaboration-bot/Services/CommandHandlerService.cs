using System;
using System.Reflection;
using System.Threading.Tasks;
using CollaborationBot.Resources;
using CollaborationBot.TypeReaders;
using Discord.Commands;
using Discord.WebSocket;
using NLog;

namespace CollaborationBot.Services {
    public class CommandHandlerService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly AppSettings _appSettings;
        private readonly IServiceProvider _services;

        public CommandHandlerService(IServiceProvider services, DiscordSocketClient client, CommandService commands, AppSettings appSettings) {
            _services = services;
            _commands = commands;
            _client = client;
            _appSettings = appSettings;

            client.MessageReceived += HandleCommandAsync;

            // Add custom type readers
            _commands.AddTypeReader<TimeSpan>(new OsuTimeTypeReader());
            _commands.AddTypeReader<Discord.Color>(new ColorTypeReader());
        }

        public async Task InstallCommandsAsync() {
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        public async Task HandleCommandAsync(SocketMessage messageParameter) {
            // Don't process the command if it was a system message
            if (messageParameter is not SocketUserMessage message) return;

            // Create a number to track where the prefix ends and the command begins
            var argPos = 0;

            // Determine if the message is a command based on the prefix and make sure no bots trigger commands
            if (message.Author.IsBot)
                return;
            if (!message.HasStringPrefix(_appSettings.Prefix, ref argPos)) return;

            // Create a WebSocket-based command context based on the message
            var context = new SocketCommandContext(_client, message);

            // Execute the command with the command context we just
            // created, along with the service provider for precondition checks.
            var result = await _commands.ExecuteAsync(
                context,
                argPos,
                _services);

            if (!result.IsSuccess) {
                logger.Error("Error of type {type} caused by {@message}: {reason}", result.Error, message.Content, result.ErrorReason);

                // We dont want to send Exception reasons in discord chat
                if (result.Error == CommandError.Exception) {
                    await context.Channel.SendMessageAsync(Strings.BackendErrorMessage);
                } else {
                    await context.Channel.SendMessageAsync(result.ErrorReason);
                }
            }
        }
    }
}