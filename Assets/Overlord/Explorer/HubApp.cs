using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Overlord.Tor;
using Xst.Rpc;
using Xst.Unity;

namespace Overlord.Explorer
{
    public class HubApp : MonoBehaviour
    {
        public const float BlockSeconds = 5f;

        public const string DayRange = "1 day";
        public const string ThreeDayRange = "3 days";
        public const string WeekRange = "week";
        public const string MonthRange = "month";
        public const string MaxRange = "5 months";

        private static readonly string[] MarketRanges =
        {
            DayRange, ThreeDayRange, WeekRange, MonthRange, MaxRange
        };

        [SerializeField] private XstSettings localSettings;
        [SerializeField] private float statsIntervalSeconds = 6f;
        [SerializeField] private float tickIntervalSeconds = 2f;
        [SerializeField] private float stakersIntervalSeconds = 5f;

        private WindowChrome chrome;
        private MenuUI menu;
        private HubPage page;
        private HubOperator operatorScreen;
        private WavePanel marketChart;
        private string marketRange = MonthRange;
        private ExplorerScreen explorer;
        private StatsScreen stats;
        private RichListScreen rich;
        private StakersScreen stakers;
        private InfoScreen news;
        private InfoScreen socials;
        private InfoScreen wallets;
        private InfoScreen dragons;
        private InfoScreen market;
        private InfoScreen tools;

        private ExplorerClient client;
        private readonly List<RemoteHubSource> remotes = new List<RemoteHubSource>();
        private LocalHubSource local;

        private bool connecting;
        private float statsTimer;
        private float tickTimer;
        private bool statsVisible;
        private bool stakersVisible;
        private bool operatorVisible;
        private bool stakersBusy;
        private float stakersTimer;
        private bool pageVisible;
        private string paintedTor;
        private string connectedTo;

        private long knownHeight = -1;
        private float sinceBlock;
        private bool tickBusy;

        private void Start()
        {
            HubDispatcher.OnInternalError = delegate(string query, Exception problem)
            {
                Debug.LogError("[hub] " + query + " threw " + problem.GetType().Name + ": " +
                    problem.Message + Environment.NewLine + problem.StackTrace);
            };

            WindowFrame.Apply();

            chrome = new WindowChrome(transform);
            chrome.MinimizeButton.onClick.AddListener(WindowFrame.Minimize);
            chrome.QuitButton.onClick.AddListener(WindowFrame.Quit);

            menu = new MenuUI(transform);
            menu.Onion.text = TorConfig.GetSavedOnionAddress();
            menu.ConnectButton.onClick.AddListener(OnConnect);
            menu.LocalButton.onClick.AddListener(OnLocal);
            menu.DisconnectButton.onClick.AddListener(OnDisconnect);
            menu.SetConnected(false);

            menu.AddTile("Board", "News, market, community and tools need no hub.",
                new Color32(0x5A, 0x9B, 0xD5, 0xFF)).Button.onClick.AddListener(ShowPage);
            menu.SetTilesVisible(true);

            menu.SetStatus("Enter a hub address, or use the daemon on this machine.", ExplorerUI.Muted);
            TorLauncher.Ensure();
        }

        private void Update()
        {
            PaintTor();

            if (pageVisible && page != null && client != null)
            {
                sinceBlock += Time.unscaledDeltaTime;
                float remaining = BlockSeconds - sinceBlock;
                page.SetNextBlock(remaining < 0f ? 0f : remaining, remaining <= 0f);

                tickTimer -= Time.unscaledDeltaTime;
                if (tickTimer <= 0f)
                {
                    tickTimer = tickIntervalSeconds < 1f ? 1f : tickIntervalSeconds;
                    Tick();
                }
            }

            if (statsVisible && client != null && stats != null)
            {
                statsTimer -= Time.unscaledDeltaTime;
                if (statsTimer <= 0f)
                {
                    statsTimer = statsIntervalSeconds < 1f ? 1f : statsIntervalSeconds;
                    stats.Refresh();
                }
            }

            if (operatorVisible && operatorScreen != null)
            {
                operatorScreen.Tick();
            }

            if (stakersVisible && client != null && stakers != null)
            {
                stakersTimer -= Time.unscaledDeltaTime;
                if (stakersTimer <= 0f)
                {
                    stakersTimer = stakersIntervalSeconds < 1f ? 1f : stakersIntervalSeconds;
                    RefreshStakers();
                }
            }
        }

        private async void Tick()
        {
            if (tickBusy || client == null || page == null)
            {
                return;
            }

            tickBusy = true;
            Verdict verdict = await client.PingAsync();
            tickBusy = false;

            if (verdict == null || !verdict.HasResult)
            {
                return;
            }

            JObject result = verdict.Result as JObject;
            if (result == null || result["blocks"] == null)
            {
                return;
            }

            long height = result.Value<long>("blocks");
            if (height != knownHeight)
            {
                knownHeight = height;
                sinceBlock = 0f;
                page.SetHeight(height);
            }
        }

        private void PaintTor()
        {
            TorLauncher.State state = TorLauncher.Status;
            string text = TorLauncher.Describe();

            switch (state)
            {
                case TorLauncher.State.Ready:
                    menu.SetTor(1f, text, ExplorerUI.Good);
                    break;
                case TorLauncher.State.Starting:
                    menu.SetTor(TorLauncher.Percent / 100f, text, ExplorerUI.Warn);
                    break;
                case TorLauncher.State.Failed:
                    menu.SetTor(0f, text, ExplorerUI.Bad);
                    break;
                default:
                    menu.SetTor(0f, text, ExplorerUI.Muted);
                    break;
            }

            if (paintedTor != text)
            {
                paintedTor = text;
                RefreshColumns();
            }
        }

        private void RefreshColumns()
        {
            if (page == null)
            {
                return;
            }

            bool hub = client != null;
            bool tor = TorLauncher.Ready;

            string torReason = TorLauncher.Status == TorLauncher.State.Failed
                ? "Tor did not start"
                : "waiting for Tor";

            foreach (HubColumn column in page.Columns)
            {
                switch (column.Needs)
                {
                    case ColumnNeeds.Hub:
                        column.SetAvailable(hub, "connect a hub");
                        break;
                    case ColumnNeeds.Tor:
                        column.SetAvailable(tor, torReason);
                        break;
                    default:
                        column.SetAvailable(true, null);
                        break;
                }
            }

            if (!pageVisible)
            {
                return;
            }

            if (hub)
            {
                page.SetStatus("Pick a column. Everything here is read only.", ExplorerUI.Muted);
                return;
            }

            page.SetStatus(tor
                ? "No hub connected. The chain columns need one, the rest work now."
                : "No hub connected. " + TorLauncher.Describe(),
                tor ? ExplorerUI.Muted : ExplorerUI.Warn);
        }

        private async void OnConnect()
        {
            if (connecting)
            {
                return;
            }

            List<string> entries = TorConfig.Entries(menu.Onion.text);
            var wanted = new List<RemoteHubSource>();
            int unreadable = 0;

            foreach (string entry in entries)
            {
                string onion;
                int port;
                if (TorConfig.TrySplit(entry, out onion, out port))
                {
                    wanted.Add(new RemoteHubSource(onion, port));
                }
                else
                {
                    unreadable++;
                }
            }

            if (wanted.Count == 0)
            {
                menu.SetStatus(unreadable > 0
                    ? "That is not a v3 onion address. It should be 56 characters then .onion."
                    : "Enter at least one hub address.", ExplorerUI.Bad);
                return;
            }

            if (!TorLauncher.Ready)
            {
                foreach (RemoteHubSource spare in wanted)
                {
                    spare.Dispose();
                }

                TorLauncher.Ensure();
                menu.SetStatus("Tor is not ready yet. " + TorLauncher.Describe(), ExplorerUI.Warn);
                return;
            }

            connecting = true;
            menu.SetStatus(wanted.Count == 1
                ? "Opening a Tor circuit ..."
                : "Opening " + wanted.Count + " Tor circuits ...", ExplorerUI.Muted);

            Release();

            var dialling = new List<Task<string>>();
            foreach (RemoteHubSource candidate in wanted)
            {
                dialling.Add(candidate.ConnectAsync());
            }

            await Task.WhenAll(dialling);

            var live = new List<IHubSource>();
            string lastProblem = null;

            for (int i = 0; i < wanted.Count; i++)
            {
                if (dialling[i].Result == null)
                {
                    remotes.Add(wanted[i]);
                    live.Add(wanted[i]);
                }
                else
                {
                    lastProblem = dialling[i].Result;
                    wanted[i].Dispose();
                }
            }

            if (live.Count == 0)
            {
                connecting = false;
                menu.SetStatus("Could not reach that hub. " + lastProblem, ExplorerUI.Bad);
                return;
            }

            client = new ExplorerClient(live);

            Verdict ping = await client.PingAsync();
            connecting = false;

            if (!ping.HasResult)
            {
                menu.SetStatus("The hub answered, but not with our protocol. " + ping.Error,
                    ExplorerUI.Bad);
                Release();
                return;
            }

            TorConfig.SaveOnionAddress(menu.Onion.text.Trim());
            OpenPage(live.Count == 1
                ? remotes[0].Name + " over Tor"
                : live.Count + " hubs over Tor");
        }

        private void OnRunHub()
        {
            if (!ConfigureLocalDaemon())
            {
                return;
            }

            if (operatorScreen == null)
            {
                operatorScreen = new HubOperator(transform, BackToMenu);
                operatorScreen.StartButton.onClick.AddListener(delegate
                {
                    operatorScreen.StartHub(XstConnection.Client);
                });
                operatorScreen.StopButton.onClick.AddListener(delegate
                {
                    operatorScreen.StopHub();
                });
            }

            Leave();
            menu.Show(false);
            operatorScreen.Show(true);
            operatorVisible = true;
        }

        private void BackToMenu()
        {
            ShowMenu();
        }

        private bool ConfigureLocalDaemon()
        {
            try
            {
                XstSettings settings = localSettings != null
                    ? localSettings
                    : ScriptableObject.CreateInstance<XstSettings>();

                string host = Setting("xst.rpc.host", "XST_RPC_HOST");
                string port = Setting("xst.rpc.port", "XST_RPC_PORT");

                if (!string.IsNullOrEmpty(host))
                {
                    settings.Host = host;
                }

                int parsed;
                if (!string.IsNullOrEmpty(port) &&
                    int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    settings.Port = parsed;
                }

                XstConnection.Configure(settings.CreateOptions(
                    Setting("xst.rpc.user", "XST_RPC_USER"),
                    Setting("xst.rpc.password", "XST_RPC_PASSWORD")));
                return true;
            }
            catch (Exception ex)
            {
                menu.SetStatus("Could not reach the daemon settings. " + ex.Message, ExplorerUI.Bad);
                return false;
            }
        }

        private async void OnLocal()
        {
            if (connecting)
            {
                return;
            }

            connecting = true;
            menu.SetStatus("Talking to the daemon on this machine ...", ExplorerUI.Muted);

            Release();

            try
            {
                XstSettings settings = localSettings != null
                    ? localSettings
                    : ScriptableObject.CreateInstance<XstSettings>();

                string host = Setting("xst.rpc.host", "XST_RPC_HOST");
                string port = Setting("xst.rpc.port", "XST_RPC_PORT");

                if (!string.IsNullOrEmpty(host))
                {
                    settings.Host = host;
                }

                int parsed;
                if (!string.IsNullOrEmpty(port) &&
                    int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    settings.Port = parsed;
                }

                XstClientOptions options = settings.CreateOptions(
                    Setting("xst.rpc.user", "XST_RPC_USER"),
                    Setting("xst.rpc.password", "XST_RPC_PASSWORD"));

                XstConnection.Configure(options);
                local = new LocalHubSource(XstConnection.Client, new PeerSet(), "local daemon");
                client = new ExplorerClient(new List<IHubSource> { local });
            }
            catch (Exception ex)
            {
                connecting = false;
                menu.SetStatus("Could not start the local client. " + ex.Message, ExplorerUI.Bad);
                return;
            }

            string problem = await local.SelfCheckAsync();
            connecting = false;

            if (problem != null)
            {
                menu.SetStatus(problem, ExplorerUI.Bad);
                Release();
                return;
            }

            OpenPage("the daemon on this machine, not over Tor");
        }

        private void OpenPage(string source)
        {
            connectedTo = source;
            knownHeight = -1;
            sinceBlock = 0f;

            ShowPage();

            if (client != null)
            {
                page.SetHeight(0);
                page.SetNextBlock(-1f, false);
            }
        }

        private void ShowPage()
        {
            if (page == null)
            {
                page = new HubPage(transform);
                page.BackButton.onClick.AddListener(ShowMenu);
                page.SetBackLabel("Connection");
                BuildColumns();
            }

            tickTimer = 0f;

            menu.Show(false);
            CloseScreens();
            page.Show(true);
            pageVisible = true;

            PaintSource();
            RefreshColumns();
        }

        private void ShowMenu()
        {
            Leave();
            menu.SetConnected(client != null);
            menu.Show(true);
            menu.SetStatus(client != null
                ? "Connected. Disconnect here, or go back to the columns."
                : "Enter a hub address, or use the daemon on this machine.", ExplorerUI.Muted);
        }

        private void PaintSource()
        {
            if (client == null)
            {
                page.SetSource("no hub - chain data unavailable", ExplorerUI.Warn);
                page.SetHeight(0);
                page.SetNextBlock(-1f, false);
                return;
            }

            page.SetSource("connected to " + connectedTo);
        }

        private void BuildColumns()
        {
            page.AddColumn("Stealth Explorer", "explorer",
                "Blocks, transactions, addresses and the rich list.",
                ExplorerUI.Good, ColumnNeeds.Hub).Button.onClick.AddListener(OpenExplorer);

            page.AddColumn("Network stats", "chain",
                "Supply, addresses, stakers and the history graphs.",
                new Color32(0x5A, 0x9B, 0xD5, 0xFF), ColumnNeeds.Hub)
                .Button.onClick.AddListener(OpenStats);

            page.AddColumn("Stakers", "qPoS",
                "Who produces the blocks, the queue and the staker price.",
                new Color32(0xB0, 0x8C, 0xE8, 0xFF), ColumnNeeds.Hub)
                .Button.onClick.AddListener(OpenStakers);

            page.AddColumn("News", "updates",
                "Releases from the Stealth and Overlord repositories.",
                new Color32(0xE8, 0xB8, 0x4B, 0xFF), ColumnNeeds.Tor)
                .Button.onClick.AddListener(OpenNews);

            page.AddColumn("Community", "socials",
                "Discord, X, Telegram, Reddit and Medium.",
                new Color32(0x6E, 0xC2, 0xB0, 0xFF), ColumnNeeds.Nothing)
                .Button.onClick.AddListener(OpenSocials);

            page.AddColumn("Wallets", "official",
                "Where to download StealthSend. Overlord holds no keys.",
                new Color32(0x9E, 0xB4, 0xC8, 0xFF), ColumnNeeds.Nothing)
                .Button.onClick.AddListener(OpenWallets);

            page.AddColumn("Market", "price",
                "Price and market cap, fetched through Tor.",
                new Color32(0xE0, 0x9A, 0x6C, 0xFF), ColumnNeeds.Tor)
                .Button.onClick.AddListener(OpenMarket);

            page.AddColumn("Tools", "build",
                "Libraries and utilities for building on Stealth.",
                new Color32(0x7F, 0xB0, 0x7F, 0xFF), ColumnNeeds.Nothing)
                .Button.onClick.AddListener(OpenTools);

            page.AddColumn("StealthDragons", "game",
                "A multiplayer card game on Stealth, played over Tor.",
                new Color32(0xC0, 0x6C, 0x4A, 0xFF), ColumnNeeds.Nothing)
                .Button.onClick.AddListener(OpenDragons);
        }

        private void OpenExplorer()
        {
            if (client == null)
            {
                return;
            }

            if (explorer == null)
            {
                explorer = new ExplorerScreen(transform, client, BackToPage);
                explorer.View.HoldersButton.onClick.AddListener(OpenRich);
            }

            Leave();
            explorer.Show(true);
            explorer.OnInfo();
        }

        private void OpenStats()
        {
            if (client == null)
            {
                return;
            }

            if (stats == null)
            {
                stats = new StatsScreen(transform, client, BackToPage);
            }

            Leave();
            stats.Show(true);
            statsVisible = true;
            statsTimer = 0f;
            stats.Refresh();
            stats.LoadSeries(288, "1 day");
        }

        private void OpenRich()
        {
            if (client == null)
            {
                return;
            }

            if (rich == null)
            {
                rich = new RichListScreen(transform, client, BackToPage, BackToExplorer);
            }

            Leave();
            rich.Show(true);
            rich.Open();
        }

        private void OpenStakers()
        {
            if (client == null)
            {
                return;
            }

            if (stakers == null)
            {
                stakers = new StakersScreen(transform, BackToPage);
            }

            Leave();
            stakers.Show(true);
            stakersVisible = true;
            stakersTimer = stakersIntervalSeconds;

            stakers.SetStatus("asking the hub", ExplorerUI.Muted);
            RefreshStakers();
        }

        private async void RefreshStakers()
        {
            if (stakersBusy || client == null || stakers == null)
            {
                return;
            }

            stakersBusy = true;
            Verdict verdict = await client.AskAsync(HubQueries.ChainStats, null);
            stakersBusy = false;

            if (verdict == null || !verdict.HasResult)
            {
                stakers.SetStatus(verdict == null ? "no answer" : verdict.Error, ExplorerUI.Bad);
                stakers.SetBadge(verdict == null ? "" : verdict.Badge, ExplorerUI.Bad);
                return;
            }

            JObject result = verdict.Result as JObject;
            JObject summary = result == null ? null : result["stakers"] as JObject;

            stakers.SetBadge(verdict.Badge,
                verdict.Unanimous ? ExplorerUI.Good : ExplorerUI.Warn);

            if (summary == null)
            {
                stakers.Clear();
                stakers.AddHeading("the daemon did not report stakers");
                stakers.AddNote("getstakersummary returned nothing.", ExplorerUI.Warn);
                stakers.SetStatus("nothing to show", ExplorerUI.Warn);
                return;
            }

            stakers.Rotate(summary);
            DescribeStakers(summary);

            stakers.SetStatus("answered by " + verdict.Badge,
                verdict.Unanimous ? ExplorerUI.Good : ExplorerUI.Warn);
        }

        private void DescribeStakers(JObject summary)
        {
            stakers.Clear();

            long enabled = Number(summary, "enabled_stakers");
            long disabled = Number(summary, "disabled_stakers");
            long terminated = Number(summary, "terminated_stakers");
            long registered = enabled + disabled + terminated;

            stakers.AddHeading("registered stakers");
            stakers.AddRow("registered", registered.ToString(CultureInfo.InvariantCulture));
            stakers.AddRow("enabled", enabled.ToString(CultureInfo.InvariantCulture));
            stakers.AddRow("disabled", disabled.ToString(CultureInfo.InvariantCulture));
            stakers.AddRow("terminated", terminated.ToString(CultureInfo.InvariantCulture));
            stakers.AddRow("productive",
                Number(summary, "productive_stakers").ToString(CultureInfo.InvariantCulture));
            stakers.AddNote(
                "Registered is enabled plus disabled plus terminated. Productive is a different " +
                "count and is usually lower, so the two do not have to agree.", ExplorerUI.Muted);

            stakers.AddSpace(10f);
            stakers.AddHeading("recent work");
            stakers.AddRow("produced recently",
                Number(summary, "produced_recently").ToString(CultureInfo.InvariantCulture));
            stakers.AddRow("missed recently",
                Number(summary, "missed_recently").ToString(CultureInfo.InvariantCulture));
            stakers.AddRow("total XST earned", Amount(summary, "total_xst_earned"));

            stakers.AddSpace(10f);
            stakers.AddHeading("this round");
            stakers.AddRow("produced",
                Number(summary, "produced_queue").ToString(CultureInfo.InvariantCulture));
            stakers.AddRow("remaining",
                Number(summary, "remaining_queue").ToString(CultureInfo.InvariantCulture));
            stakers.AddRow("missed",
                Number(summary, "missed_queue").ToString(CultureInfo.InvariantCulture));

            JArray queue = summary["remaining_queue_aliases"] as JArray;
            if (queue != null && queue.Count > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < queue.Count; i++)
                {
                    names.Add(queue[i].Value<string>());
                }
                stakers.AddNote("the whole rotation, in order: " +
                    string.Join(", ", names.ToArray()), ExplorerUI.Muted);
            }

            stakers.AddSpace(10f);
            stakers.AddHeading("price of a staker slot");
            stakers.AddRow("newest paid", Amount(summary, "newest_staker_price"));
            stakers.AddRow("next costs", Amount(summary, "next_staker_price"));
        }

        private async void OpenNews()
        {
            if (news == null)
            {
                news = new InfoScreen(transform, "News", BackToPage);
            }

            Leave();
            news.Show(true);
            news.Clear();
            news.AddHeading("fetching releases through Tor");
            news.AddNote("Every request leaves through the Tor instance this application started, "
                + "so GitHub sees an exit node and not you.", ExplorerUI.Muted);
            news.SetStatus("asking GitHub over Tor, a few seconds per repository", ExplorerUI.Muted);

            if (!TorLauncher.Ready)
            {
                TorLauncher.Ensure();
                news.Clear();
                news.AddHeading("Tor is not ready");
                news.AddNote("News goes out through Tor so GitHub never sees your address. " +
                    TorLauncher.Describe() + " Try again once it says ready.", ExplorerUI.Warn);
                news.SetStatus("waiting for Tor", ExplorerUI.Warn);
                return;
            }

            var activity = new List<ActivityNote>();
            string activityTrouble = null;

            try
            {
                activity = await TorFeeds.ActivityAsync(TorFeeds.WatchedOrg, 12);
            }
            catch (Exception ex)
            {
                activityTrouble = ex.Message;
            }

            var found = new List<ReleaseNote>();
            var failures = new List<string>();

            for (int i = 0; i < TorFeeds.Watched.Length; i++)
            {
                string owner = TorFeeds.Watched[i][0];
                string repo = TorFeeds.Watched[i][1];

                try
                {
                    List<ReleaseNote> notes = await TorFeeds.ReleasesAsync(owner, repo, 2);
                    found.AddRange(notes);
                }
                catch (Exception ex)
                {
                    failures.Add(owner + "/" + repo + ": " + ex.Message);
                }
            }

            found.Sort(delegate(ReleaseNote a, ReleaseNote b)
            {
                return b.Published.CompareTo(a.Published);
            });

            news.Clear();

            if (found.Count == 0)
            {
                news.AddHeading("nothing came back");
                for (int i = 0; i < failures.Count; i++)
                {
                    news.AddNote(failures[i], ExplorerUI.Bad);
                }
                news.AddNote("GitHub allows sixty unauthenticated requests an hour per address, and a "
                    + "Tor exit is shared, so a busy exit can use that up. Trying again usually picks "
                    + "a different circuit.", ExplorerUI.Muted);
                news.SetStatus("failed", ExplorerUI.Bad);
                return;
            }

            news.AddHeading("what has been happening on " + TorFeeds.WatchedOrg);

            if (activityTrouble != null)
            {
                news.AddNote("could not read the activity feed: " + activityTrouble, ExplorerUI.Warn);
            }
            else if (activity.Count == 0)
            {
                news.AddNote("GitHub reports nothing public in the last few weeks.", ExplorerUI.Muted);
            }
            else
            {
                for (int i = 0; i < activity.Count; i++)
                {
                    news.AddRow(activity[i].When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                        "   " + activity[i].Repository,
                        activity[i].Summary);
                }
            }

            news.AddSpace(12f);
            news.AddHeading("latest releases");

            for (int i = 0; i < found.Count; i++)
            {
                ReleaseNote note = found[i];
                string when = note.Published == default(DateTime)
                    ? "date unknown"
                    : note.Published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                news.AddRow(when + "   " + note.Repository,
                    (string.IsNullOrEmpty(note.Name) ? note.Tag : note.Name)
                    + (string.IsNullOrEmpty(note.Tag) ? "" : "   (" + note.Tag + ")"));

                if (!string.IsNullOrEmpty(note.Summary))
                {
                    news.AddNote(note.Summary, ExplorerUI.Muted);
                }

                if (!string.IsNullOrEmpty(note.Url))
                {
                    news.AddLink("release page", note.Url, "");
                }

                news.AddSpace(8f);
            }

            if (failures.Count > 0)
            {
                news.AddHeading("did not answer");
                for (int i = 0; i < failures.Count; i++)
                {
                    news.AddNote(failures[i], ExplorerUI.Warn);
                }
            }

            news.SetStatus(found.Count + " releases over Tor, " + failures.Count + " quiet",
                failures.Count == 0 ? ExplorerUI.Good : ExplorerUI.Warn);
        }

        private async void OpenMarket()
        {
            if (market == null)
            {
                market = new InfoScreen(transform, "Market", BackToPage);
            }

            Leave();
            market.Show(true);
            market.Clear();
            market.AddHeading("asking through Tor");
            market.SetStatus("fetching the ticker", ExplorerUI.Muted);

            if (!TorLauncher.Ready)
            {
                TorLauncher.Ensure();
                market.Clear();
                market.AddHeading("Tor is not ready");
                market.AddNote("The price goes out through Tor so the price service never sees your " +
                    "address. " + TorLauncher.Describe() + " Try again once it says ready.",
                    ExplorerUI.Warn);
                market.SetStatus("waiting for Tor", ExplorerUI.Warn);
                return;
            }

            MarketQuote quote = await TorFeeds.QuoteAsync();

            decimal chainSupply = 0m;
            if (client != null)
            {
                Verdict stats = await client.AskAsync(HubQueries.ChainStats, null);
                if (stats != null && stats.HasResult)
                {
                    JObject info = stats.Result as JObject;
                    if (info != null && info["moneysupply"] != null)
                    {
                        try
                        {
                            chainSupply = info["moneysupply"].Value<decimal>();
                        }
                        catch (Exception)
                        {
                            chainSupply = 0m;
                        }
                    }
                }
            }

            market.Clear();

            if (quote.Error != null)
            {
                market.AddHeading("no price");
                market.AddNote(quote.Error, ExplorerUI.Bad);
                market.SetStatus("failed", ExplorerUI.Bad);
                return;
            }

            market.AddHeading("price");

            marketChart = null;
            List<Button> buttons = InfoRows.AddButtons(market.Column, MarketRanges);
            for (int i = 0; i < buttons.Count; i++)
            {
                string picked = MarketRanges[i];
                buttons[i].onClick.AddListener(delegate
                {
                    marketRange = picked;
                    LoadPrice();
                });
            }

            marketChart = WavePanel.Into(market.Column);
            LoadPrice();

            market.AddNote("Chart data from CoinPaprika.", ExplorerUI.Muted);

            market.AddRow("USD", quote.Price.ToString("0.00000000", CultureInfo.InvariantCulture));
            market.AddRow("24 hour change",
                quote.Change24h.ToString("0.00", CultureInfo.InvariantCulture) + " %");
            market.AddRow("24 hour volume",
                quote.Volume24h.ToString("N0", CultureInfo.InvariantCulture) + " USD");
            market.AddRow("reported", quote.Updated == null ? "-" : quote.Updated);

            market.AddSpace(12f);
            market.AddHeading("market cap depends on which supply you believe");
            market.AddRow("as CoinPaprika reports it",
                quote.MarketCap.ToString("N0", CultureInfo.InvariantCulture) + " USD");
            market.AddRow("their supply figure",
                quote.ReportedSupply.ToString("N0", CultureInfo.InvariantCulture) + " XST");

            if (chainSupply > 0m)
            {
                market.AddRow("the chain's own money supply",
                    chainSupply.ToString("N0", CultureInfo.InvariantCulture) + " XST");
                market.AddRow("market cap on that supply",
                    (chainSupply * quote.Price).ToString("N0", CultureInfo.InvariantCulture) + " USD");
            }

            market.AddNote(
                "Three supply numbers exist and they disagree: what the price service reports, what "
                + "the daemon calls moneysupply, and what the addresses actually add up to. The "
                + "middle one comes straight from the chain, so the second market cap is the one this "
                + "application can stand behind.", ExplorerUI.Muted);

            market.AddSpace(12f);
            market.AddHeading("where it trades");
            for (int i = 0; i < HubLinks.Markets.Length; i++)
            {
                market.AddLink(HubLinks.Markets[i].Label, HubLinks.Markets[i].Url,
                    HubLinks.Markets[i].Note);
            }

            market.AddSpace(12f);
            market.AddNote("Somebody else's number about somebody else's market, not a chain fact.",
                ExplorerUI.Warn);

            market.SetStatus("fetched through Tor", ExplorerUI.Good);
        }

        private async void LoadPrice()
        {
            WavePanel chart = marketChart;
            if (chart == null)
            {
                return;
            }

            chart.SetTitle("USD per XST, fetching " + marketRange + " ...");

            PriceHistory history;
            if (marketRange == DayRange)
            {
                history = await TorFeeds.HourlyHistoryAsync(TorFeeds.MaxHourlyHours);
            }
            else if (marketRange == ThreeDayRange)
            {
                history = await TorFeeds.PriceHistoryAsync(3);
            }
            else if (marketRange == WeekRange)
            {
                history = await TorFeeds.PriceHistoryAsync(7);
            }
            else if (marketRange == MaxRange)
            {
                history = await TorFeeds.PriceHistoryAsync(TorFeeds.MaxHistoryDays);
            }
            else
            {
                history = await TorFeeds.PriceHistoryAsync(TorFeeds.DefaultHistoryDays);
            }

            if (chart != marketChart)
            {
                return;
            }

            if (history.Error != null || history.Points.Count < 2)
            {
                chart.SetTitle("USD per XST, " + marketRange + " - " +
                    (history.Error != null ? history.Error : "too little history to chart"));
                chart.Draw(null, "USD", false, 6);
                return;
            }

            var values = new List<double>();
            foreach (PricePoint point in history.Points)
            {
                values.Add((double)point.Price);
            }

            DateTime from = history.Points[0].When;
            DateTime to = history.Points[history.Points.Count - 1].When;
            string format = (to - from).TotalDays < 2d ? "MMM d HH:mm" : "MMM d";

            chart.SetTitle("USD per XST, " + marketRange + ",  " +
                from.ToString(format, CultureInfo.InvariantCulture) + " to " +
                to.ToString(format, CultureInfo.InvariantCulture) +
                ",  " + history.Points.Count + " points");

            chart.Draw(values, "USD", false, 6);
        }

        private void OpenTools()
        {
            if (tools == null)
            {
                tools = new InfoScreen(transform, "Tools", BackToPage);
            }

            Leave();
            tools.Show(true);
            tools.Clear();

            tools.AddHeading("what exists for building on Stealth");
            tools.AddNote(
                "Everything here is somebody else's work, linked and described, not bundled. "
                + "The two under Stealth-R-D-LLC are the project's own.", ExplorerUI.Muted);
            tools.AddSpace(10f);

            for (int i = 0; i < HubLinks.Tools.Length; i++)
            {
                tools.AddHeading(HubLinks.Tools[i].Label);
                tools.AddNote(HubLinks.Tools[i].Note, ExplorerUI.Ink);
                tools.AddLink("repository", HubLinks.Tools[i].Url, string.Empty);
                tools.AddSpace(10f);
            }

            tools.SetStatus(HubLinks.Tools.Length + " tools", ExplorerUI.Muted);
        }

        private void OpenSocials()
        {
            if (socials == null)
            {
                socials = new InfoScreen(transform, "Community", BackToPage);
            }

            Leave();
            socials.Show(true);
            socials.Clear();

            socials.AddHeading("official Stealth channels");
            for (int i = 0; i < HubLinks.Socials.Length; i++)
            {
                socials.AddLink(HubLinks.Socials[i].Label, HubLinks.Socials[i].Url,
                    HubLinks.Socials[i].Note);
            }

            socials.AddSpace(12f);
            socials.AddHeading("Overlord is not an official Stealth product");
            socials.AddNote(
                "It is built by a third party against the public RPC interface. The links above " +
                "belong to the Stealth project, which is not responsible for this application.",
                ExplorerUI.Muted);

            socials.SetStatus("Open sends you to your browser, which does not go through Tor.",
                ExplorerUI.Muted);
        }

        private void OpenWallets()
        {
            if (wallets == null)
            {
                wallets = new InfoScreen(transform, "Wallets", BackToPage);
            }

            Leave();
            wallets.Show(true);
            wallets.Clear();

            wallets.AddHeading("Overlord will never hold your keys");
            wallets.AddNote(
                "There is no wallet in this application and there is not going to be one. A wallet " +
                "means custody, key recovery and support tickets. StealthSend already does that job.",
                ExplorerUI.Ink);

            wallets.AddSpace(12f);
            wallets.AddHeading("official downloads");
            for (int i = 0; i < HubLinks.Wallets.Length; i++)
            {
                wallets.AddLink(HubLinks.Wallets[i].Label, HubLinks.Wallets[i].Url,
                    HubLinks.Wallets[i].Note);
            }

            wallets.AddSpace(12f);
            wallets.AddNote(
                "Download wallets from the official pages above and nowhere else. Nobody from " +
                "Stealth or Overlord will ever ask you for a seed phrase.", ExplorerUI.Warn);

            wallets.SetStatus("Open sends you to your browser, which does not go through Tor.",
                ExplorerUI.Muted);
        }

        private void OpenDragons()
        {
            if (dragons == null)
            {
                dragons = new InfoScreen(transform, "StealthDragons", BackToPage);
            }

            Leave();
            dragons.Show(true);
            dragons.Clear();

            dragons.AddHeading("a multiplayer card game that settles on the chain");
            dragons.AddNote(
                "StealthDragons is a player versus player card game that runs entirely over Tor as a " +
                "hidden service. Matches are provably fair: the shuffle is a commit and reveal with " +
                "entropy from both players, and each result is signed by both of them with keys the " +
                "server never sees. Stealth is where servers publish themselves, where those results " +
                "are anchored, and what the bets are paid in.", ExplorerUI.Ink);

            dragons.AddSpace(12f);
            dragons.AddHeading("how it fits together");
            dragons.AddRow("client", "Unity, all traffic over Tor");
            dragons.AddRow("server", "Dragonator, headless");
            dragons.AddRow("add-ons", "Registry, Witness, Bots and Bet, drop-in DLLs");
            dragons.AddRow("daemon", "every add-on but Bots wants one beside the server");

            dragons.AddSpace(12f);
            dragons.AddHeading("what the chain carries");
            dragons.AddRow("Registry", "the public server list, so one server finds you the rest");
            dragons.AddRow("Witness", "a 40 byte anchor per batch of matches, a hash and never a match");
            dragons.AddRow("Bots", "nothing on chain, but a bot signs its own match receipts");
            dragons.AddRow("Bet", "bets, payouts and refunds in XST");
            dragons.AddNote(
                "An add-on is a file the operator drops in. A server without them still plays, it " +
                "just offers none of this.", ExplorerUI.Muted);

            dragons.AddSpace(12f);
            dragons.AddHeading("status in this build");
            dragons.AddRow("launcher", "not wired up");
            dragons.AddRow("server list", "needs the on-chain registry, which reads OP_RETURN entries");
            dragons.AddRow("installed copy", "Overlord cannot see one, there is no launcher yet");
            dragons.AddNote(
                "The on-chain server registry is the piece that connects the two. Once the hub can " +
                "read it, this column can list which servers are up and let you join one.",
                ExplorerUI.Muted);

            dragons.AddSpace(12f);
            dragons.AddHeading("repositories");
            dragons.AddLink("StealthDragons", "https://github.com/mahusar/StealthDragons", "the game");
            dragons.AddLink("Dragonator add-ons", "https://github.com/mahusar/dragonator-addons",
                "Registry, Witness, Bots, Bet");

            dragons.SetStatus("Read only. Nothing here launches a game yet.", ExplorerUI.Muted);
        }

        private void Leave()
        {
            pageVisible = false;
            statsVisible = false;
            stakersVisible = false;
            operatorVisible = false;
            if (page != null) page.Show(false);
            CloseScreens();
        }

        private void BackToExplorer()
        {
            if (client == null)
            {
                return;
            }

            if (explorer == null)
            {
                OpenExplorer();
                return;
            }

            Leave();
            explorer.Show(true);
        }

        private void BackToPage()
        {
            statsVisible = false;
            CloseScreens();

            if (page != null)
            {
                page.Show(true);
                pageVisible = true;
                tickTimer = 0f;
            }
        }

        private void OnDisconnect()
        {
            Release();
            Leave();
            menu.SetConnected(false);
            menu.Show(true);
            menu.SetStatus("Disconnected. The columns that need no hub still work.",
                ExplorerUI.Muted);

            RefreshColumns();
        }

        private void CloseScreens()
        {
            if (explorer != null) explorer.Show(false);
            if (stats != null) stats.Show(false);
            if (rich != null) rich.Show(false);
            if (stakers != null) stakers.Show(false);
            if (operatorScreen != null) operatorScreen.Show(false);
            if (news != null) news.Show(false);
            if (socials != null) socials.Show(false);
            if (wallets != null) wallets.Show(false);
            if (dragons != null) dragons.Show(false);
            if (market != null) market.Show(false);
            if (tools != null) tools.Show(false);
        }

        private void Release()
        {
            client = null;
            connectedTo = null;
            knownHeight = -1;

            foreach (RemoteHubSource source in remotes)
            {
                try
                {
                    source.Dispose();
                }
                catch (Exception)
                {
                }
            }
            remotes.Clear();

            if (local != null)
            {
                local = null;

                if (operatorScreen == null || !operatorScreen.Running)
                {
                    XstConnection.Release();
                }
            }
        }

        private static long Number(JObject source, string field)
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

        private static string Text(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return "-";
            }

            string value = source.Value<string>(field);
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        private static string Amount(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return "-";
            }

            try
            {
                return source[field].Value<decimal>()
                    .ToString("N6", CultureInfo.InvariantCulture) + " XST";
            }
            catch (Exception)
            {
                return "-";
            }
        }

        private static string Setting(string playerPrefsKey, string environmentKey)
        {
            string value = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            try
            {
                return Environment.GetEnvironmentVariable(environmentKey) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private void OnDestroy()
        {
            Release();
            TorLauncher.Stop();
        }
    }
}
