using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class Guild
    {
        public Guild()
        {
            Projects = new HashSet<Project>();
        }

        public int Id { get; set; }
        public decimal UniqueGuildId { get; set; }
        // TODO: Add this to the database
        public decimal? CollabCategoryId { get; set; }
        // TODO: Add this to the database
        public int MaxCollabsPerPerson { get; set; }

        public virtual ICollection<Project> Projects { get; set; }
    }
}
