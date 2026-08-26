using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public class WindowChrome
    {
        public const string Prefab = "ChromeCanvas";

        public GameObject Root;
        public TextMeshProUGUI Title;
        public Button MinimizeButton;
        public Button QuitButton;

        public WindowChrome(Transform parent)
        {
            Root = UIPrefab.Instantiate(Prefab, parent);

            Title = UIPrefab.Bind<TextMeshProUGUI>(Root, "Bar/Title");
            MinimizeButton = UIPrefab.Bind<Button>(Root, "Bar/MinimizeButton");
            QuitButton = UIPrefab.Bind<Button>(Root, "Bar/QuitButton");

            Title.text = ExplorerUI.Name;
        }

        public void Show(bool visible)
        {
            Root.SetActive(visible);
        }
    }
}
