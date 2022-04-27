using Discord.Interactions;
using System;
using System.Threading.Tasks;
using Discord;

namespace CollaborationBot.TypeReaders {
    public class StringArrayReader : TypeConverter<string[]> {
        public override ApplicationCommandOptionType GetDiscordType() {
            return ApplicationCommandOptionType.String;
        }

        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, IApplicationCommandInteractionDataOption option, IServiceProvider services) {
            return Task.FromResult(TypeConverterResult.FromSuccess(((string)option.Value).Split(' ')));
        }
    }
}