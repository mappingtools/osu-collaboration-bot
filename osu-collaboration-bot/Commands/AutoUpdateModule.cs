using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord.Interactions;
using System.Threading.Tasks;
using System.Linq;
using Discord;
using CollaborationBot.Preconditions;
using System;
using System.Collections.Generic;
using CollaborationBot.Autocomplete;
using Fergun.Interactive;
using NLog;
using Microsoft.EntityFrameworkCore;

namespace CollaborationBot.Commands {
    [Group("au", "Everything about automatic update notifications")]
    public class AutoUpdateModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly CommonService _common;

        public AutoUpdateModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, CommonService common) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _common = common;
        }
        
        [SlashCommand("list", "Lists all the update notifications attached to the project", runMode:RunMode.Async)]
        public async Task List([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var autoUpdates = await _context.AutoUpdates.AsQueryable()
                .Where(o => o.ProjectId == project.Id)
                .ToListAsync();

            await _resourceService.RespondPaginator(Context, autoUpdates, GenerateAutoUpdateListPages,
                Strings.NoAutoUpdates, Strings.AutoUpdatesListMessage);
        }

        private IPageBuilder[] GenerateAutoUpdateListPages(List<AutoUpdate> autoUpdates) {
            if (autoUpdates.Count <= 0) return null;
            return _resourceService.GenerateListPages(
                autoUpdates.Select(o =>
                    (o.Id.ToString(), $"channel: {ChannelName((ulong)o.UniqueChannelId)}, cooldown: {o.Cooldown}, do ping: {o.DoPing}")),
                Strings.AutoUpdates);
        }

        private string ChannelName(ulong id) {
            var channel = Context.Guild.GetChannel(id);
            return channel == null ? Strings.DeletedChannel : channel.Name;
        }

        [SlashCommand("add", "Adds a new update notification to the project")]
        public async Task Add([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("channel", "The channel to post the notification in")]ITextChannel channel,
            [Summary("cooldown", "The cooldown on the notification (dd:hh:mm:ss:fff) (can be null)")]TimeSpan? cooldown = null,
            [Summary("mentions", "Whether to ping members on an update notification")]bool doPing = false) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }
            
            if (channel == null) {
                await RespondAsync(Strings.TextChannelNotExist);
                return;
            }

            try {
                await _context.AutoUpdates.AddAsync(new AutoUpdate() { ProjectId = project.Id, Cooldown = cooldown, DoPing = doPing, UniqueChannelId = channel.Id });
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.AddAutoUpdateSuccess, projectName, channel.Mention));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.AddAutoUpdateFail, projectName, channel.Mention));
            }
        }
        
        [SlashCommand("remove", "Removes an update notification from the project")]
        public async Task Remove([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("channel", "The channel the notification is in")]ITextChannel channel) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }
            
            var autoUpdate = await _common.GetAutoUpdateAsync(Context, _context, project, channel);

            if (autoUpdate == null) {
                return;
            }

            try {
                _context.AutoUpdates.Remove(autoUpdate);
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.RemoveAutoUpdateSuccess, projectName, channel.Mention));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.RemoveAutoUpdateFail, projectName, channel.Mention));
            }
        }

        [SlashCommand("cooldown", "Changes the cooldown of the update notification")]
        public async Task Cooldown([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("channel", "The channel the notification is in")]ITextChannel channel,
            [Summary("cooldown", "The new cooldown (dd:hh:mm:ss:fff) (can be null)")]TimeSpan? cooldown) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }
            
            var autoUpdate = await _common.GetAutoUpdateAsync(Context, _context, project, channel);

            if (autoUpdate == null) {
                return;
            }

            try {
                autoUpdate.Cooldown = cooldown;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.AutoUpdateCooldownSuccess, projectName, cooldown));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.AutoUpdateCooldownFail, projectName));
            }
        }
        
        [SlashCommand("mentions", "Changes whether the update notification pings all members")]
        public async Task DoPing([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("channel", "The channel the notification is in")]ITextChannel channel,
            [Summary("mentions", "Whether to ping all members in the update notification")]bool doPing) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }
            
            var autoUpdate = await _common.GetAutoUpdateAsync(Context, _context, project, channel);

            if (autoUpdate == null) {
                return;
            }

            try {
                autoUpdate.DoPing = doPing;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.AutoUpdateDoPingSuccess, doPing));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.AutoUpdateDoPingFail));
            }
        }
        
        [SlashCommand("trigger", "Triggers all update notifications of the project")]
        public async Task Trigger([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            if (!_fileHandler.ProjectBaseFileExists(Context.Guild, project.Name)) {
                await RespondAsync(Strings.BaseFileNotExists);
                return;
            }

            try {
                await HandleAutoUpdates(project, Context, _context, _fileHandler);
                await RespondAsync(string.Format(Strings.AutoUpdateTriggerSuccess, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.AutoUpdateTriggerFail, projectName));
            }
        }

        public static async Task HandleAutoUpdates(Project project, SocketInteractionContext context, OsuCollabContext dbContext, FileHandlingService fileHandler) {
            if (!fileHandler.ProjectBaseFileExists(context.Guild, project.Name)) {
                return;
            }
            
            // Maybe not use cooldown on the trigger command
            var updates = await dbContext.AutoUpdates.AsQueryable()
                .Where(o => o.ProjectId == project.Id)
                .ToListAsync();

            foreach (var autoUpdate in updates) {
                // Check cooldown
                if (autoUpdate.LastTime.HasValue && autoUpdate.Cooldown.HasValue &&
                    autoUpdate.LastTime.Value + autoUpdate.Cooldown.Value > DateTime.UtcNow) {
                    continue;
                }

                string message;
                if (autoUpdate.DoPing && project.UniqueRoleId.HasValue) {
                    string mention = context.Guild.GetRole((ulong) project.UniqueRoleId).Mention;
                    message = string.Format(Strings.AutoUpdateLatestMention, mention, project.Name);
                } else {
                    message = string.Format(Strings.AutoUpdateLatest, project.Name);
                }

                var channel = context.Guild.GetTextChannel((ulong) autoUpdate.UniqueChannelId);
                if (channel != null) {
                    await channel.SendFileAsync(
                        fileHandler.GetProjectBaseFilePath(context.Guild, project.Name), message);
                }
                
                autoUpdate.LastTime = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync();
        }
    }
}