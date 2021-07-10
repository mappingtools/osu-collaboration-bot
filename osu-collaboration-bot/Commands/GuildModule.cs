using System.Threading.Tasks;
using CollaborationBot.Database;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;

namespace CollaborationBot.Commands {
    [Group("guild")]
    public class GuildModule : ModuleBase<SocketCommandContext> {
        private readonly CollaborationContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public GuildModule(CollaborationContext context, FileHandlingService fileHandler,
            ResourceService resourceService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("add")]
        public async Task Add() {
            if (!await _context.AddGuildAsync(Context.Guild.Id)) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage(false));
                return;
            }

            _fileHandler.GenerateGuildDirectory(Context.Guild);

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage());
        }
    }
}