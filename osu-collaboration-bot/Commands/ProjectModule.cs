using Discord.Commands;
using CollaborationBot.Preconditions;
using System.Threading.Tasks;
using CollaborationBot.Database;

namespace CollaborationBot.Commands {

    [Group("project")]
    public class ProjectModule :ModuleBase<SocketCommandContext> {
        private readonly CollaborationContext _context;

        public ProjectModule(CollaborationContext context) {
            _context = context;
        }

        
        [Command("create")]
        public async Task Create(string name) {
            var res = await _context.AddProject(name);

            if( res ) {
                await Context.Channel.SendMessageAsync($"Added project {name}.");
            }
            else {
                await Context.Channel.SendMessageAsync($"Could not add project {name}.");
            }
        }
    }
}