using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Overlord.Tor
{
    public class ReleaseNote
    {
        public string Repository;
        public string Tag;
        public string Name;
        public DateTime Published;
        public string Url;
        public string Summary;
    }

    public class ActivityNote
    {
        public string Repository;
        public string Kind;
        public string Summary;
        public System.DateTime When;
        public string Url;
    }

    public class MarketQuote
    {
        public decimal Price;
        public decimal MarketCap;
        public decimal Volume24h;
        public decimal Change24h;
        public decimal ReportedSupply;
        public string Updated;
        public string Error;
    }

    public class PricePoint
    {
        public System.DateTime When;
        public decimal Price;
    }

    public class PriceHistory
    {
        public List<PricePoint> Points = new List<PricePoint>();
        public string Error;
    }

    public static class TorFeeds
    {
        public const string GitHubApi = "https://api.github.com";
        public const string WatchedOrg = "Stealth-R-D-LLC";
        public const string PaprikaTicker = "https://api.coinpaprika.com/v1/tickers/xst-stealth";
        public const string PaprikaHistory = PaprikaTicker + "/historical";
        public const int MaxHistoryDays = 149;
        public const string DailyInterval = "24h";
        public const string HourlyInterval = "1h";
        public const int MaxHourlyHours = 20;
        public const int DefaultHistoryDays = 30;
        public const int TimeoutMs = 45000;

        public static readonly string[][ ] Watched =
        {
            new[] { "Stealth-R-D-LLC", "Stealth", "the daemon" },
            new[] { "Stealth-R-D-LLC", "stealthsend-desktop", "the desktop wallet" },
            new[] { "mahusar", "xst-dotnet", "the RPC client" },
            new[] { "mahusar", "StealthDragons", "the game" }
        };

        public static async Task<List<ActivityNote>> ActivityAsync(string org, int count)
        {
            var notes = new List<ActivityNote>();

            string url = GitHubApi + "/orgs/" + org + "/events?per_page=" +
                count.ToString(CultureInfo.InvariantCulture);

            TorResponse response = await TorHttp
                .GetAsync(url, "application/vnd.github+json", TimeoutMs).ConfigureAwait(false);

            if (!response.Ok || string.IsNullOrEmpty(response.Body))
            {
                throw new InvalidOperationException(response.Error != null
                    ? response.Error
                    : "GitHub answered " + response.Status);
            }

            JArray rows;
            try
            {
                rows = JArray.Parse(response.Body);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("GitHub sent something that is not an event list");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                JObject row = rows[i] as JObject;
                if (row == null)
                {
                    continue;
                }

                string kind = row.Value<string>("type");
                JObject repo = row["repo"] as JObject;
                JObject payload = row["payload"] as JObject;
                string name = repo == null ? org : repo.Value<string>("name");

                DateTime when;
                DateTime.TryParse(row.Value<string>("created_at"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when);

                notes.Add(new ActivityNote
                {
                    Repository = name,
                    Kind = Shorten(kind),
                    Summary = Describe(kind, payload),
                    When = when,
                    Url = "https://github.com/" + name
                });
            }

            return notes;
        }

        private static string Shorten(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return "activity";
            }

            return kind.EndsWith("Event", StringComparison.Ordinal)
                ? kind.Substring(0, kind.Length - 5).ToLowerInvariant()
                : kind.ToLowerInvariant();
        }

        private static string Describe(string kind, JObject payload)
        {
            switch (kind)
            {
                case "PushEvent":
                    long size = payload == null ? 0 : Whole(payload, "size");
                    string branch = payload == null ? null : payload.Value<string>("ref");
                    if (!string.IsNullOrEmpty(branch))
                    {
                        int slash = branch.LastIndexOf('/');
                        if (slash >= 0 && slash + 1 < branch.Length)
                        {
                            branch = branch.Substring(slash + 1);
                        }
                    }
                    if (size <= 0)
                    {
                        return string.IsNullOrEmpty(branch) ? "pushed" : "pushed to " + branch;
                    }

                    return (size == 1 ? "1 commit" : size + " commits") +
                        (string.IsNullOrEmpty(branch) ? "" : " to " + branch);

                case "PublicEvent":
                    return "the repository was made public";

                case "CreateEvent":
                    string what = payload == null ? null : payload.Value<string>("ref_type");
                    string named = payload == null ? null : payload.Value<string>("ref");
                    return string.IsNullOrEmpty(named)
                        ? "created a " + (what ?? "repository")
                        : "created the " + what + " " + named;

                case "DeleteEvent":
                    return "deleted the " + (payload == null ? "ref" : payload.Value<string>("ref_type")) +
                        " " + (payload == null ? "" : payload.Value<string>("ref"));

                case "ReleaseEvent":
                    JObject release = payload == null ? null : payload["release"] as JObject;
                    return "released " + (release == null ? "" : release.Value<string>("tag_name"));

                case "IssuesEvent":
                    JObject issue = payload == null ? null : payload["issue"] as JObject;
                    return (payload == null ? "changed" : payload.Value<string>("action")) + " issue " +
                        (issue == null ? "" : "#" + Whole(issue, "number"));

                case "IssueCommentEvent":
                    return "commented on an issue";

                case "PullRequestEvent":
                    JObject pull = payload == null ? null : payload["pull_request"] as JObject;
                    return (payload == null ? "changed" : payload.Value<string>("action")) +
                        " pull request " + (pull == null ? "" : "#" + Whole(pull, "number"));

                case "ForkEvent":
                    return "forked";

                case "WatchEvent":
                    return "starred";

                default:
                    return Shorten(kind);
            }
        }

        private static long Whole(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return 0;
            }

            try
            {
                return source.Value<long>(field);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static async Task<List<ReleaseNote>> ReleasesAsync(string owner, string repo, int count)
        {
            var notes = new List<ReleaseNote>();

            string url = GitHubApi + "/repos/" + owner + "/" + repo +
                "/releases?per_page=" + count.ToString(CultureInfo.InvariantCulture);

            TorResponse response = await TorHttp
                .GetAsync(url, "application/vnd.github+json", TimeoutMs).ConfigureAwait(false);

            if (!response.Ok || string.IsNullOrEmpty(response.Body))
            {
                throw new InvalidOperationException(response.Error != null
                    ? response.Error
                    : "GitHub answered " + response.Status);
            }

            JArray rows;
            try
            {
                rows = JArray.Parse(response.Body);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("GitHub sent something that is not a release list");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                JObject row = rows[i] as JObject;
                if (row == null)
                {
                    continue;
                }

                var note = new ReleaseNote
                {
                    Repository = owner + "/" + repo,
                    Tag = row.Value<string>("tag_name"),
                    Name = row.Value<string>("name"),
                    Url = row.Value<string>("html_url"),
                    Summary = Shorten(row.Value<string>("body"), 220)
                };

                DateTime published;
                if (DateTime.TryParse(row.Value<string>("published_at"),
                        CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out published))
                {
                    note.Published = published;
                }

                notes.Add(note);
            }

            return notes;
        }

        public static async Task<PriceHistory> HourlyHistoryAsync(int hours)
        {
            var history = new PriceHistory();

            if (hours < 2)
            {
                hours = 2;
            }

            if (hours > MaxHourlyHours)
            {
                hours = MaxHourlyHours;
            }

            string start = DateTime.UtcNow.AddHours(-hours)
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            string url = PaprikaHistory + "?start=" + start + "&interval=" + HourlyInterval;

            TorResponse response = await TorHttp
                .GetAsync(url, "application/json", TimeoutMs).ConfigureAwait(false);

            if (!response.Ok || string.IsNullOrEmpty(response.Body))
            {
                history.Error = response.Error != null
                    ? response.Error
                    : "the price service answered " + response.Status;
                return history;
            }

            Fill(history, response.Body);
            return history;
        }

        public static async Task<PriceHistory> PriceHistoryAsync(int days)
        {
            var history = new PriceHistory();

            if (days < 2)
            {
                days = 2;
            }

            if (days > MaxHistoryDays)
            {
                days = MaxHistoryDays;
            }

            string start = DateTime.UtcNow.AddDays(-days)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string url = PaprikaHistory + "?start=" + start + "&interval=" + DailyInterval;

            TorResponse response = await TorHttp
                .GetAsync(url, "application/json", TimeoutMs).ConfigureAwait(false);

            if (!response.Ok || string.IsNullOrEmpty(response.Body))
            {
                history.Error = response.Error != null
                    ? response.Error
                    : "the price service answered " + response.Status;
                return history;
            }

            Fill(history, response.Body);
            return history;
        }

        private static void Fill(PriceHistory history, string body)
        {
            try
            {
                JArray rows = JArray.Parse(body);

                foreach (JToken row in rows)
                {
                    JObject point = row as JObject;
                    if (point == null)
                    {
                        continue;
                    }

                    decimal price = Read(point, "price");
                    if (price <= 0m)
                    {
                        continue;
                    }

                    DateTime when;
                    DateTime.TryParse(point.Value<string>("timestamp"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when);

                    history.Points.Add(new PricePoint { When = when, Price = price });
                }
            }
            catch (Exception ex)
            {
                history.Error = "the price history could not be read: " + ex.Message;
                return;
            }

            if (history.Points.Count == 0)
            {
                JObject failure = null;
                try
                {
                    failure = JObject.Parse(body);
                }
                catch (Exception)
                {
                }

                history.Error = failure != null && failure["error"] != null
                    ? failure.Value<string>("error")
                    : "the price service returned no history";
            }
        }

        public static async Task<MarketQuote> QuoteAsync()
        {
            var quote = new MarketQuote();

            TorResponse response = await TorHttp
                .GetAsync(PaprikaTicker, "application/json", TimeoutMs).ConfigureAwait(false);

            if (!response.Ok || string.IsNullOrEmpty(response.Body))
            {
                quote.Error = response.Error != null
                    ? response.Error
                    : "the price service answered " + response.Status;
                return quote;
            }

            try
            {
                JObject root = JObject.Parse(response.Body);
                JObject usd = root["quotes"] == null ? null : root["quotes"]["USD"] as JObject;

                if (usd == null)
                {
                    quote.Error = "the price service sent no USD quote";
                    return quote;
                }

                quote.Price = Read(usd, "price");
                quote.MarketCap = Read(usd, "market_cap");
                quote.Volume24h = Read(usd, "volume_24h");
                quote.Change24h = Read(usd, "percent_change_24h");
                quote.ReportedSupply = Read(root, "total_supply");
                quote.Updated = root.Value<string>("last_updated");
            }
            catch (Exception ex)
            {
                quote.Error = "the price reply could not be read: " + ex.Message;
            }

            return quote;
        }

        private static decimal Read(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return 0m;
            }

            try
            {
                return source[field].Value<decimal>();
            }
            catch (Exception)
            {
                return 0m;
            }
        }

        private static string Shorten(string text, int limit)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (flat.Contains("  "))
            {
                flat = flat.Replace("  ", " ");
            }

            return flat.Length <= limit ? flat : flat.Substring(0, limit) + " ...";
        }
    }
}
