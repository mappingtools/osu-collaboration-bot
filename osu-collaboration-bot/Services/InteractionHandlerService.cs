using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CollaborationBot.Resources;
using CollaborationBot.TypeReaders;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using NLog;

namespace CollaborationBot.Services {
    public class InteractionHandlerService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactions;
        private readonly AppSettings _appSettings;
        private readonly IServiceProvider _services;

        public InteractionHandlerService(IServiceProvider services, DiscordSocketClient client, InteractionService interactions, AppSettings appSettings) {
            _services = services;
            _interactions = interactions;
            _client = client;
            _appSettings = appSettings;

            _client.SlashCommandExecuted += SlashCommandHandler;

            // Add custom type readers
            //_interactions.AddTypeReader<TimeSpan>(new OsuTimeTypeReader());
            //_interactions.AddTypeReader<Discord.Color>(new ColorTypeReader());
        }

        private async Task SlashCommandHandler(SocketSlashCommand command) {
            var fullCommand = command.Data.Name + string.Join(' ', command.Data.Options.Select(o => o.Name));
            logger.Debug("Slash command issued by user {user}: {command}", command.User.Username, fullCommand);


            var ctx = new SocketInteractionContext(_client, command);
            var result = await _interactions.ExecuteCommandAsync(ctx, _services);

            if (!result.IsSuccess) {
                logger.Error("Error of type {type} caused by {@message}: {reason}", result.Error, fullCommand, result.ErrorReason);

                // We dont want to send Exception reasons in discord chat
                if (result.Error == InteractionCommandError.Exception) {
                    await command.RespondAsync(Strings.BackendErrorMessage);
                } else {
                    await command.RespondAsync(result.ErrorReason);
                }
            }
        }

        public async Task AddModulesAsync() {
            await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        public async Task RegisterModulesAsync() {
#if DEBUG
            await _interactions.RegisterCommandsToGuildAsync(590879727477325865);
#else
            await _interactions.RegisterCommandsGloballyAsync();
#endif
        }
    }
}