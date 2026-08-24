using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace Overlord.Explorer
{
    public class ExplorerUI
    {
        public const string Product = "StealthHub";
        public const string Name = "Overlord";
        public const string Lockup = Product + " - " + Name;

        public const string Prefab = "ExplorerCanvas";
        public const string HeadingPrefab = "ExplorerHeading";
        public const string NotePrefab = "ExplorerNote";
        public const string RowPrefab = "ExplorerRow";
        public const string CellRowPrefab = "ExplorerCellRow";
        public const string CellPrefab = "ExplorerCell";
        public const string SeparatorPrefab = "ExplorerSeparator";

        public static readonly Color Background = new Color32(0x12, 0x15, 0x1A, 0xFF);
        public static readonly Color Panel = new Color32(0x1A, 0x1F, 0x27, 0xFF);
        public static readonly Color Field = new Color32(0x0D, 0x10, 0x14, 0xFF);
        public static readonly Color Line = new Color32(0x25, 0x2C, 0x36, 0xFF);
        public static readonly Color Ink = new Color32(0xD7, 0xDE, 0xE8, 0xFF);
        public static readonly Color Muted = new Color32(0x7C, 0x87, 0x98, 0xFF);
        public static readonly Color Good = new Color32(0x4E, 0xC9, 0xA8, 0xFF);
        public static readonly Color Warn = new Color32(0xE8, 0xB8, 0x4B, 0xFF);
        public static readonly Color Bad = new Color32(0xE0, 0x6C, 0x75, 0xFF);
        public static readonly Color ButtonFace = new Color32(0x2A, 0x33, 0x40, 0xFF);

        public TMP_InputField Search;
        public Button SearchButton;
        public Button InfoButton;
        public Button PeersButton;
        public TextMeshProUGUI HeightText;
        public TextMeshProUGUI BadgeText;
        public TextMeshProUGUI StatusText;
        public RectTransform Content;
        public ScrollRect Scroll;
        public Button MenuButton;
        public Button HoldersButton;
        public GameObject Root;

        public ExplorerUI(Transform parent)
        {
            EnsureEventSystem();

            Root = UIPrefab.Instantiate(Prefab, parent);

            HeightText = UIPrefab.Bind<TextMeshProUGUI>(Root, "Header/Height");
            BadgeText = UIPrefab.Bind<TextMeshProUGUI>(Root, "Header/Badge");
            Search = UIPrefab.Bind<TMP_InputField>(Root, "SearchBar/Search");
            SearchButton = UIPrefab.Bind<Button>(Root, "SearchBar/SearchButton");
            InfoButton = UIPrefab.Bind<Button>(Root, "SearchBar/InfoButton");
            HoldersButton = UIPrefab.Bind<Button>(Root, "SearchBar/HoldersButton");
            PeersButton = UIPrefab.Bind<Button>(Root, "SearchBar/PeersButton");
            MenuButton = UIPrefab.Bind<Button>(Root, "SearchBar/MenuButton");
            Scroll = UIPrefab.Bind<ScrollRect>(Root, "Body/Scroll");
            Content = UIPrefab.Bind<RectTransform>(Root, "Body/Scroll/Viewport/Content");
            StatusText = UIPrefab.Bind<TextMeshProUGUI>(Root, "Status");
        }

        public void Clear()
        {
            for (int i = Content.childCount - 1; i >= 0; i--)
            {
                Transform child = Content.GetChild(i);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        public void ScrollToTop()
        {
            Canvas.ForceUpdateCanvases();
            Scroll.verticalNormalizedPosition = 1f;
        }

        public void AddHeading(string text)
        {
            GameObject heading = UIPrefab.Instantiate(HeadingPrefab, Content);
            UIPrefab.Require<TextMeshProUGUI>(heading).text = text;
        }

        public void AddNote(string text, Color color)
        {
            GameObject note = UIPrefab.Instantiate(NotePrefab, Content);
            TextMeshProUGUI label = UIPrefab.Require<TextMeshProUGUI>(note);
            label.text = text;
            label.color = color;
        }

        public void AddRow(string key, string value)
        {
            GameObject row = UIPrefab.Instantiate(RowPrefab, Content);
            UIPrefab.Bind<TextMeshProUGUI>(row, "Key").text = key;
            UIPrefab.Bind<TextMeshProUGUI>(row, "Value").text = value;
        }

        public void AddColumns(string[] cells, float[] widths, Color color)
        {
            GameObject row = UIPrefab.Instantiate(CellRowPrefab, Content);

            for (int i = 0; i < cells.Length; i++)
            {
                GameObject cellObject = UIPrefab.Instantiate(CellPrefab, row.transform);

                TextMeshProUGUI cell = UIPrefab.Require<TextMeshProUGUI>(cellObject);
                cell.text = cells[i];
                cell.color = i == 0 ? Muted : color;

                LayoutElement element = UIPrefab.Require<LayoutElement>(cellObject);
                if (i < widths.Length && widths[i] > 0f)
                {
                    element.preferredWidth = widths[i];
                    element.flexibleWidth = 0f;
                }
                else
                {
                    element.preferredWidth = -1f;
                    element.flexibleWidth = 1f;
                }
            }
        }

        public void AddSeparator()
        {
            UIPrefab.Instantiate(SeparatorPrefab, Content);
        }

        public void AddSpace(float height)
        {
            var spacer = new GameObject("Space", typeof(RectTransform));
            spacer.transform.SetParent(Content, false);

            var element = spacer.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        public void SetBadge(Verdict verdict)
        {
            if (verdict == null)
            {
                BadgeText.text = "no sources";
                BadgeText.color = Muted;
                return;
            }

            BadgeText.text = verdict.Badge;

            if (!verdict.HasResult)
            {
                BadgeText.color = Bad;
            }
            else if (verdict.Unanimous)
            {
                BadgeText.color = verdict.Answered > 1 ? Good : Muted;
            }
            else
            {
                BadgeText.color = Warn;
            }
        }

        public void SetStatus(string text, Color color)
        {
            StatusText.text = text;
            StatusText.color = color;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (existing != null)
            {
                return;
            }

            var systemObject = new GameObject("EventSystem",
                typeof(EventSystem), typeof(StandaloneInputModule));

            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(systemObject);
            }
        }
    }
}
