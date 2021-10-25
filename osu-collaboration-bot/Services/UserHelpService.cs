using CollaborationBot.Resources;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Services {
    public class UserHelpService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly CommandService _commandService;
        private readonly AppSettings _appSettings;

        public UserHelpService(CommandService commands, AppSettings appSettings) {
            _commandService = commands;
            _appSettings = appSettings;
        }

        public async Task DoHelp(SocketCommandContext context,
                                 string moduleName,
                                 string modulePrefix,
                                 string commandName = "",
                                 bool showReference = false) {
            List<CommandInfo> commands = _commandService.Modules.First(o => o.Name == moduleName).Commands.ToList();
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

            EmbedBuilder embedBuilder = new EmbedBuilder();
            int c = 0;
            bool first = true;
            foreach (CommandInfo command in commands) {
                // Get the command Summary attribute information
                string embedFieldText = command.Summary ?? Strings.NoDescription + Environment.NewLine;
                string nameWithArguments = prefix + command.Name + string.Concat(command.Parameters.Select(o => $" [{o.Name}]"));

                embedBuilder.AddField(nameWithArguments, embedFieldText);
                c++;

                if (c == 25) {
                    await context.Channel.SendMessageAsync(first ? Strings.ListCommandsMessage : string.Empty, false, embedBuilder.Build());
                    embedBuilder = new EmbedBuilder();
                    first = false;
                    c = 0;
                }
            }

            if (c > 0) {
                await context.Channel.SendMessageAsync(first ? Strings.ListCommandsMessage : string.Empty, false, embedBuilder.Build());
            }

            if (showReference) {
                embedBuilder = new EmbedBuilder();
                embedBuilder.AddField(Strings.OtherModuleHelpReference, string.Format(Strings.OtherModuleHelpGuide, prefix));
                await context.Channel.SendMessageAsync(string.Empty, false, embedBuilder.Build());
            }
        }

        private async Task DoCommandHelp(SocketCommandContext context, string prefix, CommandInfo command) {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            // Get the command Summary attribute information
            string embedFieldText = command.Summary ?? Strings.NoDescription + Environment.NewLine;
            string nameWithArguments = prefix + command.Name + string.Concat(command.Parameters.Select(o => $" [{o.Name}]"));

            embedBuilder.AddField(nameWithArguments, embedFieldText);

            if (command.Aliases.Count > 0) {
                embedBuilder.AddField(Strings.Aliases, string.Join(", ", command.Aliases));
            }
            
            foreach (var parameter in command.Parameters) {
                string parameterEmbedFieldText = parameter.Summary ?? Strings.NoDescription + Environment.NewLine;
                embedBuilder.AddField($"[{parameter.Name}]", parameterEmbedFieldText);
            }

            await context.Channel.SendMessageAsync(string.Empty, false, embedBuilder.Build());
        }
    }
}