using System.IO;
using System.Linq;

namespace CollaborationBot.Services {
    public class InputSanitizingService {
        private static readonly char[] illegalChars = ['@', '`'];

        public bool IsValidProjectName(string projectName) {
            return IsValidName(projectName) &&
                   !Path.GetInvalidFileNameChars().Any(projectName.Contains);
        }

        public bool IsValidName(string name) {
            return !string.IsNullOrWhiteSpace(name) &&
                   System.Text.Encoding.UTF8.GetByteCount(name) == name.Length &&
                   !illegalChars.Any(name.Contains);
        }

        public bool IsSafeToPrint(string text) {
            return !string.IsNullOrWhiteSpace(text) &&
                   System.Text.Encoding.UTF8.GetByteCount(text) == text.Length &&
                   !illegalChars.Any(text.Contains);
        }
    }
}