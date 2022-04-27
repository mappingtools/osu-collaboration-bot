using CollaborationBot.Entities;
using CollaborationBot.Preconditions;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Interactions;
using Mapping_Tools_Core.BeatmapHelper.IO.Decoding;
using Mapping_Tools_Core.BeatmapHelper.IO.Editor;
using Mapping_Tools_Core.Exceptions;
using Mapping_Tools_Core.Tools.PatternGallery;
using Microsoft.EntityFrameworkCore;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mapping_Tools_Core.BeatmapHelper.Contexts;
using Mapping_Tools_Core.MathUtil;
using System.IO.Compression;
using CollaborationBot.Autocomplete;
using Mapping_Tools_Core.BeatmapHelper;
using System.Net.Http;

namespace CollaborationBot.Commands {
    //[Name("Project module")]
    //[Summary("Main module with project and member related stuff")]
    public class ProjectModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly Random random = new();
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly UserHelpService _userHelpService;
        private readonly InputSanitizingService _inputSanitizer;
        private readonly AppSettings _appSettings;

        public ProjectModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, UserHelpService userHelpService, InputSanitizingService inputSanitizingService,
            AppSettings appSettings) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _userHelpService = userHelpService;
            _inputSanitizer = inputSanitizingService;
            _appSettings = appSettings;
        }

        [SlashCommand("help", "Shows command information")]
        public async Task Help(
            [Summary("module", "Look for a command in a specific module")]string module = "",
            [Summary("command", "Show information about a specific command")]string command = "") {
            await _userHelpService.DoHelp(Context, module, command, string.IsNullOrEmpty(module));
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

        #region files
        
        [SlashCommand("submit", "Submits a part of beatmap to the project")]
        public async Task SubmitPart([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("beatmap", "The part to submit as a .osu file")]Attachment attachment,
            [Summary("part", "The part name to submit to (optional)")]string partName=null) {
            // Find out which parts this member is allowed to edit in the project
            // Download the attached file and put it in the member's folder
            // Merge it into the base file
            // Success message

            if (attachment == null) {
                await RespondAsync(Strings.NoAttachedFile);
                return;
            }

            var project = await GetProjectAsync(projectName);

            if (project == null) {
                await RespondAsync(Strings.ProjectNotExistMessage);
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id);

            if (member == null) {
                await RespondAsync(Strings.NotJoinedMessage);
                return;
            }

            if (!_fileHandler.ProjectBaseFileExists(Context.Guild, project.Name)) {
                await RespondAsync(Strings.BaseFileNotExists);
                return;
            }

            List<Part> parts = null;
            bool partIsRestricted = project.PartRestrictedUpload || partName is not null;
            if (partIsRestricted) {
                if (partName is null) {
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
                            (ho.StartTime >= p.Start - 5 || !p.Start.HasValue) &&
                            (ho.StartTime <= p.End + 5 || !p.End.HasValue) &&
                            (ho.EndTime >= p.Start - 5 || !p.Start.HasValue) &&
                            (ho.EndTime <= p.End + 5 || !p.End.HasValue)))
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
                    Padding = 5,
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
                placer.PlaceOsuPattern(partBeatmap, beatmap);

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
            
            // Handle auto-updates
            await AutoUpdateModule.HandleAutoUpdates(project, Context, _context, _fileHandler);
        }
        
        [SlashCommand("setbasefile", "Replaces the current beatmap state of the project with attached .osu file")]
        public async Task UploadBaseFile([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("beatmap", "The new base file as a .osu file.")]Attachment attachment) {
            if (attachment == null) {
                await RespondAsync(Strings.NoAttachedFile);
                return;
            }

            if (!_inputSanitizer.IsValidName(attachment.Filename)) {
                await RespondAsync(Strings.IllegalFilename);
                return;
            }

            var project = await GetProjectAsync(projectName);

            if (project == null) {
                await RespondAsync(Strings.ProjectNotExistMessage);
                return;
            }

            if (!await _fileHandler.DownloadBaseFile(Context.Guild, projectName, attachment)) {
                await RespondAsync(Strings.UploadBaseFileFail);
                return;
            }

            await RespondAsync(string.Format(Strings.UploadBaseFileSuccess, attachment.Filename, projectName));
            
            // Handle auto updates
            await AutoUpdateModule.HandleAutoUpdates(project, Context, _context, _fileHandler);
        }
        
        [SlashCommand("getbasefile", "Gets the current beatmap state of the project")]
        public async Task GetBaseFile([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            if (!_fileHandler.ProjectBaseFileExists(Context.Guild, projectName)) {
                await RespondAsync(Strings.BaseFileNotExists);
                return;
            }

            try {
                var projectBaseFilePath = _fileHandler.GetProjectBaseFilePath(Context.Guild, projectName);
                await RespondWithFileAsync(projectBaseFilePath, text: string.Format(Strings.ShowBaseFile, projectName));
            }
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.BackendErrorMessage);
            }
        }

        #endregion

        #region creation

        [SlashCommand("list", "Lists all the projects on the server and their status")]
        public async Task List() {
            var projects = await _context.Projects.AsQueryable().Where(p => p.Guild.UniqueGuildId == Context.Guild.Id).ToListAsync();

            await RespondAsync(_resourceService.GenerateProjectListMessage(projects));
        }

        //[RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("create", "Creates a new project")]
        public async Task Create([Summary("project", "The name of the new project")]string projectName) {
            try {
                var guild = await _context.Guilds.AsAsyncEnumerable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

                if (guild == null) {
                    await RespondAsync(Strings.GuildNotExistsMessage);
                    return;
                }

                if (!_inputSanitizer.IsValidProjectName(projectName)) {
                    await RespondAsync(string.Format(Strings.IllegalProjectName, projectName));
                    return;
                }

                // Check administrator or max collab count
                if (Context.User is not IGuildUser {GuildPermissions: {Administrator: true}} && guild.MaxCollabsPerPerson <=
                    _context.Members.AsQueryable().Count(o =>
                        o.UniqueMemberId == Context.User.Id && o.ProjectRole == ProjectRole.Owner)) {
                    await RespondAsync(string.Format(Strings.MaxCollabCountReached, guild.MaxCollabsPerPerson));
                    return;
                }

                if (await _context.Projects.AsQueryable()
                    .AnyAsync(o => o.GuildId == guild.Id && o.Name == projectName)) {
                    await RespondAsync(string.Format(Strings.ProjectExistsMessage));
                    return;
                }

                var projectEntry = await _context.Projects.AddAsync(new Project {Name = projectName, GuildId = guild.Id, Status = ProjectStatus.NotStarted});
                await _context.SaveChangesAsync();
                await _context.Members.AddAsync(new Member { ProjectId = projectEntry.Entity.Id, UniqueMemberId = Context.User.Id, ProjectRole = ProjectRole.Owner });
                await _context.SaveChangesAsync();
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(_resourceService.GenerateAddProjectMessage(projectName, false));
                return;
            }
            
            _fileHandler.GenerateProjectDirectory(Context.Guild, projectName);
            await RespondAsync(_resourceService.GenerateAddProjectMessage(projectName));
        }
        
        [SlashCommand("delete", "Deletes a project")]
        public async Task Delete([RequireProjectOwner][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                _context.Projects.Remove(project);
                await _context.SaveChangesAsync();

                _fileHandler.DeleteProjectDirectory(Context.Guild, projectName);

                // Delete channels and roles
                if (project.CleanupOnDeletion) {
                    // Main channel
                    if (project.MainChannelId.HasValue) {
                        var mainChannel = Context.Guild.GetTextChannel((ulong) project.MainChannelId);
                        if (mainChannel != null) {
                            await mainChannel.DeleteAsync();
                        }
                    }
                    // Info channel
                    if (project.InfoChannelId.HasValue) {
                        var infoChannel = Context.Guild.GetTextChannel((ulong) project.InfoChannelId);
                        if (infoChannel != null) {
                            await infoChannel.DeleteAsync();
                        }
                    }
                    // Participant role
                    if (project.UniqueRoleId.HasValue) {
                        var role = Context.Guild.GetRole((ulong) project.UniqueRoleId);
                        if (role != null) {
                            await role.DeleteAsync();
                        }
                    }
                    // Manager role
                    if (project.ManagerRoleId.HasValue) {
                        var role = Context.Guild.GetRole((ulong) project.ManagerRoleId);
                        if (role != null) {
                            await role.DeleteAsync();
                        }
                    }
                }
                await RespondAsync(_resourceService.GenerateRemoveProjectMessage(projectName));
            }
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(_resourceService.GenerateRemoveProjectMessage(projectName, false));
            }
        }
        
        [SlashCommand("setup", "Automatically sets-up the project, complete with roles, channels, and update notifications")]
        public async Task Setup([RequireProjectOwner][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            // Make channel, role, and permissions
            // Automatic channels and roles will be marked for deletion on project deletion unless states otherwise

            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var guild = project.Guild;

            try {
                // Auto cleanup for auto generated collab
                project.CleanupOnDeletion = true;
                await _context.SaveChangesAsync();

                // Get/Create project role
                IRole role;
                if (!project.UniqueRoleId.HasValue) {
                    role = await Context.Guild.CreateRoleAsync($"{project.Name} Participant", isMentionable:true);
                    await DoRole(projectName, role, true);
                } else {
                    role = Context.Guild.GetRole((ulong) project.UniqueRoleId.Value);
                }

                // Get/Create manager role
                IRole managerRole;
                if (!project.ManagerRoleId.HasValue) {
                    managerRole = await Context.Guild.CreateRoleAsync($"{project.Name} Manager", isMentionable:true);
                    await DoManagerRole(projectName, managerRole, true);
                } else {
                    managerRole = Context.Guild.GetRole((ulong) project.ManagerRoleId.Value);
                }

                if (guild.CollabCategoryId.HasValue) {
                    // Create info channel
                    ITextChannel infoChannel;
                    if (!project.InfoChannelId.HasValue) {
                        infoChannel = await Context.Guild.CreateTextChannelAsync($"{project.Name}-info",
                            prop => prop.CategoryId = (ulong) guild.CollabCategoryId);

                        project.InfoChannelId = infoChannel.Id;
                        await _context.SaveChangesAsync();

                        await infoChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, GetNoPermissions(infoChannel));
                        await infoChannel.AddPermissionOverwriteAsync(role, GetReadPermissions());
                        await infoChannel.AddPermissionOverwriteAsync(managerRole, GetPartialAdminPermissions());
                    } else {
                        infoChannel = Context.Guild.GetTextChannel((ulong) project.InfoChannelId.Value);
                    }

                    // Create general channel
                    if (!project.MainChannelId.HasValue) {
                        var mainChannel = await Context.Guild.CreateTextChannelAsync($"{project.Name}-general",
                            prop => prop.CategoryId = (ulong) guild.CollabCategoryId);

                        project.MainChannelId = mainChannel.Id;
                        await _context.SaveChangesAsync();

                        await mainChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, GetNoPermissions(mainChannel));
                        await mainChannel.AddPermissionOverwriteAsync(role, GetWritePermissions());
                        await mainChannel.AddPermissionOverwriteAsync(managerRole, GetPartialAdminPermissions());
                    }

                    // Send the description in the info channel
                    if (!string.IsNullOrEmpty(project.Description)) {
                        await infoChannel.SendMessageAsync($"**{Strings.Description}**\n" + project.Description);
                    }

                    // Add auto-update
                    if (!await _context.AutoUpdates.AsQueryable().AnyAsync(o =>
                        o.ProjectId == project.Id && o.UniqueChannelId == infoChannel.Id)) {
                        _context.AutoUpdates.Add(new AutoUpdate {
                            ProjectId = project.Id,
                            UniqueChannelId = infoChannel.Id,
                            Cooldown = null,
                            DoPing = false,
                            ShowOsu = true
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.SetupSuccess, projectName));
            }
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.SetupFail, projectName));
            }
        }

        #region PermissionMakers

        public static OverwritePermissions GetPartialAdminPermissions() {
            return new (PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow, // This parameter is for the 'viewChannel' permission
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Deny);
        }

        public static OverwritePermissions GetWritePermissions() {
            return new (PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow, // This parameter is for the 'viewChannel' permission
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Deny);
        }

        public static OverwritePermissions GetReadPermissions() {
            return new (PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow, // This parameter is for the 'viewChannel' permission
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny);
        }

        public static OverwritePermissions GetNoPermissions(IChannel channel) {
            return OverwritePermissions.DenyAll(channel);
        }

        #endregion

        #endregion

        #region members

        [SlashCommand("members", "Lists all members of the project")]
        public async Task Members([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var members = await _context.Members.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

            await RespondAsync(_resourceService.GenerateMembersListMessage(members));
        }

        //[RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("join", "Lets you become a member of a project which is looking for members")]
        public async Task JoinProject([Autocomplete(typeof(ProjectJoinAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (await _context.Members.AnyAsync(o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id)) {
                await GrantProjectRole(Context.User, project);
                await RespondAsync(Strings.AlreadyJoinedMessage);
                return;
            }

            if (project.Status != ProjectStatus.SearchingForMembers) {
                await RespondAsync(Strings.NotLookingForMembers);
                return;
            }

            try {
                await _context.Members.AddAsync(new Member {ProjectId = project.Id, UniqueMemberId = Context.User.Id, ProjectRole = ProjectRole.Member});
                await _context.SaveChangesAsync();
                await GrantProjectRole(Context.User, project);
                await RespondAsync(
                    _resourceService.GenerateAddMemberToProject(Context.User, projectName));
            }
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateAddMemberToProject(Context.User, projectName, false));
            }
        }

        private async Task GrantProjectRole(IPresence user, Project project) {
            if (project.UniqueRoleId.HasValue && user is IGuildUser gu) {
                var role = Context.Guild.GetRole((ulong) project.UniqueRoleId.Value);
                if (role != null) {
                    await gu.AddRoleAsync(role);
                }
            }
        }
        
        private async Task RevokeProjectRole(IPresence user, Project project) {
            if (project.UniqueRoleId.HasValue && user is IGuildUser gu) {
                var role = Context.Guild.GetRole((ulong) project.UniqueRoleId.Value);
                if (role != null) {
                    await gu.RemoveRoleAsync(role);
                }
            }
        }

        private async Task GrantManagerRole(IPresence user, Project project) {
            if (project.ManagerRoleId.HasValue && user is IGuildUser gu) {
                var role = Context.Guild.GetRole((ulong) project.ManagerRoleId.Value);
                if (role != null) {
                    await gu.AddRoleAsync(role);
                }
            }
        }
        
        private async Task RevokeManagerRole(IPresence user, Project project) {
            if (project.ManagerRoleId.HasValue && user is IGuildUser gu) {
                var role = Context.Guild.GetRole((ulong) project.ManagerRoleId.Value);
                if (role != null) {
                    await gu.RemoveRoleAsync(role);
                }
            }
        }

        [SlashCommand("leave", "Lets you leave the project")]
        public async Task LeaveProject([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

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
                await RevokeProjectRole(Context.User, project);
                await RevokeManagerRole(Context.User, project);
                await RespondAsync(
                    _resourceService.GenerateRemoveMemberFromProject(Context.User, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateRemoveMemberFromProject(Context.User, projectName, false));
            }
        }
        
        [SlashCommand("add", "Adds a new member to the project")]
        public async Task AddMember([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName, 
            [Summary("user", "The user to add")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (await _context.Members.AnyAsync(o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id)) {
                await RespondAsync(Strings.MemberExistsMessage);
                return;
            }

            try {
                await _context.Members.AddAsync(new Member { ProjectId = project.Id, UniqueMemberId = user.Id, ProjectRole = ProjectRole.Member });
                await _context.SaveChangesAsync();
                await GrantProjectRole(user, project);
                await RespondAsync(
                    _resourceService.GenerateAddMemberToProject(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateAddMemberToProject(user, projectName, false));
            }
        }
        
        [SlashCommand("remove", "Removes a member from the project")]
        public async Task RemoveMember([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The user to remove")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await RespondAsync(Strings.OwnerCannotLeaveMessage);
                return;
            }

            try {
                _context.Members.Remove(member);
                await _context.SaveChangesAsync();
                await RevokeProjectRole(user, project);
                await RevokeManagerRole(user, project);
                await RespondAsync(
                    _resourceService.GenerateRemoveMemberFromProject(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateRemoveMemberFromProject(user, projectName, false));
            }
        }
        
        [SlashCommand("promote", "Promotes a member to a manager of the project")]
        public async Task AddManager([RequireProjectOwner][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The user to promote")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await RespondAsync(string.Format(Strings.UserAlreadyOwnerMessage, projectName));
                return;
            }

            if (member.ProjectRole == ProjectRole.Manager) {
                await RespondAsync(string.Format(Strings.UserAlreadyManagerMessage, projectName));
                return;
            }

            try {
                member.ProjectRole = ProjectRole.Manager;

                await _context.SaveChangesAsync();
                await GrantManagerRole(user, project);
                await RespondAsync(
                    _resourceService.GenerateAddManager(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateAddManager(user, projectName, false));
            }
        }
        
        [SlashCommand("demote", "Demotes a manager to a regular member of the project")]
        public async Task RemoveManager([RequireProjectOwner][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The user to demote")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await RespondAsync(string.Format(Strings.OwnerCannotBeDemotedMessage, projectName));
                return;
            }

            if (member.ProjectRole != ProjectRole.Manager) {
                await RespondAsync(string.Format(Strings.UserNotManagerMessage, projectName));
                return;
            }

            try {
                member.ProjectRole = ProjectRole.Member;

                await _context.SaveChangesAsync();
                await RevokeManagerRole(user, project);
                await RespondAsync(
                    _resourceService.GenerateRemoveManager(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateRemoveManager(user, projectName, false));
            }
        }

        // Revoked regular access since this can potentially be abused to create infinite projects by passing new projects to random people
        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("setowner", "Changes the owner of the project")]
        public async Task SetOwner([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The new owner")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await RespondAsync(string.Format(Strings.UserAlreadyOwnerMessage, projectName));
                return;
            }

            try {
                var previousOwner = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.ProjectRole == ProjectRole.Owner);

                member.ProjectRole = ProjectRole.Owner;
                
                if (previousOwner != null) {
                    previousOwner.ProjectRole = ProjectRole.Manager;
                }
                
                await _context.SaveChangesAsync();
                await GrantManagerRole(user, project);
                await RespondAsync(
                    _resourceService.GenerateSetOwner(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateSetOwner(user, projectName, false));
            }
        }
        
        [SlashCommand("alias", "Changes your alias in the project")]
        public async Task Alias([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("alias", "The new alias")]string alias) {
            await Alias(projectName, Context.User, alias);
        }
        
        [SlashCommand("alias2", "Changes the alias of a member of the project")]
        public async Task Alias([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The member")]IUser user,
            [Summary("alias", "The new alias")]string alias) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (!_inputSanitizer.IsValidName(alias)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            try {
                member.Alias = alias;

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ChangeAliasSuccess, user.Mention, alias));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.ChangeAliasFail);
            }
        }
        
        [SlashCommand("tags", "Changes your tags in the project")]
        public async Task Tags([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("tags", "The new tags")]string tags) {
            await Tags(projectName, Context.User, tags);
        }
        
        //[SlashCommand("tags2", "Changes the tags of a member of the project")]
        public async Task Tags([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The member")]IUser user,
            [Summary("tags", "The new tags")]params string[] tags) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            string tagsString = string.Join(' ', tags);

            if (!_inputSanitizer.IsValidName(tagsString)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            try {
                member.Tags = tagsString;

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ChangeTagsSuccess, user.Mention, tagsString));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.ChangeTagsFail);
            }
        }
        
        [SlashCommand("gettags", "Gets all the tags of the project")]
        public async Task Tags([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var tags = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id && o.Tags != null).Select(o => o.Tags).ToListAsync();
                var aliases = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id && o.Alias != null).Select(o => o.Alias).ToListAsync();
                var tagsClean = tags.Concat(aliases)
                 .SelectMany(o => o.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(o => o.Trim()).Distinct();

                await RespondAsync(string.Format(Strings.AllMemberTags, string.Join(' ', tagsClean)));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.BackendErrorMessage);
            }
        }
        
        [SlashCommand("id", "Changes your osu! profile ID in the project")]
        public async Task Id([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Summary("id", "The new ID")] string id) {
            int slashIndex = id.LastIndexOf('/');
            ulong id2;
            if (slashIndex < 0 ? ulong.TryParse(id, out id2) : ulong.TryParse(id.Substring(slashIndex + 1), out id2)) {
                await Id(projectName, Context.User, id2);
            } else {
                await RespondAsync(Strings.CouldNotParseInput);
            }
        }
        
        //[SlashCommand("id", "Changes your osu! profile ID in the project")]
        public async Task Id([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Summary("id", "The new ID")] ulong id) {
            await Id(projectName, Context.User, id);
        }
        
        [SlashCommand("id2", "Changes the osu! profile ID of a member of the project")]
        public async Task Id([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Summary("user", "The member")] IUser user,
            [Summary("id", "The new ID")] ulong id) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            try {
                member.ProfileId = id;

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ChangeIdSuccess, user.Mention, id));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.ChangeIdFail);
            }
        }
        
        [SlashCommand("priority", "Changes the priority of a member of the project")]
        public async Task Priority([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The member")]IUser user,
            [Summary("priority", "The new priority")]int? priority) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            try {
                member.Priority = priority;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.PriorityChangeSuccess, user.Mention, priority));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.PriorityChangeFail, user.Mention, priority));
            }
        }
        
        [SlashCommand("generatepriorities", "Automatically generates priorities for all members based on membership age")]
        public async Task GeneratePriorities([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("timeweight", "The priority value of one day")]int timeWeight = 1,
            [Summary("replace", "Whether to replace all the existing priority values")]bool replace = false) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var members = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id).ToListAsync();

                foreach (var member in members) {
                    if (member.Priority.HasValue && !replace) {
                        continue;
                    }

                    var memberUser = Context.Guild.GetUser((ulong) member.UniqueMemberId);
                    if (memberUser is not IGuildUser {JoinedAt: { }} gu) {
                        member.Priority = 0;
                        continue;
                    }
                    member.Priority = (int) (DateTimeOffset.UtcNow - gu.JoinedAt.Value).TotalDays * timeWeight;
                }

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.GeneratePrioritiesSuccess, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.GeneratePrioritiesFail, projectName));
            }
        }

        #endregion

        #region settings

        // Using admin permissions here to prevent someone assigning @everyone as the project role
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("role", "Changes the member role of a project and optionally assigns the new role to all members")]
        public async Task Role([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("role", "The new member role")]IRole role,
            [Summary("reassignroles", "Whether to revoke the old role and grant the new role to all members")]bool reassignRoles = true) {
            try {
                await DoRole(projectName, role, reassignRoles);
                await RespondAsync(string.Format(Strings.ChangeProjectRoleSuccess, projectName, role.Name));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ChangeProjectRoleFail, projectName));
            }
        }

        // Using admin permissions here to prevent someone assigning @everyone as the project role
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("managerrole", "Changes the manager role of the project and optionally assigns the new role to all managers")]
        public async Task ManagerRole([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("role", "The new manager role")]IRole role,
            [Summary("reassignroles", "Whether to revoke the old manager role and assign the new manager role to all managers")]bool reassignRoles = true) {
            try {
                await DoManagerRole(projectName, role, reassignRoles);
                await RespondAsync(string.Format(Strings.ChangeManagerRoleSuccess, projectName, role.Name));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ChangeManagerRoleFail, projectName));
            }
        }

        public async Task DoRole(string projectName, IRole role, bool reassignRoles = true) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var oldRole = project.UniqueRoleId.HasValue ? Context.Guild.GetRole((ulong)project.UniqueRoleId.Value) : null;

            project.UniqueRoleId = role.Id;
            await _context.SaveChangesAsync();

            if (reassignRoles) {
                // Give all members the new role and remove the old role if possible
                var members = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id)
                    .Select(o => o.UniqueMemberId).Cast<ulong>().ToListAsync();

                foreach (var member in members.Select(id => Context.Guild.GetUser(id))) {
                    if (member is not IGuildUser gu) continue;
                    await gu.AddRoleAsync(role);
                    if (oldRole != null)
                        await gu.RemoveRoleAsync(oldRole);
                }
            }
        }

        public async Task DoManagerRole(string projectName, IRole role, bool reassignRoles = true) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var oldRole = project.ManagerRoleId.HasValue ? Context.Guild.GetRole((ulong)project.ManagerRoleId.Value) : null;

            project.ManagerRoleId = role.Id;
            await _context.SaveChangesAsync();

            if (reassignRoles) {
                // Give all members the new role and remove the old role if possible
                var members = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id && o.ProjectRole != ProjectRole.Member)
                    .Select(o => o.UniqueMemberId).Cast<ulong>().ToListAsync();

                foreach (var member in members.Select(id => Context.Guild.GetUser(id))) {
                    if (member is not IGuildUser gu) continue;
                    await gu.AddRoleAsync(role);
                    if (oldRole != null)
                        await gu.RemoveRoleAsync(oldRole);
                }
            }
        }

        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        //[SlashCommand("rolecolor", "Changes the color of the roles of the project")]
        public async Task RoleColor([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Summary("color", "The new color as Hex code")] Color color) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var mainRole = project.UniqueRoleId.HasValue ? Context.Guild.GetRole((ulong)project.UniqueRoleId.Value) : null;
                var managerRole = project.ManagerRoleId.HasValue ? Context.Guild.GetRole((ulong)project.ManagerRoleId.Value) : null;

                if (mainRole != null) {
                    await mainRole.ModifyAsync(o => o.Color = color);
                }
                if (managerRole != null) {
                    await managerRole.ModifyAsync(o => o.Color = color);
                }

                await RespondAsync(string.Format(Strings.ChangeRoleColorSuccess, projectName, color));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ChangeRoleColorFail, projectName, color));
            }
        }
        
        [SlashCommand("rename", "Renames the project")]
        public async Task Rename([RequireProjectOwner][Summary("project", "The old project name")]string projectName,
            [Summary("newname", "The new project name")]string newProjectName) {
            if (!_inputSanitizer.IsValidProjectName(newProjectName)) {
                await RespondAsync(string.Format(Strings.IllegalProjectName, newProjectName));
                return;
            }

            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (await _context.Projects.AsQueryable()
                .AnyAsync(o => o.Guild.UniqueGuildId == Context.Guild.Id && o.Name == newProjectName)) {
                await RespondAsync(string.Format(Strings.ProjectExistsMessage));
                return;
            }

            try {
                project.Name = newProjectName;
                await _context.SaveChangesAsync();

                // Change folder name
                _fileHandler.MoveProjectPath(Context.Guild, projectName, newProjectName);

                await RespondAsync(string.Format(Strings.ProjectRenameSuccess, projectName, newProjectName));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectRenameFail, projectName, newProjectName));
            }
        }

        [SlashCommand("description", "Changes the description of the project")]
        public async Task Description([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("description", "The new description")]string description) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (!_inputSanitizer.IsValidName(description)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            try {
                project.Description = description;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ProjectDescriptionSuccess, projectName));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectDescriptionFail, projectName));
            }
        }
        
        [SlashCommand("status", "Changes the status of the project")]
        public async Task Status([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("status", "The new status")]ProjectStatus status) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.Status = status;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ProjectStatusSuccess, projectName, status));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectStatusFail, projectName, status));
            }
        }
        
        [SlashCommand("options", "Configures several boolean project options")]
        public async Task Options([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("selfassignmentallowed", "Whether members may claim parts on their own")]bool? selfAssignmentAllowed = null,
            [Summary("prioritypicking", "Whether priority picking is enabled")]bool? priorityPicking = null,
            [Summary("partrestrictedupload", "Whether to restrict part submission to just the assigned parts")]bool? partRestrictedUpload = null,
            [Summary("doreminders", "Whether to automatically remind members about their deadlines")]bool? doReminders = null) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                int n = 0;
                if (selfAssignmentAllowed.HasValue) {
                    project.SelfAssignmentAllowed = selfAssignmentAllowed.Value;
                    n++;
                }
                if (priorityPicking.HasValue) {
                    project.PriorityPicking = priorityPicking.Value;
                    n++;
                }
                if (partRestrictedUpload.HasValue) {
                    project.PartRestrictedUpload = partRestrictedUpload.Value;
                    n++;
                }
                if (doReminders.HasValue) {
                    project.DoReminders = doReminders.Value;
                    n++;
                }

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ProjectOptionsSuccess, n, projectName));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectOptionsFail, projectName));
            }
        }
        
        [SlashCommand("maxassignments", "Changes the maximum number of allowed assignments for members of the project")]
        public async Task MaxAssignments([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName, 
            [Summary("maxassignments", "The new maximum number of allowed assignments (can be null)")]int? maxAssignments) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.MaxAssignments = maxAssignments;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ProjectMaxAssignmentsSuccess, projectName, maxAssignments));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectMaxAssignmentsFail, projectName));
            }
        }
        
        [SlashCommand("assignmentlifetime", "Changes the default duration of assignments of the project")]
        public async Task AssignmentLifetime([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName, 
            [Summary("lifetime", "The new duration of assignments (dd:hh:mm:ss:fff) (can be null)")]TimeSpan? lifetime) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.AssignmentLifetime = lifetime;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ProjectAssignmentLifetimeSuccess, projectName, lifetime));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectAssignmentLifetimeFail, projectName));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("mainchannel", "Changes the main channel of the project")]
        public async Task MainChannel([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("channel", "The new main channel")]ITextChannel channel) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.MainChannelId = channel?.Id;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ProjectMainChannelSuccess, channel?.Mention));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectMainChannelFail, channel?.Mention));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("infochannel", "Changes the info channel of the project")]
        public async Task InfoChannel([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("channel", "The new info channel")]ITextChannel channel) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.InfoChannelId = channel?.Id;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ProjectInfoChannelSuccess, channel?.Mention));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectInfoChannelFail, channel?.Mention));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("deletioncleanup", "Changes whether to remove the roles and channels assigned to the project upon project deletion")]
        public async Task ChangeAutoCleanup([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("cleanup", "Whether to do cleanup")]bool cleanup) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.CleanupOnDeletion = cleanup;
                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.AutoCleanupChangeSuccess, projectName, cleanup));
            } 
            catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.AutoCleanupChangeFail, projectName, cleanup));
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

            int n_words = wordCount > 0 && wordCount <= 200 ? wordCount : wordCounts[random.Next(wordCounts.Length - 1)];
            StringBuilder diffname = new StringBuilder();
            for (int i = 0; i < n_words; i++) {
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
        
        private async Task<Project> GetProjectAsync(string projectName) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await RespondAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            var project = await _context.Projects.AsQueryable().Include(o => o.Guild)
                .SingleOrDefaultAsync(o => o.GuildId == guild.Id && o.Name == projectName);

            if (project == null) {
                await RespondAsync(Strings.ProjectNotExistMessage);
                return null;
            }

            return project;
        }
    }
}