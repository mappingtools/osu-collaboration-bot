using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class Part : IComparable<Part>
    {
        public Part()
        {
            Assignments = new HashSet<Assignment>();
        }

        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string Name { get; set; }
        public PartStatus? Status { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }

        public virtual Project Project { get; set; }
        public virtual ICollection<Assignment> Assignments { get; set; }

        public int CompareTo(Part other) {
            if (!Start.HasValue && !End.HasValue && !other.Start.HasValue && !other.End.HasValue)
                return 0;

            if (!Start.HasValue && !End.HasValue && (other.Start.HasValue || other.End.HasValue))
                return 1;

            if ((Start.HasValue || End.HasValue) && !other.Start.HasValue && !other.End.HasValue)
                return -1;

            if (End.HasValue && !other.End.HasValue)
                return -1;

            if (!End.HasValue && other.End.HasValue)
                return 1;

            if (Start.HasValue && !other.Start.HasValue)
                return 1;

            if (!Start.HasValue && other.Start.HasValue)
                return -1;

            if (Start.HasValue && other.Start.HasValue) {
                return Start.Value.CompareTo(other.Start.Value);
            } else {
                return End.Value.CompareTo(other.End.Value);
            }
        }
    }
}
