using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Interactions;
using System;

namespace CollaborationBot.Preconditions {
    public abstract class CustomPreconditionBase : ParameterPreconditionAttribute {
        protected PreconditionResult ErrorResult(IUser user, IServiceProvider services) {
            var resources = services.GetService(typeof(ResourceService)) as ResourceService;
            return PreconditionResult.FromError(resources.GenerateUnauthorizedMessage(user));
        }
    }
}