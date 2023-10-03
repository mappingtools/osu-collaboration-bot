using System.IO;
using System.Linq;

namespace CollaborationBot.Services {
    public class InputSanitizingService {

        public InputSanitizingService() {
        }

        private static readonly char[] illegalChars = new char[] { '@', '`' };

        public bool IsValidProjectName(string projectName) {
            return IsValidName(projectName) &&
                !Path.GetInvalidFileNameChars().Any(o => projectName.Contains(o));
        }

        public bool IsValidName(string name) {
            return !string.IsNullOrWhiteSpace(name) &&
                (System.Text.Encoding.UTF8.GetByteCount(name) == name.Length) &&
                !illegalChars.Any(o => name.Contains(o)); ;
        }
    }
}