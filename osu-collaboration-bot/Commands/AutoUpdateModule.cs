using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using Discord;
using CollaborationBot.Preconditions;
using System;
using System.Collections.Generic;

namespace CollaborationBot.Commands {
    [Group("au")]
    public class AutoUpdateModule : ModuleBase<SocketCommandContext> {
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public AutoUpdateModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("list")]
        public async Task List(string projectName) {
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
        [Command("add")]
        public async Task Add(string projectName, ITextChannel channel, TimeSpan? cooldown = null, bool doPing = false) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AddAutoUpdateFail, projectName, channel.Mention));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("remove")]
        public async Task Remove(string projectName, ITextChannel channel) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.RemoveAutoUpdateFail, projectName, channel.Mention));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("trigger")]
        public async Task Trigger(string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                await HandleAutoUpdates(project, Context, _context, _fileHandler);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateTriggerSuccess, projectName));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoUpdateTriggerFail, projectName));
            }
        }

        public static async Task HandleAutoUpdates(Project project, SocketCommandContext context, OsuCollabContext _context, FileHandlingService fileHandler) {
            // TODO: Add last use time and check cooldown
            var updates = await _context.AutoUpdates.AsQueryable()
                .Where(o => o.ProjectId == project.Id)
                .ToListAsync();

            foreach (var autoUpdate in updates) {
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
            }
        }

        private async Task<Project> GetProjectAsync(string projectName) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(_resourceService.GuildNotExistsMessage);
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