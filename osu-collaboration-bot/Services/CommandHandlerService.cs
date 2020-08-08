using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace CollaborationBot.Services {

    public class CommandHandlerService {
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;

        public CommandHandlerService(IServiceProvider services, DiscordSocketClient client, CommandService commands) {
            _services = services;
            _commands = commands;
            _client = client;

            client.MessageReceived += HandleCommandAsync;
        }

        public async Task InstallCommandsAsync() {
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        public async Task HandleCommandAsync(SocketMessage messageParameter) {
            // Don't process the command if it was a system message
            if( !( messageParameter is SocketUserMessage message ) ) {
                return;
            }

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command based on the prefix and make sure no bots trigger commands
            if( message.Author.IsBot ) {
                return;
            }
            else if( !message.HasStringPrefix("!!", ref argPos) ) {
                return;
            }

            // Create a WebSocket-based command context based on the message
            var context = new SocketCommandContext(_client, message);

            // Execute the command with the command context we just
            // created, along with the service provider for precondition checks.
            var result = await _commands.ExecuteAsync(
                context: context,
                argPos: argPos,
                services: _services);

            if( !result.IsSuccess ) {
                await context.Channel.SendMessageAsync(result.ErrorReason);
            }
        }
    }
}