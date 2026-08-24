using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Overlord.Explorer
{
    public class SearchOutcome
    {
        public string Query;
        public string Subject;
        public Verdict Verdict;
    }

    public class ExplorerClient
    {
        private readonly List<IHubSource> sources = new List<IHubSource>();
        private int nextId;

        public ExplorerClient(IEnumerable<IHubSource> hubs)
        {
            if (hubs != null)
            {
                foreach (IHubSource hub in hubs)
                {
                    if (hub != null)
                    {
                        sources.Add(hub);
                    }
                }
            }

            if (sources.Count == 0)
            {
                throw new ArgumentException("an explorer needs at least one source");
            }
        }

        public int SourceCount
        {
            get { return sources.Count; }
        }

        public async Task<Verdict> AskAsync(string query, JObject parameters)
        {
            nextId++;
            var request = new JObject
            {
                ["id"] = nextId.ToString(CultureInfo.InvariantCulture),
                ["q"] = query
            };

            if (parameters != null)
            {
                request["p"] = parameters;
            }

            string line = request.ToString(Formatting.None);

            var pending = new List<Task<HubAnswer>>();
            foreach (IHubSource source in sources)
            {
                pending.Add(AskOneAsync(source, line));
            }

            HubAnswer[] answers = await Task.WhenAll(pending);
            return Corroboration.Judge(new List<HubAnswer>(answers));
        }

        public async Task<SearchOutcome> SearchAsync(string text)
        {
            string subject = text == null ? string.Empty : text.Trim();
            var outcome = new SearchOutcome { Subject = subject };

            if (subject.Length == 0)
            {
                outcome.Query = HubQueries.GetInfo;
                outcome.Verdict = await AskAsync(HubQueries.GetInfo, null);
                return outcome;
            }

            if (IsDigits(subject))
            {
                outcome.Query = HubQueries.GetBlock;
                outcome.Verdict = await AskAsync(HubQueries.GetBlock,
                    new JObject { ["height"] = long.Parse(subject, CultureInfo.InvariantCulture) });
                return outcome;
            }

            if (IsHex64(subject))
            {
                outcome.Query = HubQueries.GetBlock;
                outcome.Verdict = await AskAsync(HubQueries.GetBlock,
                    new JObject { ["hash"] = subject });

                if (!outcome.Verdict.HasResult)
                {
                    Verdict asTransaction = await AskAsync(HubQueries.GetTransaction,
                        new JObject { ["txid"] = subject });

                    if (asTransaction.HasResult)
                    {
                        outcome.Query = HubQueries.GetTransaction;
                        outcome.Verdict = asTransaction;
                    }
                }

                return outcome;
            }

            outcome.Query = HubQueries.GetAddress;
            outcome.Verdict = await AskAsync(HubQueries.GetAddress,
                new JObject { ["address"] = subject });
            return outcome;
        }

        public Task<Verdict> RichListAsync(int start, int count)
        {
            return AskAsync(HubQueries.GetRichList,
                new JObject { ["start"] = start, ["count"] = count });
        }

        public Task<Verdict> PingAsync()
        {
            return AskAsync(HubQueries.Ping, null);
        }

        public Task<Verdict> PeersAsync()
        {
            return AskAsync(HubQueries.Peers, null);
        }

        private static async Task<HubAnswer> AskOneAsync(IHubSource source, string line)
        {
            try
            {
                string answer = await source.AskAsync(line);
                return HubAnswer.Parse(source.Name, answer);
            }
            catch (Exception ex)
            {
                return new HubAnswer { Source = source.Name, Ok = false, Error = ex.Message };
            }
        }

        private static bool IsDigits(string value)
        {
            if (value.Length == 0 || value.Length > 18)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsHex64(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
