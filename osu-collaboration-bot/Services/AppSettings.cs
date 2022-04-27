namespace CollaborationBot.Services {
    public class AppSettings
    {
        public string ConnectionString { get; set; }
        public string Token { get; set; }
        public string Path { get; set; }
        public string Prefix => "/";
    }
}
