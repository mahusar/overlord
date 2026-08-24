using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Overlord.Explorer
{
    public class ClickToCopy : MonoBehaviour, IPointerClickHandler
    {
        private TMP_Text text;
        private Action<string> copied;

        public static ClickToCopy Attach(TMP_Text target, Action<string> onCopied)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            target.raycastTarget = true;

            ClickToCopy hook = target.gameObject.GetComponent<ClickToCopy>();
            if (hook == null)
            {
                hook = target.gameObject.AddComponent<ClickToCopy>();
            }

            hook.text = target;
            hook.copied = onCopied;
            return hook;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (text == null || eventData == null || eventData.dragging)
            {
                return;
            }

            string value = LineAt(eventData.position);
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = value;

            if (copied != null)
            {
                copied(value);
            }
        }

        public string LineAt(Vector2 screenPoint)
        {
            int line = TMP_TextUtilities.FindIntersectingLine(text, screenPoint, null);
            if (line < 0 || line >= text.textInfo.lineCount)
            {
                return null;
            }

            TMP_LineInfo info = text.textInfo.lineInfo[line];
            int first = info.firstCharacterIndex;
            int last = info.lastCharacterIndex;

            if (first < 0 || last < first)
            {
                return null;
            }

            var value = new StringBuilder();
            for (int i = first; i <= last && i < text.textInfo.characterCount; i++)
            {
                value.Append(text.textInfo.characterInfo[i].character);
            }

            return value.ToString().Trim();
        }
    }
}
