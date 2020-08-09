namespace CollaborationBot.Database.Records {

    public enum ProjectStatus {
        Finished = 0,
        In_Review = 1,
        In_Progress = 2,
        Assigning_Parts = 3,
        Searching_For_Members = 4,
        On_Hold = 5,
        Not_Started = 6
    }

    public class ProjectRecord {
        public int Id { get; set; }
        public string Name { get; set; }
        public int GuildId { get; set; }
        public int Status { get; set; }
        public ProjectStatus ProjectStatus => (ProjectStatus) Status;
    }
}