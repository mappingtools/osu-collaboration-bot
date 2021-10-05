using Discord.Commands;
using System;
using System.Threading.Tasks;

namespace CollaborationBot.TypeReaders {
    public class ColorTypeReader : TypeReader {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services) {
            try {
                System.Drawing.Color col = System.Drawing.ColorTranslator.FromHtml(input);
                return Task.FromResult(TypeReaderResult.FromSuccess(new Discord.Color(col.R, col.G, col.B)));
            } catch {
                return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Input could not be parsed as a Hex code."));
            }
        }
    }
}