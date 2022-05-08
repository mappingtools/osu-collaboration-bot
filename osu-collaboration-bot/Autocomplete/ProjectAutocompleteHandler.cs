using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CollaborationBot.Preconditions;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace CollaborationBot.Autocomplete {
    public class ProjectAutocompleteHandler : AutocompleteHandler {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;

        public ProjectAutocompleteHandler(OsuCollabContext context) {
            _context = context;
        }

        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
            IParameterInfo parameter, IServiceProvider services) {
            var permissionLevel = parameter.Preconditions.Any(a => a is RequireProjectOwner) ? 3 : 
                    parameter.Preconditions.Any(a => a is RequireProjectManager) ? 2 :
                    parameter.Preconditions.Any(a => a is RequireProjectMember) ? 1 : 0;

            List<string> projectNames;
            if (permissionLevel == 0 || context.User is IGuildUser guildUser && guildUser.GuildPermissions.Has(GuildPermission.Administrator)) {
                projectNames = await _context.Projects.AsQueryable().Where(p => p.Guild.UniqueGuildId == context.Guild.Id).Select(p => p.Name).ToListAsync();
            } else
                projectNames = permissionLevel switch {
                    1 => await _context.Members.AsQueryable()
                        .Where(o => o.Project.Guild.UniqueGuildId == context.Guild.Id &&
                                    o.UniqueMemberId == context.User.Id)
                        .Select(o => o.Project.Name)
                        .ToListAsync(),
                    2 => await _context.Members.AsQueryable()
                        .Where(o => o.Project.Guild.UniqueGuildId == context.Guild.Id &&
                                    o.UniqueMemberId == context.User.Id && (o.ProjectRole == ProjectRole.Manager ||
                                                                            o.ProjectRole == ProjectRole.Owner))
                        .Select(o => o.Project.Name)
                        .ToListAsync(),
                    _ => await _context.Members.AsQueryable()
                        .Where(o => o.Project.Guild.UniqueGuildId == context.Guild.Id &&
                                    o.UniqueMemberId == context.User.Id && o.ProjectRole == ProjectRole.Owner)
                        .Select(o => o.Project.Name)
                        .ToListAsync()
                };

            return AutocompletionResult.FromSuccess(projectNames.Select(o => new AutocompleteResult(o, o)));
        }
    }
}