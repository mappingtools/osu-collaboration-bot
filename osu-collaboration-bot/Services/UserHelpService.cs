using CollaborationBot.Resources;
using Discord;
using Discord.Interactions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Services {
    public class UserHelpService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly InteractionService _interactionService;
        private readonly AppSettings _appSettings;

        public UserHelpService(InteractionService interactions, AppSettings appSettings) {
            _interactionService = interactions;
            _appSettings = appSettings;
        }

        public async Task DoHelp(SocketInteractionContext context,
                                 string moduleName,
                                 string modulePrefix,
                                 string commandName = "",
                                 bool showReference = false) {
            List<SlashCommandInfo> commands = _interactionService.Modules.First(o => o.Name == moduleName).SlashCommands.ToList();
            string prefix = _appSettings.Prefix + modulePrefix + (string.IsNullOrWhiteSpace(modulePrefix) ? string.Empty : " ");

            if (!string.IsNullOrEmpty(commandName)) {
                var command = commands.FirstOrDefault(o => string.Equals(o.Name, commandName, StringComparison.OrdinalIgnoreCase));

                if (command == null) {
                    await context.Channel.SendMessageAsync(string.Format(Strings.CommandNotFound, prefix + commandName));
                    return;
                }

                await DoCommandHelp(context, prefix, command);
                return;
            }

            var dmChannel = await context.User.CreateDMChannelAsync();
            EmbedBuilder embedBuilder = new EmbedBuilder();
            int c = 0;
            bool first = true;
            foreach (SlashCommandInfo command in commands) {
                // Get the command Summary attribute information
                string embedFieldText = command.Description ?? Strings.NoDescription + Environment.NewLine;
                string nameWithArguments = prefix + command.Name + string.Concat(command.Parameters.Select(o => $" [{o.Name}]"));

                embedBuilder.AddField(nameWithArguments, embedFieldText);
                c++;

                if (c == 25) {
                    await dmChannel.SendMessageAsync(first ? Strings.ListCommandsMessage : string.Empty, false, embedBuilder.Build());
                    embedBuilder = new EmbedBuilder();
                    first = false;
                    c = 0;
                }
            }

            if (c > 0) {
                await dmChannel.SendMessageAsync(first ? Strings.ListCommandsMessage : string.Empty, false, embedBuilder.Build());
            }

            if (showReference) {
                embedBuilder = new EmbedBuilder();
                embedBuilder.AddField(Strings.OtherModuleHelpReference, string.Format(Strings.OtherModuleHelpGuide, prefix));
                await dmChannel.SendMessageAsync(string.Empty, false, embedBuilder.Build());
            }
        }

        private async Task DoCommandHelp(SocketInteractionContext context, string prefix, SlashCommandInfo command) {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            // Get the command Summary attribute information
            string embedFieldText = command.Description ?? Strings.NoDescription + Environment.NewLine;
            string nameWithArguments = prefix + command.Name + string.Concat(command.Parameters.Select(o => $" [{o.Name}]"));

            embedBuilder.AddField(nameWithArguments, embedFieldText);

            foreach (var parameter in command.Parameters) {
                string parameterEmbedFieldText = parameter.Description ?? Strings.NoDescription + Environment.NewLine;
                embedBuilder.AddField($"[{parameter.Name}]", parameterEmbedFieldText);
            }

            await context.Channel.SendMessageAsync(string.Empty, false, embedBuilder.Build());
        }
    }
}