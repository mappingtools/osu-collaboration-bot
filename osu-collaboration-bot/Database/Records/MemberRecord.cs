namespace CollaborationBot.Database.Records {

    public class MemberRecord {
        public int id { get; set; }
        public ulong userId { get; set; }
        public int guildId { get; set; }
        public int projectId { get; set; }
    }
}