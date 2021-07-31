using Discord.Commands;
using System;
using System.Threading.Tasks;
using Mapping_Tools_Core;

namespace CollaborationBot.TypeReaders {
    public class OsuTimeTypeReader : TypeReader {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services) {
            try {
                TimeSpan result = InputParsers.ParseOsuTimestamp(input);
                return Task.FromResult(TypeReaderResult.FromSuccess(result));
            } catch {
                return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Input could not be parsed as an osu! timestamp."));
            }
        }
    }
}