using CollaborationBot.Database;
using CollaborationBot.Services;
using Discord.Commands;
using System.Threading.Tasks;

namespace CollaborationBot.Commands {

    [Group("guild")]
    public class GuildModule :ModuleBase<SocketCommandContext> {
        private readonly CollaborationContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public GuildModule(CollaborationContext context, FileHandlingService fileHandler, ResourceService resourceService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
        }

        [RequireUserPermission(Discord.GuildPermission.Administrator)]
        [Command("add")]
        public async Task Add() {
            if( !await _context.AddGuild(Context.Guild.Id) ) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage(false));
                return;
            }

            _fileHandler.GenerateGuildDirectory(Context.Guild);

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage());
        }
    }
}