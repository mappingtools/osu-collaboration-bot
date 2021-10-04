using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using CollaborationBot.Services;
using CollaborationBot.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace CollaborationBot.Preconditions {
    public abstract class CustomPreconditionBase : PreconditionAttribute {
        protected PreconditionResult ErrorResult(IUser user, IServiceProvider services) {
            var resources = services.GetService(typeof(ResourceService)) as ResourceService;
            return PreconditionResult.FromError(resources.GenerateUnauthorizedMessage(user));
        }

        protected async Task<object> GetParameter(string paramName, ICommandContext context, CommandInfo command, IServiceProvider services) {
            int projectParamPos = -1;
            for (int i = 0; i < command.Parameters.Count; i++) {
                var param = command.Parameters[i];
                if (param.Name == paramName) {
                    projectParamPos = i;
                    break;
                }
            }

            if (projectParamPos == -1) {
                throw new ArgumentException($"Command has no string parameter named '{paramName}'.", nameof(paramName));
            }

            var appSettings = services.GetService<AppSettings>();

            var parseResult = await command.ParseAsync(
                context,
                appSettings.Prefix.Length + (command.Module.Group?.Length ?? 0) + command.Name.Length + 2,
                SearchResult.FromSuccess(context.Message.Content, Array.Empty<CommandMatch>())
                );

            if (!parseResult.IsSuccess) {
                throw new Exception(parseResult.ErrorReason);
            }

            return parseResult.ArgValues[projectParamPos].BestMatch;
        }
    }
}