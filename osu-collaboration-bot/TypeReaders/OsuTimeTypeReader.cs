using Discord.Interactions;
using System;
using System.Threading.Tasks;
using Discord;
using Mapping_Tools_Core;

namespace CollaborationBot.TypeReaders {
    public class OsuTimeTypeReader : TypeReader<TimeSpan> {
        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, string option, IServiceProvider services) {
            try {
                TimeSpan result = InputParsers.ParseOsuTimestamp(option);
                return Task.FromResult(TypeConverterResult.FromSuccess(result));
            } catch {
                return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Input could not be parsed as an osu! timestamp."));
            }
        }
    }
}