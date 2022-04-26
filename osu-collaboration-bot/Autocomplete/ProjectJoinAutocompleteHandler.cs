using CollaborationBot.Entities;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Autocomplete {
    public class ProjectJoinAutocompleteHandler : AutocompleteHandler {
        private readonly OsuCollabContext _context;

        public ProjectJoinAutocompleteHandler(OsuCollabContext context) {
            _context = context;
        }

        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
            IParameterInfo parameter, IServiceProvider services) {
            var projectNames = await _context.Projects.AsQueryable().Where(p => p.Guild.UniqueGuildId == context.Guild.Id && p.Status == ProjectStatus.SearchingForMembers).Select(p => p.Name).ToListAsync();
            return AutocompletionResult.FromSuccess(projectNames.Select(o => new AutocompleteResult("project", o)));
        }
    }
}