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
            var id = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {uniqueGuildId}");
            return await ExecuteNonQuery($"INSERT INTO Projects (name, guildId) VALUES('{name}', '{id}')") > 0;
        }

        public async Task<bool> AddGuild(ulong uniqueGuildId) {
            return await ExecuteNonQuery($"INSERT INTO Guilds (uniqueGuildId) VALUES({uniqueGuildId})") > 0;
        }

        public async Task<bool> AddMemberToProject(string projectName, ulong uniqueMemberId, ulong uniqueGuildId) {
            var guildId = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {uniqueGuildId}");
            var projectId = await ExecuteScalar<int>($"SELECT id FROM Projects WHERE projectName = '{projectName}' AND guildId = {guildId}");
            return await ExecuteNonQuery($"INSERT INTO Members (uniqueMemberId, guildId, projectId) VALUES('{uniqueMemberId}', '{guildId}', '{projectId}')") > 0;
        }

        public async Task<List<ProjectRecord>> GetProjectList(ulong guildId) {
            var id = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {guildId}");
            var projects = new List<ProjectRecord>();
            await ExecuteReader($"SELECT name FROM Projects WHERE guildId = {id}", async reader => {
                if (reader.HasRows) {
                    while (await reader.ReadAsync()) {
                        projects.Add(new ProjectRecord {
                            id = await reader.GetFieldValueAsync<int>(0), 
                            name = await reader.GetFieldValueAsync<string>(1), 
                            guildId = await reader.GetFieldValueAsync<int>(2)
                        });
                    }
                }

                await reader.CloseAsync();
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

        private async Task ExecuteReader(string sqlQuery, Action<DbDataReader> operation) {
            try {
                using var conn = GetConnection();
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = sqlQuery;

                using var reader = await cmd.ExecuteReaderAsync();
                operation(reader);
                await reader.CloseAsync();
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