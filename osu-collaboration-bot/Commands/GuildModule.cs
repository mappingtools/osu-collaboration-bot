using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Commands {
    [Group("guild")]
    [Name("Guild module")]
    [Summary("Everything about guild settings")]
    public class GuildModule : ModuleBase<SocketCommandContext> {
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly UserHelpService _userHelpService;
        private readonly AppSettings _appSettings;

        public GuildModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, UserHelpService userHelpService,
            AppSettings appSettings) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _userHelpService = userHelpService;
            _appSettings = appSettings;
        }

        [Command("help")]
        [Summary("Shows command information")]
        public async Task Help(string command = "") {
            await _userHelpService.DoHelp(Context, "Guild module", "guild", command);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("init")]
        [Alias("add")]
        [Summary("Initializes compatibility with the server")]
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
        [Summary("Changes the category in which project channels will be automatically generated")]
        public async Task CollabCategory([Summary("The category")]ICategoryChannel category) {
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

        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("max-collabs")]
        [Summary("Changes the maximum number of projects a regular member can create")]
        public async Task MaxCollabs([Summary("The maximum number of projects")]int count) {
            var guild = await GetGuildAsync();

            if (guild == null) {
                return;
            }

            try {
                guild.MaxCollabsPerPerson = count;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.GuildMaxCollabsSuccess, count));
            }
            catch (Exception ex) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.GuildMaxCollabsFail, count));
                Console.WriteLine(ex);
            }
        }

        private async Task<Guild> GetGuildAsync() {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            return guild;
        }
    }
}