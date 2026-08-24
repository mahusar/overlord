using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace Overlord.Explorer
{
    public class WavePanel
    {
        public const string Prefab = "WavePanel";

        private readonly TextMeshProUGUI caption;
        private readonly TextMeshProUGUI reading;
        private readonly TextMeshProUGUI high;
        private readonly TextMeshProUGUI low;
        private readonly TextMeshProUGUI empty;
        private readonly WaveGraph wave;

        public static WavePanel Into(Transform parent)
        {
            return new WavePanel(UIPrefab.Instantiate(Prefab, parent));
        }

        public WavePanel(GameObject frame)
        {
            wave = UIPrefab.Bind<WaveGraph>(frame, "Graph");
            caption = UIPrefab.Bind<TextMeshProUGUI>(frame, "Caption");
            reading = UIPrefab.Bind<TextMeshProUGUI>(frame, "Reading");
            high = UIPrefab.Bind<TextMeshProUGUI>(frame, "High");
            low = UIPrefab.Bind<TextMeshProUGUI>(frame, "Low");
            empty = UIPrefab.Bind<TextMeshProUGUI>(frame, "Empty");

            reading.text = string.Empty;
            high.text = string.Empty;
            low.text = string.Empty;
            empty.text = string.Empty;
        }

        public void SetTitle(string text)
        {
            caption.text = text;
        }

        public void Draw(IList<double> values, string unit, bool zeroBased, int decimals)
        {
            int count = values == null ? 0 : values.Count;

            if (count == 0)
            {
                wave.SetValues(null, zeroBased);
                reading.text = string.Empty;
                high.text = string.Empty;
                low.text = string.Empty;
                empty.text = "no samples yet";
                return;
            }

            bool allZero = true;
            for (int i = 0; i < count; i++)
            {
                if (values[i] != 0d)
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
            {
                wave.SetValues(null, zeroBased);
                reading.text = "0";
                high.text = string.Empty;
                low.text = string.Empty;
                empty.text = "nothing across " + count.ToString(CultureInfo.InvariantCulture) +
                    (count == 1 ? " sample" : " samples");
                return;
            }

            empty.text = string.Empty;
            wave.SetValues(values, zeroBased);

            double top = double.MinValue;
            double bottom = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (values[i] > top) top = values[i];
                if (values[i] < bottom) bottom = values[i];
            }

            string suffix = string.IsNullOrEmpty(unit) ? string.Empty : " " + unit;
            reading.text = Format(values[count - 1], decimals) + suffix;
            high.text = Format(top, decimals);
            low.text = Format(bottom, decimals);
        }

        private static string Format(double value, int decimals)
        {
            if (decimals > 0)
            {
                return value.ToString("N" + decimals, CultureInfo.InvariantCulture);
            }

            return Math.Abs(value) >= 1000d
                ? value.ToString("N0", CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
