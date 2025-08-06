using Discord.Interactions;
using System;
using System.Threading.Tasks;
using Discord;
using Mapping_Tools_Core;

namespace CollaborationBot.TypeReaders {
    public class OsuTimeTypeReader : TypeConverter<TimeSpan?> {
        public bool AllowNull { get; set; } = true;

        public override ApplicationCommandOptionType GetDiscordType() {
            return ApplicationCommandOptionType.String;
        }

        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, IApplicationCommandInteractionDataOption option, IServiceProvider services) {
            try {
                var value = (string)option.Value;
                // Remove parts after a dash, for example in feedback: "01:56:474 (1,2) - ..."
                value = value.Split('-')[0].Trim();

                if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)) {
                    return Task.FromResult(
                        AllowNull ?
                            TypeConverterResult.FromSuccess(null) :
                            TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Timestamp cannot be null.")
                            );
                }

                TimeSpan result = InputParsers.ParseOsuTimestamp(value);
                return Task.FromResult(TypeConverterResult.FromSuccess(result));
            } catch {
                return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Timestamp could not be parsed as an osu! timestamp."));
            }
        }
    }
}