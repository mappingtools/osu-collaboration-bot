using System;
using System.Threading.Tasks;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;

namespace CollaborationBot.Preconditions {
    public abstract class CustomPreconditionBase : PreconditionAttribute {
        protected Task<PreconditionResult> ErrorResult(IUser user, IServiceProvider services) {
            var resources = services.GetService(typeof(ResourceService)) as ResourceService;
            return Task.FromResult(PreconditionResult.FromError(resources.GenerateUnauthorizedMessage(user)));
        }
    }
}