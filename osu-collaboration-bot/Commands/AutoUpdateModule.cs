using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using Discord;
using CollaborationBot.Preconditions;
using System;
using System.Collections.Generic;
using NLog;

namespace CollaborationBot.Commands {
    [Group("au", "Everything about automatic update notifications")]
    public class AutoUpdateModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly UserHelpService _userHelpService;
        private readonly AppSettings _appSettings;

        public AutoUpdateModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, UserHelpService userHelpService,
            AppSettings appSettings) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _userHelpService = userHelpService;
            _appSettings = appSettings;
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("list", "Lists all the update notifications attached to the project")]
        public async Task List([Summary("project", "The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var autoUpdates = await _context.AutoUpdates.AsQueryable()
                .Where(o => o.ProjectId == project.Id)
                .ToListAsync();

            await Context.Channel.SendMessageAsync(GenerateAutoUpdateListMessage(autoUpdates));
        }

        public string GenerateAutoUpdateListMessage(List<AutoUpdate> autoUpdates) {
            if (autoUpdates.Count <= 0) return "There are no automatic update notifications for this project.";
            return _resourceService.GenerateListMessage("Here are all the automatic update notifications for the project:",
                autoUpdates.Select(o => $"{o.Id}: channel: {ChannelName((ulong)o.UniqueChannelId)}, cooldown: {o.Cooldown}, do ping: {o.DoPing}"));
        }

        public string ChannelName(ulong id) {
            var channel = Context.Guild.GetChannel(id);
            return channel == null ? Strings.DeletedChannel : channel.Name;
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("add", "Adds a new update notification to the project")]
        public async Task Add([Summary("project", "The project")]string projectName,
            [Summary("channel", "The channel to post the notification in")]ITextChannel channel,
            [Summary("cooldown", "The cooldown on the notification (dd:hh:mm:ss:fff) (can be null)")]TimeSpan? cooldown = null,
            [Summary("mentions", "Whether to ping members on an update notification")]bool doPing = false) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }
            
            if (channel == null) {
                await Context.Channel.SendMessageAsync(Strings.TextChannelNotExist);
                return;
            }

            try {
                await _context.AutoUpdates.AddAsync(new AutoUpdate() { ProjectId = project.Id, Cooldown = cooldown, DoPing = doPing, UniqueChannelId = channel.Id });
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.AddAutoUpdateSuccess, projectName, channel.Mention));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AddAutoUpdateFail, projectName, channel.Mention));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("remove", "Removes an update notification from the project")]
        public async Task Remove([Summary("project", "The project")]string projectName,
            [Summary("channel", "The channel the notification is in")]ITextChannel channel) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }
            
            var autoUpdate = await GetAutoUpdateAsync(project, channel);

            if (autoUpdate == null) {
                return;
            }

            try {
                _context.AutoUpdates.Remove(autoUpdate);
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.RemoveAutoUpdateSuccess, projectName, channel.Mention));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.RemoveAutoUpdateFail, projectName, channel.Mention));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("cooldown", "Changes the cooldown of the update notification")]
        public async Task Cooldown([Summary("project", "The project")]string projectName,
            [Summary("channel", "The channel the notification is in")]ITextChannel channel,
            [Summary("cooldown", "The new cooldown (dd:hh:mm:ss:fff) (can be null)")]TimeSpan? cooldown) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }
            
            var autoUpdate = await GetAutoUpdateAsync(project, channel);

            if (autoUpdate == null) {
                return;
            }

            try {
                autoUpdate.Cooldown = cooldown;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateCooldownSuccess, projectName, cooldown));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateCooldownFail, projectName));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("mentions", "Changes whether the update notification pings all members")]
        public async Task DoPing([Summary("project", "The project")]string projectName,
            [Summary("channel", "The channel the notification is in")]ITextChannel channel,
            [Summary("mentions", "Whether to ping all members in the update notification")]bool doPing) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }
            
            var autoUpdate = await GetAutoUpdateAsync(project, channel);

            if (autoUpdate == null) {
                return;
            }

            try {
                autoUpdate.DoPing = doPing;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateDoPingSuccess, doPing));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateDoPingFail));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("trigger", "Triggers all update notifications of the project")]
        public async Task Trigger([Summary("project", "The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (!_fileHandler.ProjectBaseFileExists(Context.Guild, project.Name)) {
                await Context.Channel.SendMessageAsync(Strings.BaseFileNotExists);
                return;
            }

            try {
                await HandleAutoUpdates(project, Context, _context, _fileHandler);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateTriggerSuccess, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateTriggerFail, projectName));
            }
        }

        public static async Task HandleAutoUpdates(Project project, SocketInteractionContext context, OsuCollabContext _context, FileHandlingService fileHandler) {
            if (!fileHandler.ProjectBaseFileExists(context.Guild, project.Name)) {
                return;
            }
            
            // Maybe not use cooldown on the trigger command
            var updates = await _context.AutoUpdates.AsQueryable()
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

            await _context.SaveChangesAsync();
        }

        private async Task<Project> GetProjectAsync(string projectName) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            var project = await _context.Projects.AsQueryable().SingleOrDefaultAsync(o => o.GuildId == guild.Id && o.Name == projectName);

            if (project == null) {
                await Context.Channel.SendMessageAsync(Strings.ProjectNotExistMessage);
                return null;
            }

            return project;
        }

        private async Task<AutoUpdate> GetAutoUpdateAsync(Project project, ITextChannel channel) {
            var autoUpdate = await _context.AutoUpdates.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueChannelId == channel.Id);

            if (autoUpdate == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateNotExists, project.Name, channel.Mention));
                return null;
            }

            return autoUpdate;
        }
    }
}