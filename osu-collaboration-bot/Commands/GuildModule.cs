using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Interactions;
using NLog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Commands {
    [Group("guild", "Everything about guild settings")]
    //[Name("Guild module")]
    //[Summary("Everything about guild settings")]
    public class GuildModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly AppSettings _appSettings;

        public GuildModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService,
            AppSettings appSettings) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _appSettings = appSettings;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("init", "Initializes compatibility with the server")]
        public async Task Init() {
            try {
                if (_context.Guilds.Any(o => o.UniqueGuildId == Context.Guild.Id)) {
                    await RespondAsync(Strings.GuildExistsMessage);
                    return;
                }

                await _context.Guilds.AddAsync(new Guild { UniqueGuildId = Context.Guild.Id });
                await _context.SaveChangesAsync();

                _fileHandler.GenerateGuildDirectory(Context.Guild);
                await RespondAsync(_resourceService.GenerateAddGuildMessage());
            }
            catch (Exception ex) {
                await RespondAsync(_resourceService.GenerateAddGuildMessage(false));
                logger.Error(ex);
            }
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("collabcategory", "Changes the category in which project channels will be automatically generated")]
        public async Task CollabCategory([Summary("category")]ICategoryChannel category) {
            var guild = await GetGuildAsync();

            if (guild == null) {
                return;
            }

            try {
                guild.CollabCategoryId = category.Id;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.GuildCollabCategorySuccess, category.Name));
            }
            catch (Exception ex) {
                await RespondAsync(string.Format(Strings.GuildCollabCategoryFail, category.Name));
                logger.Error(ex);
            }
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("maxcollabs", "Changes the maximum number of projects a regular member can create")]
        public async Task MaxCollabs([Summary("count", "The maximum number of projects")]int count) {
            var guild = await GetGuildAsync();

            if (guild == null) {
                return;
            }

            try {
                guild.MaxCollabsPerPerson = count;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.GuildMaxCollabsSuccess, count));
            }
            catch (Exception ex) {
                await RespondAsync(string.Format(Strings.GuildMaxCollabsFail, count));
                logger.Error(ex);
            }
        }

        private async Task<Guild> GetGuildAsync() {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await RespondAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            return guild;
        }
    }
}