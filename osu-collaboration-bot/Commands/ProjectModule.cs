using System;
using System.Linq;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CollaborationBot.Preconditions;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Mapping_Tools_Core.BeatmapHelper.IO.Decoding;
using Mapping_Tools_Core.Tools.PatternGallery;
using Mapping_Tools_Core.BeatmapHelper.IO.Editor;
using Mapping_Tools_Core.Exceptions;
using System.Collections.Generic;

namespace CollaborationBot.Commands {
    [Group]
    public class ProjectModule : ModuleBase<SocketCommandContext> {
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public ProjectModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
        }

        #region files

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("submit")]
        public async Task SubmitPart(string projectName, string partName=null) {
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
                            ho.StartTime >= p.Start - 5 &&
                            ho.StartTime <= p.End + 5 &&
                            ho.EndTime >= p.Start - 5 &&
                            ho.EndTime <= p.End + 5))
                        .ToList();
                }

                var count = partBeatmap.HitObjects.Count;

                if (count == 0) {
                    await Context.Channel.SendMessageAsync(Strings.SubmitNoHitObjects);
                    return;
                }

                var placer = new OsuPatternPlacer {
                    PatternOverwriteMode = PatternOverwriteMode.PartitionedOverwrite,
                    TimingOverwriteMode = TimingOverwriteMode.PatternTimingOnly,
                    Padding = 5,
                    PartingDistance = 4,
                    FixColourHax = true,
                    FixBpmSv = false,
                    FixStackLeniency = false,
                    FixTickRate = false,
                    FixGlobalSv = false,
                    SnapToNewTiming = false,
                    ScaleToNewTiming = false, 
                    IncludeHitsounds = true,
                    IncludeKiai = true,
                    ScaleToNewCircleSize = false,
                };

                var editor = new BeatmapEditor(_fileHandler.GetProjectBaseFilePath(Context.Guild, projectName));
                var beatmap = editor.ReadFile();
            
                placer.PlaceOsuPattern(partBeatmap, beatmap);

                editor.WriteFile(beatmap);

                await Context.Channel.SendMessageAsync(_resourceService.GenerateSubmitPartMessage(projectName, count, true));
            } catch (BeatmapParsingException e) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.BeatmapParseFail, e.Message));
                return;
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateSubmitPartMessage(projectName, 0, false));
                return;
            }
            
            // Handle auto-updates
            await AutoUpdateModule.HandleAutoUpdates(project, Context, _context, _fileHandler);
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("uploadBaseFile")]
        public async Task UploadBaseFile(string projectName) {
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
        public async Task GetBaseFile(string projectName) {
            try {
                var projectBaseFilePath = _fileHandler.GetProjectBaseFilePath(Context.Guild, projectName);
                await Context.Channel.SendFileAsync(projectBaseFilePath, string.Format(Strings.ShowBaseFile, projectName));
            }
            catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendFileAsync(Strings.BackendErrorMessage);
            }
        }

        #endregion

        #region creation

        [Command("list")]
        public async Task List() {
            var projects = await _context.Projects.AsQueryable().Where(p => p.Guild.UniqueGuildId == Context.Guild.Id).ToListAsync();

            await Context.Channel.SendMessageAsync(_resourceService.GenerateProjectListMessage(projects));
        }

        //[RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("create")]
        public async Task Create(string projectName) {
            var guild = await _context.Guilds.AsAsyncEnumerable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(_resourceService.GuildNotExistsMessage);
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName, false));
                return;
            }
            
            _fileHandler.GenerateProjectDirectory(Context.Guild, projectName);
            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName));
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("delete")]
        public async Task Delete(string projectName) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(projectName, false));
            }
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("setup")]
        public async Task Setup(string projectName) {
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
                    role = await Context.Guild.CreateRoleAsync($"{project.Name}-Participant", isMentionable:true);
                    project.UniqueRoleId = role.Id;
                    createdRole = true;
                } else {
                    role = Context.Guild.GetRole((ulong) project.UniqueRoleId.Value);
                }

                // Get/Create manager role
                IRole managerRole;
                if (!project.ManagerRoleId.HasValue) {
                    managerRole = await Context.Guild.CreateRoleAsync($"{project.Name}-Manager", isMentionable:true);
                    project.ManagerRoleId = role.Id;
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
                        
                        await infoChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, GetNoPermissions());
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
                        
                        await mainChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, GetNoPermissions());
                        await mainChannel.AddPermissionOverwriteAsync(role, GetWritePermissions());
                        await mainChannel.AddPermissionOverwriteAsync(managerRole, GetPartialAdminPermissions());

                        project.MainChannelId = mainChannel.Id;
                        createdMain = true;
                    }

                    // Allow auto cleanup if both channels were created
                    project.CleanupOnDeletion = createdMain && createdInfo && createdRole && createdManagerRole;

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
                            Cooldown = TimeSpan.FromHours(1),
                            DoPing = false,
                            ShowOsu = true
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.SetupSuccess, projectName));
            }
            catch (Exception e) {
                Console.WriteLine(e);
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

        public static OverwritePermissions GetNoPermissions() {
            return new (PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny, // This parameter is for the 'viewChannel' permission
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny);
        }

        #endregion

        #endregion

        #region members

        [Command("members")]
        public async Task Members(string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var members = await _context.Members.AsQueryable().Where(o => o.ProjectId == project.Id).ToListAsync();

            await Context.Channel.SendMessageAsync(_resourceService.GenerateMembersListMessage(members));
        }

        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("join")]
        public async Task JoinProject(string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            if (_context.Members.Any(o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id)) {
                await Context.Channel.SendMessageAsync(Strings.AlreadyJoinedMessage);
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
                Console.WriteLine(e);
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
        public async Task LeaveProject(string projectName) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveMemberFromProject(Context.User, projectName, false));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("add")]
        public async Task AddMember(string projectName, IGuildUser user) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddMemberToProject(user, projectName, false));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("remove")]
        public async Task RemoveMember(string projectName, IGuildUser user) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveMemberFromProject(user, projectName, false));
            }
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("promote")]
        public async Task AddManager(string projectName, IGuildUser user) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddManager(user, projectName, false));
            }
        }

        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("demote")]
        public async Task RemoveManager(string projectName, IGuildUser user) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateRemoveManager(user, projectName, false));
            }
        }

        // Revoked regular access for now since this can potentially be abused to create infinite projects
        //[RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("set-owner")]
        public async Task SetOwner(string projectName, IGuildUser user) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateSetOwner(user, projectName, false));
            }
        }
        
        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("alias")]
        public async Task Alias(string projectName, string alias) {
            await Alias(projectName, Context.User, alias);
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("alias")]
        public async Task Alias(string projectName, IUser user, string alias) {
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
                member.Alias = alias;

                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeAliasSuccess, user.Mention, alias));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(Strings.ChangeAliasFail);
            }
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("tags")]
        public async Task Tags(string projectName, string tags) {
            await Alias(projectName, Context.User, tags);
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("tags")]
        public async Task Tags(string projectName, IUser user, string tags) {
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
                member.Tags = tags;

                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeTagsSuccess, user.Mention, tags));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(Strings.ChangeTagsFail);
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("tags")]
        public async Task Tags(string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var tags = (await _context.Members.AsQueryable()
                 .Where(predicate: o => o.ProjectId == project.Id && o.Tags != null).ToListAsync())
                 .SelectMany(o => o.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(o => o.Trim()).Distinct();

                await Context.Channel.SendMessageAsync(string.Format(Strings.AllMemberTags, string.Join(' ', tags)));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(Strings.BackendErrorMessage);
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("priority")]
        public async Task Priority(string projectName, IUser user, int? priority) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.PriorityChangeFail, user.Mention, priority));
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("generate-priorities")]
        public async Task GeneratePriorities(string projectName, int timeWeight = 1) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var members = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id).ToListAsync();

                foreach (var member in members) {
                    var memberUser = Context.Guild.GetUser((ulong) member.UniqueMemberId);
                    if (memberUser is not IGuildUser gu || !gu.JoinedAt.HasValue) {
                        member.Priority = 0;
                        continue;
                    }
                    member.Priority = (int) (DateTimeOffset.UtcNow - gu.JoinedAt.Value).TotalDays * timeWeight;
                }

                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.GeneratePrioritiesSuccess, projectName));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.GeneratePrioritiesFail, projectName));
            }
        }

        #endregion

        #region settings

        // Using admin permissions here to prevent someone assigning @everyone as the project role
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("role")]
        public async Task Role(string projectName, IRole role, bool reassignRoles = true) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeProjectRoleFail, projectName));
            }
        }

        // Using admin permissions here to prevent someone assigning @everyone as the project role
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("manager-role")]
        public async Task ManagerRole(string projectName, IRole role, bool reassignRoles = true) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ChangeManagerRoleFail, projectName));
            }
        }
        
        [RequireProjectOwner(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("rename")]
        public async Task Rename(string projectName, string newProjectName) {
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
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectRenameSuccess, projectName, newProjectName));
            } 
            catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectRenameFail, projectName, newProjectName));
            }
        }
        
        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("description")]
        public async Task Description(string projectName, string description) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                project.Description = description;
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectDescriptionSuccess, projectName));
            } 
            catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectDescriptionFail, projectName));
            }
        }
        
        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("status")]
        public async Task Status(string projectName, ProjectStatus? status) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectStatusFail, projectName, status));
            }
        }
        
        [NamedArgumentType]
        public class NamableOptions {
            public bool? SelfAssignmentAllowed { get; set; }
            public bool? PriorityPicking { get; set; }
            public bool? PartRestrictedUpload { get; set; }
            public bool? DoReminders { get; set; }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("options")]
        public async Task Options(string projectName, NamableOptions options) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectOptionsFail, projectName));
            }
        }
        
        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("max-assignments")]
        public async Task MaxAssignments(string projectName, int? maxAssignments) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectMaxAssignmentsFail, projectName));
            }
        }
        
        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("assignment-lifetime")]
        public async Task AssignmentLifetime(string projectName, TimeSpan? lifetime) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectAssignmentLifetimeFail, projectName));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("main-channel")]
        public async Task MainChannel(string projectName, ITextChannel channel) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectMainChannelFail, channel?.Mention));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("info-channel")]
        public async Task InfoChannel(string projectName, ITextChannel channel) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.ProjectInfoChannelFail, channel?.Mention));
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("deletion-cleanup")]
        public async Task ChangeAutoCleanup(string projectName, bool cleanup) {
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
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.AutoCleanupChangeFail, projectName, cleanup));
            }
        }

        #endregion
        
        private async Task<Project> GetProjectAsync(string projectName) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(_resourceService.GuildNotExistsMessage);
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