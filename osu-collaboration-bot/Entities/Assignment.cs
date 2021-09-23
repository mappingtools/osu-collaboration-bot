using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class Assignment : IComparable<Assignment>
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public int PartId { get; set; }
        public DateTime? Deadline { get; set; }
        // TODO: add this to the database
        public DateTime? LastReminder { get; set; }

        public virtual Member Member { get; set; }
        public virtual Part Part { get; set; }

        public int CompareTo(Assignment other) {
            if (!Deadline.HasValue && !other.Deadline.HasValue) {
                return 0;
            } 

            if (Deadline.HasValue && !other.Deadline.HasValue) {
                return -1;
            }

            if (!Deadline.HasValue) {
                return 1;
            }

            return Deadline.Value.CompareTo(other.Deadline.Value);
        }
    }
}
