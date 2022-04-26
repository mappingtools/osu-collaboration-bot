using CollaborationBot.Entities;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Preconditions {
    public class RequireProjectManager : CustomPreconditionBase {
        public override async Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, IParameterInfo parameterInfo, object value,
            IServiceProvider services) {
            if (context.User is not IGuildUser guildUser)
                return ErrorResult(context.User, services);

            if (guildUser.GuildPermissions.Administrator)
                return PreconditionResult.FromSuccess();

            if (value is not string projectName)
                return PreconditionResult.FromError("Expected project name to be string type.");

            try {
                var dbContext = services.GetService<OsuCollabContext>();

                // Check if the membership exists and they are manager or owner
                if (await dbContext.Members.AnyAsync(o =>
                    o.Project.Name == projectName &&
                    o.Project.Guild.UniqueGuildId == context.Guild.Id &&
                    o.UniqueMemberId == guildUser.Id &&
                    (o.ProjectRole == ProjectRole.Manager || o.ProjectRole == ProjectRole.Owner))) {
                    return PreconditionResult.FromSuccess();
                }
            } catch (Exception e) {
                return PreconditionResult.FromError(e);
            }

            return ErrorResult(guildUser, services);
        }
    }
}