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
using System.Globalization;
using System.IO;
using CsvHelper;

namespace CollaborationBot.Commands {
    [Group("part")]
    [Name("Part module")]
    [Summary("Everything about parts")]
    public class PartModule : ModuleBase<SocketCommandContext> {
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly UserHelpService _userHelpService;

        public PartModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, UserHelpService userHelpService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _userHelpService = userHelpService;
        }

        [Command("help")]
        [Summary("Shows command information")]
        public async Task Help(string command = "") {
            await _userHelpService.DoHelp(Context, "Part module", "part", command);
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("list")]
        [Summary("Lists all the parts of the project")]
        public async Task List([Summary("The project")]string projectName) {
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
        [Summary("Adds a new part to the project")]
        public async Task Add([Summary("The project")]string projectName,
            [Summary("The name of the part")]string name,
            [Summary("The start time (can be null)")]TimeSpan? start,
            [Summary("The end time (can be null)")]TimeSpan? end,
            [Summary("The status of the part")]PartStatus status = PartStatus.NotFinished) {
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
        [Summary("Changes the name of the part")]
        public async Task Rename([Summary("The project")]string projectName,
            [Summary("The part")]string name,
            [Summary("The new name for the part")]string newName) {
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
        [Summary("Changes the start time of the part")]
        public async Task Start([Summary("The project")]string projectName,
            [Summary("The part")]string name, 
            [Summary("The new start time (can be null)")]TimeSpan? start) {
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
        [Summary("Changes the end time of the part")]
        public async Task End([Summary("The project")]string projectName,
            [Summary("The part")]string name,
            [Summary("The new end time (can be null)")]TimeSpan? end) {
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
        [Summary("Changes the status of the part")]
        public async Task Status([Summary("The project")]string projectName,
            [Summary("The part")]string name,
            [Summary("The new status")]PartStatus status) {
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
        [Summary("Removes one or more parts from the project")]
        public async Task Remove([Summary("The project")]string projectName,
            [Summary("The parts to remove")]params string[] partNames) {
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
        [Summary("Removes all parts from the project")]
        public async Task Clear([Summary("The project")]string projectName) {
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

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("from-csv")]
        [Summary("Imports parts from a CSV file")]
        public async Task FromCSV([Summary("The project")]string projectName,
            [Summary("Whether the CSV file has explicit headers")]bool hasHeaders = true,
            [Summary("Whether to clear the existing parts before importing")]bool replace = true) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var attachment = Context.Message.Attachments.SingleOrDefault();

            if (attachment == null) {
                await Context.Channel.SendMessageAsync(Strings.NoAttachedFile);
                return;
            }

            var newParts = await _fileHandler.DownloadPartsCSV(attachment, hasHeaders);

            if (newParts == null) {
                await Context.Channel.SendMessageAsync(Strings.CouldNotReadPartCSV);
                return;
            }

            try {
                var oldParts = await _context.Parts.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

                if (replace)
                    _context.Parts.RemoveRange(oldParts);

                _context.Parts.AddRange(newParts.Select(o => new Part
                    {ProjectId = project.Id, Name = o.Name, Start = o.Start, End = o.End, Status = o.Status}));
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.PartFromCSVSuccess, projectName));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.PartFromCSVFail, projectName));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("to-csv")]
        [Summary("Exports all parts of the project to a CSV file")]
        public async Task ToCSV([Summary("The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var parts = await _context.Parts.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

                await using var dataStream = new MemoryStream();
                await using (var writer = new StreamWriter(dataStream))
                await using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture)) {
                    await csv.WriteRecordsAsync(parts.Select(o => new FileHandlingService.PartRecord
                        {Name = o.Name, Start = o.Start, End = o.End, Status = o.Status}));
                }

                await Context.Channel.SendFileAsync(dataStream, string.Format(Strings.PartToCSVSuccess, projectName));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.PartToCSVFail, projectName));
            }
        }

        private async Task<Project> GetProjectAsync(string projectName) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(Strings.GuildNotExistsMessage);
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