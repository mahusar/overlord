using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public class StatsScreen
    {
        public const string Prefab = "StatsCanvas";

        public const int LiveBlocks = 60;

        public const int SecondsPerPoint = 300;

        private static readonly DateTime Epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly ExplorerClient client;
        private readonly Action onMenu;

        private readonly GameObject root;
        private readonly TextMeshProUGUI statusText;
        private readonly TextMeshProUGUI badgeText;
        private readonly List<TextMeshProUGUI> figures = new List<TextMeshProUGUI>();

        private readonly WavePanel supplyWave;
        private readonly WavePanel rewardWave;
        private readonly WavePanel txWave;
        private readonly WavePanel paceWave;
        private readonly WavePanel liveTxWave;
        private readonly WavePanel holdersWave;

        private readonly List<double> supplySeries = new List<double>();
        private readonly List<double> rewardSeries = new List<double>();
        private readonly List<double> txSeries = new List<double>();
        private readonly List<double> paceSeries = new List<double>();
        private readonly List<double> liveTxSeries = new List<double>();
        private readonly List<double> holdersSeries = new List<double>();

        private readonly TextMeshProUGUI queueText;
        private readonly List<Button> ranges = new List<Button>();

        private int spacing = 288;
        private string rangeName = "1 day";
        private bool busy;
        private bool loadingSeries;
        private bool loadingLive;
        private bool loadingHolders;
        private bool loadingVolume;
        private long liveHeight = -1;

        public StatsScreen(Transform parent, ExplorerClient client, Action onMenu)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            this.client = client;
            this.onMenu = onMenu;

            root = UIPrefab.Instantiate(Prefab, parent);

            badgeText = UIPrefab.Bind<TextMeshProUGUI>(root, "Header/Badge");
            statusText = UIPrefab.Bind<TextMeshProUGUI>(root, "Status");
            queueText = UIPrefab.Bind<TextMeshProUGUI>(root, "Body/Queue");

            Button menu = UIPrefab.Bind<Button>(root, "Header/MenuButton");
            if (onMenu != null)
            {
                menu.onClick.AddListener(delegate { onMenu(); });
            }

            BindFigures();
            BindRanges();

            supplyWave = new WavePanel(UIPrefab.BindObject(root, "Body/SupplyWave"));
            rewardWave = new WavePanel(UIPrefab.BindObject(root, "Body/RewardWave"));
            paceWave = new WavePanel(UIPrefab.BindObject(root, "Body/PaceWave"));
            txWave = new WavePanel(UIPrefab.BindObject(root, "Body/TxWave"));
            liveTxWave = new WavePanel(UIPrefab.BindObject(root, "Body/LiveTxWave"));
            holdersWave = new WavePanel(UIPrefab.BindObject(root, "Body/HoldersWave"));
        }

        public GameObject Root
        {
            get { return root; }
        }

        public void Show(bool visible)
        {
            root.SetActive(visible);
        }

        public async void Refresh()
        {
            LoadHolders();
            if (busy)
            {
                return;
            }

            busy = true;
            statusText.text = "asking for chain stats";
            statusText.color = ExplorerUI.Muted;

            Verdict verdict = await client.AskAsync(HubQueries.ChainStats, null);

            busy = false;

            if (verdict == null || !verdict.HasResult)
            {
                statusText.text = "chain stats failed: " +
                    (verdict == null ? "no answer" : verdict.Error);
                statusText.color = ExplorerUI.Bad;
                badgeText.text = verdict == null ? "" : verdict.Badge;
                badgeText.color = ExplorerUI.Bad;
                return;
            }

            badgeText.text = verdict.Badge;
            badgeText.color = verdict.Unanimous
                ? (verdict.Answered > 1 ? ExplorerUI.Good : ExplorerUI.Muted)
                : ExplorerUI.Warn;

            Apply(verdict.Result as JObject);

            statusText.text = "updated, " + verdict.Badge;
            statusText.color = verdict.Unanimous ? ExplorerUI.Good : ExplorerUI.Warn;
        }

        private void Apply(JObject result)
        {
            if (result == null)
            {
                return;
            }

            JObject stakers = result["stakers"] as JObject;

            long height = Long(result, "height");
            long addresses = Long(result, "addresses");
            long waiting = Long(result, "mempool");

            if (height > 0 && height != liveHeight)
            {
                liveHeight = height;
                LoadLiveTx();
            }

            Set(0, height < 0 ? "-" : height.ToString("N0", CultureInfo.InvariantCulture));
            Set(1, addresses < 0 ? "explore api off" : addresses.ToString("N0", CultureInfo.InvariantCulture));
            Set(2, Decimal(result, "moneysupply", 0) + " XST");
            Set(3, waiting.ToString(CultureInfo.InvariantCulture));
            long registered = stakers == null ? 0 :
                Long(stakers, "enabled_stakers") + Long(stakers, "disabled_stakers") +
                Long(stakers, "terminated_stakers");
            Set(4, stakers == null ? "-" : Long(stakers, "enabled_stakers").ToString(CultureInfo.InvariantCulture) +
                " of " + registered.ToString(CultureInfo.InvariantCulture));
            Set(5, stakers == null ? "-" : Decimal(stakers, "next_staker_price", 0) + " XST");


            if (stakers != null)
            {
                string latest = stakers.Value<string>("latest_staker_alias") ?? "-";
                string next = stakers.Value<string>("next_staker_alias") ?? "-";
                long produced = Long(stakers, "produced_queue");
                long remaining = Long(stakers, "remaining_queue");
                long missed = Long(stakers, "missed_recently");

                queueText.text = "queue: " + produced.ToString(CultureInfo.InvariantCulture) +
                    " produced, " + remaining.ToString(CultureInfo.InvariantCulture) +
                    " remaining, " + missed.ToString(CultureInfo.InvariantCulture) +
                    " missed recently.  newest block by " + latest + ", next up " + next + ".";
            }
        }

        private void BindRanges()
        {
            string[] captions = { "5 min", "1 hour", "1 day", "30 days", "1 year", "5 years" };
            int[] blocks = { 1, 12, 288, 8640, 105120, 525600 };

            for (int i = 0; i < captions.Length; i++)
            {
                string path = "Body/Ranges/Range" + i.ToString(CultureInfo.InvariantCulture);
                UIPrefab.Bind<TextMeshProUGUI>(root, path + "/Label").text = captions[i];

                Button button = UIPrefab.Bind<Button>(root, path);
                int chosen = blocks[i];
                string named = captions[i];
                button.onClick.AddListener(delegate { LoadSeries(chosen, named); });
                ranges.Add(button);
            }
        }

        public async void LoadSeries(int blocks, string named)
        {
            if (loadingSeries)
            {
                return;
            }

            loadingSeries = true;
            spacing = blocks;
            rangeName = named;

            statusText.text = "walking blocks for the last " + named + " ...";
            statusText.color = ExplorerUI.Muted;

            var parameters = new JObject();
            parameters["points"] = 60;
            parameters["spacing"] = blocks;

            Verdict verdict = await client.AskAsync(HubQueries.Series, parameters);
            loadingSeries = false;

            if (verdict == null || !verdict.HasResult)
            {
                statusText.text = "history failed: " +
                    (verdict == null ? "no answer" : verdict.Error);
                statusText.color = ExplorerUI.Bad;
                return;
            }

            JObject result = verdict.Result as JObject;
            JArray points = result == null ? null : result["points"] as JArray;

            supplySeries.Clear();
            rewardSeries.Clear();
            paceSeries.Clear();

            long previousTime = 0;
            long firstTime = 0;
            long lastTime = 0;
            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    JObject point = points[i] as JObject;
                    if (point == null)
                    {
                        continue;
                    }

                    supplySeries.Add((double)DecimalValue(point, "supply"));
                    rewardSeries.Add((double)DecimalValue(point, "reward"));

                    long stamp = Long(point, "time");
                    if (firstTime == 0) firstTime = stamp;
                    if (stamp > 0) lastTime = stamp;

                    if (i > 0 && stamp > previousTime && blocks > 0)
                    {
                        paceSeries.Add((stamp - previousTime) / (double)blocks);
                    }
                    previousTime = stamp;
                }
            }

            string span = Span(firstTime, lastTime);

            supplyWave.SetTitle("money supply" + span);
            rewardWave.SetTitle("block reward" + span);
            paceWave.SetTitle(blocks == 1
                ? "seconds between blocks" + span
                : "seconds per block, averaged over " + blocks + " blocks" + span);

            supplyWave.Draw(supplySeries, "XST", false, 0);
            rewardWave.Draw(rewardSeries, "XST", false, 6);
            paceWave.Draw(paceSeries, "s", false, 2);

            LoadVolume((long)blocks * SecondsPerPoint, named);

            statusText.text = "history over " + rangeName + " from " +
                (points == null ? 0 : points.Count) + " sampled blocks, " + verdict.Badge;
            statusText.color = verdict.Unanimous ? ExplorerUI.Good : ExplorerUI.Warn;
        }

        private async void LoadVolume(long period, string named)
        {
            if (loadingVolume)
            {
                return;
            }

            txSeries.Clear();

            if (period > HubHandlers.MaxVolumePeriod)
            {
                txWave.SetTitle("transactions over " + named +
                    " - not available, the daemon would have to walk " +
                    (period / HubHandlers.SecondsPerBlock).ToString("N0", CultureInfo.InvariantCulture) + " blocks");
                txWave.Draw(txSeries, "tx", true, 0);
                return;
            }

            loadingVolume = true;

            var parameters = new JObject();
            parameters["period"] = period;

            Verdict verdict = await client.AskAsync(HubQueries.Volume, parameters);
            loadingVolume = false;

            if (verdict == null || !verdict.HasResult)
            {
                txWave.SetTitle("transactions over " + named + " - " +
                    (verdict == null ? "no answer" : verdict.Error));
                txWave.Draw(txSeries, "tx", true, 0);
                return;
            }

            JObject result = verdict.Result as JObject;
            JArray counts = result == null ? null : result["tx"] as JArray;
            JArray starts = result == null ? null : result["starts"] as JArray;

            if (counts != null)
            {
                for (int i = 0; i < counts.Count; i++)
                {
                    txSeries.Add((double)counts[i]);
                }
            }

            long window = result == null ? 0 : result.Value<long>("window");
            long firstTime = starts != null && starts.Count > 0 ? (long)starts[0] : 0;
            long lastTime = starts != null && starts.Count > 0 ? (long)starts[starts.Count - 1] : 0;

            txWave.SetTitle("transactions per " + Bucket(window) + " over " + named +
                Span(firstTime, lastTime));
            txWave.Draw(txSeries, "tx", true, 0);
        }

        private static string Bucket(long seconds)
        {
            if (seconds < 60)
            {
                return seconds.ToString(CultureInfo.InvariantCulture) + " s";
            }

            if (seconds < 3600)
            {
                return (seconds / 60).ToString(CultureInfo.InvariantCulture) + " min";
            }

            if (seconds < 86400)
            {
                return (seconds / 3600).ToString(CultureInfo.InvariantCulture) + " h";
            }

            return (seconds / 86400).ToString(CultureInfo.InvariantCulture) + " d";
        }

        private async void LoadHolders()
        {
            if (loadingHolders)
            {
                return;
            }

            loadingHolders = true;

            var parameters = new JObject();
            parameters["bucket"] = 90;

            Verdict verdict = await client.AskAsync(HubQueries.Holders, parameters);
            loadingHolders = false;

            if (verdict == null || !verdict.HasResult)
            {
                holdersWave.SetTitle("addresses by first appearance - " +
                    (verdict == null ? "no answer" : verdict.Error));
                return;
            }

            JObject result = verdict.Result as JObject;
            if (result == null)
            {
                return;
            }

            if (!result.Value<bool>("ready"))
            {
                if (result["error"] != null && result["error"].Type != JTokenType.Null)
                {
                    holdersWave.SetTitle("addresses by first appearance - " +
                        result.Value<string>("error"));
                    return;
                }

                long done = Long(result, "done");
                long total = Long(result, "total");
                holdersWave.SetTitle("addresses by first appearance, the hub is still counting" +
                    (total > 0
                        ? " " + done.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                          total.ToString("N0", CultureInfo.InvariantCulture)
                        : " ..."));
                return;
            }

            JArray points = result["points"] as JArray;
            if (points == null || points.Count < 2)
            {
                return;
            }

            holdersSeries.Clear();
            long firstTime = 0;
            long lastTime = 0;

            for (int i = 0; i < points.Count; i++)
            {
                JObject point = points[i] as JObject;
                if (point == null)
                {
                    continue;
                }

                holdersSeries.Add(Long(point, "total"));

                long stamp = Long(point, "time");
                if (firstTime == 0) firstTime = stamp;
                if (stamp > 0) lastTime = stamp;
            }

            holdersWave.SetTitle("addresses holding a balance, by first appearance" +
                Span(firstTime, lastTime));
            holdersWave.Draw(holdersSeries, "", true, 0);
        }

        private async void LoadLiveTx()
        {
            if (loadingLive)
            {
                return;
            }

            loadingLive = true;

            var parameters = new JObject();
            parameters["points"] = LiveBlocks;
            parameters["spacing"] = 1;

            Verdict verdict = await client.AskAsync(HubQueries.Series, parameters);
            loadingLive = false;

            if (verdict == null || !verdict.HasResult)
            {
                liveTxWave.SetTitle("transactions per block, live - " +
                    (verdict == null ? "no answer" : verdict.Error));
                return;
            }

            JObject result = verdict.Result as JObject;
            JArray points = result == null ? null : result["points"] as JArray;

            liveTxSeries.Clear();

            long firstTime = 0;
            long lastTime = 0;

            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    JObject point = points[i] as JObject;
                    if (point == null)
                    {
                        continue;
                    }

                    liveTxSeries.Add(Long(point, "tx"));

                    long stamp = Long(point, "time");
                    if (firstTime == 0) firstTime = stamp;
                    if (stamp > 0) lastTime = stamp;
                }
            }

            liveTxWave.SetTitle("transactions per block, last " +
                liveTxSeries.Count.ToString(CultureInfo.InvariantCulture) + " blocks" +
                Span(firstTime, lastTime));
            liveTxWave.Draw(liveTxSeries, "tx", true, 0);
        }

        private void BindFigures()
        {
            string[] labels =
            {
                "height", "addresses with a balance", "money supply",
                "mempool", "stakers enabled", "next staker price"
            };

            for (int i = 0; i < labels.Length; i++)
            {
                string cell = "Body/Figures/Figure" + i.ToString(CultureInfo.InvariantCulture);
                UIPrefab.Bind<TextMeshProUGUI>(root, cell + "/Caption").text = labels[i];
                figures.Add(UIPrefab.Bind<TextMeshProUGUI>(root, cell + "/Value"));
            }
        }

        private static string Span(long from, long to)
        {
            if (from <= 0 || to <= 0 || to < from)
            {
                return string.Empty;
            }

            DateTime start = Epoch.AddSeconds(from);
            DateTime end = Epoch.AddSeconds(to);
            string format = (to - from) < 172800 ? "MMM d HH:mm" : "yyyy-MM-dd";

            return ",  " + start.ToString(format, CultureInfo.InvariantCulture) +
                " to " + end.ToString(format, CultureInfo.InvariantCulture);
        }

        private void Set(int index, string text)
        {
            if (index >= 0 && index < figures.Count)
            {
                figures[index].text = text;
            }
        }

        private static long Long(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return 0;
            }

            if (source[field].Type == JTokenType.Integer)
            {
                return source.Value<long>(field);
            }

            if (source[field].Type == JTokenType.Float)
            {
                return (long)source.Value<double>(field);
            }

            return 0;
        }

        private static decimal DecimalValue(JObject source, string field)
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

        private static string Decimal(JObject source, string field, int places)
        {
            decimal value = DecimalValue(source, field);
            return value.ToString(places > 0 ? "N" + places : "N0", CultureInfo.InvariantCulture);
        }

    }
}
