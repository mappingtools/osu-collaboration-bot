using Discord;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace CollaborationBot.Services {

    public class FileHandlingService {

        public enum PermissibleFileType {
            DOT_OSU
        }

        public Dictionary<PermissibleFileType, string> PermissibleFileExtensions = new Dictionary<PermissibleFileType, string> {
            { PermissibleFileType.DOT_OSU, ".osu" }
        };

        private string _path;

        public void Initialize(string path) {
            _path = path;
        }

        public async Task<bool> DownloadBaseFile(IGuild guild, string projectName, Attachment att) {
            try {
                if( !IsFilePermissible(att.Url, PermissibleFileType.DOT_OSU) ) {
                    return false;
                }

                var localProjectPath = GetProjectPath(guild, projectName);

                if( !Directory.Exists(localProjectPath) ) {
                    return false;
                }

                if( !Uri.TryCreate(att.Url, UriKind.Absolute, out var uri) ) {
                    return false;
                }

                var filePath = Path.Combine(localProjectPath, att.Filename);

                using var client = new WebClient();
                await client.DownloadFileTaskAsync(uri, filePath);

                return true;
            }
            catch( Exception ) {
                return false;
            }
        }

        public string GetProjectBaseFilePath(IGuild guild, string projectName) {
            var localProjectPath = GetProjectPath(guild, projectName);
            string[] osuFiles = Directory.GetFiles(localProjectPath, "*.osu");

            if (osuFiles.Length == 0)
                throw new FileNotFoundException("No .osu files found in project directory.");

            return osuFiles[0];
        }

        public string GetGuildPath(IGuild guild) {
            return Path.Combine(_path, guild.Id.ToString());
        }

        public string GetProjectPath(IGuild guild, string projectName) {
            return Path.Combine(_path, guild.Id.ToString(), projectName);
        }

        public void GenerateGuildDirectory(IGuild guild) {
            var localGuildPath = GetGuildPath(guild);

            if( !Directory.Exists(localGuildPath) ) {
                Directory.CreateDirectory(localGuildPath);
            }
        }

        public void GenerateProjectDirectory(IGuild guild, string projectName) {
            var localProjectPath = GetProjectPath(guild, projectName);

            if ( !Directory.Exists(localProjectPath) ) {
                Directory.CreateDirectory(localProjectPath);
            }
        }

        private bool IsFilePermissible(string url, PermissibleFileType fileType) {
            if( !PermissibleFileExtensions.TryGetValue(fileType, out var ext) ) {
                return false;
            }

            return ext == Path.GetExtension(url);
        }
    }
}