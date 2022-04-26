using CollaborationBot.Entities;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Preconditions {
    public class RequireProjectMember : CustomPreconditionBase {
        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, IParameterInfo parameterInfo, object value,
            IServiceProvider services) {
            if (context.User is not IGuildUser guildUser)
                return Task.FromResult(ErrorResult(context.User, services));

            if (guildUser.GuildPermissions.Administrator)
                return Task.FromResult(PreconditionResult.FromSuccess());

            if (value is not string projectName)
                return Task.FromResult(PreconditionResult.FromError("Expected project name to be string type."));

            try {
                var dbContext = services.GetService<OsuCollabContext>();

                // Check if the membership exists
                if (dbContext.Members.Any(o =>
                    o.Project.Name == projectName &&
                    o.Project.Guild.UniqueGuildId == context.Guild.Id &&
                    o.UniqueMemberId == guildUser.Id)) {
                    return Task.FromResult(PreconditionResult.FromSuccess());
                }
            } catch (Exception e) {
                return Task.FromResult(PreconditionResult.FromError(e));
            }

            return Task.FromResult(ErrorResult(guildUser, services));
        }
    }
}