using CollaborationBot.Entities;
using CollaborationBot.Preconditions;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;
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
using System.Net;

namespace CollaborationBot.Commands {
    [Group]
    [Name("Project module")]
    [Summary("Main module with project and member related stuff")]
    public class ProjectModule : ModuleBase<SocketCommandContext> {
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

        [Command("help")]
        [Summary("Shows command information")]
        public async Task Help(string command = "") {
            await _userHelpService.DoHelp(Context, "Project module", "", command, true);
        }

        #region guides

        [Command("admin-guide")]
        [Alias("guild-guide", "server-guide")]
        [Summary("Shows a guide for server admins on how to set-up the bot")]
        public async Task AdminGuide() {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            string title = Strings.AdminGuideTitle;
            string content = string.Format(Strings.AdminGuideContent, _appSettings.Prefix);

            embedBuilder.AddField(title, content);
            
            await Context.Channel.SendMessageAsync(string.Empty, false, embedBuilder.Build());
        }

        [Command("collab-guide")]
        [Alias("collab-manager-guide", "owner-guide")]
        [Summary("Shows a guide for collab organisers on how to set-up a collab with the bot")]
        public async Task CollabGuide() {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            string title = Strings.CollabGuideTitle;
            string content = string.Format(Strings.CollabGuideContent, _appSettings.Prefix);

            embedBuilder.AddField(title, content);
            
            await Context.Channel.SendMessageAsync(string.Empty, false, embedBuilder.Build());
        }

        [Command("participant-guide")]
        [Alias("member-guide")]
        [Summary("Shows a guide for collab participants on how to use the bot")]
        public async Task ParticipantGuide(string projectName = null) {
            if (projectName != null && !_inputSanitizer.IsValidProjectName(projectName)) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.IllegalProjectName, projectName));
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
            
            await Context.Channel.SendMessageAsync(string.Empty, false, embedBuilder.Build());
        }

        #endregion

        #region files

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("submit")]
        [Summary("Submits a part of beatmap to the project")]
        public async Task SubmitPart([Summary("The project")]string projectName, 
            [Summary("The part name to submit to (optional)")]string partName=null) {
            // Find out which parts this member is allowed to edit in the project
            // Download the attached file and put it in the member's folder
            // Merge it into the base file
            // Success message

            var attachment = Context.Message.Attachments.SingleOrDefault();

            if (attachment == null) {
                await Context.Channel.SendMessageAsync(Strings.NoAttachedFile);
                return;
            }

            var project = await GetProjectAsync(projectName);

            if (project == null) {
                await Context.Channel.SendMessageAsync(Strings.ProjectNotExistMessage);
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.NotJoinedMessage);
                return;
            }

            if (!_fileHandler.ProjectBaseFileExists(Context.Guild, project.Name)) {
                await Context.Channel.SendMessageAsync(Strings.BaseFileNotExists);
                return;
            }

            List<Part> parts = null;
            if (project.PartRestrictedUpload || partName != null) {
                if (partName == null) {
                    // Submit to claimed part
                    parts = await _context.Assignments.AsQueryable()
                        .Where(o => o.Part.ProjectId == project.Id && o.Member.UniqueMemberId == Context.User.Id)
                        .Select(o => o.Part)
                        .ToListAsync();
                } else {
                    if (member.ProjectRole == ProjectRole.Member && !project.PartRestrictedUpload) {
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
                }

                if (parts.Count == 0) {
                    await Context.Channel.SendMessageAsync(Strings.NoPartsToSubmit);
                    return;
                }
            }

            string beatmapString = await _fileHandler.DownloadPartSubmit(Context.Guild, projectName, attachment);

            if (beatmapString == null) {
                await Context.Channel.SendMessageAsync(Strings.AttachedFileInvalid);
                return;
            }

            try {
                var partBeatmap = new OsuBeatmapDecoder().Decode(beatmapString);

                if (project.PartRestrictedUpload) {
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
                    await Context.Channel.SendMessageAsync(Strings.SubmitNoHitObjects);
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
                    await Context.Channel.SendMessageAsync(string.Format(Strings.GlobalSVMismatchWarning));
                }

                if (!Precision.AlmostEquals(partBeatmap.General.StackLeniency, beatmap.General.StackLeniency)) {
                    await Context.Channel.SendMessageAsync(string.Format(Strings.StackLeniencyMismatchWarning));
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

                editor.WriteFile(beatmap);

                await Context.Channel.SendMessageAsync(_resourceService.GenerateSubmitPartMessage(projectName, count, true));
            } catch (BeatmapParsingException e) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.BeatmapParseFail, e.Message));
                return;
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateSubmitPartMessage(projectName, 0, false));
                return;
            }
            
            // Handle auto-updates
            await AutoUpdateModule.HandleAutoUpdates(project, Context, _context, _fileHandler);
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("setBaseFile")]
        [Alias("set-base-file", "uploadBaseFile", "upload-base-file")]
        [Summary("Replaces the current beatmap state of the project with attached .osu file")]
        public async Task UploadBaseFile([Summary("The project")]string projectName) {
            var attachment = Context.Message.Attachments.SingleOrDefault();

            if (attachment == null) {
                await Context.Channel.SendMessageAsync(Strings.NoAttachedFile);
                return;
            }

            if (!_inputSanitizer.IsValidName(attachment.Filename)) {
                await Context.Channel.SendMessageAsync(Strings.IllegalFilename);
                return;
            }

            var project = await GetProjectAsync(projectName);

            if (project == null) {
                await Context.Channel.SendMessageAsync(Strings.ProjectNotExistMessage);
                return;
            }

            if (!await _fileHandler.DownloadBaseFile(Context.Guild, projectName, attachment)) {
                await Context.Channel.SendMessageAsync(Strings.UploadBaseFileFail);
                return;
            }

            await Context.Channel.SendMessageAsync(string.Format(Strings.UploadBaseFileSuccess, attachment.Filename, projectName));
            
            // Handle auto updates
            await AutoUpdateModule.HandleAutoUpdates(project, Context, _context, _fileHandler);
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("getBaseFile")]
        [Alias("get-base-file", "downloadBaseFile", "download-base-file")]
        [Summary("Gets the current beatmap state of the project")]
        public async Task GetBaseFile([Summary("The project")]string projectName) {
            if (!_fileHandler.ProjectBaseFileExists(Context.Guild, projectName)) {
                await Context.Channel.SendMessageAsync(Strings.BaseFileNotExists);
                return;
            }

            try {
                var projectBaseFilePath = _fileHandler.GetProjectBaseFilePath(Context.Guild, projectName);
                await Context.Channel.SendFileAsync(projectBaseFilePath, string.Format(Strings.ShowBaseFile, projectName));
            }
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendFileAsync(Strings.BackendErrorMessage);
            }
        }

        #endregion

        #region creation

        [Command("list")]
        [Summary("Lists all the projects on the server and their status")]
        public async Task List() {
            var projects = await _context.Projects.AsQueryable().Where(p => p.Guild.UniqueGuildId == Context.Guild.Id).ToListAsync();

            await Context.Channel.SendMessageAsync(_resourceService.GenerateProjectListMessage(projects));
        }

        //[RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("create")]
        [Summary("Creates a new project")]
        public async Task Create([Summary("The name of the new project")]string projectName) {
            var guild = await _context.Guilds.AsAsyncEnumerable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(Strings.GuildNotExistsMessage);
                return;
            }

            if (!_inputSanitizer.IsValidProjectName(projectName)) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.IllegalProjectName, projectName));
                return;
            }

            // Check administrator or max collab count
            if (Context.User is not IGuildUser {GuildPermissions: {Administrator: true}} && guild.MaxCollabsPerPerson <=
                _context.Members.AsQueryable().Count(o =>
                    o.UniqueMemberId == Context.User.Id && o.ProjectRole == ProjectRole.Owner)) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.MaxCollabCountReached, guild.MaxCollabsPerPerson));
                return;
            }

            if (_context.Projects.AsQueryable()
                .Any(o => o.GuildId == guild.Id && o.Name == projectName)) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectExistsMessage));
                return;
            }

            try {
                var projectEntry = await _context.Projects.AddAsync(new Project {Name = projectName, GuildId = guild.Id, Status = ProjectStatus.NotStarted});
                await _context.SaveChangesAsync();
                await _context.Members.AddAsync(new Member { ProjectId = projectEntry.Entity.Id, UniqueMemberId = Context.User.Id, ProjectRole = ProjectRole.Owner });
                await _context.SaveChangesAsync();
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName, false));
                return;
            }
            
            _fileHandler.GenerateProjectDirectory(Context.Guild, projectName);
            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName));
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("delete")]
        [Summary("Deletes a project")]
        public async Task Delete([Summary("The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                _context.Projects.Remove(project);
                await _context.SaveChangesAsync();

                _fileHandler.DeleteProjectDirectory(Context.Guild, projectName);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(projectName));

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
            }
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(projectName, false));
            }
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("setup")]
        [Summary("Automatically sets-up the project, complete with roles, channels, and update notifications")]
        public async Task Setup([Summary("The project")]string projectName) {
            // Make channel, role, and permissions
            // Automatic channels and roles will be marked for deletion on project deletion unless states otherwise

            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var guild = project.Guild;

            try {
                bool createdRole = false;
                bool createdManagerRole = false;
                bool createdInfo = false;
                bool createdMain = false;

                // Get/Create project role
                IRole role;
                if (!project.UniqueRoleId.HasValue) {
                    role = await Context.Guild.CreateRoleAsync($"{project.Name} Participant", isMentionable:true);
                    await Role(projectName, role, true);
                    createdRole = true;
                } else {
                    role = Context.Guild.GetRole((ulong) project.UniqueRoleId.Value);
                }

                // Get/Create manager role
                IRole managerRole;
                if (!project.ManagerRoleId.HasValue) {
                    managerRole = await Context.Guild.CreateRoleAsync($"{project.Name} Manager", isMentionable:true);
                    await ManagerRole(projectName, managerRole, true);
                    createdManagerRole = true;
                } else {
                    managerRole = Context.Guild.GetRole((ulong) project.ManagerRoleId.Value);
                }

                if (guild.CollabCategoryId.HasValue) {
                    // Create info channel
                    ITextChannel infoChannel;
                    if (!project.InfoChannelId.HasValue) {
                        infoChannel = await Context.Guild.CreateTextChannelAsync($"{project.Name}-info",
                            prop => prop.CategoryId = (ulong) guild.CollabCategoryId);
                        
                        await infoChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, GetNoPermissions(infoChannel));
                        await infoChannel.AddPermissionOverwriteAsync(role, GetReadPermissions());
                        await infoChannel.AddPermissionOverwriteAsync(managerRole, GetPartialAdminPermissions());

                        project.InfoChannelId = infoChannel.Id;
                        createdInfo = true;
                    } else {
                        infoChannel = Context.Guild.GetTextChannel((ulong) project.InfoChannelId.Value);
                    }

                    // Create general channel
                    if (!project.MainChannelId.HasValue) {
                        var mainChannel = await Context.Guild.CreateTextChannelAsync($"{project.Name}-general",
                            prop => prop.CategoryId = (ulong) guild.CollabCategoryId);
                        
                        await mainChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, GetNoPermissions(mainChannel));
                        await mainChannel.AddPermissionOverwriteAsync(role, GetWritePermissions());
                        await mainChannel.AddPermissionOverwriteAsync(managerRole, GetPartialAdminPermissions());

                        project.MainChannelId = mainChannel.Id;
                        createdMain = true;
                    }

                    // Allow auto cleanup if both channels were created
                    project.CleanupOnDeletion = project.CleanupOnDeletion ||
                        createdMain && createdInfo && createdRole && createdManagerRole;

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
                await Context.Channel.SendMessageAsync(string.Format(Strings.SetupSuccess, projectName));
            }
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.SetupFail, projectName));
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

        [Command("members")]
        [Summary("Lists all members of the project")]
        public async Task Members([Summary("The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var members = await _context.Members.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

            await Context.Channel.SendMessageAsync(_resourceService.GenerateMembersListMessage(members));
        }

        //[RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("join")]
        [Summary("Lets you become a member of the project")]
        public async Task JoinProject([Summary("The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (_context.Members.Any(o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id)) {
                await GrantProjectRole(Context.User, project);
                await Context.Channel.SendMessageAsync(Strings.AlreadyJoinedMessage);
                return;
            }

            if (project.Status != ProjectStatus.SearchingForMembers) {
                await Context.Channel.SendMessageAsync(Strings.NotLookingForMembers);
                return;
            }

            try {
                await _context.Members.AddAsync(new Member {ProjectId = project.Id, UniqueMemberId = Context.User.Id, ProjectRole = ProjectRole.Member});
                await _context.SaveChangesAsync();
                await GrantProjectRole(Context.User, project);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddMemberToProject(Context.User, projectName));
            }
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(
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

        [Command("leave")]
        [Summary("Lets you leave the project")]
        public async Task LeaveProject([Summary("The project")]string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.NotJoinedMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await Context.Channel.SendMessageAsync(Strings.OwnerCannotLeaveMessage);
                return;
            }

            try {
                _context.Members.Remove(member);
                await _context.SaveChangesAsync();
                await RevokeProjectRole(Context.User, project);
                await RevokeManagerRole(Context.User, project);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveMemberFromProject(Context.User, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveMemberFromProject(Context.User, projectName, false));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("add")]
        [Summary("Adds a new member to the project")]
        public async Task AddMember([Summary("The project")]string projectName, 
            [Summary("The user to add")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (_context.Members.Any(o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id)) {
                await Context.Channel.SendMessageAsync(Strings.MemberExistsMessage);
                return;
            }

            try {
                await _context.Members.AddAsync(new Member { ProjectId = project.Id, UniqueMemberId = user.Id, ProjectRole = ProjectRole.Member });
                await _context.SaveChangesAsync();
                await GrantProjectRole(user, project);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddMemberToProject(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddMemberToProject(user, projectName, false));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("remove")]
        [Summary("Removes a member from the project")]
        public async Task RemoveMember([Summary("The project")]string projectName,
            [Summary("The user to remove")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await Context.Channel.SendMessageAsync(Strings.OwnerCannotLeaveMessage);
                return;
            }

            try {
                _context.Members.Remove(member);
                await _context.SaveChangesAsync();
                await RevokeProjectRole(user, project);
                await RevokeManagerRole(user, project);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveMemberFromProject(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveMemberFromProject(user, projectName, false));
            }
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("promote")]
        [Summary("Promotes a member to a manager of the project")]
        public async Task AddManager([Summary("The project")]string projectName,
            [Summary("The user to promote")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.UserAlreadyOwnerMessage, projectName));
                return;
            }

            if (member.ProjectRole == ProjectRole.Manager) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.UserAlreadyManagerMessage, projectName));
                return;
            }

            try {
                member.ProjectRole = ProjectRole.Manager;

                await _context.SaveChangesAsync();
                await GrantManagerRole(user, project);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddManager(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddManager(user, projectName, false));
            }
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("demote")]
        [Summary("Demotes a manager to a regular member of the project")]
        public async Task RemoveManager([Summary("The project")]string projectName,
            [Summary("The user to demote")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.OwnerCannotBeDemotedMessage, projectName));
                return;
            }

            if (member.ProjectRole != ProjectRole.Manager) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.UserNotManagerMessage, projectName));
                return;
            }

            try {
                member.ProjectRole = ProjectRole.Member;

                await _context.SaveChangesAsync();
                await RevokeManagerRole(user, project);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveManager(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveManager(user, projectName, false));
            }
        }

        // Revoked regular access for now since this can potentially be abused to create infinite projects
        //[RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("set-owner")]
        [Summary("Changes the owner of the project")]
        public async Task SetOwner([Summary("The project")]string projectName,
            [Summary("The new owner")]IGuildUser user) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.UserAlreadyOwnerMessage, projectName));
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
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateSetOwner(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateSetOwner(user, projectName, false));
            }
        }
        
        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("alias")]
        [Summary("Changes your alias in the project")]
        public async Task Alias([Summary("The project")]string projectName,
            [Summary("The new alias")]string alias) {
            await Alias(projectName, Context.User, alias);
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("alias")]
        [Summary("Changes the alias of a member of the project")]
        public async Task Alias([Summary("The project")]string projectName,
            [Summary("The member")]IUser user, [Summary("The new alias")]string alias) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (!_inputSanitizer.IsValidName(alias)) {
                await Context.Channel.SendMessageAsync(Strings.IllegalInput);
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            try {
                member.Alias = alias;

                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeAliasSuccess, user.Mention, alias));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(Strings.ChangeAliasFail);
            }
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("tags")]
        [Summary("Changes your tags in the project")]
        public async Task Tags([Summary("The project")]string projectName,
            [Summary("The new tags")]string tags) {
            await Tags(projectName, Context.User, tags);
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("tags")]
        [Summary("Changes the tags of a member of the project")]
        public async Task Tags([Summary("The project")]string projectName,
            [Summary("The member")]IUser user, [Summary("The new tags")]params string[] tags) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            string tagsString = string.Join(' ', tags);

            if (!_inputSanitizer.IsValidName(tagsString)) {
                await Context.Channel.SendMessageAsync(Strings.IllegalInput);
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            try {
                member.Tags = tagsString;

                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeTagsSuccess, user.Mention, tagsString));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(Strings.ChangeTagsFail);
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("tags")]
        [Summary("Gets all the tags of the project")]
        public async Task Tags([Summary("The project")]string projectName) {
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

                await Context.Channel.SendMessageAsync(string.Format(Strings.AllMemberTags, string.Join(' ', tagsClean)));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(Strings.BackendErrorMessage);
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("priority")]
        [Summary("Changes the priority of a member of the project")]
        public async Task Priority([Summary("The project")]string projectName,
            [Summary("The member")]IUser user, [Summary("The new priority")]int? priority) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            try {
                member.Priority = priority;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.PriorityChangeSuccess, user.Mention, priority));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.PriorityChangeFail, user.Mention, priority));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("generate-priorities")]
        [Summary("Automatically generates priorities for all members of the project based on total number of days they've been on the server")]
        public async Task GeneratePriorities([Summary("The project")]string projectName,
            [Summary("The priority value of one day")]int timeWeight = 1,
            [Summary("Whether to replace all the existing priority values")]bool replace = false) {
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
                await Context.Channel.SendMessageAsync(string.Format(Strings.GeneratePrioritiesSuccess, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.GeneratePrioritiesFail, projectName));
            }
        }

        #endregion

        #region settings

        // Using admin permissions here to prevent someone assigning @everyone as the project role
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("role")]
        [Summary("Changes the member role of a project and optionally assigns the new role to all members")]
        public async Task Role([Summary("The project")]string projectName,
            [Summary("The new member role")]IRole role,
            [Summary("Whether to revoke the old role and grant the new role to all members")]bool reassignRoles = true) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var oldRole = project.UniqueRoleId.HasValue ? Context.Guild.GetRole((ulong) project.UniqueRoleId.Value) : null;

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

                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeProjectRoleSuccess, projectName, role.Name));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeProjectRoleFail, projectName));
            }
        }

        // Using admin permissions here to prevent someone assigning @everyone as the project role
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("manager-role")]
        [Summary("Changes the manager role of the project and optionally assigns the new role to all managers")]
        public async Task ManagerRole([Summary("The project")]string projectName,
            [Summary("The new manager role")]IRole role,
            [Summary("Whether to revoke the old manager role and assign the new manager role to all managers")]bool reassignRoles = true) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var oldRole = project.ManagerRoleId.HasValue ? Context.Guild.GetRole((ulong) project.ManagerRoleId.Value) : null;

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

                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeManagerRoleSuccess, projectName, role.Name));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeManagerRoleFail, projectName));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("role-color")]
        [Summary("Changes the color of the roles of the project")]
        public async Task RoleColor([Summary("The project")] string projectName,
            [Summary("The new color as Hex code")] Color color) {
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

                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeRoleColorSuccess, projectName, color));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeRoleColorFail, projectName, color));
            }
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("rename")]
        [Summary("Renames the project")]
        public async Task Rename([Summary("The old project name")]string projectName,
            [Summary("The new project name")]string newProjectName) {
            if (!_inputSanitizer.IsValidProjectName(newProjectName)) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.IllegalProjectName, newProjectName));
                return;
            }

            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (_context.Projects.AsQueryable()
                .Any(o => o.Guild.UniqueGuildId == Context.Guild.Id && o.Name == newProjectName)) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectExistsMessage));
                return;
            }

            try {
                project.Name = newProjectName;
                await _context.SaveChangesAsync();

                // Change folder name
                _fileHandler.MoveProjectPath(Context.Guild, projectName, newProjectName);

                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectRenameSuccess, projectName, newProjectName));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectRenameFail, projectName, newProjectName));
            }
        }
        
        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("description")]
        [Alias("desc")]
        [Summary("Changes the description of the project")]
        public async Task Description([Summary("The project")]string projectName,
            [Summary("The new description")]string description) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (!_inputSanitizer.IsValidName(description)) {
                await Context.Channel.SendMessageAsync(Strings.IllegalInput);
                return;
            }

            try {
                project.Description = description;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectDescriptionSuccess, projectName));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectDescriptionFail, projectName));
            }
        }
        
        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("status")]
        [Summary("Changes the status of the project")]
        public async Task Status([Summary("The project")]string projectName,
            [Summary("The new status")]ProjectStatus status) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.Status = status;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectStatusSuccess, projectName, status));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectStatusFail, projectName, status));
            }
        }
        
        [NamedArgumentType]
        public class NamableOptions {
            //[Summary("Whether members can claim parts on their own")]
            public bool? SelfAssignmentAllowed { get; set; }
            //[Summary("Whether priority picking is enabled")]
            public bool? PriorityPicking { get; set; }
            //[Summary("Whether to restrict part submission to just the assigned parts")]
            public bool? PartRestrictedUpload { get; set; }
            //[Summary("Whether to remind members about their deadlines")]
            public bool? DoReminders { get; set; }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("options")]
        [Summary("Configures several boolean project options")]
        public async Task Options([Summary("The project")]string projectName,
            [Summary("The options [SelfAssignmentAllowed, PriorityPicking, PartRestrictedUpload, DoReminders]")]NamableOptions options) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                int n = 0;
                if (options.SelfAssignmentAllowed.HasValue) {
                    project.SelfAssignmentAllowed = options.SelfAssignmentAllowed.Value;
                    n++;
                }
                if (options.PriorityPicking.HasValue) {
                    project.PriorityPicking = options.PriorityPicking.Value;
                    n++;
                }
                if (options.PartRestrictedUpload.HasValue) {
                    project.PartRestrictedUpload = options.PartRestrictedUpload.Value;
                    n++;
                }
                if (options.DoReminders.HasValue) {
                    project.DoReminders = options.DoReminders.Value;
                    n++;
                }

                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectOptionsSuccess, n, projectName));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectOptionsFail, projectName));
            }
        }
        
        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("max-assignments")]
        [Summary("Changes the maximum number of allowed assignments for members of the project")]
        public async Task MaxAssignments([Summary("The project")]string projectName, 
            [Summary("The new maximum number of allowed assignments (can be null)")]int? maxAssignments) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.MaxAssignments = maxAssignments;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectMaxAssignmentsSuccess, projectName, maxAssignments));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectMaxAssignmentsFail, projectName));
            }
        }
        
        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("assignment-lifetime")]
        [Summary("Changes the default duration of assignments of the project")]
        public async Task AssignmentLifetime([Summary("The project")]string projectName, 
            [Summary("The new duration of assignments (dd:hh:mm:ss:fff) (can be null)")]TimeSpan? lifetime) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.AssignmentLifetime = lifetime;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectAssignmentLifetimeSuccess, projectName, lifetime));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectAssignmentLifetimeFail, projectName));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("main-channel")]
        [Summary("Changes the main channel of the project")]
        public async Task MainChannel([Summary("The project")]string projectName,
            [Summary("The new main channel")]ITextChannel channel) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.MainChannelId = channel?.Id;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectMainChannelSuccess, channel?.Mention));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectMainChannelFail, channel?.Mention));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("info-channel")]
        [Summary("Changes the info channel of the project")]
        public async Task InfoChannel([Summary("The project")]string projectName,
            [Summary("The new info channel")]ITextChannel channel) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.InfoChannelId = channel?.Id;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectInfoChannelSuccess, channel?.Mention));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectInfoChannelFail, channel?.Mention));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("deletion-cleanup")]
        [Summary("Changes whether to remove the roles and channels assigned to the project upon project deletion")]
        public async Task ChangeAutoCleanup([Summary("The project")]string projectName,
            [Summary("Whether to do cleanup")]bool cleanup) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.CleanupOnDeletion = cleanup;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoCleanupChangeSuccess, projectName, cleanup));
            } 
            catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoCleanupChangeFail, projectName, cleanup));
            }
        }

        #endregion

        #region misc

        private static readonly int[] wordCounts = { 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 10 };

        [Command("diffname")]
        [Summary("Generates a random difficulty name")]
        public async Task Diffname([Summary("The number of words to use in the sentence")] int wordCount = -1) {
            await DoRandomString(@"CollaborationBot.Resources.Diffname Words.txt", wordCount, 0.3);
        }

        [Command("blixys")]
        [Summary("Generates some inspiration")]
        public async Task Blixys([Summary("The number of words to use in the sentence")]int wordCount=-1) {
            await DoRandomString(@"CollaborationBot.Resources.blixys.txt", wordCount);
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
                await Context.Channel.SendMessageAsync(Strings.DiffnameLoadFail);
                return;
            }

            int n_words = wordCount >= 0 && wordCount <= 200 ? wordCount : wordCounts[random.Next(wordCounts.Length - 1)];
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

            await Context.Channel.SendMessageAsync(diffname.ToString());
        }

        //[Command("collage")]
        [Summary("Generates a collage with images from a channel")]
        public async Task Collage([Summary("The channel to get the images from")]ITextChannel channel,
            [Summary("The number of messages to use in the collage")] int messageCount = 100) {
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

                        using var client = new WebClient();
                        var name = m.Content + Path.GetExtension(a.Filename);
                        var tempname = "temp" + Path.GetExtension(a.Filename);
                        client.DownloadFile(uri, tempname);

                        zip.CreateEntryFromFile(tempname, name, CompressionLevel.Optimal);
                    }
                }
            }

            zip.Dispose();
            await Context.Channel.SendFileAsync("temp.zip", mss.ToString());
        }

        #endregion

        private async Task<Project> GetProjectAsync(string projectName) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            var project = await _context.Projects.AsQueryable().Include(o => o.Guild)
                .SingleOrDefaultAsync(o => o.GuildId == guild.Id && o.Name == projectName);

            if (project == null) {
                await Context.Channel.SendMessageAsync(Strings.ProjectNotExistMessage);
                return null;
            }

            return project;
        }
    }
}