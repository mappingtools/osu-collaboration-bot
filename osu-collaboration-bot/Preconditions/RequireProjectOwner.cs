using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord;
using CollaborationBot.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace CollaborationBot.Preconditions {
    public class RequireProjectOwner : CustomPreconditionBase {
        private const string PROJECT_PARAM_NAME = "projectName";

        public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command,
            IServiceProvider services) {
            if (context.User is not IGuildUser guildUser) return ErrorResult(context.User, services);

            try {
                string projectName = (string)await GetParameter(PROJECT_PARAM_NAME, context, command, services);

                var dbContext = services.GetService<OsuCollabContext>();

                // Check if the membership exists and they are manager or owner
                if (dbContext.Members.Any(o =>
                o.Project.Name == projectName &&
                o.Project.Guild.UniqueGuildId == context.Guild.Id &&
                o.UniqueMemberId == guildUser.Id &&
                o.ProjectRole == ProjectRole.Owner)) {
                    return PreconditionResult.FromSuccess();
                }
            } catch (Exception e) {
                return PreconditionResult.FromError(e);
            }

            return ErrorResult(guildUser, services);
        }
    }
}