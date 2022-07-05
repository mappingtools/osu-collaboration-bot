using CollaborationBot.Resources;
using Discord;
using NLog;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using Microsoft.EntityFrameworkCore;

namespace CollaborationBot.Services {
    public class CommonService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly AppSettings _appSettings;
        private readonly OsuCollabContext _context;

        public CommonService(AppSettings appSettings, OsuCollabContext context) {
            _appSettings = appSettings;
            _context = context;
        }

        public async Task<Guild> GetGuildAsync(IInteractionContext context) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == context.Guild.Id);

            if (guild == null) {
                await context.Interaction.RespondAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            return guild;
        }

        public async Task<Project> GetProjectAsync(IInteractionContext context, string projectName) {
            var guild = await _context.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == context.Guild.Id);

            if (guild == null) {
                await context.Interaction.RespondAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            var project = await _context.Projects.AsQueryable().Include(o => o.Guild)
                .SingleOrDefaultAsync(o => o.GuildId == guild.Id && o.Name == projectName);

            if (project == null) {
                await context.Interaction.RespondAsync(Strings.ProjectNotExistMessage);
                return null;
            }

            return project;
        }

        public async Task<Part> GetPartAsync(IInteractionContext context, Project project, string partName) {
            var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

            if (part == null) {
                await context.Interaction.RespondAsync(string.Format(Strings.PartNotExists, partName, project.Name));
                return null;
            }

            return part;
        }

        public async Task<Assignment> GetAssignmentAsync(IInteractionContext context, Project project, string partName, IUser user) {
            var part = await _context.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

            if (part == null) {
                await context.Interaction.RespondAsync(string.Format(Strings.PartNotExists, partName, project.Name));
                return null;
            }

            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await context.Interaction.RespondAsync(Strings.MemberNotExistsMessage);
                return null;
            }

            var assignment = await _context.Assignments.AsQueryable()
                .SingleOrDefaultAsync(o => o.PartId == part.Id && o.MemberId == member.Id);

            if (assignment == null) {
                await context.Interaction.RespondAsync(Strings.AssignmentNotExists);
            }

            return assignment;
        }

        public async Task<AutoUpdate> GetAutoUpdateAsync(IInteractionContext context, Project project, ITextChannel channel) {
            var autoUpdate = await _context.AutoUpdates.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueChannelId == channel.Id);

            if (autoUpdate == null) {
                await context.Interaction.RespondAsync(string.Format(Strings.AutoUpdateNotExists, project.Name, channel.Mention));
                return null;
            }

            return autoUpdate;
        }

        public async Task<Member> GetMemberAsync(IInteractionContext context, Project project, IUser user) {
            var member = await _context.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await context.Interaction.RespondAsync(Strings.MemberNotExistsMessage);
                return null;
            }

            return member;
        }
    }
}