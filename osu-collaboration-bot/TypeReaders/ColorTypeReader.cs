using Discord.Interactions;
using System;
using System.Threading.Tasks;
using Discord;

namespace CollaborationBot.TypeReaders {
    public class ColorTypeReader : TypeConverter<Color> {
        public override ApplicationCommandOptionType GetDiscordType() {
            return ApplicationCommandOptionType.String;
        }

        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, IApplicationCommandInteractionDataOption option, IServiceProvider services) {
            try {
                System.Drawing.Color col = System.Drawing.ColorTranslator.FromHtml((string)option.Value);
                return Task.FromResult(TypeConverterResult.FromSuccess(new Color(col.R, col.G, col.B)));
            } catch {
                return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Input could not be parsed as a Hex code."));
            }
        }
    }
}