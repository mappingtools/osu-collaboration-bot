using Discord;

namespace CollaborationBot.Services {

    public class ResourceService {
        public string BackendErrorMessage = "Something went wrong while processing the request on our backend!";

        public string GenerateUnauthorizedMessage(IMentionable mention) {
            return $"{mention.Mention}, you are not authorized to use this command!";
        }
    }
}