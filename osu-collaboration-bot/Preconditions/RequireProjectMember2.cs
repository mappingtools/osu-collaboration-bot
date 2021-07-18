using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;

namespace CollaborationBot.Preconditions {
    public class RequireProjectMember2 : CustomPreconditionBase {
        private const string PROJECT_MEMBER_ROLE = "osu-project-member";

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command,
            IServiceProvider services) {
            if (!(context.User is SocketGuildUser guildUser)) return ErrorResult(context.User, services);

            Console.WriteLine(context.Message.Content);


            if (guildUser.Roles.All(o => o.Mention != PROJECT_MEMBER_ROLE)) return ErrorResult(context.User, services);

            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}