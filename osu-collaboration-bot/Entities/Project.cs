using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class Project
    {
        public Project()
        {
            AutoUpdates = new HashSet<AutoUpdate>();
            Members = new HashSet<Member>();
            Parts = new HashSet<Part>();
        }

        public int Id { get; set; }
        public int GuildId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal? UniqueRoleId { get; set; }
        public decimal? ManagerRoleId { get; set; }
        public ProjectStatus? Status { get; set; }
        public bool SelfAssignmentAllowed { get; set; }
        public int? MaxAssignments { get; set; }
        public bool PriorityPicking { get; set; }
        public bool PartRestrictedUpload { get; set; }
        public TimeSpan? AssignmentLifetime { get; set; }
        public decimal? MainChannelId { get; set; }
        public decimal? InfoChannelId { get; set; }
        public bool CleanupOnDeletion { get; set; }
        public bool DoReminders { get; set; }
        public DateTime? LastActivity { get; set; }
        public bool AutoGeneratePriorities { get; set; }
        public bool JoinAllowed { get; set; }

        public virtual Guild Guild { get; set; }
        public virtual ICollection<AutoUpdate> AutoUpdates { get; set; }
        public virtual ICollection<Member> Members { get; set; }
        public virtual ICollection<Part> Parts { get; set; }
    }
}
