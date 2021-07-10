using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class Part
    {
        public Part()
        {
            Assignments = new HashSet<Assignment>();
        }

        public int Id { get; set; }
        public int? ProjectId { get; set; }
        public string Name { get; set; }
        public PartStatus? Status { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }

        public virtual Project Project { get; set; }
        public virtual ICollection<Assignment> Assignments { get; set; }
    }
}
