namespace CollaborationBot.Database.Records {

    public class MemberRecord {
        public int Id { get; set; }
        public ulong UserId { get; set; }
        public int GuildId { get; set; }
        public int ProjectId { get; set; }
    }
}