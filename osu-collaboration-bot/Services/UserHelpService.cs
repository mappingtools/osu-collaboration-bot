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
                                 string moduleName = "",
                                 string commandName = "",
                                 bool showReference = false) {
            string prefix = "/" + moduleName + (string.IsNullOrWhiteSpace(moduleName) ? string.Empty : " ");
            IReadOnlyList<SlashCommandInfo> commands;
            if (!string.IsNullOrEmpty(moduleName)) {
                commands = _interactionService.Modules.First(o => o.SlashGroupName == moduleName).SlashCommands.ToList();
            } else {
                commands = _interactionService.SlashCommands;
            }
            
            if (!string.IsNullOrEmpty(commandName)) {
                var command = commands.FirstOrDefault(o => string.Equals(o.Name, commandName, StringComparison.OrdinalIgnoreCase));

                if (command == null) {
                    await context.Interaction.RespondAsync(string.Format(Strings.CommandNotFound, prefix + commandName), ephemeral: true);
                    return;
                }

                await DoCommandHelp(context, prefix, command);
                return;
            }
            
            Embed[] embeds = new Embed[(commands.Count - 1) / 25 + 1 + (showReference ? 1 : 0)];
            int e = 0;
            int c = 0;
            EmbedBuilder embedBuilder = new EmbedBuilder();
            foreach (SlashCommandInfo command in commands) {
                // Get the command Summary attribute information
                string embedFieldText = command.Description ?? Strings.NoDescription + Environment.NewLine;
                string nameWithArguments = prefix + command.Name + string.Concat(command.Parameters.Select(o => $" [{o.Name}]"));

                embedBuilder.AddField(nameWithArguments, embedFieldText);
                c++;

                if (c == 25) {
                    embeds[e++] = embedBuilder.Build();
                    embedBuilder = new EmbedBuilder();
                    c = 0;
                }
            }

            if (c > 0) {
                embeds[e++] = embedBuilder.Build();
            }

            if (showReference) {
                embedBuilder = new EmbedBuilder();
                embedBuilder.AddField(Strings.OtherModuleHelpReference, string.Format(Strings.OtherModuleHelpGuide, prefix));
                embeds[e] = embedBuilder.Build();
            }

            await context.Interaction.RespondAsync(Strings.ListCommandsMessage, embeds, ephemeral: true);
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

            await context.Interaction.RespondAsync(string.Empty, embed: embedBuilder.Build(), ephemeral: true);
        }
    }
}