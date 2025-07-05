using CollaborationBot.Entities;
using CollaborationBot.Preconditions;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using CsvHelper;
using Discord;
using Discord.Interactions;
using Mapping_Tools_Core.BeatmapHelper.IO.Decoding;
using Mapping_Tools_Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CollaborationBot.Autocomplete;

namespace CollaborationBot.Commands {
    [Group("part", "Everything about parts")]
    public class PartModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly InputSanitizingService _inputSanitizer;
        private readonly AppSettings _appSettings;
        private readonly CommonService _common;

        public PartModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, InputSanitizingService inputSanitizingService,
            AppSettings appSettings, CommonService common) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _inputSanitizer = inputSanitizingService;
            _appSettings = appSettings;
            _common = common;
        }
        
        [SlashCommand("list", "Lists all the parts of the project", runMode:RunMode.Async)]
        public async Task List([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var parts = await _context.Parts.AsQueryable()
                .Where(o => o.ProjectId == project.Id)
                .Include(o => o.Assignments)
                .ThenInclude(o => o.Member)
                .ToListAsync();

            parts.Sort();

            await _resourceService.RespondPaginator(Context, parts, _resourceService.GeneratePartsListPages,
                Strings.NoParts, Strings.PartListMessage);
        }
        
        [SlashCommand("listclaimable", "Lists all the claimable parts of the project", runMode:RunMode.Async)]
        public async Task ListClaimable([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, Context.User);

            List<Part> parts;
            if (project.PriorityPicking && member is { Priority: not null }) {
                // Include parts which are claimed but can be stolen because you have higher priority
                parts = await _context.Parts.AsQueryable()
                    .Where(o => o.ProjectId == project.Id && o.Assignments.All(a => a.Member.Priority < member.Priority))
                    .Include(o => o.Assignments)
                    .ThenInclude(o => o.Member)
                    .ToListAsync();
            } else {
                parts = await _context.Parts.AsQueryable()
                    .Where(o => o.ProjectId == project.Id && o.Assignments.Count == 0)
                    .Include(o => o.Assignments)
                    .ThenInclude(o => o.Member)
                    .ToListAsync();
            }

            parts.Sort();

            await _resourceService.RespondPaginator(Context, parts, _resourceService.GeneratePartsListPages,
                Strings.NoParts, Strings.PartListClaimableMessage);
        }
        
        [SlashCommand("add", "Adds a new part to the project")]
        public async Task Add([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("name", "The name of the part")]string name,
            [Summary("start", "The start time (can be null)")]TimeSpan? start = null,
            [Summary("end", "The end time (can be null)")]TimeSpan? end = null,
            [Summary("status", "The status of the part")]PartStatus status = PartStatus.NotFinished) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            if (!_inputSanitizer.IsValidName(name)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            try {
                int? intStart = start.HasValue ? (int)start.Value.TotalMilliseconds : null;
                int? intEnd = end.HasValue ? (int)end.Value.TotalMilliseconds : null;
                await _context.Parts.AddAsync(new Part { ProjectId = project.Id, Name = name, Start = intStart, End = intEnd, Status = status });
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.AddPartSuccess, name, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.AddPartFail, name, projectName));
            }
        }

        #region edit
        
        [SlashCommand("rename", "Changes the name of the part")]
        public async Task Rename([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("part", "The part")]string name,
            [Summary("newname", "The new name for the part")]string newName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            if (!_inputSanitizer.IsValidName(newName)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            var part = await _common.GetPartAsync(Context, _context, project, name);

            if (part == null) {
                return;
            }

            try {
                part.Name = newName;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.EditPartSuccess));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.EditPartFail));
            }
        }

        [SlashCommand("start", "Changes the start time of the part")]
        public async Task Start([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("part", "The part")]string name,
            [Summary("start", "The new start time (can be null)")]TimeSpan? start = null) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var part = await _common.GetPartAsync(Context, _context, project, name);

            if (part == null) {
                return;
            }

            try {
                part.Start = start.HasValue ? (int)start.Value.TotalMilliseconds : null;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.EditPartSuccess));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.EditPartFail));
            }
        }
        
        [SlashCommand("end", "Changes the end time of the part")]
        public async Task End([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("part", "The part")]string name,
            [Summary("end", "The new end time (can be null)")]TimeSpan? end = null) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var part = await _common.GetPartAsync(Context, _context, project, name);

            if (part == null) {
                return;
            }

            try {
                part.End = end.HasValue ? (int)end.Value.TotalMilliseconds : null;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.EditPartSuccess));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.EditPartFail));
            }
        }

        [SlashCommand("status", "Changes the status of the part")]
        public async Task Status([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("part", "The part")]string name,
            [Summary("status", "The new status")]PartStatus status) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var part = await _common.GetPartAsync(Context, _context, project, name);

            if (part == null) {
                return;
            }

            try {
                part.Status = status;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.EditPartSuccess));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.EditPartFail));
            }
        }

        #endregion

        [SlashCommand("remove", "Removes one or more parts from the project")]
        public async Task Remove([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("parts", "The parts to remove")]params string[] partNames) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            foreach (var partName in partNames) {
                var part = await _common.GetPartAsync(Context, _context, project, partName);

                if (part == null) {
                    return;
                }

                try {
                    _context.Parts.Remove(part);
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.RemovePartSuccess, part.Name, projectName));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.RemovePartFail, part.Name, projectName));
                }
            }
        }
        
        [SlashCommand("clear", "Removes all parts from the project")]
        public async Task Clear([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var parts = await _context.Parts.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

            try {
                _context.Parts.RemoveRange(parts);
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.MultiRemovePartSuccess, parts.Count, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.MultiRemovePartFail, projectName));
            }
        }

        [SlashCommand("frombookmarks", "Imports parts from a beatmap's bookmarks")]
        public async Task FromBookmarks([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Summary("beatmap", "The beatmap .osu to import bookmarks from")]Attachment attachment,
            [Summary("hasstart", "Whether there is a bookmark indicating the start of the first part")] bool hasStart = true,
            [Summary("hasend", "Whether there is a bookmark indicating the end of the last part")] bool hasEnd = false,
            [Summary("replace", "Whether to clear the existing parts before importing")] bool replace = true) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            if (attachment == null) {
                await RespondAsync(Strings.NoAttachedFile);
                return;
            }

            string beatmapString = await _fileHandler.DownloadPartSubmit(Context.Guild, projectName, attachment);

            if (beatmapString == null) {
                await RespondAsync(Strings.AttachedFileInvalid);
                return;
            }

            var newParts = new List<Part>();
            try {
                var beatmap = new OsuBeatmapDecoder().Decode(beatmapString);
                var bookmarks = beatmap.Editor.Bookmarks;
                var count = bookmarks.Count;

                if (count == 0) {
                    await RespondAsync(Strings.NoBookmarksFound);
                    return;
                }

                int? start = null;
                int partCount = 0;

                for (int i = 0; i < count; i++){
                    int b = (int)bookmarks[i];
                    int? end = b;

                    if (i != 0 || !hasStart) {
                        newParts.Add(new Part {
                            ProjectId = project.Id,
                            Name = $"part{++partCount}",
                            Start = start,
                            End = end,
                            Status = PartStatus.NotFinished
                        });
                    }

                    start = b;

                    if (i == count -1 && !hasEnd) {
                        newParts.Add(new Part {
                            ProjectId = project.Id,
                            Name = $"part{++partCount}",
                            Start = start,
                            End = null,
                            Status = PartStatus.NotFinished
                        });
                    }
                }
            } catch (BeatmapParsingException e) {
                await RespondAsync(string.Format(Strings.BeatmapParseFail, e.Message));
                return;
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.PartFromBookmarkFail);
                return;
            }

            if (newParts.Any(o => !_inputSanitizer.IsValidName(o.Name))) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            try {
                var oldParts = await _context.Parts.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

                if (replace)
                    _context.Parts.RemoveRange(oldParts);

                _context.Parts.AddRange(newParts);
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.PartFromBookmarkSuccess, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.PartFromBookmarkFail));
            }
        }

        [SlashCommand("fromcsv", "Imports parts from a CSV file")]
        public async Task FromCSV([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("file", "The .csv file to import parts from")]Attachment attachment,
            [Summary("hasheaders", "Whether the CSV file has explicit headers")]bool hasHeaders = true,
            [Summary("replace", "Whether to clear the existing parts before importing")]bool replace = true) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            if (attachment == null) {
                await RespondAsync(Strings.NoAttachedFile);
                return;
            }

            var newParts = await _fileHandler.DownloadPartsCSV(attachment, hasHeaders);

            if (newParts == null) {
                await RespondAsync(Strings.CouldNotReadPartCSV);
                return;
            }

            if (newParts.Any(o => !_inputSanitizer.IsValidName(o.Name))) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            try {
                var oldParts = await _context.Parts.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

                if (replace)
                    _context.Parts.RemoveRange(oldParts);

                _context.Parts.AddRange(newParts.Select(o => new Part
                    {ProjectId = project.Id, Name = o.Name, Start = o.Start, End = o.End, Status = o.Status}));
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.PartFromCSVSuccess, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.PartFromCSVFail, projectName));
            }
        }

        [SlashCommand("tocsv", "Exports all parts of the project to a CSV file")]
        public async Task ToCSV([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("includemappers", "Whether to include columns showing the mappers assigned to each part")]bool includeMappers=false) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            try {
                var parts = await _context.Parts.AsQueryable()
                    .Where(o => o.ProjectId == project.Id)
                    .Include(o => o.Assignments)
                    .ThenInclude(o => o.Member).ToListAsync();

                parts.Sort();

                async Task<FileHandlingService.PartRecord> selector(Part o) => new() {
                    Name = o.Name,
                    Start = o.Start,
                    End = o.End,
                    Status = o.Status,
                    MapperNames = includeMappers ? string.Join(";", await Task.WhenAll(o.Assignments.Select(async a => await _resourceService.MemberAliasOrName(a.Member)))) : null
                };

                await using var dataStream = new MemoryStream();
                var writer = new StreamWriter(dataStream);
                await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                await csv.WriteRecordsAsync(await Task.WhenAll(parts.Select(selector)));

                await writer.FlushAsync();
                dataStream.Position = 0;

                await RespondWithFileAsync(dataStream, projectName + "_parts.csv",
                    string.Format(Strings.PartToCSVSuccess, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.PartToCSVFail, projectName));
            }
        }

        [SlashCommand("todescription", "Generates an element with all the parts which you can add to your beatmap description.")]
        public async Task ToDesc([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Summary("includemappers", "Whether to show the mappers assigned to each part")] bool includeMappers = true,
            [Summary("includepartnames", "Whether to show the name of each part")] bool includePartNames = false) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            try {
                var parts = await _context.Parts.AsQueryable()
                    .Where(o => o.ProjectId == project.Id)
                    .Include(o => o.Assignments)
                    .ThenInclude(o => o.Member).ToListAsync();

                parts.Sort();

                await _resourceService.RespondTextOrFile(Context.Interaction,
                    await _resourceService.GeneratePartsListDescription(parts, includeMappers, includePartNames));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.BackendErrorMessage, projectName));
            }
        }
    }
}