using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public class MenuTile
    {
        public Button Button;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Detail;
    }

    public class MenuUI
    {
        public const string Prefab = "MenuCanvas";
        public const string TilePrefab = "MenuTile";

        public GameObject Root;
        public TextMeshProUGUI Blurb;
        public TMP_InputField Onion;
        public Button ConnectButton;
        public Button LocalButton;
        public Button DisconnectButton;
        public TextMeshProUGUI TorLine;
        public TextMeshProUGUI StatusLine;
        public Image TorBar;
        public RectTransform TileRow;
        public GameObject ConnectPanel;

        private readonly List<MenuTile> tiles = new List<MenuTile>();

        public IList<MenuTile> Tiles
        {
            get { return tiles; }
        }

        public MenuUI(Transform parent)
        {
            EnsureEventSystem();

            Root = UIPrefab.Instantiate(Prefab, parent);

            Blurb = UIPrefab.Bind<TextMeshProUGUI>(Root, "Header/Blurb");
            Blurb.text = "Read-only access to the Stealth chain over Tor, and the rest of " +
                "Stealth in one place.";

            ConnectPanel = UIPrefab.BindObject(Root, "Connect");
            Onion = UIPrefab.Bind<TMP_InputField>(Root, "Connect/Onion");
            ConnectButton = UIPrefab.Bind<Button>(Root, "Connect/ConnectButton");
            LocalButton = UIPrefab.Bind<Button>(Root, "Connect/LocalButton");
            DisconnectButton = UIPrefab.Bind<Button>(Root, "Connect/DisconnectButton");
            TorBar = UIPrefab.Bind<Image>(Root, "Connect/TorTrack/TorBar");
            TorLine = UIPrefab.Bind<TextMeshProUGUI>(Root, "Connect/TorLine");
            StatusLine = UIPrefab.Bind<TextMeshProUGUI>(Root, "Status");
            TileRow = UIPrefab.Bind<RectTransform>(Root, "Tiles");

            DisconnectButton.gameObject.SetActive(false);

            Image field = UIPrefab.Require<Image>(Onion.gameObject);
            Color resting = field.color;
            Color focused = new Color32(0x18, 0x20, 0x2A, 0xFF);
            Onion.onSelect.AddListener(delegate { field.color = focused; });
            Onion.onDeselect.AddListener(delegate { field.color = resting; });
        }

        public MenuTile AddTile(string title, string detail, Color accent)
        {
            GameObject tileObject = UIPrefab.Instantiate(TilePrefab, TileRow);

            var tile = new MenuTile
            {
                Button = UIPrefab.Require<Button>(tileObject),
                Title = UIPrefab.Bind<TextMeshProUGUI>(tileObject, "Title"),
                Detail = UIPrefab.Bind<TextMeshProUGUI>(tileObject, "Detail")
            };

            tile.Title.text = title;
            tile.Detail.text = detail;
            UIPrefab.Bind<Image>(tileObject, "Accent").color = accent;

            tiles.Add(tile);
            return tile;
        }

        public void SetTilesVisible(bool visible)
        {
            TileRow.gameObject.SetActive(visible);
        }

        public void SetConnected(bool connected)
        {
            ConnectButton.gameObject.SetActive(!connected);
            LocalButton.gameObject.SetActive(!connected);
            DisconnectButton.gameObject.SetActive(connected);
            Onion.interactable = !connected;
        }

        public void SetTor(float fraction, string text, Color tint)
        {
            float clamped = fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
            TorBar.rectTransform.anchorMax = new Vector2(clamped, 1f);
            TorBar.color = tint;
            TorLine.text = text;
            TorLine.color = tint;
        }

        public void SetStatus(string text, Color tint)
        {
            StatusLine.text = text;
            StatusLine.color = tint;
        }

        public void Show(bool visible)
        {
            Root.SetActive(visible);
        }

        public static Sprite LoadSprite(string resourceName)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourceName);
            if (texture == null)
            {
                return null;
            }

            return Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
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
