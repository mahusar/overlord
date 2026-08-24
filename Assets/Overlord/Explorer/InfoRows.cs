using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Overlord.Explorer
{
    public static class InfoRows
    {
        public const string HeadingPrefab = "InfoHeading";
        public const string NotePrefab = "InfoNote";
        public const string RowPrefab = "InfoRow";
        public const string LinkPrefab = "InfoLink";
        public const string ButtonRowPrefab = "InfoButtonRow";
        public const string ButtonPrefab = "InfoButton";

        public static void Clear(RectTransform column)
        {
            for (int i = column.childCount - 1; i >= 0; i--)
            {
                Transform child = column.GetChild(i);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        public static void AddHeading(RectTransform column, string text)
        {
            GameObject heading = UIPrefab.Instantiate(HeadingPrefab, column);
            UIPrefab.Require<TextMeshProUGUI>(heading).text = text;
        }

        public static void AddNote(RectTransform column, string text, Color tint)
        {
            GameObject note = UIPrefab.Instantiate(NotePrefab, column);
            TextMeshProUGUI label = UIPrefab.Require<TextMeshProUGUI>(note);
            label.text = text;
            label.color = tint;
        }

        public static void AddRow(RectTransform column, string key, string value)
        {
            GameObject row = UIPrefab.Instantiate(RowPrefab, column);
            UIPrefab.Bind<TextMeshProUGUI>(row, "Key").text = key;
            UIPrefab.Bind<TextMeshProUGUI>(row, "Value").text = value;
        }

        public static void AddLink(RectTransform column, string label, string url, string note)
        {
            GameObject row = UIPrefab.Instantiate(LinkPrefab, column);

            UIPrefab.Bind<TextMeshProUGUI>(row, "Key").text = label;
            UIPrefab.Bind<TextMeshProUGUI>(row, "Url").text =
                string.IsNullOrEmpty(note) ? url : url + "   " + note;

            string target = url;
            UIPrefab.Bind<Button>(row, "Open").onClick.AddListener(delegate
            {
                if (!string.IsNullOrEmpty(target))
                {
                    Application.OpenURL(target);
                }
            });
        }

        public static List<Button> AddButtons(RectTransform column, string[] captions)
        {
            GameObject row = UIPrefab.Instantiate(ButtonRowPrefab, column);
            var buttons = new List<Button>();

            for (int i = 0; i < captions.Length; i++)
            {
                GameObject item = UIPrefab.Instantiate(ButtonPrefab, row.transform);
                UIPrefab.Bind<TextMeshProUGUI>(item, "Label").text = captions[i];
                buttons.Add(UIPrefab.Require<Button>(item));
            }

            return buttons;
        }

        public static void AddSpace(RectTransform column, float height)
        {
            var spacer = new GameObject("Space", typeof(RectTransform));
            spacer.transform.SetParent(column, false);
            var element = spacer.AddComponent<LayoutElement>();
            element.preferredHeight = height;
        }
    }
}
