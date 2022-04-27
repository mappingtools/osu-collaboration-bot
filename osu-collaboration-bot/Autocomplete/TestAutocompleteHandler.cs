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
    public class TestAutocompleteHandler : AutocompleteHandler {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly OsuCollabContext _context;

        public TestAutocompleteHandler(OsuCollabContext context) {
            _context = context;
        }

        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
            IParameterInfo parameter, IServiceProvider services) {

            IEnumerable<AutocompleteResult> results = new[] { new AutocompleteResult("testtesttest", "test") };
            return AutocompletionResult.FromSuccess(results);
        }
    }
}