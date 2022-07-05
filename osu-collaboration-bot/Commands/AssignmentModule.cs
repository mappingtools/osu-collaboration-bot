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
using Fergun.Interactive;
using Microsoft.EntityFrameworkCore;

namespace CollaborationBot.Commands {
    [Group("asn", "Everything about assignments")]
    public class AssignmentModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;
        private readonly ResourceService _resourceService;
        private readonly CommonService _common;

        public AssignmentModule(OsuCollabContext context,
            ResourceService resourceService, CommonService common) {
            _context = context;
            _resourceService = resourceService;
            _common = common;
        }
        
        [SlashCommand("list", "Lists all the assignments in the project")]
        public async Task List([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName) {
            var project = await _common.GetProjectAsync(Context, projectName);

            if (project == null) {
                return;
            }

            var assignments = await _context.Assignments.AsQueryable()
                .Where(o => o.Part.ProjectId == project.Id)
                .Include(o => o.Part)
                .Include(o => o.Member)
                .ToListAsync();

            assignments.Sort();

            await _resourceService.RespondPaginator(Context, assignments, _resourceService.GenerateAssignmentListPages,
                Strings.NoAssignments, Strings.AssignmentListMessage);
        }
        
        [SlashCommand("add", "Adds one or more assignments")]
        public async Task Add([RequireProjectManager][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")]string projectName,
            [Summary("user", "The member to assign to")]IGuildUser user, 
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("parts", "The parts to assign to the member")]string[] partNames,
            [Summary("deadline", "The deadline for the assignment (can be null)")] DateTime? deadline = null) {
            var project = await _common.GetProjectAsync(Context, projectName);

            if (project == null) {
                return;
            }

            var member = await _common.GetMemberAsync(Context, project, user);

            if (member == null) {
                return;
            }

            foreach (var partName in partNames) {
                var part = await _common.GetPartAsync(Context, project, partName);

                if (part == null) {
                    return;
                }

                try {
                    deadline = deadline?.ToUniversalTime();
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
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("parts", "The parts to unassign from the member")]params string[] partNames) {
            var project = await _common.GetProjectAsync(Context, projectName);

            if (project == null) {
                return;
            }

            foreach (var partName in partNames) {
                var assignment = await _common.GetAssignmentAsync(Context, project, partName, user);

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
            [Autocomplete(typeof(PartAutocompleteHandler))][Summary("part", "The part of the assignment")]string partName,
            [Summary("user", "The member of the assignment")]IGuildUser user,
            [Summary("deadline", "The new deadline (can be null)")]DateTime? deadline) {
            var project = await _common.GetProjectAsync(Context, projectName);

            if (project == null) {
                return;
            }

            var assignment = await _common.GetAssignmentAsync(Context, project, partName, user);

            if (assignment == null) {
                return;
            }

            try {
                deadline = deadline?.ToUniversalTime();
                assignment.Deadline = deadline;
                await _context.SaveChangesAsync();
                if (deadline.HasValue)
                    await RespondAsync(string.Format(Strings.MoveDeadlineSuccess, deadline.Value.ToString("yyyy-MM-dd")));
                else
                    await RespondAsync(string.Format(Strings.RemoveDeadlineSuccess, user.Username));
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.MoveDeadlineFail, deadline.HasValue ? deadline.Value.ToString("yyyy-MM-dd") : Strings.None));
            }
        }

        [SlashCommand("draintimes", "Calculates the total drain time assigned to each participant.")]
        public async Task DrainTimes([RequireProjectMember][Autocomplete(typeof(ProjectAutocompleteHandler))][Summary("project", "The project")] string projectName) {
            var project = await _common.GetProjectAsync(Context, projectName);

            if (project == null) {
                return;
            }

            try {
                var assignments = (await _context.Assignments.AsQueryable()
                    .Where(o => o.Part.ProjectId == project.Id)
                    .Include(o => o.Part)
                    .Include(o => o.Member).ToListAsync())
                    .GroupBy(o => o.Member);

                var drainTimes = new List<KeyValuePair<Member, int>>();
                foreach (var ass in assignments) {
                    int drainTime = ass.Sum(o => o.Part.End.HasValue && o.Part.Start.HasValue ? o.Part.End.Value - o.Part.Start.Value : 0);
                    drainTimes.Add(new KeyValuePair<Member, int>(ass.Key, drainTime));
                }

                drainTimes = drainTimes.OrderBy(o => o.Value).ToList();

                await _resourceService.RespondPaginator(Context, drainTimes, _resourceService.GenerateDrainTimePages, Strings.NoAssignments,
                    Strings.DrainTimeListMessage);
            } catch (Exception e) {
                logger.Error(e);
                await RespondAsync(string.Format(Strings.BackendErrorMessage, projectName));
            }
        }
    }
}