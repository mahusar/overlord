using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public class RichListScreen
    {
        public const string Prefab = "RichListCanvas";

        public static readonly int[] Buckets = { 10, 20, 50, 100, 250, 500, 1000 };
        public static readonly Color RichBlue = new Color32(0x4A, 0x9E, 0xE0, 0xFF);

        private readonly ExplorerClient client;

        private readonly GameObject root;
        private readonly TextMeshProUGUI badgeText;
        private readonly TextMeshProUGUI statusText;
        private readonly TextMeshProUGUI countText;
        private readonly TextMeshProUGUI summaryText;
        private readonly Slider slider;

        private readonly TextMeshProUGUI rankColumn;
        private readonly TextMeshProUGUI addressColumn;
        private readonly TextMeshProUGUI balanceColumn;
        private readonly TextMeshProUGUI shareColumn;
        private ScrollRect scroll;
        private RectTransform listContent;

        private readonly List<Image> bucketFills = new List<Image>();
        private readonly List<TextMeshProUGUI> bucketLabels = new List<TextMeshProUGUI>();
        private readonly List<TextMeshProUGUI> bucketValues = new List<TextMeshProUGUI>();
        private readonly WavePanel curve;

        private decimal supply;
        private decimal minted;
        private bool busy;

        public RichListScreen(Transform parent, ExplorerClient client, Action onMenu, Action onBack)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            this.client = client;

            root = UIPrefab.Instantiate(Prefab, parent);

            badgeText = UIPrefab.Bind<TextMeshProUGUI>(root, "Header/Badge");
            statusText = UIPrefab.Bind<TextMeshProUGUI>(root, "Status");
            countText = UIPrefab.Bind<TextMeshProUGUI>(root, "Controls/Count");
            summaryText = UIPrefab.Bind<TextMeshProUGUI>(root, "Controls/Summary");

            slider = UIPrefab.Bind<Slider>(root, "Controls/Slider");
            slider.minValue = 20f;
            slider.maxValue = HubHandlers.MaxRichListCount;
            slider.wholeNumbers = true;

            Button menu = UIPrefab.Bind<Button>(root, "Header/MenuButton");
            if (onMenu != null)
            {
                menu.onClick.AddListener(delegate { onMenu(); });
            }

            Button back = UIPrefab.Bind<Button>(root, "Header/BackButton");
            if (onBack == null)
            {
                back.gameObject.SetActive(false);
            }
            else
            {
                back.onClick.AddListener(delegate { onBack(); });
            }

            UIPrefab.Bind<Button>(root, "Controls/LoadButton").onClick.AddListener(
                delegate { Load((int)slider.value); });

            slider.onValueChanged.AddListener(delegate(float value)
            {
                countText.text = ((int)value).ToString(CultureInfo.InvariantCulture);
            });

            scroll = UIPrefab.Bind<ScrollRect>(root, "List/Scroll");
            listContent = UIPrefab.Bind<RectTransform>(root, "List/Scroll/Viewport/Content");

            rankColumn = UIPrefab.Bind<TextMeshProUGUI>(root, "List/Scroll/Viewport/Content/Rank");
            addressColumn = UIPrefab.Bind<TextMeshProUGUI>(root, "List/Scroll/Viewport/Content/Address");
            balanceColumn = UIPrefab.Bind<TextMeshProUGUI>(root, "List/Scroll/Viewport/Content/Balance");
            shareColumn = UIPrefab.Bind<TextMeshProUGUI>(root, "List/Scroll/Viewport/Content/Share");

            ClickToCopy.Attach(addressColumn, Copied);

            TextMeshProUGUI heads = UIPrefab.Bind<TextMeshProUGUI>(root, "List/Heads");
            heads.text = heads.text + "        click an address to copy it";

            BindBuckets();

            curve = new WavePanel(UIPrefab.BindObject(root, "Concentration/Column/Curve"));
        }

        public GameObject Root
        {
            get { return root; }
        }

        public void Show(bool visible)
        {
            root.SetActive(visible);
        }

        public void Open()
        {
            if (rankColumn.text.Length == 0)
            {
                Load((int)slider.value);
            }
        }

        public async void Load(int count)
        {
            if (busy)
            {
                return;
            }

            busy = true;
            statusText.text = "asking for the top " + count + " addresses ...";
            statusText.color = ExplorerUI.Muted;

            Verdict verdict = await client.RichListAsync(1, count);
            busy = false;

            badgeText.text = verdict == null ? "" : verdict.Badge;
            badgeText.color = verdict != null && verdict.Unanimous
                ? (verdict.Answered > 1 ? ExplorerUI.Good : ExplorerUI.Muted)
                : ExplorerUI.Warn;

            if (verdict == null || !verdict.HasResult)
            {
                statusText.text = "rich list failed: " +
                    (verdict == null ? "no answer" : verdict.Error);
                statusText.color = ExplorerUI.Bad;
                return;
            }

            JObject result = verdict.Result as JObject;
            JArray rows = result == null ? null : result["rows"] as JArray;

            supply = 0m;
            if (result != null && result["total"] != null)
            {
                try
                {
                    supply = result["total"].Value<decimal>();
                }
                catch (Exception)
                {
                    supply = 0m;
                }
            }

            long held = result == null || result["addresses"] == null
                ? -1
                : result.Value<long>("addresses");

            minted = 0m;
            if (result != null && result["moneysupply"] != null)
            {
                try
                {
                    minted = result["moneysupply"].Value<decimal>();
                }
                catch (Exception)
                {
                    minted = 0m;
                }
            }

            string line = (held < 0 ? "?" : held.ToString("N0", CultureInfo.InvariantCulture)) +
                " addresses hold " + supply.ToString("N0", CultureInfo.InvariantCulture) + " XST";

            if (minted > 0m)
            {
                line += "    |    money supply " + minted.ToString("N0", CultureInfo.InvariantCulture) + " XST";
            }

            summaryText.text = line;

            UIPrefab.Bind<TextMeshProUGUI>(root, "Concentration/Column/Heading").text =
                supply > 0m
                    ? "share of the " + supply.ToString("N0", CultureInfo.InvariantCulture) +
                      " XST held in addresses"
                    : "share of all XST held in addresses";

            if (rows == null || rows.Count == 0)
            {
                statusText.text = "the rich list came back empty";
                statusText.color = ExplorerUI.Warn;
                return;
            }

            Render(rows);

            statusText.text = "top " + rows.Count + " of " +
                (supply > 0m ? supply.ToString("N0", CultureInfo.InvariantCulture) + " XST held" : "the held supply") +
                ", " + verdict.Badge;
            statusText.color = verdict.Unanimous ? ExplorerUI.Good : ExplorerUI.Warn;
        }

        private void Render(JArray rows)
        {
            var ranks = new StringBuilder();
            var addresses = new StringBuilder();
            var balances = new StringBuilder();
            var shares = new StringBuilder();

            var cumulative = new List<double>();
            decimal running = 0m;

            for (int i = 0; i < rows.Count; i++)
            {
                JObject row = rows[i] as JObject;
                if (row == null)
                {
                    continue;
                }

                decimal balance;
                try
                {
                    balance = row["balance"].Value<decimal>();
                }
                catch (Exception)
                {
                    balance = 0m;
                }

                running += balance;

                ranks.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
                addresses.Append(row.Value<string>("address") ?? "-").Append('\n');
                balances.Append(balance.ToString("N6", CultureInfo.InvariantCulture)).Append('\n');
                shares.Append(Percent(balance)).Append('\n');

                cumulative.Add(Share(running));
            }

            rankColumn.text = ranks.ToString();
            addressColumn.text = addresses.ToString();
            balanceColumn.text = balances.ToString();
            shareColumn.text = shares.ToString();

            float lineHeight = 19f;
            float wanted = rows.Count * lineHeight + 20f;
            listContent.sizeDelta = new Vector2(0f, wanted);

            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;

            for (int i = 0; i < Buckets.Length; i++)
            {
                int bucket = Buckets[i];
                bool covered = bucket <= cumulative.Count;
                double value = covered ? cumulative[bucket - 1] : 0d;

                bucketLabels[i].color = covered ? ExplorerUI.Muted : ExplorerUI.Line;
                bucketValues[i].text = covered
                    ? value.ToString("0.0", CultureInfo.InvariantCulture) + " %"
                    : "-";
                bucketValues[i].color = covered ? ExplorerUI.Ink : ExplorerUI.Line;

                float fraction = covered ? (float)(value / 100d) : 0f;
                if (fraction > 1f) fraction = 1f;
                bucketFills[i].rectTransform.anchorMax = new Vector2(fraction, 1f);
            }

            curve.SetTitle("cumulative share held, top " + cumulative.Count);
            curve.Draw(cumulative, "%", true, 1);
        }

        private double Share(decimal amount)
        {
            if (supply <= 0m)
            {
                return 0d;
            }

            return (double)(amount / supply) * 100d;
        }

        private string Percent(decimal amount)
        {
            if (supply <= 0m)
            {
                return "-";
            }

            double value = Share(amount);
            return value < 0.01d
                ? "<0.01 %"
                : value.ToString("0.00", CultureInfo.InvariantCulture) + " %";
        }

        private void Copied(string address)
        {
            statusText.text = "copied " + address + " to the clipboard";
            statusText.color = ExplorerUI.Good;
        }

        private void BindBuckets()
        {
            for (int i = 0; i < Buckets.Length; i++)
            {
                string path = "Concentration/Column/Bucket" + i.ToString(CultureInfo.InvariantCulture);

                TextMeshProUGUI name = UIPrefab.Bind<TextMeshProUGUI>(root, path + "/Name");
                name.text = "top " + Buckets[i].ToString(CultureInfo.InvariantCulture);

                bucketFills.Add(UIPrefab.Bind<Image>(root, path + "/Track/Fill"));
                bucketLabels.Add(name);
                bucketValues.Add(UIPrefab.Bind<TextMeshProUGUI>(root, path + "/Value"));
            }
        }
    }
}
