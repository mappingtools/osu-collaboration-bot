using CollaborationBot.Autocomplete;
using CollaborationBot.Entities;
using CollaborationBot.Preconditions;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Interactions;
using Mapping_Tools_Core.BeatmapHelper;
using Mapping_Tools_Core.BeatmapHelper.Contexts;
using Mapping_Tools_Core.BeatmapHelper.IO.Decoding;
using Mapping_Tools_Core.BeatmapHelper.IO.Editor;
using Mapping_Tools_Core.Exceptions;
using Mapping_Tools_Core.MathUtil;
using Mapping_Tools_Core.Tools.PatternGallery;
using Microsoft.EntityFrameworkCore;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace CollaborationBot.Commands {
    [Group("", "All common commands")]
    public class TopModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly Random random = new();
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly UserHelpService _userHelpService;
        private readonly InputSanitizingService _inputSanitizer;
        private readonly AppSettings _appSettings;
        private readonly CommonService _common;
        private readonly DiscordSocketClient _client;

        public TopModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, UserHelpService userHelpService, InputSanitizingService inputSanitizingService,
            AppSettings appSettings, CommonService common, DiscordSocketClient client) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _userHelpService = userHelpService;
            _inputSanitizer = inputSanitizingService;
            _appSettings = appSettings;
            _common = common;
            _client = client;
        }

        [SlashCommand("help", "Shows command information")]
        public async Task Help(
            [Autocomplete(typeof(ModuleAutocompleteHandler))][Summary("module", "Look for a command in a specific module")]string module = "") {
            await _userHelpService.DoHelp(Context, module, "", string.IsNullOrEmpty(module));
        }

        #region guides

        [SlashCommand("adminguide", "Shows a guide for server admins on how to set-up the bot")]
        public async Task AdminGuide() {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            string title = Strings.AdminGuideTitle;
            string content = string.Format(Strings.AdminGuideContent, _appSettings.Prefix);

            embedBuilder.AddField(title, content);
            
            await RespondAsync(string.Empty, embed: embedBuilder.Build());
        }

        [SlashCommand("collabguide", "Shows a guide for collab organisers on how to set-up a collab with the bot")]
        public async Task CollabGuide() {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            string title = Strings.CollabGuideTitle;
            string content = string.Format(Strings.CollabGuideContent, _appSettings.Prefix);

            embedBuilder.AddField(title, content);
            
            await RespondAsync(string.Empty, embed: embedBuilder.Build());
        }

        [SlashCommand("participantguide", "Shows a guide for collab participants on how to use the bot")]
        public async Task ParticipantGuide(
            [Summary("project", "The name of the project to replace occurances of '[PROJECT NAME]' in the guide")]string projectName = null) {
            if (projectName != null && !_inputSanitizer.IsValidProjectName(projectName)) {
                await RespondAsync(string.Format(Strings.IllegalProjectName, projectName));
                return;
            }

            EmbedBuilder embedBuilder = new EmbedBuilder();

            // Make sure the project name is in between quotation marks if it contains spaces so members dont mess it up
            string projectNameEdit = projectName ?? "[PROJECT NAME]";
            if (projectNameEdit.Any(char.IsWhiteSpace)) {
                projectNameEdit = $"\"{projectNameEdit}\"";
            }

            string title = Strings.MemberGuideTitle;
            string content = string.Format(Strings.MemberGuideContent, _appSettings.Prefix, projectNameEdit);

            embedBuilder.AddField(title, content);
            
            await RespondAsync(string.Empty, embed: embedBuilder.Build());
        }

        #endregion

        #region project

        [SlashCommand("list", "Lists all the projects on the server and their status", runMode:RunMode.Async)]
        public async Task List() {
            var projects = await _context.Projects.AsQueryable().Where(p => p.Guild.UniqueGuildId == Context.Guild.Id).ToListAsync();

            await _resourceService.RespondPaginator(Context, projects, _resourceService.GenerateProjectListPages,
                Strings.NoProjects, Strings.ProjectListMessage);
        }

        [SlashCommand("info", "Shows general information of the project")]
        public async Task Info([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName) {
            // Name, description, owner, status, member count, part count, completion %, join allowed, self assignment allowed, priority picking,
            // part restricted upload, reminders, assignment lifetime, max assignments, main channel, info channel
            // Embed in role color
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var memberCount = await _context.Members.AsQueryable().Where(o => o.ProjectId == project.Id).CountAsync();
            var partCount = await _context.Parts.AsQueryable().Where(o => o.ProjectId == project.Id).CountAsync();
            var completedPartCount = await _context.Parts.AsQueryable().Where(o => o.ProjectId == project.Id && o.Status == PartStatus.Finished).CountAsync();
            var completionPercent = partCount <= 0 ? 0 : 100 * completedPartCount / partCount;
            var ownerMember = await _context.Members.AsQueryable().Where(o => o.ProjectId == project.Id && o.ProjectRole == ProjectRole.Owner).SingleOrDefaultAsync();
            var mainRole = project.UniqueRoleId.HasValue ? Context.Guild.GetRole((ulong)project.UniqueRoleId.Value) : null;
            var infoChannel = project.InfoChannelId.HasValue ? Context.Guild.GetTextChannel((ulong)project.InfoChannelId.Value) : null;
            var mainChannel = project.MainChannelId.HasValue ? Context.Guild.GetTextChannel((ulong)project.MainChannelId.Value) : null;

            var embed = new EmbedBuilder()
                .WithTitle(project.Name)
                .WithDescription(project.Description)
                .WithFields(
                    new EmbedFieldBuilder().WithName("Owner").WithValue($"<@{ownerMember.UniqueMemberId}>").WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Info").WithValue(infoChannel?.Mention ?? Strings.None).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Chat").WithValue(mainChannel?.Mention ?? Strings.None).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Status").WithValue(project.Status.HasValue ? project.Status : Strings.None).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Can join").WithValue(project.JoinAllowed).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Can claim").WithValue(project.SelfAssignmentAllowed).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Priority picking").WithValue(project.PriorityPicking).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Auto generate priorities").WithValue(project.AutoGeneratePriorities).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Restricted submission").WithValue(project.PartRestrictedUpload).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Reminders").WithValue(project.DoReminders).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Max claims").WithValue(project.MaxAssignments.HasValue ? project.MaxAssignments : Strings.Unbounded).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Max claim time").WithValue(project.MaxAssignmentTime.HasValue ? project.MaxAssignmentTime.Value.ToString() : Strings.Unbounded).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Claim lifetime").WithValue(project.AssignmentLifetime.HasValue ? project.AssignmentLifetime : Strings.Unbounded).WithIsInline(true),
                    new EmbedFieldBuilder().WithName("Last activity").WithValue(project.LastActivity.HasValue ? project.LastActivity.Value.ToString("yyyy-MM-dd") : Strings.None).WithIsInline(true)
                    )
                .WithFooter($"{memberCount} members {partCount} parts {completionPercent}% completed")
                .WithColor(mainRole?.Color ?? Color.Blue);
            embed.Color = mainRole?.Color;

            await RespondAsync(embed: embed.Build());
        }

        [SlashCommand("members", "Lists all members of the project", runMode:RunMode.Async)]
        public async Task Members([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var members = await _context.Members.AsQueryable()
                .Where(o => o.ProjectId == project.Id)
                .Include(o => o.Person)
                .ToListAsync();

            await _resourceService.RespondPaginator(Context, members, _resourceService.GenerateMembersListPages,
                Strings.NoMembers, Strings.MemberListMessage);
        }

        [SlashCommand("join", "Lets you become a member of a project which is looking for members")]
        public async Task JoinProject([Autocomplete(typeof(ProjectJoinAutocompleteHandler))][Summary("project", "The project")] string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            if (await _context.Members.AnyAsync(o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id)) {
                await ProjectModule.GrantProjectRole(Context, Context.User, project);
                await RespondAsync(Strings.AlreadyJoinedMessage);
                return;
            }

            if (!project.JoinAllowed) {
                await RespondAsync(Strings.NotLookingForMembers);
                return;
            }

            try {
                await ProjectModule.AddMemberInternal(_client.Rest, Context, _context, project, Context.User);
                await RespondAsync(
                    _resourceService.GenerateAddMemberToProject(Context.User, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateAddMemberToProject(Context.User, projectName, false));
            }
        }

        [SlashCommand("leave", "Lets you leave the project")]
        public async Task LeaveProject([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id);

            if (member == null) {
                await RespondAsync(Strings.NotJoinedMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await RespondAsync(Strings.OwnerCannotLeaveMessage);
                return;
            }

            try {
                _context.Members.Remove(member);
                await _context.SaveChangesAsync();
                await ProjectModule.RevokeProjectRole(Context, Context.User, project);
                await ProjectModule.RevokeManagerRole(Context, Context.User, project);
                await RespondAsync(
                    _resourceService.GenerateRemoveMemberFromProject(Context.User, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateRemoveMemberFromProject(Context.User, projectName, false));
            }
        }

        [SlashCommand("alias", "Changes your alias")]
        public async Task Alias([Summary("alias", "The new alias")] string alias) {
            if (!_inputSanitizer.IsValidName(alias)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            var person = await CommonService.GetPersonAsync(_context, _client.Rest, Context.User.Id);

            if (person == null) {
                return;
            }

            try {
                person.Alias = alias;

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ChangeAliasSuccess, Context.User.Mention, alias));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.ChangeAliasFail);
            }
        }

        [SlashCommand("tags", "Changes your tags")]
        public async Task Tags([Summary("tags", "The new tags")] string tags) {
            string tagsString = string.Join(' ', tags);

            if (!_inputSanitizer.IsValidName(tagsString)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            var person = await CommonService.GetPersonAsync(_context, _client.Rest, Context.User.Id);

            if (person == null) {
                return;
            }

            try {
                person.Tags = tagsString;

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ChangeTagsSuccess, Context.User.Mention, tagsString));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.ChangeTagsFail);
            }
        }

        [SlashCommand("id", "Changes your osu! profile ID")]
        public async Task Id([Summary("id", "The new ID")] string id) {
            int slashIndex = id.LastIndexOf('/');
            ulong id2;
            if (slashIndex < 0 ? ulong.TryParse(id, out id2) : ulong.TryParse(id.Substring(slashIndex + 1), out id2)) {
                var person = await CommonService.GetPersonAsync(_context, _client.Rest, Context.User.Id);

                if (person == null) {
                    return;
                }

                try {
                    person.ProfileId = id2;

                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.ChangeIdSuccess, Context.User.Mention, id2));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(Strings.ChangeIdFail);
                }
            } else {
                await RespondAsync(Strings.CouldNotParseInput);
            }
        }

        [SlashCommand("updateusername", "Updates your Discord username cache")]
        public async Task UpdateName() {
            var user = await _client.GetUserAsync(Context.User.Id);

            if (user == null) {
                await RespondAsync(Strings.BackendErrorMessage);
                return;
            }

            try {
                var person = await _context.People.AsQueryable().SingleOrDefaultAsync(o => o.UniqueMemberId == Context.User.Id);

                if (person == null) {
                    person = new Person {
                        UniqueMemberId = user.Id,
                        Username = user.Username,
                        GlobalName = user.GlobalName,
                    };
                    _context.People.Add(person);
                }
                else {
                    person.Username = user.Username;
                    person.GlobalName = user.GlobalName;
                }

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.UpdateNameSuccess, user.Username, user.GlobalName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.UpdateNameFail);
            }
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("updateall", "Updates all your Discord username cache in a project")]
        public async Task UpdateName([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            try {
                var members = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id)
                    .Include(o => o.Person)
                    .ToListAsync();

                foreach (var member in members) {
                    var person = member.Person;
                    var user = await _client.GetUserAsync((ulong)member.UniqueMemberId);

                    if (person == null) {
                        person = new Person {
                            UniqueMemberId = user.Id,
                            Username = user.Username,
                            GlobalName = user.GlobalName,
                        };
                        _context.People.Add(person);
                    }
                    else {
                        person.Username = user.Username;
                        person.GlobalName = user.GlobalName;
                    }
                }

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.UpdateNameSuccess, "everyone", "everyone's global name"));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.UpdateNameFail);
            }
        }

        [SlashCommand("submit", "Submits a part of beatmap to the project")]
        public async Task SubmitPart([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Summary("beatmap", "The part to submit as a .osu file")] Attachment attachment,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("part", "The part name to submit to (optional). Will overwrite the entire part including endpoints.")] string partName = null) {
            // Find out which parts this member is allowed to edit in the project
            // Download the attached file and put it in the member's folder
            // Merge it into the base file
            // Success message

            if (attachment == null) {
                await RespondAsync(Strings.NoAttachedFile);
                return;
            }

            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, Context.User);

            if (member == null) {
                return;
            }

            if (!_fileHandler.ProjectBaseFileExists(Context.Guild, project.Name)) {
                await RespondAsync(Strings.BaseFileNotExists);
                return;
            }

            List<Part> parts = null;
            bool partNameProvided = partName is not null;
            bool partIsRestricted = project.PartRestrictedUpload || partNameProvided;
            if (partIsRestricted) {
                if (!partNameProvided) {
                    // Submit to claimed part
                    parts = await _context.Assignments.AsQueryable()
                        .Where(o => o.Part.ProjectId == project.Id && o.Member.UniqueMemberId == Context.User.Id)
                        .Select(o => o.Part)
                        .ToListAsync();
                } else if (member.ProjectRole == ProjectRole.Member && project.PartRestrictedUpload) {
                    // Member submit to specific claimed part
                    parts = await _context.Assignments.AsQueryable()
                        .Where(o => o.Part.ProjectId == project.Id && o.Member.Id == member.Id &&
                                    o.Part.Name == partName)
                        .Select(o => o.Part)
                        .ToListAsync();
                } else {
                    // Manager submit override
                    // OR no part restricted upload with part name provided by member
                    parts = await _context.Parts.AsQueryable()
                        .Where(o => o.ProjectId == project.Id && o.Name == partName)
                        .ToListAsync();
                }

                if (parts.Count == 0) {
                    await RespondAsync(Strings.NoPartsToSubmit);
                    return;
                }
            }

            string beatmapString = await _fileHandler.DownloadPartSubmit(Context.Guild, projectName, attachment);

            if (beatmapString == null) {
                await RespondAsync(Strings.AttachedFileInvalid);
                return;
            }

            try {
                var partBeatmap = new OsuBeatmapDecoder().Decode(beatmapString);

                if (partIsRestricted) {
                    // Restrict beatmap to only the hit objects inside any assigned part
                    partBeatmap.HitObjects = partBeatmap.HitObjects
                        .Where(ho => parts!.Any(p =>
                            p.Status != PartStatus.Locked &&
                            (ho.StartTime >= p.Start || !p.Start.HasValue) &&
                            (ho.StartTime <= p.End || !p.End.HasValue) &&
                            (ho.EndTime >= p.Start || !p.Start.HasValue) &&
                            (ho.EndTime <= p.End || !p.End.HasValue)))
                        .ToList();
                }

                var count = partBeatmap.HitObjects.Count;

                if (count == 0) {
                    await RespondAsync(Strings.SubmitNoHitObjects);
                    return;
                }

                var editor = new BeatmapEditor(_fileHandler.GetProjectBaseFilePath(Context.Guild, projectName));
                var beatmap = editor.ReadFile();

                // Check the global SV and stack leniency and warn the user if problems arise
                double svFactor = partBeatmap.Difficulty.SliderMultiplier / beatmap.Difficulty.SliderMultiplier;
                if (!Precision.AlmostEquals(svFactor, 1) &&
                    partBeatmap.HitObjects.Any(o => {
                        var newSV = svFactor * MathHelper.Clamp(o.GetContext<TimingContext>().SliderVelocity, 0.1, 10);
                        return double.IsNaN(newSV) ||
                               Precision.DefinitelySmaller(newSV, 0.1) ||
                               Precision.DefinitelyBigger(newSV, 10);
                    })) {
                    await RespondAsync(string.Format(Strings.GlobalSVMismatchWarning));
                }

                if (!Precision.AlmostEquals(partBeatmap.General.StackLeniency, beatmap.General.StackLeniency)) {
                    await RespondAsync(string.Format(Strings.StackLeniencyMismatchWarning));
                }

                // Merge the part and save
                var placer = new OsuPatternPlacer {
                    PatternOverwriteMode = PatternOverwriteMode.PartitionedOverwrite,
                    TimingOverwriteMode = TimingOverwriteMode.PatternTimingOnly,
                    Padding = 0,
                    PartingDistance = 4,
                    FixColourHax = true,
                    FixBpmSv = false,
                    FixStackLeniency = false,
                    FixTickRate = false,
                    FixGlobalSv = true,
                    SnapToNewTiming = false,
                    ScaleToNewTiming = false,
                    IncludeHitsounds = true,
                    IncludeKiai = true,
                    ScaleToNewCircleSize = false,
                };

                if (partNameProvided) {
                    placer.PlaceOsuPattern(
                        partBeatmap,
                        beatmap,
                        overwriteStartTime: parts.FirstOrDefault()?.Start,
                        overwriteEndTime: parts.FirstOrDefault()?.End
                        );
                } else {
                    placer.PlaceOsuPattern(partBeatmap, beatmap);
                }

                // Fix break periods
                beatmap.FixBreakPeriods();

                editor.WriteFile(beatmap);

                await RespondAsync(_resourceService.GenerateSubmitPartMessage(projectName, count, true));
            } catch (BeatmapParsingException e) {
                await RespondAsync(string.Format(Strings.BeatmapParseFail, e.Message));
                return;
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(_resourceService.GenerateSubmitPartMessage(projectName, 0, false));
                return;
            }

            // Reset the activity timer
            project.LastActivity = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Handle auto-updates
            await AutoUpdateModule.HandleAutoUpdates(project, Context, _context, _fileHandler);
        }

        #endregion

        #region assignments

        [SlashCommand("claim", "Claims one or more parts and assigns them to you")]
        public async Task Claim([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("parts", "The parts to claim")] params string[] partNames) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, Context.User);

            if (member == null) {
                return;
            }

            foreach (var partName in partNames) {
                var part = await _common.GetPartAsync(Context, _context, project, partName);

                if (part == null) {
                    return;
                }

                // Check project permissions
                if (!await CheckClaimPermissionsAsync(project, member, part)) {
                    return;
                }

                try {
                    var claimants = await _context.Assignments.AsQueryable()
                        .Where(o => o.Part.Id == part.Id && o.MemberId != member.Id)
                        .Include(o => o.Member).ToListAsync();
                    if (claimants.Count > 0) {
                        if (!project.PriorityPicking) {
                            await RespondAsync(Strings.PartClaimedAlready);
                            return;
                        }

                        // Perhaps steal parts
                        if (claimants.All(o => o.Member.Priority < member.Priority)) {
                            // EZ steal
                            _context.Assignments.RemoveRange(claimants);

                            // Notify theft
                            foreach (var victim in claimants) {
                                var victimUser = await _client.GetUserAsync((ulong)victim.Member.UniqueMemberId);
                                var victimDm = await victimUser.CreateDMChannelAsync();
                                await victimDm.SendMessageAsync(string.Format(Strings.PriorityPartSteal,
                                    Context.User.Username, member.Priority, part.Name,
                                    victim.Member.Priority));
                            }
                        } else {
                            // Sorry you can't steal this
                            await RespondAsync(Strings.PartClaimedAlready);
                            return;
                        }
                    }

                    var deadline = DateTime.UtcNow + project.AssignmentLifetime;
                    await _context.Assignments.AddAsync(new Assignment { MemberId = member.Id, PartId = part.Id, Deadline = deadline, LastReminder = DateTime.UtcNow });
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.AddAssignmentSuccess, part.Name, Context.User.Username));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.AddAssignmentFail, part.Name, Context.User.Username));
                }
            }
        }

        private async Task<bool> CheckClaimPermissionsAsync(Project project, Member member, Part part) {
            // Manager and owner can always claim parts
            if (member.ProjectRole != ProjectRole.Member) {
                return true;
            }

            if (!project.SelfAssignmentAllowed) {
                await RespondAsync(Strings.SelfAssignmentNotAllowed);
                return false;
            }

            if (project.MaxAssignments.HasValue) {
                // Count the number of total assignments (parts claimed)
                int assignments = await _context.Assignments.AsQueryable()
                    .CountAsync(o => o.MemberId == member.Id && o.Part.ProjectId == project.Id);

                if (assignments >= project.MaxAssignments) {
                    await RespondAsync(string.Format(Strings.MaxAssignmentsReached, project.MaxAssignments));
                    return false;
                }
            }

            if (project.MaxAssignmentTime.HasValue) {
                // Count the aggregate time of all claimed parts
                int totalTimeMs = await _context.Assignments.AsQueryable()
                    .Where(o => o.MemberId == member.Id && o.Part.ProjectId == project.Id && o.Part.End.HasValue && o.Part.Start.HasValue)
                    .SumAsync(o => o.Part.End.Value - o.Part.Start.Value);

                if (part.Start.HasValue && part.End.HasValue)
                    totalTimeMs += part.End.Value - part.Start.Value;

                if (totalTimeMs > project.MaxAssignmentTime.Value.TotalMilliseconds) {
                    await RespondAsync(string.Format(Strings.MaxAssignmentTimeReached, project.MaxAssignmentTime.Value));
                    return false;
                }
            }

            return true;
        }

        [SlashCommand("unclaim", "Unclaims one or more parts and unassigns them")]
        public async Task Unclaim([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("parts", "The parts to unclaim")] params string[] partNames) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            foreach (string partName in partNames) {
                var assignment = await _common.GetAssignmentAsync(Context, _context, project, partName, Context.User);

                if (assignment == null) {
                    return;
                }

                try {
                    _context.Assignments.Remove(assignment);
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.RemoveAssignmentSuccess, Context.User.Username));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.RemoveAssignmentFail, Context.User.Username));
                }
            }

            // Try to remove the unclaimed parts from the base file
            if (!_fileHandler.ProjectBaseFileExists(Context.Guild, project.Name)) return;

            // Get all the parts that still have claims or are locked
            var claimedParts = await _context.Assignments.AsQueryable()
                .Where(o => o.Part.ProjectId == project.Id)
                .Select(o => o.Part)
                .Distinct()
                .Union(_context.Parts.AsQueryable()
                    .Where(o => o.ProjectId == project.Id && o.Status == PartStatus.Locked))
                .ToListAsync();

            var editor = new BeatmapEditor(_fileHandler.GetProjectBaseFilePath(Context.Guild, projectName));
            var beatmap = editor.ReadFile();
            int removedCount = 0;

            // Remove all hit objects that are not (partially) in the claimed parts
            foreach (var hitObject in beatmap.HitObjects.ToList()) {
                if (claimedParts.Any(p =>
                        (hitObject.EndTime >= p.Start - 5 || !p.Start.HasValue) &&
                        (hitObject.StartTime <= p.End + 5 || !p.End.HasValue))) {
                    continue;
                }

                beatmap.HitObjects.Remove(hitObject);
                removedCount++;
            }

            if (removedCount == 0) return;

            // Fix break periods
            beatmap.FixBreakPeriods();

            editor.WriteFile(beatmap);

            // Reset the activity timer
            project.LastActivity = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Handle auto-updates
            await AutoUpdateModule.HandleAutoUpdates(project, Context, _context, _fileHandler);
        }

        [SlashCommand("done", "Marks one or more parts as done")]
        public async Task Done([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("parts", "The parts to complete")] params string[] partNames) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, Context.User);

            if (member == null) {
                return;
            }

            foreach (var partName in partNames) {
                var part = await _common.GetPartAsync(Context, _context, project, partName);

                if (part == null) {
                    return;
                }

                var assignments = await _context.Assignments.AsQueryable()
                    .Where(o => o.PartId == part.Id && o.MemberId == member.Id).ToListAsync();

                if (member.ProjectRole == ProjectRole.Member && assignments.All(o => o.MemberId != member.Id)) {
                    await RespondAsync(Strings.NotAssigned);
                    return;
                }

                try {
                    assignments.ForEach(o => o.Deadline = null);
                    part.Status = PartStatus.Finished;
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.FinishPartSuccess, part.Name));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.FinishPartFail));
                }
            }
        }

        #endregion

        #region misc

        private static readonly int[] wordCounts = { 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 10 };

        [SlashCommand("diffname", "Generates a random difficulty name")]
        public async Task Diffname([Summary("wordcount", "The number of words to use in the sentence")][MinValue(1)][MaxValue(200)]int wordCount = -1) {
            await DoRandomString(@"CollaborationBot.Resources.Diffname Words.txt", wordCount, 0.02);
        }

        [SlashCommand("blixys", "Generates some inspiration")]
        public async Task Blixys([Summary("wordcount", "The number of words to use in the sentence")][MinValue(1)][MaxValue(200)]int wordCount=-1) {
            await DoRandomString(@"CollaborationBot.Resources.blixys.txt", wordCount, 0.05);
        }

        private async Task DoRandomString(string resourceName, int wordCount=-1, double mixChance=0) {
            List<string> words = new List<string>();
            try {
                var assembly = Assembly.GetExecutingAssembly();

                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                using StreamReader reader = new StreamReader(stream);
                while (true) {
                    string word = await reader.ReadLineAsync();
                    if (word is null) break;
                    words.Add(word.Trim());
                }
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.DiffnameLoadFail);
                return;
            }

            int nWords = wordCount > 0 && wordCount <= 200 ? wordCount : wordCounts[random.Next(wordCounts.Length - 1)];
            StringBuilder diffname = new StringBuilder();
            for (int i = 0; i < nWords; i++) {
                if (i != 0)
                    diffname.Append(' ');
                if (random.NextDouble() < mixChance) {
                    string word1 = words[random.Next(words.Count)];
                    string word2 = words[random.Next(words.Count)];
                    int sp1 = random.Next(Math.Min(word1.Length, 3), word1.Length);
                    int sp2 = random.Next(0, Math.Max(0, word2.Length - 3));
                    diffname.Append(word1[..sp1]);
                    diffname.Append(word2[sp2..]);
                } else {
                    diffname.Append(words[random.Next(words.Count)]);
                }
            }

            await RespondAsync(diffname.ToString());
        }

        //[SlashCommand("collage", "Generates a collage with images from a channel")]
        public async Task Collage([Summary("channel", "The channel to get the images from")]ITextChannel channel,
            [Summary("count", "The number of messages to use in the collage")] int messageCount = 100) {
            messageCount = Math.Min(messageCount, 200);

            var messages = channel.GetMessagesAsync(messageCount, CacheMode.AllowDownload);

            var zip = ZipFile.Open("temp.zip", ZipArchiveMode.Create);
            var mss = new StringBuilder();
            await foreach (var ms in messages) {
                foreach (var m in ms) {
                    foreach (var a in m.Attachments) {
                        if (!(Path.GetExtension(a.Filename) == ".png" || Path.GetExtension(a.Filename) == ".jpg")) continue;
                        mss.AppendLine(a.Filename);

                        if (!Uri.TryCreate(a.Url, UriKind.Absolute, out var uri)) continue;

                        var name = m.Content + Path.GetExtension(a.Filename);
                        var tempname = "temp" + Path.GetExtension(a.Filename);

                        using var client = new HttpClient();
                        var response = await client.GetAsync(uri);
                        using (var fs = new FileStream(tempname, FileMode.CreateNew)) {
                            await response.Content.CopyToAsync(fs);
                        }

                        zip.CreateEntryFromFile(tempname, name, CompressionLevel.Optimal);
                    }
                }
            }

            zip.Dispose();
            await RespondWithFileAsync("temp.zip", text: mss.ToString());
        }

        #endregion
    }
}