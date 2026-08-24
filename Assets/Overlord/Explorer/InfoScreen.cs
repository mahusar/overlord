using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public class InfoScreen
    {
        public const string Prefab = "InfoCanvas";

        private readonly GameObject root;
        private readonly RectTransform column;
        private readonly TextMeshProUGUI titleText;
        private readonly TextMeshProUGUI statusText;

        public InfoScreen(Transform parent, string title, Action onBack)
        {
            root = UIPrefab.Instantiate(Prefab, parent);

            titleText = UIPrefab.Bind<TextMeshProUGUI>(root, "Header/Title");
            statusText = UIPrefab.Bind<TextMeshProUGUI>(root, "Status");
            column = UIPrefab.Bind<RectTransform>(root, "Scroll/Viewport/Content");

            titleText.text = title;
            statusText.text = string.Empty;

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

        public void AddLink(string label, string url, string note)
        {
            InfoRows.AddLink(column, label, url, note);
        }

        public void AddSpace(float height)
        {
            InfoRows.AddSpace(column, height);
        }
    }
}
