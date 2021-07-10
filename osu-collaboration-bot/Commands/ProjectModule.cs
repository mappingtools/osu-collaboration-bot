using System;
using System.Linq;
using System.Threading.Tasks;
using CollaborationBot.Database;
using CollaborationBot.Preconditions;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;

namespace CollaborationBot.Commands {
    [Group("project")]
    public class ProjectModule : ModuleBase<SocketCommandContext> {
        private readonly CollaborationContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public ProjectModule(CollaborationContext context, FileHandlingService fileHandler,
            ResourceService resourceService) {
            _context = context;
            _fileHandler = fileHandler;
            _resourceService = resourceService;
        }

        [Command("submitPart")]
        public async Task SubmitPart(string projectName) {
            // Find out which parts this member is allowed to edit in the project
            // Download the attached file and put it in the member's folder
            // Merge it into the base file
            // Success message
            await Context.Channel.SendMessageAsync(_resourceService.GenerateSubmitPartMessage(projectName, false));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("addBaseFile")]
        public async Task AddBaseFile(string projectName) {
            var attachment = Context.Message.Attachments.SingleOrDefault();

            if (attachment == null) {
                await Context.Channel.SendMessageAsync("Could not find an attached .osu file.");
                return;
            }

            if (!await _fileHandler.DownloadBaseFile(Context.Guild, projectName, attachment)) {
                await Context.Channel.SendMessageAsync("Something went wrong while trying to upload the base file.");
                return;
            }

            await Context.Channel.SendMessageAsync(
                $"Successfully uploaded {attachment.Filename} as base file for project '{projectName}'");
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("getBaseFile")]
        public async Task GetBaseFile(string projectName) {
            try {
                var projectBaseFilePath = _fileHandler.GetProjectBaseFilePath(Context.Guild, projectName);
                await Context.Channel.SendFileAsync(projectBaseFilePath, $"Compiled .osu of project '{projectName}':");
            }
            catch (Exception) {
                await Context.Channel.SendFileAsync(_resourceService.BackendErrorMessage);
            }
        }

        [Command("list")]
        public async Task List() {
            var projects = await _context.GetProjectListAsync(Context.Guild.Id);
            await Context.Channel.SendMessageAsync(_resourceService.GenerateProjectListMessage(projects));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("create")]
        public async Task Create(string projectName) {
            if (!await _context.AddProjectAsync(projectName, Context.Guild.Id)) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName, false));

                return;
            }

            _fileHandler.GenerateProjectDirectory(Context.Guild, projectName);
            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("remove")]
        public async Task Remove(string name) {
            if (await _context.RemoveProjectAsync(name, Context.Guild.Id)) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(name));
                return;
            }

            await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(name, false));
        }

        [RequireProjectManager]
        [Command("add")]
        public async Task AddMember(string projectName) {
            if (await _context.AddMemberToProjectAsync(projectName, Context.User.Id, Context.Guild.Id)) {
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddMemberToProject(Context.User, projectName));
                return;
            }

            await Context.Channel.SendMessageAsync(
                _resourceService.GenerateAddMemberToProject(Context.User, projectName, false));
        }
    }
}