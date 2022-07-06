using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
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
        private readonly CommonService _common;

        public GuildModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, CommonService common) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _common = common;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("init", "Initializes compatibility with the server")]
        public async Task Init() {
            try {
                if (await _context.Guilds.AnyAsync(o => o.UniqueGuildId == Context.Guild.Id)) {
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
            var guild = await _common.GetGuildAsync(Context);

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
            var guild = await _common.GetGuildAsync(Context);

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

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("inactivitytimer", "Changes the duration of inactivity after which a project will be deleted. If null, never deleted")]
        public async Task InactivityTimer([Summary("time", "The new inactivity timer duration (dd:hh:mm:ss:fff) (can be null)")] TimeSpan? time) {
            var guild = await _common.GetGuildAsync(Context);

            if (guild == null) {
                return;
            }

            try {
                guild.InactivityTimer = time;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.GuildInactivityTimerSuccess, time.HasValue ? time.Value.ToString("g") : Strings.None));
            }
            catch (Exception ex) {
                await RespondAsync(string.Format(Strings.GuildInactivityTimerFail));
                logger.Error(ex);
            }
        }
    }
}