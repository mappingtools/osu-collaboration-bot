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
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public ProjectModule(CollaborationContext context, FileHandlingService fileHandler, ResourceService resourceService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(Discord.GuildPermission.Administrator, Group = "Permission")]
        [Command("addBaseFile")]
        public async Task AddBaseFile(string projectName) {
            var attachment = Context.Message.Attachments.SingleOrDefault();

            if( attachment == null ) {
                await Context.Channel.SendMessageAsync("Could not find an attached .osu file.");
                return;
            }

            if( !await _fileHandler.UploadBaseFile(Context.Guild, projectName, attachment) ) {
                await Context.Channel.SendMessageAsync("Something went wrong while trying to upload the base file.");
                return;
            }

            await Context.Channel.SendMessageAsync($"Successfully uploaded {attachment.Filename} as base file for project {projectName}");
        }

        [Command("list")]
        public async Task List() {
            var projects = await _context.GetProjectList(Context.Guild.Id);
            await Context.Channel.SendMessageAsync(_resourceService.GenerateProjectListMessage(projects));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(Discord.GuildPermission.Administrator, Group = "Permission")]
        [Command("create")]
        public async Task Create(string projectName) {
            if( !await _context.AddProject(projectName, Context.Guild.Id) ) {
                _fileHandler.GenerateProjectDirectory(Context.Guild, projectName);
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName));

                return;
            }

            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName, false));
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