using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public enum ColumnNeeds
    {
        Nothing,
        Tor,
        Hub
    }

    public class HubColumn
    {
        public Button Button;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Kind;
        public TextMeshProUGUI Detail;
        public TextMeshProUGUI Footer;
        public Image Accent;
        public HoverGlow Glow;
        public ColumnNeeds Needs;
        public bool Available = true;

        private Graphic[] lifted;
        private Color accent;
        private Color titleInk;
        private Color detailInk;

        public void Remember(Graphic[] graphics)
        {
            lifted = graphics;
            accent = Accent.color;
            titleInk = Title.color;
            detailInk = Detail.color;
        }

        public void SetAvailable(bool available, string reason)
        {
            Available = available;
            Button.interactable = available;

            Title.color = available ? titleInk : ExplorerUI.Muted;
            Detail.color = available ? detailInk : ExplorerUI.Muted;
            Kind.color = available ? accent : ExplorerUI.Muted;
            Accent.color = available ? accent : ExplorerUI.Line;

            Footer.text = available || reason == null ? string.Empty : reason;
            Footer.color = available ? accent : ExplorerUI.Muted;

            if (Glow != null)
            {
                Glow.Adopt(lifted);
                Glow.enabled = available;
            }
        }
    }

    public class HubPage
    {
        public const string Prefab = "HubPageCanvas";
        public const string ColumnPrefab = "HubColumn";

        public GameObject Root;
        public TextMeshProUGUI SourceText;
        public TextMeshProUGUI NextBlockText;
        public TextMeshProUGUI HeightText;
        public TextMeshProUGUI StatusText;
        public Button BackButton;
        public TextMeshProUGUI BackLabel;

        private readonly List<HubColumn> columns = new List<HubColumn>();
        private readonly RectTransform grid;
        private readonly Color sourceInk;

        public IList<HubColumn> Columns
        {
            get { return columns; }
        }

        public HubPage(Transform parent)
        {
            Root = UIPrefab.Instantiate(Prefab, parent);

            SourceText = UIPrefab.Bind<TextMeshProUGUI>(Root, "Header/Source");
            NextBlockText = UIPrefab.Bind<TextMeshProUGUI>(Root, "Header/NextBlock");
            HeightText = UIPrefab.Bind<TextMeshProUGUI>(Root, "Header/Height");
            BackButton = UIPrefab.Bind<Button>(Root, "Header/BackButton");
            BackLabel = BackButton.GetComponentInChildren<TextMeshProUGUI>();
            StatusText = UIPrefab.Bind<TextMeshProUGUI>(Root, "Status");
            grid = UIPrefab.Bind<RectTransform>(Root, "Columns");

            sourceInk = SourceText.color;

            UIPrefab.Bind<TextMeshProUGUI>(Root, "Header/Title").text = ExplorerUI.Name;
            UIPrefab.Bind<TextMeshProUGUI>(Root, "Version").text =
                ExplorerUI.Name + " " + Hub.Version;
        }

        public HubColumn AddColumn(string title, string kind, string detail, Color accent,
            ColumnNeeds needs)
        {
            GameObject columnObject = UIPrefab.Instantiate(ColumnPrefab, grid);

            var column = new HubColumn
            {
                Button = UIPrefab.Require<Button>(columnObject),
                Title = UIPrefab.Bind<TextMeshProUGUI>(columnObject, "Title"),
                Kind = UIPrefab.Bind<TextMeshProUGUI>(columnObject, "Kind"),
                Detail = UIPrefab.Bind<TextMeshProUGUI>(columnObject, "Detail"),
                Footer = UIPrefab.Bind<TextMeshProUGUI>(columnObject, "Footer"),
                Accent = UIPrefab.Bind<Image>(columnObject, "Accent"),
                Needs = needs
            };

            column.Title.text = title;
            column.Kind.text = kind;
            column.Kind.color = accent;
            column.Detail.text = detail;
            column.Footer.text = string.Empty;
            column.Footer.color = accent;
            column.Accent.color = accent;

            var lifted = new Graphic[]
            {
                UIPrefab.Require<Image>(columnObject), column.Accent, column.Title
            };

            column.Glow = HoverGlow.Attach(columnObject, HoverGlow.DefaultLift, lifted);
            column.Remember(lifted);

            columns.Add(column);
            return column;
        }

        public void SetSource(string text)
        {
            SetSource(text, sourceInk);
        }

        public void SetSource(string text, Color tint)
        {
            SourceText.text = text;
            SourceText.color = tint;
        }

        public void SetBackLabel(string text)
        {
            if (BackLabel != null)
            {
                BackLabel.text = text;
            }
        }

        public void SetHeight(long height)
        {
            HeightText.text = height <= 0
                ? "height  -"
                : "height  " + height.ToString("N0", CultureInfo.InvariantCulture);
        }

        public void SetNextBlock(float seconds, bool overdue)
        {
            if (seconds < 0f)
            {
                NextBlockText.text = "next block  -";
                NextBlockText.color = ExplorerUI.Muted;
                return;
            }

            NextBlockText.text = overdue
                ? "next block  due"
                : "next block  " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            NextBlockText.color = overdue ? ExplorerUI.Warn : ExplorerUI.Good;
        }

        public void SetStatus(string text, Color tint)
        {
            StatusText.text = text;
            StatusText.color = tint;
        }

        public void Show(bool visible)
        {
            Root.SetActive(visible);
        }
    }
}
