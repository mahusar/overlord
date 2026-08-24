using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public class HoverGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const float DefaultLift = 0.16f;

        public float Speed = 9f;
        public float Lift = DefaultLift;

        private Graphic[] targets;
        private Color[] resting;
        private float amount;
        private float wanted;

        public static HoverGlow Attach(GameObject target, float lift, params Graphic[] graphics)
        {
            if (target == null)
            {
                return null;
            }

            HoverGlow glow = target.GetComponent<HoverGlow>();
            if (glow == null)
            {
                glow = target.AddComponent<HoverGlow>();
            }

            glow.Lift = lift;
            glow.Adopt(graphics);
            return glow;
        }

        public void Adopt(Graphic[] graphics)
        {
            targets = graphics;
            resting = new Color[graphics == null ? 0 : graphics.Length];

            for (int i = 0; i < resting.Length; i++)
            {
                resting[i] = graphics[i] == null ? Color.clear : graphics[i].color;
            }

            amount = 0f;
            wanted = 0f;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            wanted = 1f;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            wanted = 0f;
        }

        private void OnDisable()
        {
            amount = 0f;
            wanted = 0f;
            Apply();
        }

        private void Update()
        {
            if (amount == wanted)
            {
                return;
            }

            amount = Mathf.MoveTowards(amount, wanted, Time.unscaledDeltaTime * Speed);
            Apply();
        }

        private void Apply()
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                {
                    continue;
                }

                Color rest = resting[i];
                float lift = Lift * amount;

                targets[i].color = new Color(
                    Mathf.Clamp01(rest.r + lift),
                    Mathf.Clamp01(rest.g + lift),
                    Mathf.Clamp01(rest.b + lift),
                    rest.a);
            }
        }
    }
}
