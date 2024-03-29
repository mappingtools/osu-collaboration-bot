﻿using System;
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
        public decimal? CollabCategoryId { get; set; }
        public int MaxCollabsPerPerson { get; set; }
        public bool GenerateRoles { get; set; }
        public TimeSpan? InactivityTimer { get; set; }

        public virtual ICollection<Project> Projects { get; set; }
    }
}
