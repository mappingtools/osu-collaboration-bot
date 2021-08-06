using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CollaborationBot.Entities;
using CollaborationBot.Resources;
using Discord;
using Discord.WebSocket;

namespace CollaborationBot.Services {
    public class ResourceService {
        private readonly DiscordSocketClient _client;

        public string BackendErrorMessage = Strings.BackendErrorMessage;
        public string GuildExistsMessage = Strings.GuildExistsMessage;

        public string GuildNotExistsMessage = Strings.GuildNotExistsMessage;

        public ResourceService(DiscordSocketClient client) {
            _client = client;
        }

        public string GenerateSubmitPartMessage(string projectName, int hitObjectCount, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.SubmitPartSuccessMessage, hitObjectCount, projectName);
            return string.Format(Strings.SubmitPartFailMessage, projectName);
        }

        public string GenerateAddProjectMessage(string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return $"Added project with name '{projectName}'.";
            return $"Could not add project with name '{projectName}'.";
        }

        public string GenerateRemoveProjectMessage(string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return $"Removed project with name '{projectName}'.";
            return $"Could not remove project with name '{projectName}'.";
        }

        public string GenerateAddGuildMessage(bool isSuccessful = true) {
            if (isSuccessful)
                return "Added this server.";
            return "Could not add this server.";
        }

        public string GenerateAddMemberToProject(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.AddMemberSuccessMessage, user.Mention, projectName);
            return string.Format(Strings.AddMemberFailMessage, user.Mention, projectName);
        }

        public string GenerateRemoveMemberFromProject(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.RemoveMemberSuccessMessage, user.Mention, projectName);
            return string.Format(Strings.RemoveMemberFailMessage, user.Mention, projectName);
        }

        public string GenerateSetOwner(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.SetOwnerSuccessMessage, projectName, user.Mention);
            return string.Format(Strings.SetOwnerFailMessage, projectName, user.Mention);
        }

        public string GenerateAddManager(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.AddManagerSuccessMessage, user.Mention, projectName);
            return string.Format(Strings.AddManagerFailMessage, user.Mention, projectName);
        }

        public string GenerateRemoveManager(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.RemoveManagerSuccessMessage, user.Mention, projectName);
            return string.Format(Strings.RemoveManagerFailMessage, user.Mention, projectName);
        }

        public string GenerateUnauthorizedMessage(IMentionable mention) {
            return $"{mention.Mention}, you are not authorized to use this command.";
        }
        
        public string GenerateProjectListMessage(List<Project> projects) {
            if (projects.Count <= 0) return "There are no projects in this server."; 
            return GenerateListMessage("Here are all the projects in the server:", projects.Select(p => p.Name));
        }

        public string GenerateMembersListMessage(List<Member> members) {
            if (members.Count <= 0) return "There are no members of this project.";
            return GenerateListMessage("Here are all members of the project:", 
                members.Select(o => $"{_client.GetUser((ulong)o.UniqueMemberId).Username} ({o.ProjectRole})"));
        }

        public string GeneratePartsListMessage(List<Part> parts) {
            if (parts.Count <= 0) return "There are no parts in this project.";
            return GenerateListMessage("Here are all the parts of the project:",
                parts.Select(o => $"{o.Name} ({TimeToString(o.Start)} - {TimeToString(o.End)}): {o.Status}"));
        }

        public string GenerateAssignmentListMessage(List<Assignment> assignments) {
            if (assignments.Count <= 0) return "There are no assignments in this project.";
            return GenerateListMessage("Here are all the assignments of the project:",
                assignments.Select(o => $"{o.Part.Name}: {_client.GetUser((ulong)o.Member.UniqueMemberId).Username}{(o.Deadline.HasValue ? " - " + o.Deadline.Value.ToString("yyyy-MM-dd") : string.Empty)}"));
        }

        public string GenerateListMessage(string message, IEnumerable<string> list) {
            var builder = new StringBuilder();
            builder.AppendLine(message);
            builder.Append("```");
            foreach (var item in list) builder.AppendLine($"- {item}");
            builder.Append("```");
            return builder.ToString();
        }

        public string TimeToString(int? time) {
            if (!time.HasValue)
                return "XX:XX:XXX";

            var timeSpan = TimeSpan.FromMilliseconds(time.Value);

            return $"{(timeSpan.Days > 0 ? $"{timeSpan.Days:####}:" : string.Empty)}" +
                    $"{(timeSpan.Hours > 0 ? $"{timeSpan.Hours:00}:" : string.Empty)}" +
                    $"{timeSpan.Minutes:00}:{timeSpan.Seconds:00}:{timeSpan.Milliseconds:000}";
        }
    }
}