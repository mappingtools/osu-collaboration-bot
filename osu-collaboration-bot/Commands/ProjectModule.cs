using Discord.Commands;
using CollaborationBot.Preconditions;
using System.Threading.Tasks;
using CollaborationBot.Database;
using CollaborationBot.Services;

namespace CollaborationBot.Commands {

    [Group("project")]
    public class ProjectModule :ModuleBase<SocketCommandContext> {
        private readonly CollaborationContext _context;
        private readonly ResourceService _resourceService;

        public ProjectModule(CollaborationContext context, ResourceService resourceService) {
            _context = context;
            _resourceService = resourceService;
        }

        [RequireProjectManager]
        [Command("create")]
        public async Task Create(string name) {
            if( await _context.AddProject(name, Context.Guild.Id) ) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(name));
                return;
            }

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(name, false));
        }
    }
}