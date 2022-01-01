using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class Member
    {
        public Member()
        {
            Assignments = new HashSet<Assignment>();
        }

        public int Id { get; set; }
        public int ProjectId { get; set; }
        public decimal UniqueMemberId { get; set; }
        public ProjectRole ProjectRole { get; set; }
        public int? Priority { get; set; }
        public string Alias { get; set; }
        public string Tags { get; set; }
        public decimal? ProfileId { get; set; }

        public virtual Project Project { get; set; }
        public virtual ICollection<Assignment> Assignments { get; set; }
    }
}
