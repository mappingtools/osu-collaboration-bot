using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CollaborationBot.Entities;
using CollaborationBot.Resources;
using Discord;
using Discord.WebSocket;

namespace CollaborationBot.Services {
    public class ResourceService {
        private readonly DiscordSocketClient _client;

        public ResourceService(DiscordSocketClient client) {
            _client = client;
        }

        public string GenerateSubmitPartMessage(string projectName, int hitObjectCount, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.SubmitPartSuccessMessage, hitObjectCount, projectName);
            return string.Format(Strings.SubmitPartFailMessage, projectName);
        }

        public string GenerateAddProjectMessage(string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.AddProjectSuccess, projectName);
            return string.Format(Strings.AddProjectFail, projectName);
        }

        public string GenerateRemoveProjectMessage(string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.RemoveProjectSuccess, projectName);
            return string.Format(Strings.RemoveProjectFail, projectName);
        }

        public string GenerateAddGuildMessage(bool isSuccessful = true) {
            if (isSuccessful)
                return Strings.AddGuildSuccess;
            return Strings.AddGuildFail;
        }

        public string GenerateAddMemberToProject(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.AddMemberSuccessMessage, user.Mention, projectName);
            return string.Format(Strings.AddMemberFailMessage, user.Mention, projectName);
        }

        public string GenerateRemoveMemberFromProject(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.RemoveMemberSuccessMessage, user.Mention, projectName);
            return string.Format(Strings.RemoveMemberFailMessage, user.Mention, projectName);
        }

        public string GenerateSetOwner(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.SetOwnerSuccessMessage, projectName, user.Mention);
            return string.Format(Strings.SetOwnerFailMessage, projectName, user.Mention);
        }

        public string GenerateAddManager(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.AddManagerSuccessMessage, user.Mention, projectName);
            return string.Format(Strings.AddManagerFailMessage, user.Mention, projectName);
        }

        public string GenerateRemoveManager(IMentionable user, string projectName, bool isSuccessful = true) {
            if (isSuccessful)
                return string.Format(Strings.RemoveManagerSuccessMessage, user.Mention, projectName);
            return string.Format(Strings.RemoveManagerFailMessage, user.Mention, projectName);
        }

        public string GenerateUnauthorizedMessage(IMentionable mention) {
            return string.Format(Strings.CommandUnauthorized, mention.Mention);
        }
        
        public string GenerateProjectListMessage(List<Project> projects) {
            if (projects.Count <= 0) return Strings.NoProjects; 
            return GenerateListMessage(Strings.ProjectListMessage, projects.Select(p => $"{p.Name}{(p.Status.HasValue ? $" ({p.Status})" : string.Empty)}"));
        }

        public string GenerateMembersListMessage(List<Member> members) {
            if (members.Count <= 0) return Strings.NoMembers;
            return GenerateListMessage(Strings.MemberListMessage, 
                members.Select(o =>
                    $"{MemberName(o)}{(o.Priority.HasValue ? $" ({o.Priority.Value})" : string.Empty)} [{o.ProjectRole}]"));
        }

        public string GeneratePartsListMessage(List<Part> parts) {
            if (parts.Count <= 0) return Strings.NoParts;
            return GenerateListMessage(Strings.PartListMessage,
                parts.Select(o => {
                    var str = $"{o.Name} ({TimeToString(o.Start)} - {TimeToString(o.End)}): {o.Status}";
                    if (o.Assignments.Count > 0) {
                        var builder = new StringBuilder(" {");
                        builder.AppendJoin(", ", o.Assignments.Select(a => MemberName(a.Member)));
                        builder.Append('}');
                        str += builder.ToString();
                    }
                    return str;
                }));
        }

        public Embed[] GeneratePartsListEmbeds(List<Part> parts) {
            if (parts.Count <= 0) return null;
            return GenerateListEmbeds(parts.Select(o => {
                var str = $"({TimeToString(o.Start)} - {TimeToString(o.End)}): {o.Status}";
                if (o.Assignments.Count > 0) {
                    var builder = new StringBuilder(" {");
                    builder.AppendJoin(", ", o.Assignments.Select(a => MemberName(a.Member)));
                    builder.Append('}');
                    str += builder.ToString();
                }

                return (o.Name, str);
            }));
        }

        public string GeneratePartsListDescription(List<Part> parts, bool includeMappers = true, bool includePartNames = false) {
            var builder = new StringBuilder("```[notice][box=Parts]\n");
            foreach (Part part in parts) {
                string mappers = includeMappers ? ": " + string.Join(", ", part.Assignments.Select(a => a.Member.ProfileId.HasValue ? $"[profile={a.Member.ProfileId}]{MemberAliasOrName(a.Member)}[/profile]" : MemberAliasOrName(a.Member))) : string.Empty;
                string partName = includePartNames ? " " + part.Name : string.Empty;
                builder.AppendLine($"({TimeToString(part.Start)} - {TimeToString(part.End)}){partName}{mappers}");
            }
            builder.Append("[/box][/notice]\n```");

            return builder.ToString();
        }

        public string GenerateDraintimesListMessage(List<KeyValuePair<Member, int>> draintimes) {
            if (draintimes.Count <= 0) return Strings.NoAssignments;
            return GenerateListMessage(Strings.DrainTimeListMessage,
                draintimes.Select(m => $"{MemberName(m.Key)}: {TimeToString(m.Value)}"));
        }

        public string GenerateAssignmentListMessage(List<Assignment> assignments) {
            if (assignments.Count <= 0) return Strings.NoAssignments;
            return GenerateListMessage(Strings.AssignmentListMessage,
                assignments.Select(o => $"{o.Part.Name}: {MemberName(o.Member)}{(o.Deadline.HasValue ? " - " + o.Deadline.Value.ToString("yyyy-MM-dd") : string.Empty)}"));
        }

        public string GenerateListMessage(string message, IEnumerable<string> list) {
            var builder = new StringBuilder();
            builder.AppendLine(message);
            builder.Append("```");
            foreach (var item in list) builder.AppendLine($"- {item}");
            builder.Append("```");
            return builder.ToString();
        }

        public Embed[] GenerateListEmbeds(IEnumerable<(string, string)> list) {
            var array = list.ToArray();
            var embeds = new Embed[(array.Length - 1) / 25 + 1];
            var e = 0;
            var c = 0;
            var embedBuilder = new EmbedBuilder();
            foreach (var (name, value) in array) {
                embedBuilder.AddField(name, value);
                c++;

                if (c != 25) continue;
                embeds[e++] = embedBuilder.Build();
                embedBuilder = new EmbedBuilder();
                c = 0;
            }

            if (c > 0) {
                embeds[e] = embedBuilder.Build();
            }

            return embeds;
        }

        public string MemberName(Member member) {
            var user = _client.GetUser((ulong)member.UniqueMemberId);
            string name = user is not null ? user.Username : Strings.UnknownUser;
            if (member.Alias != null) {
                name += $" \"{member.Alias}\"";
            }
            return name;
        }

        public string MemberAliasOrName(Member member) {
            if (member.Alias != null) {
                return member.Alias;
            }
            var user = _client.GetUser((ulong)member.UniqueMemberId);
            string name = user is not null ? user.Username : Strings.UnknownUser;
            return name;
        }

        public string TimeToString(int? time) {
            if (!time.HasValue)
                return "XX:XX:XXX";

            var timeSpan = TimeSpan.FromMilliseconds(time.Value);

            return $"{(timeSpan.Days > 0 ? $"{timeSpan.Days:####}:" : string.Empty)}" +
                    $"{(timeSpan.Hours > 0 ? $"{timeSpan.Hours:00}:" : string.Empty)}" +
                    $"{timeSpan.Minutes:00}:{timeSpan.Seconds:00}:{timeSpan.Milliseconds:000}";
        }
    }
}