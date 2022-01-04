﻿using CollaborationBot.Entities;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using Discord;
using CollaborationBot.Preconditions;
using System;
using NLog;
using System.Collections.Generic;

namespace CollaborationBot.Commands {
    [Group("asn")]
    [Name("Assignment module")]
    [Summary("Everything about assignments")]
    public class AssignmentModule : ModuleBase<SocketCommandContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;
        private readonly UserHelpService _userHelpService;
        private readonly AppSettings _appSettings;

        public AssignmentModule(OsuCollabContext context, FileHandlingService fileHandler,
            ResourceService resourceService, UserHelpService userHelpService,
            AppSettings appSettings) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
            _userHelpService = userHelpService;
            _appSettings = appSettings;
        }

        [Command("help")]
        [Summary("Shows command information")]
        public async Task Help(string command = "") {
            await _userHelpService.DoHelp(Context, "Assignment module", "asn", command);
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("list")]
        [Summary("Lists all the assignments in the project")]
        public async Task List([Summary("The project")]string projectName) {
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
        [Summary("Adds one or more assignments")]
        public async Task Add([Summary("The project")]string projectName,
            [Summary("The member to assign to")]IGuildUser user,
            [Summary("The deadline for the assignment (can be null)")]DateTime? deadline = null, 
            [Summary("The parts to assign to the member")]params string[] partNames) {
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
                    await _context.Assignments.AddAsync(new Assignment { MemberId = member.Id, PartId = part.Id, Deadline = deadline, LastReminder = DateTime.UtcNow });
                    await _context.SaveChangesAsync();
                    await Context.Channel.SendMessageAsync(string.Format(Strings.AddAssignmentSuccess, partName, user.Username));
                } catch (Exception e) {
                    logger.Error(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.AddAssignmentFail, partName, user.Username));
                }
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("remove")]
        [Summary("Removes one or more assignments")]
        public async Task Remove([Summary("The project")]string projectName,
            [Summary("The member to remove assignments from")]IUser user,
            [Summary("The parts to unassign from the member")]params string[] partNames) {
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
                    logger.Error(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.RemoveAssignmentFail, user.Username));
                }
            }
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("deadline")]
        [Summary("Changes the deadline of the assignment")]
        public async Task Deadline([Summary("The project")]string projectName,
            [Summary("The part of the assignment")]string partName,
            [Summary("The member of the assignment")]IGuildUser user,
            [Summary("The new deadline (can be null)")]DateTime? deadline) {
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
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.MoveDeadlineFail, deadline));
            }
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("claim")]
        [Summary("Claims one or more parts and assigns them to you")]
        public async Task Claim([Summary("The project")]string projectName,
            [Summary("The parts to claim")]params string[] partNames) {
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
                    var claimants = await _context.Assignments.AsQueryable()
                        .Where(o => o.Part.Id == part.Id && o.MemberId != member.Id)
                        .Include(o => o.Member).ToListAsync();
                    if (claimants.Count > 0) {
                        if (!project.PriorityPicking) {
                            await Context.Channel.SendMessageAsync(Strings.PartClaimedAlready);
                            return;
                        }

                        // Perhaps steal parts
                        if (claimants.All(o => o.Member.Priority < member.Priority)) {
                            // EZ steal
                            _context.Assignments.RemoveRange(claimants);

                            // Notify theft
                            foreach (var victim in claimants) {
                                var victimUser = Context.Guild.GetUser((ulong) victim.Member.UniqueMemberId);
                                await Context.Channel.SendMessageAsync(string.Format(Strings.PriorityPartSteal,
                                    Context.User.Mention, member.Priority, partName, victimUser.Mention,
                                    victim.Member.Priority));
                            }
                        } else {
                            // Sorry you can't steal this
                            await Context.Channel.SendMessageAsync(Strings.PartClaimedAlready);
                            return;
                        }
                    }

                    var deadline = DateTime.UtcNow + project.AssignmentLifetime;
                    await _context.Assignments.AddAsync(new Assignment { MemberId = member.Id, PartId = part.Id, Deadline = deadline, LastReminder = DateTime.UtcNow});
                    await _context.SaveChangesAsync();
                    await Context.Channel.SendMessageAsync(string.Format(Strings.AddAssignmentSuccess, partName, Context.User.Username));
                } catch (Exception e) {
                    logger.Error(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.AddAssignmentFail, partName, Context.User.Username));
                }
            }
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("unclaim")]
        [Summary("Unclaims one or more parts and unassigns them")]
        public async Task Unclaim([Summary("The project")]string projectName,
            [Summary("The parts to unclaim")]params string[] partNames) {
            await Remove(projectName, Context.User, partNames);
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("done")]
        [Summary("Marks one or more parts as done")]
        public async Task Done([Summary("The project")]string projectName,
            [Summary("The parts to complete")]params string[] partNames) {
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

                if (member.ProjectRole == ProjectRole.Member && assignments.All(o => o.MemberId != member.Id)) {
                    await Context.Channel.SendMessageAsync(Strings.NotAssigned);
                    return;
                }

                try {
                    assignments.ForEach(o => o.Deadline = null);
                    part.Status = PartStatus.Finished;
                    await _context.SaveChangesAsync();
                    await Context.Channel.SendMessageAsync(string.Format(Strings.FinishPartSuccess, part.Name));
                } catch (Exception e) {
                    logger.Error(e);
                    await Context.Channel.SendMessageAsync(string.Format(Strings.FinishPartFail));
                }
            }
        }

        [RequireProjectMember(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("draintimes")]
        [Alias("draintime")]
        [Summary("Calculates the total draintime assigned to each participant.")]
        public async Task Draintimes([Summary("The project")] string projectName) {
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

                await Context.Channel.SendMessageAsync(_resourceService.GenerateDraintimesListMessage(draintimes.OrderBy(o => o.Value).ToList()));
            } catch (Exception e) {
                logger.Error(e);
                await Context.Channel.SendMessageAsync(string.Format(Strings.BackendErrorMessage, projectName));
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

            // Count the number of active assignments (has deadline)
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
                await Context.Channel.SendMessageAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
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