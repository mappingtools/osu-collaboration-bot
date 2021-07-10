using System.Collections.Generic;
using System.Linq;
using System.Text;
using CollaborationBot.Database.Records;
using CollaborationBot.Resources;
using Discord;

namespace CollaborationBot.Services {
    public class ResourceService {
        public string BackendErrorMessage = Strings.BackendErrorMessage;
        public string GuildExistsMessage = Strings.GuildExistsMessage;

        public string GuildNotExistsMessage = Strings.GuildNotExistsMessage;

        public string GenerateSubmitPartMessage(string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.SubmitPartSuccessMessage, projectName);
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
                return $"Added {user.Mention} to project '{projectName}'.";
            return $"Could not add {user.Mention} to project '{projectName}'.";
        }

        public string GenerateUnauthorizedMessage(IMentionable mention) {
            return $"{mention.Mention}, you are not authorized to use this command.";
        }

        public string GenerateProjectListMessage(List<ProjectRecord> projects) {
            if (projects.Count <= 0) return "There are no projects in this server.";
            return GenerateListMessage("Here are all the projects in the server:", projects.Select(p => p.Name));
        }

        public string GenerateListMessage(string message, IEnumerable<string> list) {
            var builder = new StringBuilder();
            builder.AppendLine(message);
            builder.Append("```");
            foreach (var item in list) builder.AppendLine($"- {item}");
            builder.Append("```");
            return builder.ToString();
        }
    }
}