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

        private string InsertNewGuildStatement => "INSERT INTO Guilds (uniqueGuildId) VALUES(@uniqueGuildid)";
        private string GetGuildStatement => "SELECT * FROM Guilds WHERE uniqueGuildId=@uniqueGuildId";
        private string GetGuildIdStatement => "SELECT id FROM Guilds WHERE uniqueGuildId=@uniqueGuildId";
        private string GetProjectsFromGuildStatement => "SELECT * FROM Projects INNER JOIN Guilds ON Guilds.id=Projects.guildId HAVING Guilds.uniqueGuildId=@uniqueGuildId";
        private string GetProjectFromGuildStatement => "SELECT * FROM Projects INNER JOIN Guilds ON Guilds.id=Projects.guildID HAVING Guilds.uniqueGuildId=@uniqueGuildId AND Projects.name=@projectName";
        private string InsertNewProjectStatement => "INSERT INTO Projects (name, guildId, status) VALUES(@name, @guildId, @status)";
        private string DeleteProjectStatement => "DELETE FROM Projects WHERE name=@name AND guildId=@guildId";
        private string InsertNewMemberStatement => "INSERT INTO Members (uniqueMemberId, guildId, projectId, role) VALUES(@uniqueMemberId, @guildId, @projectId, @role)";
        private string GetMemberFromProjectStatement => "SELECT * FROM Members INNER JOIN Projects ON Projects.id=Members.projectId HAVING Projects.Name=@projectName AND Members.uniqueMemberId=@uniqueMemberId";

        public string ConnectionString { get; set; }

        public CollaborationContext(ResourceService resourceService) {
            _resourceService = resourceService;
        }

        public void Initialize(string connectionString) {
            ConnectionString = connectionString;
        }

        public async Task<bool> AddProjectAsync(string name, ulong uniqueGuildId) {
            var uniqueGuildIdParam = new MySqlParameter("@uniqueGuildId", uniqueGuildId);
            var guildId = await ExecuteScalarAsync<int>(GetGuildIdStatement, uniqueGuildIdParam);

            var nameParam = new MySqlParameter("@name", name);
            var guildIdParam = new MySqlParameter("@guildId", guildId);
            var statusParam = new MySqlParameter("@status", ProjectStatus.Not_Started);

            return await ExecuteAsync(InsertNewProjectStatement, nameParam, guildIdParam, statusParam) > 0;
        }

        public async Task<bool> RemoveProjectAsync(string name, ulong uniqueGuildId) {
            var uniqueGuildIdParam = new MySqlParameter("@uniqueGuildId", uniqueGuildId);
            var guildId = await ExecuteScalarAsync<int>(GetGuildIdStatement, uniqueGuildIdParam);

            var nameParam = new MySqlParameter("@name", name);
            var guildIdParam = new MySqlParameter("@guildId", guildId);

            return await ExecuteAsync(DeleteProjectStatement, nameParam, guildIdParam) > 0;
        }

        public async Task<bool> AddGuildAsync(ulong uniqueGuildId) {
            var uniqueGuildIdParam = new MySqlParameter("@uniqueGuildId", uniqueGuildId);
            return await ExecuteAsync(InsertNewGuildStatement, uniqueGuildIdParam) > 0;
        }

        public async Task<GuildRecord> GetGuildAsync(ulong uniqueGuildId) {
            var uniqueGuildIdParam = new MySqlParameter("@uniqueGuildId", uniqueGuildId);

            var guild = new GuildRecord();

            await ExecuteReaderAsync(async reader => {
                while( await reader.ReadAsync() ) {
                    guild.Id = await reader.GetFieldValueAsync<int>(0);
                    guild.UniqueGuildId = await reader.GetFieldValueAsync<ulong>(1);
                }
            }, GetGuildStatement, uniqueGuildIdParam);

            return guild;
        }

        public async Task<MemberRecord> GetMemberAsync(ulong uniqueMemberId, string projectName) {
            var uniqueMemberIdParam = new MySqlParameter("@uniqueMemberId", uniqueMemberId);
            var projectNameParam = new MySqlParameter("@projectName", projectName);

            var member = new MemberRecord();

            await ExecuteReaderAsync(async reader => {
                while( await reader.ReadAsync() ) {
                    member.Id = await reader.GetFieldValueAsync<int>(0);
                    member.UniqueMemberId = await reader.GetFieldValueAsync<ulong>(1);
                    member.GuildId = await reader.GetFieldValueAsync<int>(2);
                    member.ProjectId = await reader.GetFieldValueAsync<int>(3);
                    member.Role = await reader.GetFieldValueAsync<int>(4);
                }
            }, GetMemberFromProjectStatement, uniqueMemberIdParam, projectNameParam);

            return member;
        }

        public async Task<bool> AddMemberToProjectAsync(string projectName, ulong uniqueMemberId, ulong uniqueGuildId) {
            var projectNameParam = new MySqlParameter("@projectName", projectName);
            var uniqueGuildIdParam = new MySqlParameter("@uniqueGuildId", uniqueGuildId);

            int projectId = 0;
            int guildId = 0;

            await ExecuteReaderAsync(async reader => {
                while( await reader.ReadAsync() ) {
                    projectId = await reader.GetFieldValueAsync<int>(0);
                    guildId = await reader.GetFieldValueAsync<int>(2);
                }
            }, GetProjectFromGuildStatement, projectNameParam, uniqueGuildIdParam);

            if( projectId <= 0 || guildId <= 0 ) {
                return false;
            }

            var uniqueMemberIdParam = new MySqlParameter("@uniqueMemberId", uniqueMemberId);
            var guildIdParam = new MySqlParameter("@guildId", guildId);
            var projectIdParam = new MySqlParameter("@projectId", projectId);
            var roleParam = new MySqlParameter("@role", ProjectRole.Member);

            return await ExecuteAsync(InsertNewMemberStatement, uniqueMemberIdParam, guildIdParam, projectIdParam, roleParam) > 0;
        }

        public async Task<List<ProjectRecord>> GetProjectListAsync(ulong uniqueGuildId) {
            var uniqueGuildIdParam = new MySqlParameter("@uniqueGuildId", uniqueGuildId);

            var projects = new List<ProjectRecord>();

            await ExecuteReaderAsync(async reader => {
                while( await reader.ReadAsync() ) {
                    projects.Add(new ProjectRecord {
                        Id = await reader.GetFieldValueAsync<int>(0),
                        Name = await reader.GetFieldValueAsync<string>(1),
                        GuildId = await reader.GetFieldValueAsync<int>(2),
                        Status = await reader.GetFieldValueAsync<int>(3)
                    });
                }
            }, GetProjectsFromGuildStatement, uniqueGuildIdParam);

            return projects;
        }

        private async Task<int> ExecuteAsync(string sqlStatement, params MySqlParameter[] parameters) {
            try {
                using var conn = GetConnection();
                await conn.OpenAsync();

                var command = new MySqlCommand(sqlStatement, conn);

                foreach( var param in parameters ) {
                    command.Parameters.Add(param);
                }

                return await command.ExecuteNonQueryAsync();
            }
            catch( Exception ) {
                throw new Exception(_resourceService.BackendErrorMessage);
            }
        }

        private async Task<T> ExecuteScalarAsync<T>(string sqlStatement, params MySqlParameter[] parameters) {
            try {
                using var conn = GetConnection();
                await conn.OpenAsync();

                var command = new MySqlCommand(sqlStatement, conn);

                foreach( var param in parameters ) {
                    command.Parameters.Add(param);
                }

                return (T) await command.ExecuteScalarAsync();
            }
            catch( Exception ) {
                throw new Exception(_resourceService.BackendErrorMessage);
            }
        }

        private async Task<bool> ExecuteReaderAsync(Action<DbDataReader> operation, string sqlStatement, params MySqlParameter[] parameters) {
            try {
                using var conn = GetConnection();
                await conn.OpenAsync();

                var command = new MySqlCommand(sqlStatement, conn);

                foreach( var param in parameters ) {
                    command.Parameters.Add(param);
                }

                using var reader = await command.ExecuteReaderAsync();
                operation(reader);

                var res = reader.HasRows;

                await reader.CloseAsync();

                return res;
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