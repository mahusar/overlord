using System;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overlord.Tor;
using Xst.Rpc;

namespace Overlord.Explorer
{
    public class HubOperator
    {
        public const string Prefab = "HubOperatorCanvas";
        public const string PortKey = "overlord.hub.port";

        private readonly GameObject root;
        private readonly TextMeshProUGUI badgeText;
        private readonly TextMeshProUGUI statusText;
        private readonly TextMeshProUGUI onionText;
        private readonly TextMeshProUGUI torLine;
        private readonly TextMeshProUGUI checkText;
        private readonly TextMeshProUGUI queriesText;
        private readonly TextMeshProUGUI noteText;
        private readonly Image torFill;
        private readonly Button startButton;
        private readonly Button stopButton;
        private readonly TextMeshProUGUI[] figures = new TextMeshProUGUI[4];
        private readonly TMP_InputField portField;

        private HubServer server;
        private bool starting;
        private bool announced;

        public HubOperator(Transform parent, Action onBack)
        {
            root = UIPrefab.Instantiate(Prefab, parent);

            badgeText = UIPrefab.Bind<TextMeshProUGUI>(root, "Header/Badge");
            statusText = UIPrefab.Bind<TextMeshProUGUI>(root, "Status");
            onionText = UIPrefab.Bind<TextMeshProUGUI>(root, "Service/OnionWell/Onion");
            torLine = UIPrefab.Bind<TextMeshProUGUI>(root, "Service/TorLine");
            checkText = UIPrefab.Bind<TextMeshProUGUI>(root, "Service/Check");
            queriesText = UIPrefab.Bind<TextMeshProUGUI>(root, "Served/Queries");
            noteText = UIPrefab.Bind<TextMeshProUGUI>(root, "Note");
            torFill = UIPrefab.Bind<Image>(root, "Service/TorTrack/TorFill");
            startButton = UIPrefab.Bind<Button>(root, "Service/StartButton");
            stopButton = UIPrefab.Bind<Button>(root, "Service/StopButton");
            portField = UIPrefab.Bind<TMP_InputField>(root, "Service/PortField");
            portField.text = PlayerPrefs.GetInt(PortKey, HubServer.DefaultPort)
                .ToString(CultureInfo.InvariantCulture);

            for (int i = 0; i < figures.Length; i++)
            {
                figures[i] = UIPrefab.Bind<TextMeshProUGUI>(root,
                    "Figure" + i.ToString(CultureInfo.InvariantCulture) + "/Value");
            }

            Button back = UIPrefab.Bind<Button>(root, "Header/BackButton");
            if (onBack != null)
            {
                back.onClick.AddListener(delegate { onBack(); });
            }

            UIPrefab.Bind<Button>(root, "Service/CopyButton").onClick.AddListener(Copy);

            var served = new StringBuilder();
            foreach (string query in HubQueries.All)
            {
                if (served.Length > 0)
                {
                    served.Append("   ");
                }
                served.Append(query);
            }
            queriesText.text = served.ToString();

            noteText.text =
                "Read only. The allowlist above is the whole surface, enforced by name, and nothing " +
                "else reaches the daemon. Callers are rate limited per connection, because a Tor " +
                "endpoint has no address to ban. The daemon and this window both have to keep " +
                "running for the service to stay reachable.";

            stopButton.gameObject.SetActive(false);
            Paint();
        }

        public GameObject Root
        {
            get { return root; }
        }

        public bool Running
        {
            get { return server != null && server.Listening; }
        }

        public Button StartButton
        {
            get { return startButton; }
        }

        public Button StopButton
        {
            get { return stopButton; }
        }

        public void Show(bool visible)
        {
            root.SetActive(visible);
        }

        public async void StartHub(XstClient client)
        {
            if (starting || Running)
            {
                return;
            }

            if (client == null)
            {
                statusText.text = "no daemon is configured on this machine";
                statusText.color = ExplorerUI.Bad;
                return;
            }

            starting = true;
            statusText.text = "checking the daemon";
            statusText.color = ExplorerUI.Muted;
            checkText.text = string.Empty;

            var dispatcher = new HubDispatcher();
            var handlers = new HubHandlers(client, new PeerSet());
            handlers.RegisterAll(dispatcher);

            int port = ChosenPort();
            PlayerPrefs.SetInt(PortKey, port);
            PlayerPrefs.Save();

            var fresh = new HubServer(dispatcher, port);
            string trouble = await fresh.StartAsync(handlers.SelfCheckAsync);

            if (trouble != null)
            {
                starting = false;
                fresh.Dispose();
                checkText.text = trouble;
                checkText.color = ExplorerUI.Bad;
                statusText.text = "the hub refused to start";
                statusText.color = ExplorerUI.Bad;
                Paint();
                return;
            }

            server = fresh;
            starting = false;

            checkText.text = "The daemon answered and the explore api is on. Listening on " +
                port.ToString(CultureInfo.InvariantCulture) + " on loopback.";
            checkText.color = ExplorerUI.Good;

            if (server.Notice != null)
            {
                checkText.text = checkText.text + "  " + server.Notice;
                checkText.color = ExplorerUI.Warn;
            }

            statusText.text = "publishing the hidden service, this takes a moment";
            statusText.color = ExplorerUI.Muted;
            TorLauncher.PublishHiddenService(port);

            Paint();
        }

        public void StopHub()
        {
            if (server != null)
            {
                server.Dispose();
                server = null;
            }

            announced = false;

            statusText.text = "stopped";
            statusText.color = ExplorerUI.Muted;
            Paint();
        }

        public void Tick()
        {
            Paint();
        }

        private int ChosenPort()
        {
            int parsed;
            if (portField != null &&
                int.TryParse(portField.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) &&
                parsed > 0 && parsed <= 65535)
            {
                return parsed;
            }

            return HubServer.DefaultPort;
        }

        private void Copy()
        {
            string onion = TorLauncher.Onion;
            if (string.IsNullOrEmpty(onion))
            {
                statusText.text = "there is no address to copy yet";
                statusText.color = ExplorerUI.Warn;
                return;
            }

            int port = ChosenPort();
            string full = port == TorConfig.DefaultHubPort
                ? onion
                : onion + ":" + port.ToString(CultureInfo.InvariantCulture);

            GUIUtility.systemCopyBuffer = full;
            statusText.text = "copied " + full + " to the clipboard";
            statusText.color = ExplorerUI.Good;
        }

        private void Paint()
        {
            bool running = Running;

            startButton.gameObject.SetActive(!running);
            stopButton.gameObject.SetActive(running);

            string onion = TorLauncher.Onion;

            if (!running)
            {
                badgeText.text = "stopped";
                badgeText.color = ExplorerUI.Muted;
                onionText.text = "not published yet";
                onionText.color = ExplorerUI.Muted;
            }
            else if (string.IsNullOrEmpty(onion))
            {
                badgeText.text = "listening, not reachable yet";
                badgeText.color = ExplorerUI.Warn;
                onionText.text = "waiting for Tor to publish the address";
                onionText.color = ExplorerUI.Warn;
            }
            else
            {
                badgeText.text = "reachable over Tor";
                badgeText.color = ExplorerUI.Good;
                onionText.text = onion;
                onionText.color = ExplorerUI.Good;
            }

            if (running && !string.IsNullOrEmpty(onion) && !announced)
            {
                announced = true;
                statusText.text = "the hub is reachable at the address above";
                statusText.color = ExplorerUI.Good;
            }

            TorLauncher.State state = TorLauncher.Status;
            torLine.text = TorLauncher.Describe();
            torLine.color = state == TorLauncher.State.Ready
                ? ExplorerUI.Good
                : (state == TorLauncher.State.Failed ? ExplorerUI.Bad : ExplorerUI.Muted);

            float fraction = state == TorLauncher.State.Ready ? 1f : TorLauncher.Percent / 100f;
            torFill.rectTransform.anchorMax =
                new Vector2(fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction), 1f);

            figures[0].text = running ? server.Connections.ToString(CultureInfo.InvariantCulture) : "-";
            figures[1].text = running ? server.Served.ToString("N0", CultureInfo.InvariantCulture) : "-";
            figures[2].text = running ? server.Limited.ToString("N0", CultureInfo.InvariantCulture) : "-";
            figures[3].text = running ? server.Refused.ToString("N0", CultureInfo.InvariantCulture) : "-";
        }
    }
}
