using UnityEngine;
using UnityEngine.EventSystems;

namespace Overlord.Explorer
{
    public class WindowDrag : MonoBehaviour, IPointerDownHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            WindowFrame.BeginDrag();
        }
    }
}
