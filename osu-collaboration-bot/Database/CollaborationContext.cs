using CollaborationBot.Database.Records;
using CollaborationBot.Services;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace CollaborationBot.Database {

    public class CollaborationContext {
        private readonly ResourceService _resourceService;

        public string ConnectionString { get; set; }

        public CollaborationContext(ResourceService resourceService) {
            _resourceService = resourceService;
        }

        public void Initialize(string connectionString) {
            ConnectionString = connectionString;
        }

        public async Task<bool> AddProject(string name, ulong uniqueGuildId) {
            var id = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {uniqueGuildId} LIMIT 1");
            return await ExecuteNonQuery($"INSERT INTO Projects (name, guildId, status) VALUES('{name}', '{id}', '{(int) ProjectStatus.Not_Started}')") > 0;
        }

        public async Task<bool> RemoveProject(string name, ulong uniqueGuildId) {
            var id = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {uniqueGuildId}");
            return await ExecuteNonQuery($"DELETE FROM Projects WHERE name = {name} AND guildId = {id}") > 0;
        }

        public async Task<bool> AddGuild(ulong uniqueGuildId) {
            return await ExecuteNonQuery($"INSERT INTO Guilds (uniqueGuildId) VALUES({uniqueGuildId})") > 0;
        }

        public async Task<GuildRecord> GetGuild(ulong uniqueGuildId) {
            var guild = new GuildRecord();

            await ExecuteReader($"SELECT * FROM Guilds WHERE uniqueGuildId = {uniqueGuildId} LIMIT 1", async reader => {
                while( await reader.ReadAsync() ) {
                    guild.Id = await reader.GetFieldValueAsync<int>(0);
                    guild.UniqueGuildId = await reader.GetFieldValueAsync<ulong>(1);
                }
            });

            return guild;
        }

        public async Task<bool> AddMemberToProject(string projectName, ulong uniqueMemberId, ulong uniqueGuildId) {
            var guildId = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {uniqueGuildId}");
            var projectId = await ExecuteScalar<int>($"SELECT id FROM Projects WHERE projectName = '{projectName}' AND guildId = {guildId}");
            return await ExecuteNonQuery($"INSERT INTO Members (uniqueMemberId, guildId, projectId) VALUES('{uniqueMemberId}', '{guildId}', '{projectId}')") > 0;
        }

        public async Task<List<ProjectRecord>> GetProjectList(ulong guildId) {
            var id = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {guildId}");
            var projects = new List<ProjectRecord>();
            await ExecuteReader($"SELECT * FROM Projects WHERE guildId = {id}", async reader => {
                while( await reader.ReadAsync() ) {
                    projects.Add(new ProjectRecord {
                        Id = await reader.GetFieldValueAsync<int>(0),
                        Name = await reader.GetFieldValueAsync<string>(1),
                        GuildId = await reader.GetFieldValueAsync<int>(2)
                    });
                }
            });
            return projects;
        }

        private async Task<int> ExecuteNonQuery(string sqlQuery) {
            try {
                using var conn = GetConnection();
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = sqlQuery;

                return await cmd.ExecuteNonQueryAsync();
            }
            catch( Exception ) {
                throw new Exception(_resourceService.BackendErrorMessage);
            }
        }

        private async Task<bool> ExecuteReader(string sqlQuery, Action<DbDataReader> operation) {
            try {
                using var conn = GetConnection();
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = sqlQuery;

                using var reader = await cmd.ExecuteReaderAsync();
                operation(reader);

                var res = reader.HasRows;
                await reader.CloseAsync();
                return res;
            }
            catch( Exception ) {
                throw new Exception(_resourceService.BackendErrorMessage);
            }
        }

        private async Task<T> ExecuteScalar<T>(string sqlQuery) {
            try {
                using var conn = GetConnection();
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = sqlQuery;

                return (T) await cmd.ExecuteScalarAsync();
            }
            catch( Exception ) {
                throw new Exception(_resourceService.BackendErrorMessage);
            }
        }

        private MySqlConnection GetConnection() {
            return new MySqlConnection(ConnectionString);
        }
    }
}