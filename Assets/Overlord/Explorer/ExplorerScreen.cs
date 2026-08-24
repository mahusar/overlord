using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Overlord.Explorer
{
    public class ExplorerScreen
    {
        private readonly ExplorerUI ui;
        private readonly ExplorerClient client;
        private readonly Action onMenu;
        private bool busy;

        public ExplorerScreen(Transform parent, ExplorerClient client, Action onMenu)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            this.client = client;
            this.onMenu = onMenu;

            ui = new ExplorerUI(parent);
            ui.SearchButton.onClick.AddListener(OnSearch);
            ui.InfoButton.onClick.AddListener(OnInfo);
            ui.PeersButton.onClick.AddListener(OnPeers);
            ui.Search.onSubmit.AddListener(delegate { OnSearch(); });

            if (onMenu == null)
            {
                ui.MenuButton.gameObject.SetActive(false);
            }
            else
            {
                ui.MenuButton.onClick.AddListener(delegate { onMenu(); });
            }
        }

        public GameObject Root
        {
            get { return ui.Root; }
        }

        public ExplorerUI View
        {
            get { return ui; }
        }

        public void Show(bool visible)
        {
            ui.Root.SetActive(visible);
        }

        public void OnInfo()
        {
            Info();
        }

        private async void Info()
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

        private void OnSearch()
        {
            Run(ui.Search.text);
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
                Info();
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

            busy = true;
            ui.SetStatus("asking " + client.SourceCount +
                (client.SourceCount == 1 ? " source about " : " sources about ") + what,
                ExplorerUI.Muted);
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
    }
}
