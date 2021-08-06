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
    [Group("asn")]
    public class AssignmentModule : ModuleBase<SocketCommandContext> {
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public AssignmentModule(OsuCollabContext context, FileHandlingService fileHandler,
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

            var assignments = await _context.Assignments.AsQueryable()
                .Where(o => o.Part.ProjectId == project.Id)
                .Include(o => o.Part)
                .Include(o => o.Member)
                .ToListAsync();

            assignments.Sort();

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAssignmentListMessage(assignments));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("add")]
        public async Task Add(string projectName, IGuildUser user, DateTime? deadline = null, params string[] partNames) {
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

            foreach (var partName in partNames) {
                var part = await _context.Parts.AsQueryable()
                                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

                if (part == null) {
                    await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, partName, projectName));
                    return;
                }

                try {
                    await _context.Assignments.AddAsync(new Assignment { MemberId = member.Id, PartId = part.Id, Deadline = deadline });
                    await _context.SaveChangesAsync();
                    await Context.Channel.SendMessageAsync(string.Format(Strings.AddAssignmentSuccess, partName, user.Username));
                } catch (Exception e) {
                    Console.WriteLine(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.AddAssignmentFail, partName, user.Username));
                }
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("remove")]
        public async Task Remove(string projectName, IUser user, params string[] partNames) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            foreach (var partName in partNames) {
                var assignment = await GetAssignmentAsync(project, partName, user);

                if (assignment == null) {
                    return;
                }

                try {
                    _context.Assignments.Remove(assignment);
                    await _context.SaveChangesAsync();
                    await Context.Channel.SendMessageAsync(string.Format(Strings.RemoveAssignmentSuccess, user.Username));
                } catch (Exception e) {
                    Console.WriteLine(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.RemoveAssignmentFail, user.Username));
                }
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("deadline")]
        public async Task Deadline(string projectName, string partName, IGuildUser user, DateTime? deadline) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var assignment = await GetAssignmentAsync(project, partName, user);

            if (assignment == null) {
                return;
            }

            try {
                assignment.Deadline = deadline;
                await _context.SaveChangesAsync();
                if (deadline.HasValue)
                    await Context.Channel.SendMessageAsync(string.Format(Strings.MoveDeadlineSuccess, deadline.Value.ToString("yyyy-MM-dd")));
                else
                    await Context.Channel.SendMessageAsync(string.Format(Strings.RemoveDeadlineSuccess, user.Username));
            } catch (Exception e) {
                Console.WriteLine(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.MoveDeadlineFail, deadline));
            }
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("claim")]
        public async Task Claim(string projectName, params string[] partNames) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            foreach (var partName in partNames) {
                var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

                if (part == null) {
                    await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, partName, projectName));
                    return;
                }

                // Check project permissions
                if (!await CheckClaimPermissionsAsync(project, member, part)) {
                    return;
                }

                try {
                    var deadline = DateTime.Now + project.AssignmentLifetime;
                    await _context.Assignments.AddAsync(new Assignment { MemberId = member.Id, PartId = part.Id, Deadline = deadline });
                    await _context.SaveChangesAsync();
                    await Context.Channel.SendMessageAsync(string.Format(Strings.AddAssignmentSuccess, partName, Context.User.Username));
                } catch (Exception e) {
                    Console.WriteLine(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.AddAssignmentFail, partName, Context.User.Username));
                }
            }
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("unclaim")]
        public async Task Unclaim(string projectName, params string[] partNames) {
            await Remove(projectName, Context.User, partNames);
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("done")]
        public async Task Done(string projectName, params string[] partNames) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return;
            }

            foreach (var partName in partNames) {
                var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

                if (part == null) {
                    await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, partName, project.Name));
                    return;
                }

                var assignments = await _context.Assignments.AsQueryable()
                    .Where(o => o.PartId == part.Id && o.MemberId == member.Id).ToListAsync();

                if (member.ProjectRole == ProjectRole.Member && !assignments.Any(o => o.MemberId == member.Id)) {
                    await Context.Channel.SendMessageAsync(Strings.NotAssigned);
                    return;
                }

                try {
                    assignments.ForEach(o => o.Deadline = null);
                    part.Status = PartStatus.Finished;
                    await _context.SaveChangesAsync();
                    await Context.Channel.SendMessageAsync(string.Format(Strings.FinishPartSuccess, part.Name));
                } catch (Exception e) {
                    Console.WriteLine(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.FinishPartFail));
                }
            }
        }

        private async Task<bool> CheckClaimPermissionsAsync(Project project, Member member, Part part) {
            if (member.ProjectRole != ProjectRole.Member) {
                return true;
            }

            if (!project.SelfAssignmentAllowed) {
                await Context.Channel.SendMessageAsync(Strings.SelfAssignmentNotAllowed);
                return false;
            }

            bool claimed = await _context.Assignments.AsQueryable().AnyAsync(o => o.Part.Id == part.Id);
            if (claimed) {
                await Context.Channel.SendMessageAsync(Strings.PartClaimedAlready);
                return false;
            }

            int assignments = await _context.Assignments.AsQueryable().CountAsync(o => o.MemberId == member.Id && o.Part.ProjectId == project.Id && o.Deadline.HasValue);
            if (project.MaxAssignments.HasValue && assignments >= project.MaxAssignments) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.MaxAssignmentsReached, project.MaxAssignments));
                return false;
            }

            return true;
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

        private async Task<Assignment> GetAssignmentAsync(Project project, string partName, IUser user) {
            var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

            if (part == null) {
                await Context.Channel.SendMessageAsync(string.Format(Strings.PartNotExists, partName, project.Name));
                return null;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await Context.Channel.SendMessageAsync(Strings.MemberNotExistsMessage);
                return null;
            }

            var assignment = await _context.Assignments.AsQueryable()
                .SingleOrDefaultAsync(o => o.PartId == part.Id && o.MemberId == member.Id);

            if (assignment == null) {
                await Context.Channel.SendMessageAsync(Strings.AssignmentNotExists);
            }

            return assignment;
        }
    }
}