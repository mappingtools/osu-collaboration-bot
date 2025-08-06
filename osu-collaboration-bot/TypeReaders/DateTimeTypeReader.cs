using Discord.Interactions;
using System;
using System.Threading.Tasks;
using Discord;
using Mapping_Tools_Core;

namespace CollaborationBot.TypeReaders {
    public class DateTimeTypeReader : TypeConverter<DateTime?> {
        public bool AllowNull { get; set; } = true;

        public override ApplicationCommandOptionType GetDiscordType() {
            return ApplicationCommandOptionType.String;
        }

        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, IApplicationCommandInteractionDataOption option, IServiceProvider services) {
            return Task.FromResult(TryRead((string)option.Value, AllowNull));
        }

        public static TypeConverterResult TryRead(string value, bool allowNull = true) {
            try {
                if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)) {
                    return allowNull ?
                            TypeConverterResult.FromSuccess(null) :
                            TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Date time cannot be null.");
                }

                DateTime result = DateTime.Parse(value);
                return TypeConverterResult.FromSuccess(result);
            } catch {
                return TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Date time could not be parsed.");
            }
        }
    }
}