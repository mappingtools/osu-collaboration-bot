using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;

namespace CollaborationBot.Commands {
    [Group("guild")]
    public class GuildModule : ModuleBase<SocketCommandContext> {
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public GuildModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("init")]
        public async Task Init() {
            try {
                if (_context.Guilds.Any(o => o.UniqueGuildId == Context.Guild.Id)) {
                    await Context.Channel.SendMessageAsync(Strings.GuildExistsMessage);
                    return;
                }

                await _context.Guilds.AddAsync(new Guild { UniqueGuildId = Context.Guild.Id });
                await _context.SaveChangesAsync();
            }
            catch (Exception) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage(false));
                return;
            }

            _fileHandler.GenerateGuildDirectory(Context.Guild);

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage());
        }
    }
}