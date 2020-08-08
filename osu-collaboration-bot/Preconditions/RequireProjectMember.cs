using Discord;
using Discord.Commands;
using Discord.WebSocket;
using CollaborationBot.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Preconditions {

    public class RequireProjectMember :PreconditionAttribute {
        private const string PROJECT_MEMBER_ROLE = "osu-project-member";

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services) {
            if( !( context.User is SocketGuildUser guildUser ) ) {
                return ErrorResult(context.User, context.Channel, services);
            }

            if( guildUser.Roles.All(o => o.Name != PROJECT_MEMBER_ROLE) ) {
                return ErrorResult(context.User, context.Channel, services);
            }

            return Task.FromResult(PreconditionResult.FromSuccess());
        }

        private Task<PreconditionResult> ErrorResult(IUser user, IMessageChannel channel, IServiceProvider services) {
            var resources = services.GetService(typeof(ResourceService)) as ResourceService;
            return Task.FromResult(PreconditionResult.FromError(resources.GenerateUnauthorizedMessage(user)));
        }
    }
}