using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public class StakersScreen
    {
        public const string Prefab = "StakersCanvas";
        public const int ComingUpRows = 12;

        private readonly GameObject root;
        private readonly RectTransform column;
        private readonly TextMeshProUGUI badgeText;
        private readonly TextMeshProUGUI statusText;
        private readonly TextMeshProUGUI queueText;
        private readonly Image queueFill;

        private readonly TextMeshProUGUI[] slotAlias = new TextMeshProUGUI[3];
        private readonly TextMeshProUGUI[] slotHeight = new TextMeshProUGUI[3];
        private readonly TextMeshProUGUI[] comingAlias = new TextMeshProUGUI[ComingUpRows];

        private string latestAlias;
        private string previousAlias;
        private long latestHeight = -1;
        private long previousHeight = -1;

        public StakersScreen(Transform parent, Action onBack)
        {
            root = UIPrefab.Instantiate(Prefab, parent);

            badgeText = UIPrefab.Bind<TextMeshProUGUI>(root, "Header/Badge");
            statusText = UIPrefab.Bind<TextMeshProUGUI>(root, "Status");
            column = UIPrefab.Bind<RectTransform>(root, "Info/Scroll/Viewport/Content");
            queueText = UIPrefab.Bind<TextMeshProUGUI>(root, "Rotation/Queue");
            queueFill = UIPrefab.Bind<Image>(root, "Rotation/Track/Fill");

            string[] slots = { "Previous", "Latest", "Next" };
            for (int i = 0; i < slots.Length; i++)
            {
                slotAlias[i] = UIPrefab.Bind<TextMeshProUGUI>(root, "Rotation/" + slots[i] + "/Alias");
                slotHeight[i] = UIPrefab.Bind<TextMeshProUGUI>(root, "Rotation/" + slots[i] + "/Height");
            }

            for (int i = 0; i < ComingUpRows; i++)
            {
                comingAlias[i] = UIPrefab.Bind<TextMeshProUGUI>(root,
                    "Rotation/Up" + i.ToString(CultureInfo.InvariantCulture) + "/Alias");
            }

            Button back = UIPrefab.Bind<Button>(root, "Header/BackButton");
            if (onBack != null)
            {
                back.onClick.AddListener(delegate { onBack(); });
            }
        }

        public GameObject Root
        {
            get { return root; }
        }

        public RectTransform Column
        {
            get { return column; }
        }

        public void Show(bool visible)
        {
            root.SetActive(visible);
        }

        public void SetStatus(string text, Color tint)
        {
            statusText.text = text;
            statusText.color = tint;
        }

        public void SetBadge(string text, Color tint)
        {
            badgeText.text = text == null ? string.Empty : text;
            badgeText.color = tint;
        }

        public void Clear()
        {
            InfoRows.Clear(column);
        }

        public void AddHeading(string text)
        {
            InfoRows.AddHeading(column, text);
        }

        public void AddNote(string text, Color tint)
        {
            InfoRows.AddNote(column, text, tint);
        }

        public void AddRow(string key, string value)
        {
            InfoRows.AddRow(column, key, value);
        }

        public void AddSpace(float height)
        {
            InfoRows.AddSpace(column, height);
        }

        public void Rotate(JObject summary)
        {
            if (summary == null)
            {
                return;
            }

            string latest = Text(summary, "latest_staker_alias");
            long height = Number(summary, "latest_block_height");

            JArray rotation = summary["remaining_queue_aliases"] as JArray;
            string next = rotation != null && rotation.Count > 1
                ? rotation[1].Value<string>()
                : Text(summary, "next_staker_alias");

            if (height > 0 && height != latestHeight)
            {
                if (latestHeight > 0)
                {
                    previousAlias = latestAlias;
                    previousHeight = latestHeight;
                }

                latestHeight = height;
                latestAlias = latest;
            }

            Slot(0, previousAlias, previousHeight);
            Slot(1, latest, height);
            Slot(2, next, height > 0 ? height + 1 : -1);

            long produced = Number(summary, "produced_queue");
            long remaining = Number(summary, "remaining_queue");
            long round = produced + remaining;

            queueText.text = round > 0
                ? "queue " + produced.ToString(CultureInfo.InvariantCulture) + "/" +
                  round.ToString(CultureInfo.InvariantCulture)
                : "queue -";

            float share = round > 0 ? (float)produced / round : 0f;
            queueFill.rectTransform.anchorMax = new Vector2(share < 0f ? 0f : (share > 1f ? 1f : share), 1f);

            for (int i = 0; i < ComingUpRows; i++)
            {
                int source = i + 2;
                comingAlias[i].text = rotation != null && source < rotation.Count
                    ? rotation[source].Value<string>()
                    : string.Empty;
            }
        }

        private void Slot(int index, string alias, long height)
        {
            slotAlias[index].text = string.IsNullOrEmpty(alias) ? "-" : alias;
            slotHeight[index].text = height > 0
                ? height.ToString("N0", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string Text(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return string.Empty;
            }
            return source.Value<string>(field);
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
    }
}
