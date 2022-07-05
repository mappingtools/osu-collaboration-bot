using Discord.Interactions;
using System;
using System.Threading.Tasks;
using Discord;
using Mapping_Tools_Core;

namespace CollaborationBot.TypeReaders {
    public class OsuTimeTypeReader : TypeConverter<TimeSpan> {
        public override ApplicationCommandOptionType GetDiscordType() {
            return ApplicationCommandOptionType.String;
        }

        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, IApplicationCommandInteractionDataOption option, IServiceProvider services) {
            try {
                if (string.Equals((string)option.Value, "null", StringComparison.OrdinalIgnoreCase)) {
                    return Task.FromResult(TypeConverterResult.FromSuccess(null));
                }

                TimeSpan result = InputParsers.ParseOsuTimestamp((string)option.Value);
                return Task.FromResult(TypeConverterResult.FromSuccess(result));
            } catch {
                return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Input could not be parsed as an osu! timestamp."));
            }
        }
    }
}