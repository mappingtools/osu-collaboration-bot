using Discord.Commands;
using CollaborationBot.Preconditions;
using System.Threading.Tasks;
using CollaborationBot.Database;
using CollaborationBot.Services;
using System.Linq;

namespace CollaborationBot.Commands {

    [Group("project")]
    public class ProjectModule :ModuleBase<SocketCommandContext> {
        private readonly CollaborationContext _context;
        private readonly ResourceService _resourceService;

        public ProjectModule(CollaborationContext context, ResourceService resourceService) {
            _context = context;
            _resourceService = resourceService;
        }

        [Command("addBaseFile")]
        public async Task AddBaseFile(string projectName) {
            var attachments = Context.Message.Attachments;

            foreach( var att in attachments ) {
                await Context.Channel.SendFileAsync(att.Url, "a file");
            }
        }

        [Command("list")]
        public async Task List() {
            var projects = await _context.GetProjectList(Context.Guild.Id);

            await Context.Channel.SendMessageAsync(_resourceService.GenerateProjectListMessage(projects));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(Discord.GuildPermission.Administrator, Group = "Permission")]
        [Command("create")]
        public async Task Create(string name) {
            if( await _context.AddProject(name, Context.Guild.Id) ) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(name));
                return;
            }

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(name, false));
        }

        [RequireProjectManager]
        [RequireUserPermission(Discord.GuildPermission.Administrator)]
        [Command("remove")]
        public async Task Remove(string name) {
            if( await _context.RemoveProject(name, Context.Guild.Id) ) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(name));
                return;
            }

            await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(name, false));
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