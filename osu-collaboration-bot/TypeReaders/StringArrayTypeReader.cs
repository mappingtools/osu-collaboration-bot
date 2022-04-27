using Discord.Interactions;
using System;
using System.Threading.Tasks;
using Discord;

namespace CollaborationBot.TypeReaders {
    public class StringArrayReader : TypeReader<string[]> {
        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, string option, IServiceProvider services) {
            return Task.FromResult(TypeConverterResult.FromSuccess(option.Split(' ')));
        }
    }
}