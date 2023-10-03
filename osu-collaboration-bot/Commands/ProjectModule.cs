using CollaborationBot.Autocomplete;
using CollaborationBot.Entities;
using CollaborationBot.Preconditions;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace CollaborationBot.Commands {
    [Group("project", "Everything about project and member related stuff")]
    public class ProjectModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly InputSanitizingService _inputSanitizer;
        private readonly CommonService _common;
        private readonly DiscordSocketClient _client;
        private readonly AppSettings _appSettings;

        public ProjectModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, InputSanitizingService inputSanitizingService, CommonService common, DiscordSocketClient client, AppSettings appSettings) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _inputSanitizer = inputSanitizingService;
            _common = common;
            _client = client;
            _appSettings = appSettings;
        }

        #region files
        
        
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

            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                await RespondAsync(Strings.ProjectNotExistMessage);
                return;
            }

            string errorMsg = await _fileHandler.DownloadBaseFile(Context.Guild, projectName, attachment);
            if (errorMsg is not null) {
                await RespondAsync(errorMsg);
                return;
            }

            await RespondAsync(string.Format(Strings.UploadBaseFileSuccess, attachment.Filename, projectName));

            // Reset the activity timer
            project.LastActivity = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            
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

        //[RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [SlashCommand("create", "Creates a new project")]
        public async Task Create([Summary("project", "The name of the new project")]string projectName) {
            try {
                var guild = await _context.Guilds.AsAsyncEnumerable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

                if (guild == null) {
                    await RespondAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
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

                var projectEntry = await _context.Projects.AddAsync(new Project {Name = projectName, GuildId = guild.Id, Status = ProjectStatus.NotStarted, LastActivity = DateTime.UtcNow});
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
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            // Start with a response because this operation may take longer than 3 seconds
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await RespondAsync(string.Format(Strings.RemoveProjectStart, projectName));

            try {
                await DeleteProjectAsync(project, Context.Guild, _context, _fileHandler);

                stopwatch.Stop();
                await FollowupAsync(string.Format(Strings.RemoveProjectSuccess, projectName, stopwatch.Elapsed.TotalSeconds));
            }
            catch (Exception e) {
                logger.Error(e);
                await FollowupAsync(string.Format(Strings.RemoveProjectFail, projectName));
            }
        }

        public static async Task DeleteProjectAsync(Project project, SocketGuild guild, OsuCollabContext dbContext, FileHandlingService fileHandler) {
            dbContext.Projects.Remove(project);
            await dbContext.SaveChangesAsync();

            fileHandler.DeleteProjectDirectory(guild, project.Name);

            // Delete channels and roles
            if (project.CleanupOnDeletion) {
                // Main channel
                if (project.MainChannelId.HasValue) {
                    var mainChannel = guild.GetTextChannel((ulong) project.MainChannelId);
                    if (mainChannel != null) {
                        await mainChannel.DeleteAsync();
                    }
                }
                // Info channel
                if (project.InfoChannelId.HasValue) {
                    var infoChannel = guild.GetTextChannel((ulong) project.InfoChannelId);
                    if (infoChannel != null) {
                        await infoChannel.DeleteAsync();
                    }
                }
                // Participant role
                if (project.UniqueRoleId.HasValue) {
                    var role = guild.GetRole((ulong) project.UniqueRoleId);
                    if (role != null) {
                        await role.DeleteAsync();
                    }
                }
                // Manager role
                if (project.ManagerRoleId.HasValue) {
                    var role = guild.GetRole((ulong) project.ManagerRoleId);
                    if (role != null) {
                        await role.DeleteAsync();
                    }
                }
            }
        }
        
        [SlashCommand("setup", "Automatically sets-up the project, complete with roles, channels, and update notifications")]
        public async Task Setup([RequireProjectOwner][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            // Make channel, role, and permissions
            // Automatic channels and roles will be marked for deletion on project deletion unless states otherwise

            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            // Start with a response because this operation may take longer than 3 seconds
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await RespondAsync(string.Format(Strings.SetupStart, projectName));

            var guild = project.Guild;

            try {
                // Auto cleanup for auto generated collab
                project.CleanupOnDeletion = true;
                await _context.SaveChangesAsync();

                var members = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id).ToListAsync();

                // Get/Create project role
                async Task<IRole> createMemberRole() {
                    var newMemberRole = await Context.Guild.CreateRoleAsync($"{project.Name} Participant", isMentionable: true);

                    project.UniqueRoleId = newMemberRole.Id;
                    await _context.SaveChangesAsync();

                    // Give all members the new role and remove the old role if possible
                    await UpdateRolesAsync(members.Select(o => (ulong)o.UniqueMemberId), Context.Guild, newMemberRole);

                    return newMemberRole;
                }

                IRole memberRole = null;
                if (guild.GenerateRoles) {
                    memberRole = project.UniqueRoleId.HasValue
                        ? Context.Guild.GetRole((ulong)project.UniqueRoleId) ?? await createMemberRole()
                        : await createMemberRole();
                }

                // Get/Create manager role
                async Task<IRole> createManagerRole() {
                    var newManagerRole = await Context.Guild.CreateRoleAsync($"{project.Name} Manager", isMentionable: true);

                    project.ManagerRoleId = newManagerRole.Id;
                    await _context.SaveChangesAsync();

                    // Give all members the new role and remove the old role if possible
                    await UpdateRolesAsync(
                        members.Where(o => o.ProjectRole != ProjectRole.Member).Select(o => (ulong)o.UniqueMemberId),
                        Context.Guild, newManagerRole);

                    return newManagerRole;
                }

                IRole managerRole = null;
                if (guild.GenerateRoles) {
                    managerRole = project.ManagerRoleId.HasValue
                        ? Context.Guild.GetRole((ulong)project.ManagerRoleId) ?? await createManagerRole()
                        : await createManagerRole();
                }

                if (guild.CollabCategoryId.HasValue) {
                    // Create info channel
                    async Task<ITextChannel> createInfoChannel() {
                        if (!guild.CollabCategoryId.HasValue) return null;
                        var newInfoChannel = await Context.Guild.CreateTextChannelAsync($"{project.Name}-info",
                            prop => prop.CategoryId = (ulong)guild.CollabCategoryId);
                        project.InfoChannelId = newInfoChannel.Id;
                        await _context.SaveChangesAsync();
                        return newInfoChannel;
                    }

                    var infoChannel = project.InfoChannelId.HasValue
                        ? Context.Guild.GetTextChannel((ulong)project.InfoChannelId.Value) ?? await createInfoChannel()
                        : await createInfoChannel();

                    // Create general channel
                    async Task<ITextChannel> createMainChannel() {
                        if (!guild.CollabCategoryId.HasValue) return null;
                        var newMainChannel = await Context.Guild.CreateTextChannelAsync($"{project.Name}-general",
                            prop => prop.CategoryId = (ulong)guild.CollabCategoryId);

                        project.MainChannelId = newMainChannel.Id;
                        await _context.SaveChangesAsync();
                        return newMainChannel;
                    }

                    var mainChannel = project.MainChannelId.HasValue
                        ? Context.Guild.GetTextChannel((ulong)project.MainChannelId.Value) ?? await createMainChannel()
                        : await createMainChannel();

                    // Update permissions
                    await UpdateProjectChannelPermissions(Context, project, members, mainChannel, infoChannel, memberRole, managerRole);

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
                stopwatch.Stop();
                await FollowupAsync(string.Format(Strings.SetupSuccess, projectName, stopwatch.Elapsed.TotalSeconds));
            }
            catch (Exception e) {
                logger.Error(e);
                await FollowupAsync(string.Format(Strings.SetupFail, projectName));
            }
        }

        #endregion

        #region members

        [SlashCommand("add", "Adds a new member to the project")]
        public async Task AddMember([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName, 
            [Summary("user", "The user to add")]IGuildUser user) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

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
                await GrantProjectRole(Context, user, project);
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
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, user);

            if (member == null) {
                return;
            }

            if (member.ProjectRole == ProjectRole.Owner) {
                await RespondAsync(Strings.OwnerCannotLeaveMessage);
                return;
            }

            try {
                _context.Members.Remove(member);
                await _context.SaveChangesAsync();
                await RevokeManagerRole(Context, user, project);
                await RevokeProjectRole(Context, user, project);
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
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, user);

            if (member == null) {
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
                await GrantManagerRole(Context, user, project);
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
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, user);

            if (member == null) {
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
                await RevokeManagerRole(Context, user, project);
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
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, user);

            if (member == null) {
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
                await GrantManagerRole(Context, user, project);
                await RespondAsync(
                    _resourceService.GenerateSetOwner(user, projectName));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(
                    _resourceService.GenerateSetOwner(user, projectName, false));
            }
        }
        
        [SlashCommand("alias", "Changes the alias of a member of the project")]
        public async Task Alias([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The member")]IUser user,
            [Summary("alias", "The new alias")]string alias) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            if (!_inputSanitizer.IsValidName(alias)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, user);

            if (member == null) {
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
        
        [SlashCommand("tags", "Changes the tags of a member of the project")]
        public async Task Tags([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The member")]IUser user,
            [Summary("tags", "The new tags")]string tags) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            if (!_inputSanitizer.IsValidName(tags)) {
                await RespondAsync(Strings.IllegalInput);
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, user);

            if (member == null) {
                return;
            }

            try {
                member.Tags = tags;

                await _context.SaveChangesAsync();
                await RespondAsync(string.Format(Strings.ChangeTagsSuccess, user.Mention, tags));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.ChangeTagsFail);
            }
        }
        
        [SlashCommand("gettags", "Gets all the tags of the project including aliases")]
        public async Task Tags([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            try {
                var tags = await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id && o.Tags != null).Select(o => o.Tags).ToListAsync();
                string[] namesOrAliases = await Task.WhenAll((await _context.Members.AsQueryable()
                    .Where(o => o.ProjectId == project.Id).ToListAsync()).Select(async o => await _resourceService.MemberAliasOrName(o)));
                var tagsClean = namesOrAliases.Concat(tags)
                 .SelectMany(o => o.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(o => o.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);

                await RespondAsync(string.Format(Strings.AllMemberTags, string.Join(' ', tagsClean)));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(Strings.BackendErrorMessage);
            }
        }
        
        [SlashCommand("id", "Changes the osu! profile ID of a member of the project")]
        public async Task Id([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
            [Summary("user", "The member")] IUser user,
            [Summary("id", "The new ID")] ulong id) {
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, user);

            if (member == null) {
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
            var project = await _common.GetProjectAsync(Context, _context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, _context, project, user);

            if (member == null) {
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
            var project = await _common.GetProjectAsync(Context, _context, projectName);

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

                    var memberUser = await _client.Rest.GetGuildUserAsync(Context.Guild.Id, (ulong) member.UniqueMemberId);
                    if (memberUser is not IGuildUser {JoinedAt: not null } gu) {
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

        [SlashCommand("rename", "Renames the project")]
        public async Task Rename([RequireProjectOwner][Summary("project", "The old project name")] string projectName,
            [Summary("newname", "The new project name")] string newProjectName) {
            if (!_inputSanitizer.IsValidProjectName(newProjectName)) {
                await RespondAsync(string.Format(Strings.IllegalProjectName, newProjectName));
                return;
            }

            var project = await _common.GetProjectAsync(Context, _context, projectName);

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
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.ProjectRenameFail, projectName, newProjectName));
            }
        }

        #endregion

        #region role & permission helpers

        private static async Task UpdateRolesAsync(IEnumerable<ulong> memberIds, SocketGuild guild, IRole addRole = null, IRole removeRole = null) {
            foreach (var member in memberIds.Select(guild.GetUser)) {
                if (member is not IGuildUser gu) continue;

                if (removeRole is not null)
                    try {
                        await gu.RemoveRoleAsync(removeRole);
                    } catch (Exception ex) {
                        logger.Error(ex, "Could not remove role {role} from user {user}.", removeRole.Id, member.Id);
                    }
                if (addRole is not null)
                    try {
                        await gu.AddRoleAsync(addRole);
                    } catch (Exception ex) {
                        logger.Error(ex, "Could not add role {role} to user {user}.", addRole.Id, member.Id);
                    }
            }
        }

        private static async Task UpdateProjectChannelPermissions(SocketInteractionContext context, Project project, List<Member> members, IGuildChannel mainChannel = null, IGuildChannel infoChannel = null, IRole memberRole = null, IRole managerRole = null) {
            if (project.MainChannelId.HasValue) {
                var channel = mainChannel ?? context.Guild.GetChannel((ulong)project.MainChannelId);
                await UpdateChannelPermissions(project, channel, members, false, memberRole, managerRole);
            }

            if (project.InfoChannelId.HasValue) {
                var channel = infoChannel ?? context.Guild.GetChannel((ulong)project.InfoChannelId);
                await UpdateChannelPermissions(project, channel, members, true, memberRole, managerRole);
            }
        }

        private static async Task UpdateChannelPermissions(Project project, IGuildChannel channel, List<Member> members, bool isInfoChannel, IRole memberRole = null, IRole managerRole = null) {
            await channel.AddPermissionOverwriteAsync(channel.Guild.EveryoneRole, GetNoPermissions(channel));

            if (project.Guild.GenerateRoles) {
                if (project.UniqueRoleId.HasValue) {
                    var role = memberRole ?? channel.Guild.GetRole((ulong)project.UniqueRoleId);
                    await channel.AddPermissionOverwriteAsync(role, isInfoChannel ? GetReadPermissions() : GetWritePermissions());
                }
                if (project.ManagerRoleId.HasValue) {
                    var role = managerRole ?? channel.Guild.GetRole((ulong)project.ManagerRoleId);
                    await channel.AddPermissionOverwriteAsync(role, GetPartialAdminPermissions());
                }
            }else {
                foreach (var member in members) {
                    await UpdateChannelMemberPermission(channel, (ulong)member.UniqueMemberId, member.ProjectRole,
                        isInfoChannel);
                }
            }
        }

        private static async Task UpdateChannelMemberPermission(IGuildChannel channel, ulong memberId, ProjectRole? role, bool isInfoChannel) {
            var user = await channel.Guild.GetUserAsync(memberId);
            if (user is null) return;

            switch (role) {
                case ProjectRole.Owner:
                case ProjectRole.Manager:
                    await channel.AddPermissionOverwriteAsync(user, GetPartialAdminPermissions());
                    break;
                case ProjectRole.Member:
                    await channel.AddPermissionOverwriteAsync(user, isInfoChannel ? GetReadPermissions() : GetWritePermissions());
                    break;
                default:
                    await channel.RemovePermissionOverwriteAsync(user);
                    break;
            }
        }

        private static async Task UpdateProjectRole(SocketGuild guild, IPresence user, Project project, bool managerRole, bool remove) {
            if (user is not IGuildUser gu) return;

            var roleId = managerRole ? project.ManagerRoleId : project.UniqueRoleId;
            if (roleId.HasValue) {
                var role = guild.GetRole((ulong) roleId);
                if (role is not null) {
                    if (remove)
                        await gu.RemoveRoleAsync(role);
                    else
                        await gu.AddRoleAsync(role);
                }
            }

            ProjectRole? projectRole = managerRole ?
                remove ? ProjectRole.Member : ProjectRole.Manager :
                remove ? null : ProjectRole.Member;

            if (!project.Guild.GenerateRoles && project.MainChannelId.HasValue) {
                var channel = guild.GetChannel((ulong)project.MainChannelId);
                await UpdateChannelMemberPermission(channel, gu.Id, projectRole, false);
            }

            if (!project.Guild.GenerateRoles && project.InfoChannelId.HasValue) {
                var channel = guild.GetChannel((ulong)project.InfoChannelId);
                await UpdateChannelMemberPermission(channel, gu.Id, projectRole, true);
            }
        }

        public static async Task GrantProjectRole(SocketInteractionContext context, IPresence user, Project project) {
            await UpdateProjectRole(context.Guild, user, project, false, false);
        }

        public static async Task RevokeProjectRole(SocketInteractionContext context, IPresence user, Project project) {
            await UpdateProjectRole(context.Guild, user, project, false, true);
        }

        public static async Task GrantManagerRole(SocketInteractionContext context, IPresence user, Project project) {
            await UpdateProjectRole(context.Guild, user, project, true, false);
        }

        public static async Task RevokeManagerRole(SocketInteractionContext context, IPresence user, Project project) {
            await UpdateProjectRole(context.Guild, user, project, true, true);
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
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow);
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
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Deny,
                PermValue.Deny,
                PermValue.Allow,
                PermValue.Allow,
                PermValue.Allow);
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

        public static OverwritePermissions GetNoPermissions(IChannel channel) {
            return OverwritePermissions.DenyAll(channel);
        }

        #endregion

        #endregion

        #region settings

        [Group("options", "All project options")]
        public class ProjectOptionsModule : InteractionModuleBase<SocketInteractionContext> {
            private readonly OsuCollabContext _context;
            private readonly InputSanitizingService _inputSanitizer;
            private readonly CommonService _common;

            public ProjectOptionsModule(OsuCollabContext context, InputSanitizingService inputSanitizingService, CommonService common) {
                _context = context;
                _inputSanitizer = inputSanitizingService;
                _common = common;
            }

            [SlashCommand("options", "Configures several boolean project options")]
            public async Task Options([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("canclaim", "Whether members may claim parts on their own")] bool? selfAssignmentAllowed = null,
                [Summary("canjoin", "Whether anyone may join the project")] bool? anyoneJoinAllowed = null,
                [Summary("prioritypicking", "Whether priority picking is enabled")] bool? priorityPicking = null,
                [Summary("partrestrictedupload", "Whether to restrict part submission to just the assigned parts")] bool? partRestrictedUpload = null,
                [Summary("doreminders", "Whether to automatically remind members about their deadlines")] bool? doReminders = null) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

                if (project == null) {
                    return;
                }

                try {
                    int n = 0;
                    if (selfAssignmentAllowed.HasValue) {
                        project.SelfAssignmentAllowed = selfAssignmentAllowed.Value;
                        n++;
                    }
                    if (anyoneJoinAllowed.HasValue) {
                        project.JoinAllowed = anyoneJoinAllowed.Value;
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
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ProjectOptionsFail, projectName));
                }
            }

            // Using admin permissions here to prevent someone assigning @everyone as the project role
            [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
            [SlashCommand("role", "Changes the member role of a project and optionally assigns the new role to all members")]
            public async Task Role([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("role", "The new member role")] IRole role,
                [Summary("update", "Whether to update member roles and channel permissions")] bool update = true) {
                try {
                    var project = await _common.GetProjectAsync(Context, _context, projectName);

                    if (project == null) {
                        return;
                    }

                    var oldRole = project.UniqueRoleId.HasValue ? Context.Guild.GetRole((ulong)project.UniqueRoleId.Value) : null;

                    project.UniqueRoleId = role.Id;
                    await _context.SaveChangesAsync();

                    if (update) {
                        // Remove old role, and add new role
                        var members = await _context.Members.AsQueryable()
                            .Where(o => o.ProjectId == project.Id).ToListAsync();

                        await UpdateRolesAsync(members.Select(o => (ulong)o.UniqueMemberId), Context.Guild, role, oldRole);

                        if (project.Guild.GenerateRoles) {
                            await UpdateProjectChannelPermissions(Context, project, members, memberRole: role);
                        }
                    }

                    await RespondAsync(string.Format(Strings.ChangeProjectRoleSuccess, projectName, role.Name));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ChangeProjectRoleFail, projectName));
                }
            }

            // Using admin permissions here to prevent someone assigning @everyone as the project role
            [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
            [SlashCommand("managerrole", "Changes the manager role of the project and optionally assigns the new role to all managers")]
            public async Task ManagerRole([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("role", "The new manager role")] IRole role,
                [Summary("update", "Whether to update member roles and channel permissions")] bool update = true) {
                try {
                    var project = await _common.GetProjectAsync(Context, _context, projectName);

                    if (project == null) {
                        return;
                    }

                    var oldRole = project.ManagerRoleId.HasValue ? Context.Guild.GetRole((ulong)project.ManagerRoleId.Value) : null;

                    project.ManagerRoleId = role.Id;
                    await _context.SaveChangesAsync();

                    if (update) {
                        // Give all members the new role and remove the old role if possible
                        var members = await _context.Members.AsQueryable()
                            .Where(o => o.ProjectId == project.Id && o.ProjectRole != ProjectRole.Member).ToListAsync();

                        await UpdateRolesAsync(members.Select(o => (ulong)o.UniqueMemberId), Context.Guild, role, oldRole);

                        if (project.Guild.GenerateRoles) {
                            await UpdateProjectChannelPermissions(Context, project, members, managerRole: role);
                        }
                    }

                    await RespondAsync(string.Format(Strings.ChangeManagerRoleSuccess, projectName, role.Name));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ChangeManagerRoleFail, projectName));
                }
            }

            [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
            [SlashCommand("rolecolor", "Changes the color of the roles of the project")]
            public async Task RoleColor([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("color", "The new color as Hex code")] Color color) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

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

            [SlashCommand("description", "Changes the description of the project")]
            public async Task Description([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("description", "The new description")] string description) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

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
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ProjectDescriptionFail, projectName));
                }
            }

            [SlashCommand("status", "Changes the status of the project")]
            public async Task Status([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("status", "The new status")] ProjectStatus status) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

                if (project == null) {
                    return;
                }

                try {
                    project.Status = status;
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.ProjectStatusSuccess, projectName, status));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ProjectStatusFail, projectName, status));
                }
            }

            [SlashCommand("maxassignments", "Changes the maximum number of allowed assignments for members of the project")]
            public async Task MaxAssignments([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("maxassignments", "The new maximum number of allowed assignments (can be null)")] int? maxAssignments) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

                if (project == null) {
                    return;
                }

                try {
                    project.MaxAssignments = maxAssignments;
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.ProjectMaxAssignmentsSuccess, projectName, maxAssignments));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ProjectMaxAssignmentsFail, projectName));
                }
            }

            [SlashCommand("assignmentlifetime", "Changes the default duration of assignments of the project")]
            public async Task AssignmentLifetime([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("lifetime", "The new duration of assignments (dd:hh:mm:ss:fff) (can be null)")] TimeSpan? lifetime) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

                if (project == null) {
                    return;
                }

                try {
                    project.AssignmentLifetime = lifetime;
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.ProjectAssignmentLifetimeSuccess, projectName, lifetime.HasValue ? lifetime.Value.ToString("g") : Strings.Unbounded));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ProjectAssignmentLifetimeFail, projectName));
                }
            }

            [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
            [SlashCommand("mainchannel", "Changes the main channel of the project")]
            public async Task MainChannel([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("channel", "The new main channel")] ITextChannel channel) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

                if (project == null) {
                    return;
                }

                try {
                    project.MainChannelId = channel?.Id;
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.ProjectMainChannelSuccess, channel?.Mention));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ProjectMainChannelFail, channel?.Mention));
                }
            }

            [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
            [SlashCommand("infochannel", "Changes the info channel of the project")]
            public async Task InfoChannel([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("channel", "The new info channel")] ITextChannel channel) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

                if (project == null) {
                    return;
                }

                try {
                    project.InfoChannelId = channel?.Id;
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.ProjectInfoChannelSuccess, channel?.Mention));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.ProjectInfoChannelFail, channel?.Mention));
                }
            }

            [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
            [SlashCommand("deletioncleanup", "Changes whether to remove the roles and channels assigned to the project upon project deletion")]
            public async Task ChangeAutoCleanup([Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName,
                [Summary("cleanup", "Whether to do cleanup")] bool cleanup) {
                var project = await _common.GetProjectAsync(Context, _context, projectName);

                if (project == null) {
                    return;
                }

                try {
                    project.CleanupOnDeletion = cleanup;
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.AutoCleanupChangeSuccess, projectName, cleanup));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.AutoCleanupChangeFail, projectName, cleanup));
                }
            }
        }

        #endregion
    }
}