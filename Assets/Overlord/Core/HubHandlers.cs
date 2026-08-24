using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xst.Rpc;
using Xst.Rpc.Models;
using Overlord.Registry;

namespace Overlord
{
    public static class Hub
    {
        public const string Version = "0.1.0";
    }

    public class HubHandlers
    {
        public const int DefaultPerPage = 25;
        public const int MaxPerPage = 100;
        public const int DefaultRichListCount = 25;
        public const int MaxRichListCount = 1000;
        public const int MaxPeersReturned = 64;
        public const int MaxMempoolReturned = 200;
        public const int DefaultSeriesPoints = 60;
        public const int MaxSeriesPoints = 120;
        public const int MaxSeriesSpacing = 1000000;
        public const int SecondsPerBlock = 5;
        public const int HoldersCacheHours = 6;

        private static readonly DateTime Epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public const int DefaultHoldersBucketDays = 30;
        public const int MaxHoldersBucketDays = 365;
        public const int DefaultVolumePeriod = 3600;
        public const int MaxVolumePeriod = 86400;
        public const int MinVolumeWindow = 5;
        public const int VolumeBuckets = 60;
        public const int DefaultRegistryBlocks = 500;
        public const int MaxRegistryBlocks = 5000;
        public const int MaxAddressLength = 128;

        public const string Chain = "XST";

        private readonly XstClient client;
        private readonly PeerSet peers;
        private readonly List<string> registered = new List<string>();

        public HubHandlers(XstClient client, PeerSet peers)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            this.client = client;
            this.peers = peers ?? new PeerSet();
        }

        public void RegisterAll(HubDispatcher dispatcher)
        {
            if (dispatcher == null)
            {
                throw new ArgumentNullException("dispatcher");
            }

            registered.Clear();
            dispatcher.Register(HubQueries.Ping, Ping);
            dispatcher.Register(HubQueries.Peers, Peers);
            dispatcher.Register(HubQueries.GetInfo, GetInfo);
            dispatcher.Register(HubQueries.GetBlock, GetBlock);
            dispatcher.Register(HubQueries.GetTransaction, GetTransaction);
            dispatcher.Register(HubQueries.GetAddress, GetAddress);
            dispatcher.Register(HubQueries.GetRichList, GetRichList);
            dispatcher.Register(HubQueries.ChainStats, ChainStats);
            dispatcher.Register(HubQueries.Mempool, Mempool);
            dispatcher.Register(HubQueries.Series, Series);
            dispatcher.Register(HubQueries.Registry, Registry);
            dispatcher.Register(HubQueries.Volume, Volume);
            dispatcher.Register(HubQueries.Holders, Holders);

            foreach (string query in dispatcher.Registered)
            {
                registered.Add(query);
            }
        }

        public async Task<string> SelfCheckAsync()
        {
            string audit = HubQueries.AuditAllowlist();
            if (audit != null)
            {
                return audit;
            }

            try
            {
                await client.GetBlockCountAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return "the daemon is not answering: " + ex.Message;
            }

            try
            {
                await client.GetRichListSizeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return "the explore api looks disabled, getaddress and getrichlist will fail: " + ex.Message;
            }

            return null;
        }

        private async Task<object> Ping(HubRequest request)
        {
            int blocks = await client.GetBlockCountAsync().ConfigureAwait(false);

            var answers = new List<string>();
            foreach (string query in registered)
            {
                answers.Add(query);
            }
            answers.Sort(StringComparer.Ordinal);

            return new
            {
                version = Hub.Version,
                chain = Chain,
                blocks = blocks,
                queries = answers
            };
        }

        private Task<object> Peers(HubRequest request)
        {
            object result = new { peers = peers.Newest(MaxPeersReturned) };
            return Task.FromResult(result);
        }

        private async Task<object> GetInfo(HubRequest request)
        {
            XstInfo info = await client.GetInfoAsync().ConfigureAwait(false);

            return new
            {
                hub = Hub.Version,
                version = info.Version,
                buildversion = info.BuildVersion,
                protocolversion = info.ProtocolVersion,
                blocks = info.Blocks,
                blockhash = info.BlockHash,
                moneysupply = info.MoneySupply,
                connections = info.Connections,
                difficulty = info.Difficulty,
                testnet = info.Testnet,
                errors = info.Errors
            };
        }

        private async Task<object> GetBlock(HubRequest request)
        {
            string hash = OptionalString(request, "hash");
            long? height = OptionalLong(request, "height");

            if (hash != null && height.HasValue)
            {
                throw new ArgumentException("give hash or height, not both");
            }

            if (hash != null)
            {
                RequireHex(hash, "hash");
                return await client.GetBlockAsync(hash).ConfigureAwait(false);
            }

            if (height.HasValue)
            {
                if (height.Value < 0)
                {
                    throw new ArgumentException("height cannot be negative");
                }

                return await client.GetBlockByNumberAsync(height.Value).ConfigureAwait(false);
            }

            throw new ArgumentException("hash or height is required");
        }

        private async Task<object> GetTransaction(HubRequest request)
        {
            string txid = RequiredString(request, "txid");
            RequireHex(txid, "txid");
            return await client.GetRawTransactionAsync(txid, true).ConfigureAwait(false);
        }

        private async Task<object> GetAddress(HubRequest request)
        {
            string address = RequiredString(request, "address");
            RequireAddress(address);

            int page = OptionalInt(request, "page", 1, 1, int.MaxValue);
            int perPage = OptionalInt(request, "perpage", DefaultPerPage, 1, MaxPerPage);

            XstAddressInfo info = await client.GetAddressInfoAsync(address).ConfigureAwait(false);

            XstPage<XstAddressTx> history;
            if (info == null || info.Transactions <= 0)
            {
                history = new XstPage<XstAddressTx>
                {
                    Total = 0,
                    Page = page,
                    PerPage = perPage,
                    LastPage = 1,
                    Data = new List<XstAddressTx>()
                };
            }
            else
            {
                history = await client.GetAddressTxsPageAsync(address, page, perPage, false)
                    .ConfigureAwait(false);
            }

            return new
            {
                address = info == null ? address : info.Address,
                balance = info == null ? 0m : info.Balance,
                rank = info == null ? 0L : info.Rank,
                transactions = info == null ? 0L : info.Transactions,
                inputs = info == null ? 0L : info.Inputs,
                outputs = info == null ? 0L : info.Outputs,
                received = info == null ? 0m : info.Received,
                sent = info == null ? 0m : info.Sent,
                unspent = info == null ? 0L : info.Unspent,
                inouts = info == null ? 0L : info.InOuts,
                blocks = info == null ? 0L : info.Blocks,
                history = history
            };
        }

        private async Task<object> GetRichList(HubRequest request)
        {
            int start = OptionalInt(request, "start", 1, 1, int.MaxValue);
            int count = OptionalInt(request, "count", DefaultRichListCount, 1, MaxRichListCount);

            long addresses = 0;
            try
            {
                addresses = await client.GetRichListSizeAsync().ConfigureAwait(false);
            }
            catch (XstRpcException)
            {
            }

            int reach = addresses > 0 && addresses < int.MaxValue ? (int)addresses : count;
            if (reach < 1)
            {
                reach = 1;
            }

            if (start > reach)
            {
                start = reach;
            }

            int wanted = count;
            if (start + wanted - 1 > reach)
            {
                wanted = reach - start + 1;
            }
            if (wanted < 1)
            {
                wanted = 1;
            }

            JToken rows = await client.GetRichListAsync(1, reach).ConfigureAwait(false);

            decimal minted = 0m;
            try
            {
                XstInfo info = await client.GetInfoAsync().ConfigureAwait(false);
                minted = info.MoneySupply;
            }
            catch (XstRpcException)
            {
            }

            var every = new List<KeyValuePair<string, decimal>>();
            JObject map = rows as JObject;
            if (map != null)
            {
                foreach (KeyValuePair<string, JToken> entry in map)
                {
                    every.Add(new KeyValuePair<string, decimal>(entry.Key, entry.Value.Value<decimal>()));
                }
            }
            else if (rows is JArray)
            {
                foreach (JToken row in (JArray)rows)
                {
                    JObject item = row as JObject;
                    if (item == null)
                    {
                        continue;
                    }
                    every.Add(new KeyValuePair<string, decimal>(
                        item.Value<string>("address"), item["balance"].Value<decimal>()));
                }
            }

            decimal total = 0m;
            for (int i = 0; i < every.Count; i++)
            {
                total += every[i].Value;
            }

            var list = new List<object>();
            for (int i = start - 1; i < every.Count && list.Count < wanted; i++)
            {
                list.Add(new { address = every[i].Key, balance = every[i].Value });
            }

            return new
            {
                start = start,
                count = list.Count,
                addresses = every.Count,
                total = total,
                moneysupply = minted,
                rows = list
            };
        }

        private async Task<object> ChainStats(HubRequest request)
        {
            XstInfo info = await client.GetInfoAsync().ConfigureAwait(false);

            long addresses = -1;
            try
            {
                addresses = await client.GetRichListSizeAsync().ConfigureAwait(false);
            }
            catch (XstRpcException)
            {
            }

            int waiting = 0;
            try
            {
                IReadOnlyList<string> pending =
                    await client.GetRawMempoolAsync().ConfigureAwait(false);
                waiting = pending == null ? 0 : pending.Count;
            }
            catch (XstRpcException)
            {
            }

            JToken stakers = null;
            try
            {
                stakers = await client.GetStakerSummaryAsync().ConfigureAwait(false);
            }
            catch (XstRpcException)
            {
            }

            return new
            {
                hub = Hub.Version,
                height = info.Blocks,
                blockhash = info.BlockHash,
                moneysupply = info.MoneySupply,
                connections = info.Connections,
                testnet = info.Testnet,
                addresses = addresses,
                mempool = waiting,
                stakers = stakers
            };
        }

        private async Task<object> Mempool(HubRequest request)
        {
            IReadOnlyList<string> pending = await client.GetRawMempoolAsync().ConfigureAwait(false);

            var txids = new List<string>();
            if (pending != null)
            {
                for (int i = 0; i < pending.Count && i < MaxMempoolReturned; i++)
                {
                    txids.Add(pending[i]);
                }
            }

            return new
            {
                count = pending == null ? 0 : pending.Count,
                txids = txids
            };
        }

        private readonly object holdersGate = new object();
        private List<long> holdersFirstSeen;
        private DateTime holdersStamp;
        private bool holdersBusy;
        private int holdersDone;
        private int holdersTotal;
        private string holdersTrouble;

        private Task<object> Holders(HubRequest request)
        {
            int bucket = OptionalInt(request, "bucket", DefaultHoldersBucketDays, 1, MaxHoldersBucketDays);

            lock (holdersGate)
            {
                bool fresh = holdersFirstSeen != null &&
                    (DateTime.UtcNow - holdersStamp).TotalHours < HoldersCacheHours;

                if (fresh)
                {
                    return Task.FromResult<object>(new
                    {
                        ready = true,
                        addresses = holdersFirstSeen.Count,
                        bucket_days = bucket,
                        computed = (long)(holdersStamp - Epoch).TotalSeconds,
                        points = Buckets(holdersFirstSeen, bucket)
                    });
                }

                if (!holdersBusy)
                {
                    holdersBusy = true;
                    holdersDone = 0;
                    holdersTotal = 0;
                    holdersTrouble = null;
                    Task ignored = BuildHoldersAsync();
                }

                return Task.FromResult<object>(new
                {
                    ready = false,
                    done = holdersDone,
                    total = holdersTotal,
                    error = holdersTrouble
                });
            }
        }

        private async Task BuildHoldersAsync()
        {
            var found = new List<long>();
            string trouble = null;

            try
            {
                long size = await client.GetRichListSizeAsync().ConfigureAwait(false);
                int reach = size > 0 && size < int.MaxValue ? (int)size : 0;

                lock (holdersGate)
                {
                    holdersTotal = reach;
                }

                if (reach > 0)
                {
                    JToken rows = await client.GetRichListAsync(1, reach).ConfigureAwait(false);
                    JObject map = rows as JObject;

                    if (map != null)
                    {
                        foreach (KeyValuePair<string, JToken> entry in map)
                        {
                            try
                            {
                                IReadOnlyList<XstAddressInOut> inouts = await client
                                    .GetAddressInOutsAsync(entry.Key, 1, 1).ConfigureAwait(false);

                                if (inouts != null && inouts.Count > 0 && inouts[0].BlockTime > 0)
                                {
                                    found.Add(inouts[0].BlockTime);
                                }
                            }
                            catch (XstRpcException)
                            {
                            }

                            lock (holdersGate)
                            {
                                holdersDone++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                trouble = ex.Message;
            }

            found.Sort();

            lock (holdersGate)
            {
                holdersBusy = false;
                holdersTrouble = trouble;

                if (found.Count > 0)
                {
                    holdersFirstSeen = found;
                    holdersStamp = DateTime.UtcNow;
                }
            }
        }

        private static List<object> Buckets(List<long> sorted, int bucketDays)
        {
            var points = new List<object>();
            if (sorted.Count == 0)
            {
                return points;
            }

            long span = (long)bucketDays * 86400L;
            long last = sorted[sorted.Count - 1];
            long edge = sorted[0] + span;
            int index = 0;
            int running = 0;

            while (true)
            {
                while (index < sorted.Count && sorted[index] < edge)
                {
                    index++;
                    running++;
                }

                if (edge > last)
                {
                    points.Add(new { time = last, total = running });
                    break;
                }

                points.Add(new { time = edge, total = running });

                edge += span;
            }

            return points;
        }

        private async Task<object> Volume(HubRequest request)
        {
            int period = OptionalInt(request, "period", DefaultVolumePeriod,
                MinVolumeWindow, MaxVolumePeriod);

            int suggested = period / VolumeBuckets;
            if (suggested < MinVolumeWindow)
            {
                suggested = MinVolumeWindow;
            }

            int window = OptionalInt(request, "window", suggested, MinVolumeWindow, period);
            int spacing = OptionalInt(request, "spacing", window, MinVolumeWindow, period);

            JToken volume = await client.GetTxVolumeAsync(period, window, spacing)
                .ConfigureAwait(false);

            return new
            {
                period = period,
                window = window,
                spacing = spacing,
                max_period = MaxVolumePeriod,
                starts = Longs(volume, "window_start"),
                blocks = Longs(volume, "number_blocks"),
                tx = Longs(volume, "tx_volume")
            };
        }

        private static long[] Longs(JToken source, string field)
        {
            JArray array = source == null ? null : source[field] as JArray;
            if (array == null)
            {
                return new long[0];
            }

            var values = new long[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                JToken item = array[i];
                values[i] = item == null || item.Type == JTokenType.Null
                    ? 0
                    : item.Value<long>();
            }
            return values;
        }

        private async Task<object> Series(HubRequest request)
        {
            int points = OptionalInt(request, "points", DefaultSeriesPoints, 1, MaxSeriesPoints);
            int spacing = OptionalInt(request, "spacing", 1, 1, MaxSeriesSpacing);

            int tip = await client.GetBlockCountAsync().ConfigureAwait(false);

            long oldest = tip - (long)(points - 1) * spacing;
            if (oldest < 1)
            {
                points = (int)((tip - 1) / spacing) + 1;
                if (points < 1)
                {
                    points = 1;
                }
                oldest = tip - (long)(points - 1) * spacing;
            }

            var rows = new List<object>();

            for (int i = 0; i < points; i++)
            {
                long height = oldest + (long)i * spacing;
                if (height < 1 || height > tip)
                {
                    continue;
                }

                XstBlock block = await client.GetBlockByNumberAsync(height).ConfigureAwait(false);
                if (block == null)
                {
                    continue;
                }

                rows.Add(new
                {
                    height = block.Height,
                    time = block.Time,
                    supply = block.MoneySupply.HasValue ? block.MoneySupply.Value : 0m,
                    reward = block.BlockReward.HasValue ? block.BlockReward.Value : 0m,
                    size = block.Size,
                    tx = block.Transactions == null ? 0 : block.Transactions.Count,
                    staker = block.StakerAlias
                });
            }

            return new
            {
                tip = tip,
                spacing = spacing,
                seconds_per_block = SecondsPerBlock,
                points = rows
            };
        }

        private async Task<object> Registry(HubRequest request)
        {
            int window = OptionalInt(request, "blocks", DefaultRegistryBlocks, 1, MaxRegistryBlocks);
            int kind = OptionalInt(request, "flags", 0, 0, 255);

            int tip = await client.GetBlockCountAsync().ConfigureAwait(false);
            long oldest = tip - window + 1;
            if (oldest < 1)
            {
                oldest = 1;
            }

            var seen = new Dictionary<string, object>(StringComparer.Ordinal);
            var rows = new List<object>();
            int scanned = 0;
            long newest = 0;

            for (long height = tip; height >= oldest; height--)
            {
                XstBlock block = await client.GetBlockByNumberAsync(height, true).ConfigureAwait(false);
                scanned++;

                if (block == null || block.Transactions == null || block.Transactions.Count == 0)
                {
                    continue;
                }

                List<OnionListing> listings = OnionListing.ScanBlock(
                    block.Transactions.ToString(Newtonsoft.Json.Formatting.None));

                for (int i = 0; i < listings.Count; i++)
                {
                    OnionListing listing = listings[i];

                    if (kind != 0 && (listing.Flags & kind) == 0)
                    {
                        continue;
                    }

                    if (seen.ContainsKey(listing.Entry))
                    {
                        continue;
                    }

                    seen[listing.Entry] = null;
                    rows.Add(new
                    {
                        onion = listing.Onion,
                        port = listing.Port,
                        flags = listing.Flags,
                        height = block.Height
                    });

                    if (block.Height > newest)
                    {
                        newest = block.Height;
                    }
                }
            }

            return new
            {
                tip = tip,
                scanned = scanned,
                from = oldest,
                newest = newest,
                found = rows.Count,
                listings = rows
            };
        }

        private static void RequireHex(string value, string name)
        {
            if (value.Length != 64)
            {
                throw new ArgumentException(name + " must be 64 hex characters");
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    throw new ArgumentException(name + " must be 64 hex characters");
                }
            }
        }

        private static void RequireAddress(string value)
        {
            if (value.Length < 26 || value.Length > MaxAddressLength)
            {
                throw new ArgumentException("address length is out of range");
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (!ok)
                {
                    throw new ArgumentException("address contains an unexpected character");
                }
            }
        }

        private static string RequiredString(HubRequest request, string name)
        {
            string value = OptionalString(request, name);
            if (value == null)
            {
                throw new ArgumentException(name + " is required");
            }
            return value;
        }

        private static string OptionalString(HubRequest request, string name)
        {
            JToken token = Token(request, name);
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type != JTokenType.String)
            {
                throw new ArgumentException(name + " must be a string");
            }

            string value = token.Value<string>();
            if (value == null)
            {
                return null;
            }

            value = value.Trim();
            return value.Length == 0 ? null : value;
        }

        private static long? OptionalLong(HubRequest request, string name)
        {
            JToken token = Token(request, name);
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type != JTokenType.Integer)
            {
                throw new ArgumentException(name + " must be a whole number");
            }

            return token.Value<long>();
        }

        private static int OptionalInt(HubRequest request, string name, int fallback, int min, int max)
        {
            long? raw = OptionalLong(request, name);
            if (!raw.HasValue)
            {
                return fallback;
            }

            if (raw.Value < min)
            {
                throw new ArgumentException(name + " must be " + min + " or more");
            }

            return raw.Value > max ? max : (int)raw.Value;
        }

        private static JToken Token(HubRequest request, string name)
        {
            if (request == null || request.Parameters == null)
            {
                return null;
            }
            return request.Parameters[name];
        }
    }
}
