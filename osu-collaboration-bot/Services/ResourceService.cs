using Discord;

namespace CollaborationBot.Services {

    public class ResourceService {
        public string BackendErrorMessage = "Something went wrong while processing the request on our backend";
        public string GuildExistsMessage = "Server is already registered!";
        public string GuildNotExistsMessage = "Your server is not registered! You can add it via command '!!guild add'";

        public string GenerateAddProjectMessage(string projectName, bool isSuccessful = true) {
            if( isSuccessful ) {
                return $"Added project with name '{projectName}'";
            }
            else {
                return $"Could not add project with name '{projectName}'";
            }
        }

        public string GenerateAddGuildMessage(bool isSuccessful = true) {
            if( isSuccessful ) {
                return $"Added this server";
            }
            else {
                return $"Could not add this server";
            }
        }

        public string GenerateUnauthorizedMessage(IMentionable mention) {
            return $"{mention.Mention}, you are not authorized to use this command";
        }
    }
}