using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;

namespace CollaborationBot.Preconditions {
    public class RequireProjectManager : CustomPreconditionBase {
        private const string PROJECT_MANAGER_ROLE = "osu-project-manager";

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command,
            IServiceProvider services) {
            if (!(context.User is SocketGuildUser guildUser)) return ErrorResult(context.User, services);

            if (guildUser.Roles.All(o => o.Name != PROJECT_MANAGER_ROLE)) return ErrorResult(context.User, services);

            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}