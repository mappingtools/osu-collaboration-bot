using Discord.Interactions;
using System;
using System.Threading.Tasks;
using Discord;

namespace CollaborationBot.TypeReaders {
    public class ColorTypeReader : TypeReader<Color> {
        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, string option, IServiceProvider services) {
            try {
                System.Drawing.Color col = System.Drawing.ColorTranslator.FromHtml(option);
                return Task.FromResult(TypeConverterResult.FromSuccess(new Color(col.R, col.G, col.B)));
            } catch {
                return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Input could not be parsed as a Hex code."));
            }
        }
    }
}