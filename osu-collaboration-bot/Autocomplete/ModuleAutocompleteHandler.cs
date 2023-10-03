using CollaborationBot.Entities;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollaborationBot.Autocomplete {
    public class ModuleAutocompleteHandler : AutocompleteHandler {
        private static readonly string[] modules = { "project", "guild", "part", "asn", "au" };

        public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
            IParameterInfo parameter, IServiceProvider services) {
            var prefix = (string)autocompleteInteraction.Data.Current.Value;
            return Task.FromResult(AutocompletionResult.FromSuccess(modules.Where(o => o.StartsWith(prefix)).Select(o => new AutocompleteResult(o, o))));
        }
    }
}