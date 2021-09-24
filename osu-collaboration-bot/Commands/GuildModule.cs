using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;

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

                _fileHandler.GenerateGuildDirectory(Context.Guild);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage());
            }
            catch (Exception ex) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddGuildMessage(false));
                Console.WriteLine(ex);
            }
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("collab-category")]
        public async Task CollabCategory(ICategoryChannel category) {
            var guild = await GetGuildAsync();

            if (guild == null) {
                return;
            }

            try {
                guild.CollabCategoryId = category.Id;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.GuildCollabCategorySuccess, category.Name));
            }
            catch (Exception ex) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.GuildCollabCategoryFail, category.Name));
                Console.WriteLine(ex);
            }
        }

        private async Task<Guild> GetGuildAsync() {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(_resourceService.GuildNotExistsMessage);
                return null;
            }

            return guild;
        }
    }
}