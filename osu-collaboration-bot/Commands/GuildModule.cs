using CollaborationBot.Database;
using Discord.Commands;
using System.Threading.Tasks;

namespace CollaborationBot.Commands {

    [Group("guild")]
    public class GuildModule :ModuleBase<SocketCommandContext> {
        private readonly CollaborationContext _context;

        public GuildModule(CollaborationContext context) {
            _context = context;
        }

        [RequireUserPermission(Discord.GuildPermission.Administrator)]
        [Command("add")]
        public async Task Add() {
            if( await _context.AddGuild(Context.Guild.Id) ) {
                await Context.Channel.SendMessageAsync($"Added guild '{Context.Guild.Name}'.");
                return;
            }

            await Context.Channel.SendMessageAsync($"Could not add guild '{Context.Guild.Name}'.");
        }
    }
}