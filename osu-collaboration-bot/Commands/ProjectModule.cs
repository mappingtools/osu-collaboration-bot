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

        [Command("list")]
        public async Task List() {
            var projects = await _context.GetProjectList(Context.Guild.Id);

            await Context.Channel.SendMessageAsync(_resourceService.GenerateProjectListMessage(projects));
        }

        [RequireProjectManager]
        [RequireUserPermission(Discord.GuildPermission.Administrator)]
        [Command("create")]
        public async Task Create(string name) {
            if( await _context.AddProject(name, Context.Guild.Id) ) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(name));
                return;
            }

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(name, false));
        }

        [RequireProjectManager]
        [Command("add")]
        public async Task AddMember(string projectName) {
            if( await _context.AddMemberToProject(projectName, Context.User.Id, Context.Guild.Id) ) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddMemberToProject(Context.User, projectName));
                return;
            }

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddMemberToProject(Context.User, projectName, false));
        }
    }
}