using CollaborationBot.Entities;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Autocomplete {
    public class PartAutocompleteHandler : AutocompleteHandler {
        private const int MaxSuggestions = 25;
        private readonly OsuCollabContext _context;

        public PartAutocompleteHandler(OsuCollabContext context) {
            _context = context;
        }

        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
            IParameterInfo parameter, IServiceProvider services) {
            var prefix = (string)autocompleteInteraction.Data.Current.Value;
            var projectName = (string)autocompleteInteraction.Data.Options.First(o => o.Name == "project").Value;
            var partNames = await _context.Parts.AsQueryable()
                .Where(p => p.Project.Guild.UniqueGuildId == context.Guild.Id && p.Project.Name == projectName && p.Name.StartsWith(prefix))
                .Take(MaxSuggestions)
                .Select(p => p.Name).ToListAsync();
            return AutocompletionResult.FromSuccess(partNames.Select(o => new AutocompleteResult(o, o)));
        }
    }
}