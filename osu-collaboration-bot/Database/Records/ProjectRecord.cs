namespace CollaborationBot.Database.Records {

    public enum ProjectStatus {
        Finished,
        In_Review,
        In_Progress,
        Assigning_Parts,
        Searching_For_Members,
        On_Hold,
        Not_Started
    }

    public class ProjectRecord {
        public int id { get; set; }
        public string name { get; set; }
        public int guildId { get; set; }
    }
}