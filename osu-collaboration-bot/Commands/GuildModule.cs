using CollaborationBot.Database;
using CollaborationBot.Services;
using Discord.Commands;
using System.Threading.Tasks;

namespace CollaborationBot.Commands {

    [Group("guild")]
    public class GuildModule :ModuleBase<SocketCommandContext> {
        private readonly CollaborationContext _context;
        private readonly ResourceService _resourceService;

        public GuildModule(CollaborationContext context, ResourceService resourceService) {
            _context = context;
            _resourceService = resourceService;
        }

        [RequireUserPermission(Discord.GuildPermission.Administrator)]
        [Command("add")]
        public async Task Add() {
            if( await _context.AddGuild(Context.Guild.Id) ) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage());
                return;
            }

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage(false));
        }
    }
}