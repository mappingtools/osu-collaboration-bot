﻿using System;
using System.Linq;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CollaborationBot.Preconditions;
using CollaborationBot.Resources;
using CollaborationBot.Services;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;

namespace CollaborationBot.Commands {
    [Group("project")]
    public class ProjectModule : ModuleBase<SocketCommandContext> {
        private readonly OsuCollabContext _context;
        private readonly FileHandlingService _fileHandler;
        private readonly ResourceService _resourceService;

        public ProjectModule(OsuCollabContext context, FileHandlingService fileHandler,
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
            var projects = _context.Projects.Include(p => p.Guild).Where(p => p.Guild.UniqueGuildId == Context.Guild.Id).ToList();
            await Context.Channel.SendMessageAsync(_resourceService.GenerateProjectListMessage(projects));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("create")]
        public async Task Create(string projectName) {
            var guild = _context.Guilds.FirstOrDefault(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(_resourceService.GuildNotExistsMessage);
                return;
            }

            try {
                var projectEntry = await _context.Projects.AddAsync(new Project {Name = projectName, GuildId = guild.Id, Status = ProjectStatus.NotStarted});
                await _context.SaveChangesAsync();
                await _context.Members.AddAsync(new Member { ProjectId = projectEntry.Entity.Id, UniqueMemberId = Context.User.Id, ProjectRole = ProjectRole.Owner });
                await _context.SaveChangesAsync();
            } 
            catch (Exception e) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName, false));
                Console.WriteLine(e);
                return;
            }
            
            _fileHandler.GenerateProjectDirectory(Context.Guild, projectName);
            await Context.Channel.SendMessageAsync(_resourceService.GenerateAddProjectMessage(projectName));
        }

        [RequireProjectManager(Group = "Permission")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command("remove")]
        public async Task Remove(string projectName) {
            var guild = _context.Guilds.FirstOrDefault(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(_resourceService.GuildNotExistsMessage);
                return;
            }

            var project = _context.Projects.FirstOrDefault(o => o.GuildId == guild.Id && o.Name == projectName);

            if (project == null) {
                await Context.Channel.SendMessageAsync(Strings.ProjectNotExistMessage);
                return;
            }

            try {
                _context.Projects.Remove(project);
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(projectName));
            }
            catch (Exception e) {
                await Context.Channel.SendMessageAsync(_resourceService.GenerateRemoveProjectMessage(projectName, false));
                Console.WriteLine(e);
            }
        }

        [RequireProjectManager]
        [Command("join")]
        public async Task JoinProject(string projectName) {
            var guild = _context.Guilds.FirstOrDefault(o => o.UniqueGuildId == Context.Guild.Id);

            if (guild == null) {
                await Context.Channel.SendMessageAsync(_resourceService.GuildNotExistsMessage);
                return;
            }

            var project = _context.Projects.FirstOrDefault(o => o.GuildId == guild.Id && o.Name == projectName);

            if (project == null) {
                await Context.Channel.SendMessageAsync(Strings.ProjectNotExistMessage);
                return;
            }

            if (_context.Members.Any(o => o.ProjectId == project.Id && o.UniqueMemberId == Context.User.Id)) {
                await Context.Channel.SendMessageAsync(Strings.AlreadyJoinedMessage);
                return;
            }

            try {
                await _context.Members.AddAsync(new Member {ProjectId = project.Id, UniqueMemberId = Context.User.Id, ProjectRole = ProjectRole.Member});
                await _context.SaveChangesAsync();
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddMemberToProject(Context.User, projectName));
            }
            catch (Exception) {
                await Context.Channel.SendMessageAsync(
                    _resourceService.GenerateAddMemberToProject(Context.User, projectName, false));
            }
        }
    }
}