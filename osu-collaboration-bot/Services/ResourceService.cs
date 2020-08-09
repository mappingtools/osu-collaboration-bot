﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CollaborationBot.Database.Records;
using Discord;

namespace CollaborationBot.Services {

    public class ResourceService {
        public string BackendErrorMessage = "Something went wrong while processing the request on our backend.";
        public string GuildExistsMessage = "Server is already registered.";
        public string GuildNotExistsMessage = "Your server is not registered! You can add it via command '!!guild add'.";

        public string GenerateAddProjectMessage(string projectName, bool isSuccessful = true) {
            if( isSuccessful ) {
                return $"Added project with name '{projectName}'.";
            }
            else {
                return $"Could not add project with name '{projectName}'.";
            }
        }

        public string GenerateAddGuildMessage(bool isSuccessful = true) {
            if( isSuccessful ) {
                return $"Added this server.";
            }
            else {
                return $"Could not add this server.";
            }
        }

        public string GenerateAddMemberToProject(IMentionable user, string projectName, bool isSuccessful = true) {
            if( isSuccessful ) {
                return $"Added {user.Mention} to project '{projectName}'.";
            }
            else {
                return $"Could not add {user.Mention} to project '{projectName}'.";
            }
        }

        public string GenerateUnauthorizedMessage(IMentionable mention) {
            return $"{mention.Mention}, you are not authorized to use this command.";
        }

        public string GenerateProjectListMessage(List<ProjectRecord> projects) {
            return GenerateListMessage("Here are all the projects going on the server:", projects.Select(p => p.name));
        }

        public string GenerateListMessage(string message, IEnumerable<string> list) {
            var builder = new StringBuilder();
            builder.AppendLine(message);
            builder.Append("```");
            foreach (var item in list) {
                builder.AppendLine($"- {item}");
            }
            builder.Append("```");
            return builder.ToString();
        }
    }
}