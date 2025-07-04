﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Discord;
using NLog;
using Strings = CollaborationBot.Resources.Strings;

namespace CollaborationBot.Services {
    public class FileHandlingService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private enum PermissibleFileType {
            DotOsu,
            DotTsv,
            DotCSV
        }

        private string _path;

        private readonly Dictionary<PermissibleFileType, string> _permissibleFileExtensions = new() {
            {PermissibleFileType.DotOsu, ".osu"},
            {PermissibleFileType.DotTsv, ".tsv"},
            {PermissibleFileType.DotCSV, ".csv"},
        };

        public void Initialize(string path) {
            _path = path;
        }

        public async Task<string> DownloadBaseFile(IGuild guild, string projectName, Attachment att) {
            try {
                if (!IsFilePermissible(att.Filename, PermissibleFileType.DotOsu)) return Strings.FileTypeNeedsToBeOsu;

                var localProjectPath = GetProjectPath(guild, projectName);

                if (!Directory.Exists(localProjectPath)) {
                    GenerateProjectDirectory(guild, projectName);
                }

                if (!Uri.TryCreate(att.Url, UriKind.Absolute, out var uri)) return Strings.CouldNotCreateUri;

                var oldFilePath = GetProjectBaseFilePath(guild, projectName);
                var filePath = Path.Combine(localProjectPath, att.Filename);

                using var client = new HttpClient();
                var response = await client.GetAsync(uri);
                await using (var fs = new FileStream(filePath, FileMode.Create)) {
                    await response.Content.CopyToAsync(fs);
                }

                if (!string.IsNullOrEmpty(oldFilePath) && oldFilePath != filePath && File.Exists(oldFilePath)) {
                    File.Delete(oldFilePath);
                }

                return null;
            }
            catch (Exception e) {
                logger.Error(e);
                return Strings.UploadBaseFileFail;
            }
        }

        public async Task<string> DownloadPartSubmit(IGuild guild, string projectName, Attachment att) {
            try {
                if (!IsFilePermissible(att.Filename, PermissibleFileType.DotOsu)) return null;

                var localProjectPath = GetProjectPath(guild, projectName);

                if (!Directory.Exists(localProjectPath)) return null;

                if (!Uri.TryCreate(att.Url, UriKind.Absolute, out var uri)) return null;

                using var client = new HttpClient();
                var result = await client.GetStringAsync(uri);

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
                if (!IsFilePermissible(att.Filename, PermissibleFileType.DotCSV)) return null;

                if (!Uri.TryCreate(att.Url, UriKind.Absolute, out var uri)) return null;

                using var client = new HttpClient();
                var result = await client.GetStreamAsync(uri);

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    PrepareHeaderForMatch = args => args.Header.ToLower(),
                    HasHeaderRecord = hasHeaders,
                };
                using var reader = new StreamReader(result);
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

        public bool GuildDirectoryExists(IGuild guild) {
            return Directory.Exists(GetGuildPath(guild));
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
            if (!_permissibleFileExtensions.TryGetValue(fileType, out var ext)) return false;

            return ext == Path.GetExtension(url);
        }
    }
}