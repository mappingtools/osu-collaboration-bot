using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class AutoUpdate
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public decimal UniqueChannelId { get; set; }
        public TimeSpan? Cooldown { get; set; }
        public bool DoPing { get; set; }
        public bool ShowOsu { get; set; }
        public bool ShowOsz { get; set; }

        public virtual Project Project { get; set; }
    }
}
