using CollaborationBot.Database.Records;
using CollaborationBot.Services;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

namespace CollaborationBot.Database {

    public class CollaborationContext {
        private readonly ResourceService _resourceService;

        private string GetGuildIdStatement => $"SELECT id FROM Guilds WHERE uniqueGuildId=@uniqueGuildId";
        private string InsertNewProject => $"INSERT INTO Projects (name, guildId, status) VALUES(@name, @guildId, @projectStatus)";

        public string ConnectionString { get; set; }

        public CollaborationContext(ResourceService resourceService) {
            _resourceService = resourceService;
        }

        public void Initialize(string connectionString) {
            ConnectionString = connectionString;
        }

        public async Task<bool> AddProjectAsync(string name, ulong uniqueGuildId) {
            var uniqueGuildIdParam = new MySqlParameter("@uniqueGuildId", uniqueGuildId);
            var guildId = await SelectScalar<int>(GetGuildIdStatement, uniqueGuildIdParam);

            var nameParam = new MySqlParameter("name", name);
            var guildIdParam = new MySqlParameter("guildId", guildId);
            var statusParam = new MySqlParameter("status", ProjectStatus.Not_Started);

            return await Insert(InsertNewProject, nameParam, guildIdParam, statusParam) > 0;
        }

        public async Task<bool> RemoveProjectAsync(string name, ulong uniqueGuildId) {
            var id = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {uniqueGuildId}");
            return await ExecuteNonQueryAsync($"DELETE FROM Projects WHERE name = {name} AND guildId = {id}") > 0;
        }

        public async Task<bool> AddGuildAsync(ulong uniqueGuildId) {
            return await ExecuteNonQueryAsync($"INSERT INTO Guilds (uniqueGuildId) VALUES({uniqueGuildId})") > 0;
        }

        public async Task<GuildRecord> GetGuildAsync(ulong uniqueGuildId) {
            var guild = new GuildRecord();

            await ExecuteReaderAsync($"SELECT * FROM Guilds WHERE uniqueGuildId = {uniqueGuildId} LIMIT 1", async reader => {
                while( await reader.ReadAsync() ) {
                    guild.Id = await reader.GetFieldValueAsync<int>(0);
                    guild.UniqueGuildId = await reader.GetFieldValueAsync<ulong>(1);
                }
            });

            return guild;
        }

        public async Task<MemberRecord> GetMemberAsync(ulong uniqueMemberId, string projectName) {
            var member = new MemberRecord();

            await ExecuteReaderAsync($"SELECT * FROM Members INNER JOIN Projects ON Projects.id = Members.projectId HAVING Projects.Name = '{projectName}' AND Members.uniqueMemberId = {uniqueMemberId} LIMIT 1", async reader => {
                while( await reader.ReadAsync() ) {
                    member.Id = await reader.GetFieldValueAsync<int>(0);
                    member.UniqueMemberId = await reader.GetFieldValueAsync<ulong>(1);
                    member.GuildId = await reader.GetFieldValueAsync<int>(2);
                    member.ProjectId = await reader.GetFieldValueAsync<int>(3);
                    member.Role = await reader.GetFieldValueAsync<int>(4);
                }
            });

            return member;
        }

        public async Task<bool> AddMemberToProjectAsync(string projectName, ulong uniqueMemberId, ulong uniqueGuildId) {
            var guildId = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {uniqueGuildId}");
            var projectId = await ExecuteScalar<int>($"SELECT id FROM Projects WHERE projectName = '{projectName}' AND guildId = {guildId}");
            return await ExecuteNonQueryAsync($"INSERT INTO Members (uniqueMemberId, guildId, projectId) VALUES('{uniqueMemberId}', '{guildId}', '{projectId}')") > 0;
        }

        public async Task<List<ProjectRecord>> GetProjectListAsync(ulong guildId) {
            var id = await ExecuteScalar<int>($"SELECT id FROM Guilds WHERE uniqueGuildId = {guildId}");
            var projects = new List<ProjectRecord>();
            await ExecuteReaderAsync($"SELECT * FROM Projects WHERE guildId = {id}", async reader => {
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

        private async Task<int> Insert(string sqlStatement, params IDataParameter[] parameters) {
            try {
                using var conn = GetConnection();
                var command = new MySqlCommand(sqlStatement, conn);
                command.Parameters.AddRange(parameters);
                return await command.ExecuteNonQueryAsync();
            }
            catch( Exception ) {
                throw new Exception(_resourceService.BackendErrorMessage);
            }
        }

        private async Task<T> SelectScalar<T>(string sqlStatement, params IDataParameter[] parameters) {
            try {
                using var conn = GetConnection();
                var command = new MySqlCommand(sqlStatement, conn);
                command.Parameters.AddRange(parameters);
                return (T) await command.ExecuteScalarAsync();
            }
            catch( Exception ) {
                throw new Exception(_resourceService.BackendErrorMessage);
            }
        }

        private async Task<int> ExecuteNonQueryAsync(string sqlQuery) {
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

        private async Task<bool> ExecuteReaderAsync(string sqlQuery, Action<DbDataReader> operation) {
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