namespace CollaborationBot.Database.Records {

    public enum ProjectRole {
        Owner = 0,
        Manager = 1,
        Member = 2
    }

    public class MemberRecord {
        public int Id { get; set; }
        public ulong UniqueMemberId { get; set; }
        public int GuildId { get; set; }
        public int ProjectId { get; set; }
        public int Role { get; set; }
        public ProjectRole ProjectRole => (ProjectRole) Role;
    }
}