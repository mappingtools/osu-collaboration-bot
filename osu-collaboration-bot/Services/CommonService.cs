using CollaborationBot.Resources;
using Discord;
using NLog;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace CollaborationBot.Services {
    public class CommonService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly AppSettings _appSettings;

        public CommonService(AppSettings appSettings) {
            _appSettings = appSettings;
        }

        public static async Task<Person> GetPersonAsync(OsuCollabContext dbContext, DiscordSocketRestClient client, ulong uniqueMemberId) {
            var person = await dbContext.People.AsQueryable().SingleOrDefaultAsync(o => o.UniqueMemberId == uniqueMemberId);

            if (person != null) return person;

            var user = await client.GetUserAsync(uniqueMemberId);

            if (user != null) {
                person = new Person {
                    UniqueMemberId = uniqueMemberId,
                    Username = user.Username,
                    GlobalName = user.GlobalName,
                };
                dbContext.People.Add(person);
                await dbContext.SaveChangesAsync();
            }
            else {
                person = new Person {
                    UniqueMemberId = uniqueMemberId,
                    Username = "unknown_user",
                    GlobalName = "Unknown User",
                };
            }

            return person;
        }

        public async Task<Guild> GetGuildAsync(IInteractionContext context, OsuCollabContext dbContext) {
            var guild = await dbContext.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == context.Guild.Id);

            if (guild == null) {
                await context.Interaction.RespondAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            return guild;
        }

        public async Task<Project> GetProjectAsync(IInteractionContext context, OsuCollabContext dbContext, string projectName) {
            var guild = await dbContext.Guilds.AsQueryable().SingleOrDefaultAsync(o => o.UniqueGuildId == context.Guild.Id);

            if (guild == null) {
                await context.Interaction.RespondAsync(string.Format(Strings.GuildNotExistsMessage, _appSettings.Prefix));
                return null;
            }

            var project = await dbContext.Projects.AsQueryable().Include(o => o.Guild)
                .SingleOrDefaultAsync(o => o.GuildId == guild.Id && o.Name == projectName);

            if (project == null) {
                await context.Interaction.RespondAsync(Strings.ProjectNotExistMessage);
                return null;
            }

            return project;
        }

        public async Task<Part> GetPartAsync(IInteractionContext context, OsuCollabContext dbContext, Project project, string partName) {
            var part = await dbContext.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == partName);

            if (part != null) return part;

            // Attempt getting the part prefixed with 'part' because forgetting this is a common mistake
            part = await dbContext.Parts.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.Name == "part" + partName);

            if (part != null) return part;

            await context.Interaction.RespondAsync(string.Format(Strings.PartNotExists, partName, project.Name));
            return null;
        }

        public async Task<Assignment> GetAssignmentAsync(IInteractionContext context, OsuCollabContext dbContext, Project project, string partName, IUser user) {
            var part = await GetPartAsync(context, dbContext, project, partName);

            if (part == null) {
                return null;
            }

            var member = await dbContext.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await context.Interaction.RespondAsync(Strings.MemberNotExistsMessage);
                return null;
            }

            var assignment = await dbContext.Assignments.AsQueryable()
                .SingleOrDefaultAsync(o => o.PartId == part.Id && o.MemberId == member.Id);

            if (assignment == null) {
                await context.Interaction.RespondAsync(Strings.AssignmentNotExists);
            }

            return assignment;
        }

        public async Task<AutoUpdate> GetAutoUpdateAsync(IInteractionContext context, OsuCollabContext dbContext, Project project, ITextChannel channel) {
            var autoUpdate = await dbContext.AutoUpdates.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueChannelId == channel.Id);

            if (autoUpdate == null) {
                await context.Interaction.RespondAsync(string.Format(Strings.AutoUpdateNotExists, project.Name, channel.Mention));
                return null;
            }

            return autoUpdate;
        }

        public async Task<Member> GetMemberAsync(IInteractionContext context, OsuCollabContext dbContext, Project project, IUser user) {
            var member = await dbContext.Members.AsQueryable()
                .SingleOrDefaultAsync(predicate: o => o.ProjectId == project.Id && o.UniqueMemberId == user.Id);

            if (member == null) {
                await context.Interaction.RespondAsync(Strings.MemberNotExistsMessage);
                return null;
            }

            return member;
        }
    }
}