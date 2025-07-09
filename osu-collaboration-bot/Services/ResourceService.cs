using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using CollaborationBot.Resources;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;

namespace CollaborationBot.Services {
    public class ResourceService {
        private readonly DiscordSocketClient _client;
        private readonly InteractiveService _interactive;

        public ResourceService(DiscordSocketClient client, InteractiveService interactive) {
            _client = client;
            _interactive = interactive;
        }

        /// <summary>
        /// Responds to the interaction with a paginator. Any command which uses this MUST run async.
        /// </summary>
        /// <param name="context">The interaction context.</param>
        /// <param name="items">The items to put in the paginator.</param>
        /// <param name="pageMaker">Function mapping items to pages.</param>
        /// <param name="nothingString">The message to respond with if there are no items.</param>
        /// <param name="message">The message to respond with if there are items.</param>
        /// <typeparam name="T">The type of item to display.</typeparam>
        public async Task RespondPaginator<T>(SocketInteractionContext context, List<T> items,
        Func<List<T>, Task<IPageBuilder[]>> pageMaker, string nothingString, string message) {
            if (items.Count == 0) {
                await context.Interaction.RespondAsync(nothingString);
                return;
            }

            await context.Interaction.RespondAsync(message);

            var paginator = new StaticPaginatorBuilder()
                .WithPages(await pageMaker(items))
                .WithDefaultTimeoutPage()
                .WithDefaultCanceledPage()
                .Build();

            await _interactive.SendPaginatorAsync(paginator, context.Channel);
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
            return projects.Count <= 0 ? Strings.NoProjects : GenerateListMessage(Strings.ProjectListMessage,
                projects.Select(p => $"{p.Name}{(p.Status.HasValue ? $" ({p.Status})" : string.Empty)}"));
        }

        public Task<IPageBuilder[]> GenerateProjectListPages(List<Project> projects) {
            return Task.FromResult(projects.Count <= 0 ? null : GenerateListPages(projects.Select(p => (p.Name, p.Status.ToString())), Strings.Projects));
        }

        public string GenerateMembersListMessage(List<Member> members) {
            if (members.Count <= 0) return Strings.NoMembers;
            return GenerateListMessage(Strings.MemberListMessage, 
                members.Select(o =>
                    $"{MemberName(o)}{(o.Priority.HasValue ? $" ({o.Priority.Value})" : string.Empty)} [{o.ProjectRole}]"));
        }

        public async Task<IPageBuilder[]> GenerateMembersListPages(List<Member> members) {
            if (members.Count <= 0) return null;
            return await GenerateListPages(members.Select(async o =>
                (await MemberName(o), $"{o.ProjectRole}{(o.Priority.HasValue ? $" ({o.Priority.Value})" : string.Empty)}")), Strings.Members);
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

        public async Task<IPageBuilder[]> GeneratePartsListPages(List<Part> parts) {
            if (parts.Count <= 0) return null;
            return await GenerateListPages(parts.Select(async o => {
                var str = $"({TimeToString(o.Start)} - {TimeToString(o.End)}): {o.Status}";
                if (o.Assignments.Count > 0) {
                    var builder = new StringBuilder(" {");
                    builder.AppendJoin(", ", await Task.WhenAll(o.Assignments.Select(async a => await MemberName(a.Member))));
                    builder.Append('}');
                    str += builder.ToString();
                }

                return (o.Name, str);
            }), Strings.Parts);
        }

        public async Task<string> GeneratePartsListDescription(List<Part> parts, bool includeMappers = true, bool includePartNames = false) {
            var builder = new StringBuilder("```[notice][box=Parts]\n");
            var info = new List<(int?, int?, string, string)>();
            foreach (Part part in parts) {
                string mappers = includeMappers ? ": " + string.Join(", ", await Task.WhenAll(part.Assignments.Select(async a => a.Member.ProfileId.HasValue ? $"[profile={a.Member.ProfileId}]{await MemberAliasOrName(a.Member)}[/profile]" : await MemberAliasOrName(a.Member)))) : string.Empty;
                string partName = includePartNames ? " " + part.Name : string.Empty;
                info.Add((part.Start, part.End, partName, mappers));
            }

            // Merge consecutive parts with the same name and mappers
            for (int i = 1; i < info.Count; i++) {
                if (info[i].Item3 == info[i - 1].Item3 && info[i].Item4 == info[i - 1].Item4) {
                    info[i - 1] = (info[i - 1].Item1, info[i].Item2, info[i - 1].Item3, info[i - 1].Item4);
                    info.RemoveAt(i);
                    i--;
                }
            }

            foreach (var (start, end, partName, mappers) in info) {
                builder.AppendLine($"[{TimeToString(start)} - {TimeToString(end)}]{partName}{mappers}");
            }
            builder.Append("[/box][/notice]\n```");

            return builder.ToString();
        }

        public async Task RespondTextOrFile(IDiscordInteraction interaction, string text) {
            // Respond with a file if the content is over 2000 characters
            if (text.Length <= 2000) {
                await interaction.RespondAsync(text);
            } else {
                const string tempTextFileName = "message.txt";
                await File.WriteAllTextAsync(tempTextFileName, text);
                await interaction.RespondWithFileAsync(tempTextFileName);
                File.Delete(tempTextFileName);
            }
        }

        public string GenerateDraintimesListMessage(List<KeyValuePair<Member, int>> draintimes) {
            if (draintimes.Count <= 0) return Strings.NoAssignments;
            return GenerateListMessage(Strings.DrainTimeListMessage,
                draintimes.Select(m => $"{MemberName(m.Key)}: {TimeToString(m.Value)}"));
        }

        public async Task<IPageBuilder[]> GenerateDrainTimePages(List<KeyValuePair<Member, int>> drainTimes) {
            if (drainTimes.Count <= 0) return null;
            return await GenerateListPages(
                drainTimes.Select(async m => (await MemberName(m.Key), TimeToString(m.Value))), Strings.AutoUpdates);
        }

        public string GenerateAssignmentListMessage(List<Assignment> assignments) {
            if (assignments.Count <= 0) return Strings.NoAssignments;
            return GenerateListMessage(Strings.AssignmentListMessage,
                assignments.Select(o => $"{o.Part.Name}: {MemberName(o.Member)}{(o.Deadline.HasValue ? " - " + o.Deadline.Value.ToString("yyyy-MM-dd") : string.Empty)}"));
        }

        public async Task<IPageBuilder[]> GenerateAssignmentListPages(List<Assignment> assignments) {
            if (assignments.Count <= 0) return null;
            return await GenerateListPages(assignments.Select(async o =>
                    ($"{o.Part.Name}: {await MemberName(o.Member)}", $"{(o.Deadline.HasValue ? o.Deadline.Value.ToString("yyyy-MM-dd") : Strings.NoDeadline)}")),
                Strings.Assignments);
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

        public IPageBuilder[] GenerateListPages(IEnumerable<(string, string)> list, string itemName) {
            const int maxItemsPerPage = 10;

            var array = list.ToArray();
            var pages = new IPageBuilder[(array.Length - 1) / maxItemsPerPage + 1];
            var e = 0;
            var c = 0;
            var pageBuilder = new PageBuilder();
            foreach ((string name, string value) in array) {
                pageBuilder.AddField(name, value);
                c++;

                if (c != maxItemsPerPage) continue;
                pageBuilder.Title = $"{itemName} {e * maxItemsPerPage + 1}-{Math.Min((e + 1) * maxItemsPerPage, array.Length)}";
                pages[e++] = pageBuilder;
                pageBuilder = new PageBuilder();
                c = 0;
            }

            if (c > 0) {
                pageBuilder.Title = $"{itemName} {e * maxItemsPerPage + 1}-{Math.Min((e + 1) * maxItemsPerPage, array.Length)}";
                pages[e] = pageBuilder;
            }

            return pages;
        }

        public async Task<IPageBuilder[]> GenerateListPages(IEnumerable<Task<(string, string)>> list, string itemName) {
            return GenerateListPages(await Task.WhenAll(list), itemName);
        }

        public static string UserOrGlobalName(IUser user) {
            return user is not null ? string.IsNullOrEmpty(user.GlobalName) ? user.Username : user.GlobalName : Strings.UnknownUser;
        }

        public static string UserName(IUser user) {
            return user is not null ? user.Username : Strings.UnknownUser;
        }

        public async Task<string> MemberName(Member member) {
            var user = await _client.GetUserAsync((ulong)member.UniqueMemberId);
            string name = UserName(user);
            if (member.Alias != null) {
                name += $" \"{member.Alias}\"";
            }
            return name;
        }

        public async Task<string> MemberAliasOrName(Member member) {
            if (member.Alias != null) {
                return member.Alias;
            }
            var user = await _client.GetUserAsync((ulong)member.UniqueMemberId);
            string name = UserOrGlobalName(user);
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