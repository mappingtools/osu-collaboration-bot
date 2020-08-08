using CollaborationBot.Services;
using MySql.Data.MySqlClient;
using System;
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

        public async Task<bool> AddProject(string name) {
            return await ExecuteNonQuery($"INSERT INTO Projects (name) VALUES('{name}')") > 0;
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

        private async void ExecuteReader(string sqlQuery, Action<DbDataReader> operation) {
            try {
                using var conn = GetConnection();
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = sqlQuery;

                using var reader = await cmd.ExecuteReaderAsync();
                operation(reader);
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