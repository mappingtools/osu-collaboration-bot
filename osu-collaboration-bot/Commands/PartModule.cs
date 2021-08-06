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

namespace CollaborationBot.Commands {
    [Group("part")]
    public class PartModule : ModuleBase<SocketCommandContext> {
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public PartModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("list")]
        public async Task List(string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var parts = await _context.Parts.AsQueryable()
                .Where(o => o.ProjectId == project.Id)
                .Include(o => o.Assignments)
                .ThenInclude(o => o.Member)
                .ToListAsync();

            parts.Sort();

            await Context.Channel.SendMessageAsync(_resourceService.GeneratePartsListMessage(parts));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("add")]
        public async Task Add(string projectName, string name, TimeSpan? start, TimeSpan? end, PartStatus status = PartStatus.NotFinished) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                int? intStart = start.HasValue ? (int)start.Value.TotalMilliseconds : null;
                int? intEnd = end.HasValue ? (int)end.Value.TotalMilliseconds : null;
                await _context.Parts.AddAsync(new Part { ProjectId = project.Id, Name = name, Start = intStart, End = intEnd, Status = status });
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.AddPartSuccess, name, projectName));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AddPartFail, name, projectName));
            }
        }

        #region edit

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("rename")]
        public async Task Rename(string projectName, string name, string newName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == name);

            if (part == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, name, projectName));
                return;
            }

            try {
                part.Name = newName;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.EditPartSuccess));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.EditPartFail));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("start")]
        public async Task Start(string projectName, string name, TimeSpan? start) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == name);

            if (part == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, name, projectName));
                return;
            }

            try {
                part.Start = start.HasValue ? (int)start.Value.TotalMilliseconds : null;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.EditPartSuccess));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.EditPartFail));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("end")]
        public async Task End(string projectName, string name, TimeSpan? end) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == name);

            if (part == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, name, projectName));
                return;
            }

            try {
                part.End = end.HasValue ? (int)end.Value.TotalMilliseconds : null;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.EditPartSuccess));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.EditPartFail));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("status")]
        public async Task Status(string projectName, string name, PartStatus status) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == name);

            if (part == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, name, projectName));
                return;
            }

            try {
                part.Status = status;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.EditPartSuccess));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.EditPartFail));
            }
        }

        #endregion

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("remove")]
        public async Task Remove(string projectName, params string[] partNames) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            foreach (var partName in partNames) {
                var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

                if (part == null) {
                    await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, partName, projectName));
                    return;
                }

                try {
                    _context.Parts.Remove(part);
                    await _context.SaveChangesAsync();
                    await Context.Channel.SendMessageAsync(string.Format(Strings.RemovePartSuccess, partName, projectName));
                } catch (Exception e) {
                    Console.WriteLine(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.RemovePartFail, partName, projectName));
                }
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("clear")]
        public async Task Clear(string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var parts = await _context.Parts.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

            try {
                _context.Parts.RemoveRange(parts);
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.MultiRemovePartSuccess, parts.Count, projectName));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.MultiRemovePartFail, projectName));
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
    }
}