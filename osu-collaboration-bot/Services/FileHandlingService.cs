using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Discord;
using NLog;

namespace CollaborationBot.Services {
    public class FileHandlingService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public enum PermissibleFileType {
            DOT_OSU,
            DOT_TSV,
            DOT_CSV
        }

        private string _path;

        public Dictionary<PermissibleFileType, string> PermissibleFileExtensions = new() {
            {PermissibleFileType.DOT_OSU, ".osu"},
            {PermissibleFileType.DOT_TSV, ".tsv"},
            {PermissibleFileType.DOT_CSV, ".csv"},
        };

        public void Initialize(string path) {
            _path = path;
        }

        public async Task<bool> DownloadBaseFile(IGuild guild, string projectName, Attachment att) {
            try {
                if (!IsFilePermissible(att.Url, PermissibleFileType.DOT_OSU)) return false;

                var localProjectPath = GetProjectPath(guild, projectName);

                if (!Directory.Exists(localProjectPath)) return false;

                if (!Uri.TryCreate(att.Url, UriKind.Absolute, out var uri)) return false;

                var oldFilePath = GetProjectBaseFilePath(guild, projectName);
                var filePath = Path.Combine(localProjectPath, att.Filename);

                using var client = new WebClient();
                await client.DownloadFileTaskAsync(uri, filePath);

                if (!string.IsNullOrEmpty(oldFilePath) && oldFilePath != filePath && File.Exists(oldFilePath)) {
                    File.Delete(oldFilePath);
                }

                return true;
            }
            catch (Exception e) {
                logger.Error(e);
                return false;
            }
        }

        public async Task<string> DownloadPartSubmit(IGuild guild, string projectName, Attachment att) {
            try {
                if (!IsFilePermissible(att.Url, PermissibleFileType.DOT_OSU)) return null;

                var localProjectPath = GetProjectPath(guild, projectName);

                if (!Directory.Exists(localProjectPath)) return null;

                if (!Uri.TryCreate(att.Url, UriKind.Absolute, out var uri)) return null;

                using var client = new WebClient();
                var result = await client.DownloadStringTaskAsync(uri);

                return result;
            } catch (Exception e) {
                logger.Error(e);
                return null;
            }
        }

        #region part io

        public class PartRecord {
            [Index(0)]
            public string Name { get; set; }
            [Optional]
            [Index(1)]
            public int? Start { get; set; }
            [Optional]
            [Index(2)]
            public int? End { get; set; }
            [Optional]
            [Index(3)]
            public PartStatus? Status { get; set; }
            [Optional]
            [Index(4)]
            public string MapperNames { get; set; }
        }

        public async Task<List<PartRecord>> DownloadPartsCSV(Attachment att, bool hasHeaders = true) {
            try {
                if (!IsFilePermissible(att.Url, PermissibleFileType.DOT_CSV)) return null;

                if (!Uri.TryCreate(att.Url, UriKind.Absolute, out var uri)) return null;

                using var client = new WebClient();
                var result = await client.DownloadDataTaskAsync(uri);

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    PrepareHeaderForMatch = args => args.Header.ToLower(),
                    HasHeaderRecord = hasHeaders,
                };
                using var reader = new StreamReader(new MemoryStream(result));
                using var csv = new CsvReader(reader, config);
                var records = csv.GetRecords<PartRecord>().ToList();

                return records;
            } catch (Exception e) {
                logger.Error(e);
                return null;
            }
        }

        #endregion

        public string GetProjectBaseFilePath(IGuild guild, string projectName) {
            var localProjectPath = GetProjectPath(guild, projectName);
            var osuFiles = Directory.GetFiles(localProjectPath, "*.osu");

            if (osuFiles.Length == 0)
                return null;

            return osuFiles[0];
        }

        public string GetProjectBaseFileName(IGuild guild, string projectName) {
            return Path.GetFileName(GetProjectBaseFilePath(guild, projectName));
        }

        public bool ProjectBaseFileExists(IGuild guild, string projectName) {
            return GetProjectBaseFilePath(guild, projectName) != null;
        }

        public void MoveProjectPath(IGuild guild, string projectName, string newProjectName) {
            Directory.Move(GetProjectPath(guild, projectName), GetProjectPath(guild, newProjectName));
        }

        public string GetGuildPath(IGuild guild) {
            return Path.Combine(_path, guild.Id.ToString());
        }

        public string GetProjectPath(IGuild guild, string projectName) {
            return Path.Combine(_path, guild.Id.ToString(), projectName);
        }

        public void GenerateGuildDirectory(IGuild guild) {
            var localGuildPath = GetGuildPath(guild);

            if (!Directory.Exists(localGuildPath)) Directory.CreateDirectory(localGuildPath);
        }

        public void GenerateProjectDirectory(IGuild guild, string projectName) {
            var localProjectPath = GetProjectPath(guild, projectName);

            if (!Directory.Exists(localProjectPath)) Directory.CreateDirectory(localProjectPath);
        }

        public void DeleteProjectDirectory(IGuild guild, string projectName) {
            var localProjectPath = GetProjectPath(guild, projectName);

            if (Directory.Exists(localProjectPath)) Directory.Delete(localProjectPath, true);
        }

        private bool IsFilePermissible(string url, PermissibleFileType fileType) {
            if (!PermissibleFileExtensions.TryGetValue(fileType, out var ext)) return false;

            return ext == Path.GetExtension(url);
        }
    }
}