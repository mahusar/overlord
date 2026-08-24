using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Xst.Rpc;
using Xst.Unity;

namespace Overlord.Explorer
{
    public class ExplorerApp : MonoBehaviour
    {
        public const string UserKey = "xst.rpc.user";
        public const string PasswordKey = "xst.rpc.password";
        public const string HostKey = "xst.rpc.host";
        public const string PortKey = "xst.rpc.port";

        [SerializeField] private XstSettings settings;

        private ExplorerUI ui;
        private ExplorerClient client;
        private LocalHubSource local;
        private bool busy;

        private void Start()
        {
            ui = new ExplorerUI(transform);
            ui.SearchButton.onClick.AddListener(OnSearch);
            ui.InfoButton.onClick.AddListener(OnInfo);
            ui.PeersButton.onClick.AddListener(OnPeers);
            ui.Search.onSubmit.AddListener(delegate { OnSearch(); });

            ui.Clear();
            ui.AddHeading("Overlord explorer");
            ui.AddNote("Connecting to the local daemon.", ExplorerUI.Muted);

            try
            {
                XstConnection.Configure(BuildOptions());
                local = new LocalHubSource(XstConnection.Client, new PeerSet(), "local daemon");
                client = new ExplorerClient(new List<IHubSource> { local });
            }
            catch (Exception ex)
            {
                ui.SetStatus("cannot start: " + ex.Message, ExplorerUI.Bad);
                return;
            }

            SelfCheck();
        }

        private XstClientOptions BuildOptions()
        {
            string host = Setting(HostKey, "XST_RPC_HOST");
            string port = Setting(PortKey, "XST_RPC_PORT");
            string user = Setting(UserKey, "XST_RPC_USER");
            string password = Setting(PasswordKey, "XST_RPC_PASSWORD");

            XstSettings source = settings;
            if (source == null)
            {
                source = ScriptableObject.CreateInstance<XstSettings>();
            }

            if (!string.IsNullOrEmpty(host))
            {
                source.Host = host;
            }

            int parsed;
            if (!string.IsNullOrEmpty(port) &&
                int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                source.Port = parsed;
            }

            return source.CreateOptions(user, password);
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

        private async void SelfCheck()
        {
            ui.SetStatus("checking the daemon", ExplorerUI.Muted);

            string problem = await local.SelfCheckAsync();
            if (problem != null)
            {
                ui.SetStatus(problem, ExplorerUI.Bad);
                ui.Clear();
                ui.AddHeading("the hub cannot serve yet");
                ui.AddNote(problem, ExplorerUI.Bad);
                return;
            }

            ui.SetStatus(client.SourceCount + " source connected", ExplorerUI.Good);
            OnInfo();
        }

        private void OnSearch()
        {
            Run(ui.Search.text);
        }

        private async void OnInfo()
        {
            if (!Begin("chain info"))
            {
                return;
            }

            Verdict verdict = await client.AskAsync(HubQueries.GetInfo, null);
            ExplorerRender.Show(ui, HubQueries.GetInfo, verdict);
            UpdateHeight(verdict);
            End(verdict, "chain info");
        }

        private async void OnPeers()
        {
            if (!Begin("peers"))
            {
                return;
            }

            Verdict verdict = await client.PeersAsync();
            ExplorerRender.Show(ui, HubQueries.Peers, verdict);
            End(verdict, "peers");
        }

        private async void Run(string text)
        {
            string subject = text == null ? string.Empty : text.Trim();
            if (subject.Length == 0)
            {
                OnInfo();
                return;
            }

            if (!Begin(subject))
            {
                return;
            }

            SearchOutcome outcome = await client.SearchAsync(subject);
            ExplorerRender.Show(ui, outcome.Query, outcome.Verdict);
            End(outcome.Verdict, outcome.Query);
        }

        private bool Begin(string what)
        {
            if (busy)
            {
                return false;
            }

            if (client == null)
            {
                ui.SetStatus("not connected", ExplorerUI.Bad);
                return false;
            }

            busy = true;
            ui.SetStatus("asking " + client.SourceCount + " source about " + what, ExplorerUI.Muted);
            return true;
        }

        private void End(Verdict verdict, string what)
        {
            busy = false;

            if (verdict == null || !verdict.HasResult)
            {
                ui.SetStatus(what + " failed: " +
                    (verdict == null ? "no answer" : verdict.Error), ExplorerUI.Bad);
                return;
            }

            ui.SetStatus(what + " answered by " + verdict.Badge,
                verdict.Unanimous ? ExplorerUI.Good : ExplorerUI.Warn);
        }

        private void UpdateHeight(Verdict verdict)
        {
            JObject result = verdict == null ? null : verdict.Result as JObject;
            if (result == null || result["blocks"] == null)
            {
                return;
            }

            ui.HeightText.text = "height " +
                result.Value<long>("blocks").ToString("N0", CultureInfo.InvariantCulture);
        }

        private void OnDestroy()
        {
            XstConnection.Release();
        }
    }
}
