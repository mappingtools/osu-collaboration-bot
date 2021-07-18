using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class Assignment
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public int PartId { get; set; }
        public DateTime? Deadline { get; set; }

        public virtual Member Member { get; set; }
        public virtual Part Part { get; set; }
    }
}
