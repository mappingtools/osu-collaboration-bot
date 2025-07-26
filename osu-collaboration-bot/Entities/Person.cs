using System;
using System.Collections.Generic;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class Person
    {
        public Person()
        {
        }

        public decimal UniqueMemberId { get; set; }
        public string Username { get; set; }
        public string GlobalName { get; set; }
        public string Alias { get; set; }
        public string Tags { get; set; }
        public decimal? ProfileId { get; set; }

        public virtual ICollection<Member> Members { get; set; }

        public string Mention => $"<@{UniqueMemberId}>";
    }
}
