using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord.Interactions;
using System.Threading.Tasks;
using System.Linq;
using Discord;
using CollaborationBot.Preconditions;
using System;
using NLog;
using System.Collections.Generic;
using CollaborationBot.Autocomplete;
using Microsoft.EntityFrameworkCore;

namespace CollaborationBot.Commands {
    [Group("asn", "Everything about assignments")]
    public class AssignmentModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;
        private readonly ResourceService _resourceService;
        private readonly AppSettings _appSettings;

        public AssignmentModule(OsuCollabContext context,
            ResourceService resourceService,
            AppSettings appSettings) {
            _context = context;
            _resourceService = resourceService;
            _appSettings = appSettings;
        }
        
        [SlashCommand("list", "Lists all the assignments in the project")]
        public async Task List([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
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

            await RespondAsync(_resourceService.GenerateAssignmentListMessage(assignments));
        }
        
        [SlashCommand("add", "Adds one or more assignments")]
        public async Task Add([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The member to assign to")]IGuildUser user,
            [Summary("deadline", "The deadline for the assignment (can be null)")]DateTime? deadline = null, 
            [Summary("parts", "The parts to assign to the member")]params string[] partNames) {
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

            foreach (var partName in partNames) {
                var part = await _context.Parts.AsQueryable()
                                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

                if (part == null) {
                    await RespondAsync(string.Format(Strings.PartNotExists, partName, projectName));
                    return;
                }

                try {
                    await _context.Assignments.AddAsync(new Assignment { MemberId = member.Id, PartId = part.Id, Deadline = deadline, LastReminder = DateTime.UtcNow });
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.AddAssignmentSuccess, partName, user.Username));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.AddAssignmentFail, partName, user.Username));
                }
            }
        }
        
        [SlashCommand("remove", "Removes one or more assignments")]
        public async Task Remove([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The member to remove assignments from")]IUser user,
            [Summary("parts", "The parts to unassign from the member")]params string[] partNames) {
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
                    await RespondAsync(string.Format(Strings.RemoveAssignmentSuccess, user.Username));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.RemoveAssignmentFail, user.Username));
                }
            }
        }

        [SlashCommand("deadline", "Changes the deadline of the assignment")]
        public async Task Deadline([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("part", "The part of the assignment")]string partName,
            [Summary("user", "The member of the assignment")]IGuildUser user,
            [Summary("deadline", "The new deadline (can be null)")]DateTime? deadline) {
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
                    await RespondAsync(string.Format(Strings.MoveDeadlineSuccess, deadline.Value.ToString("yyyy-MM-dd")));
                else
                    await RespondAsync(string.Format(Strings.RemoveDeadlineSuccess, user.Username));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.MoveDeadlineFail, deadline));
            }
        }
        
        [SlashCommand("claim", "Claims one or more parts and assigns them to you")]
        public async Task Claim([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("parts", "The parts to claim")]params string[] partNames) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            foreach (var partName in partNames) {
                var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

                if (part == null) {
                    await RespondAsync(string.Format(Strings.PartNotExists, partName, projectName));
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
                                var victimUser = Context.Guild.GetUser((ulong) victim.Member.UniqueMemberId);
                                var victimDm = await victimUser.CreateDMChannelAsync();
                                await victimDm.SendMessageAsync(string.Format(Strings.PriorityPartSteal,
                                    Context.User.Username, member.Priority, partName,
                                    victim.Member.Priority));
                            }
                        } else {
                            // Sorry you can't steal this
                            await RespondAsync(Strings.PartClaimedAlready);
                            return;
                        }
                    }

                    var deadline = DateTime.UtcNow + project.AssignmentLifetime;
                    await _context.Assignments.AddAsync(new Assignment { MemberId = member.Id, PartId = part.Id, Deadline = deadline, LastReminder = DateTime.UtcNow});
                    await _context.SaveChangesAsync();
                    await RespondAsync(string.Format(Strings.AddAssignmentSuccess, partName, Context.User.Username));
                } catch (Exception e) {
                    logger.Error(e);
                    await RespondAsync(string.Format(Strings.AddAssignmentFail, partName, Context.User.Username));
                }
            }
        }
        
        [SlashCommand("unclaim", "Unclaims one or more parts and unassigns them")]
        public async Task Unclaim([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("parts", "The parts to unclaim")]params string[] partNames) {
            await Remove(projectName, Context.User, partNames);
        }
        
        [SlashCommand("done", "Marks one or more parts as done")]
        public async Task Done([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("parts", "The parts to complete")]params string[] partNames) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return;
            }

            foreach (var partName in partNames) {
                var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

                if (part == null) {
                    await RespondAsync(string.Format(Strings.PartNotExists, partName, project.Name));
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

        [SlashCommand("draintimes", "Calculates the total draintime assigned to each participant.")]
        public async Task Draintimes([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName) {
            var project = await GetProjectAsync(projectName);

            if (project == null) {
                return;
            }

            try {
                var assignments = (await _context.Assignments.AsQueryable()
                    .Where(o => o.Part.ProjectId == project.Id)
                    .Include(o => o.Part)
                    .Include(o => o.Member).ToListAsync())
                    .GroupBy(o => o.Member);

                var draintimes = new List<KeyValuePair<Member, int>>();
                foreach (var ass in assignments) {
                    int draintime = ass.Sum(o => o.Part.End.HasValue && o.Part.Start.HasValue ? o.Part.End.Value - o.Part.Start.Value : 0);
                    draintimes.Add(new KeyValuePair<Member, int>(ass.Key, draintime));
                }

                await RespondAsync(_resourceService.GenerateDraintimesListMessage(draintimes.OrderBy(o => o.Value).ToList()));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.BackendErrorMessage, projectName));
            }
        }

        private async Task<bool> CheckClaimPermissionsAsync(Project project, Member member, Part part) {
            if (member.ProjectRole != ProjectRole.Member) {
                return true;
            }

            if (!project.SelfAssignmentAllowed) {
                await RespondAsync(Strings.SelfAssignmentNotAllowed);
                return false;
            }

            // Count the number of active assignments (has deadline)
            int assignments = await _context.Assignments.AsQueryable().CountAsync(o => o.MemberId == member.Id && o.Part.ProjectId == project.Id && o.Deadline.HasValue);
            if (project.MaxAssignments.HasValue && assignments >= project.MaxAssignments) {
                await RespondAsync(string.Format(Strings.MaxAssignmentsReached, project.MaxAssignments));
                return false;
            }

            return true;
        }

        private async Task<Project> GetProjectAsync(string projectName) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await RespondAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            var project = await _context.Projects.AsQueryable().SingleOrDefaultAsync(o => o.GuildId == guild.Id && o.Name == projectName);

            if (project == null) {
                await RespondAsync(Strings.ProjectNotExistMessage);
                return null;
            }

            return project;
        }

        private async Task<Assignment> GetAssignmentAsync(Project project, string partName, IUser user) {
            var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

            if (part == null) {
                await RespondAsync(string.Format(Strings.PartNotExists, partName, project.Name));
                return null;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await RespondAsync(Strings.MemberNotExistsMessage);
                return null;
            }

            var assignment = await _context.Assignments.AsQueryable()
                .SingleOrDefaultAsync(o => o.PartId == part.Id && o.MemberId == member.Id);

            if (assignment == null) {
                await RespondAsync(Strings.AssignmentNotExists);
            }

            return assignment;
        }
    }
}