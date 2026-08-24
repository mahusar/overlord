using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Overlord.Explorer
{
    public class WaveGraph : MaskableGraphic
    {
        public float LineThickness = 2f;
        public int SmoothingSteps = 8;
        public int GridRows = 3;
        public Color LineColour = new Color32(0x4E, 0xC9, 0xA8, 0xFF);
        public Color FillTop = new Color32(0x4E, 0xC9, 0xA8, 0x66);
        public Color FillBottom = new Color32(0x4E, 0xC9, 0xA8, 0x00);
        public Color GridColour = new Color32(0x25, 0x2C, 0x36, 0xFF);

        private readonly List<double> values = new List<double>();
        private double floor;
        private double ceiling = 1d;
        private bool hasRange;

        public int Count
        {
            get { return values.Count; }
        }

        public double Floor
        {
            get { return floor; }
        }

        public double Ceiling
        {
            get { return ceiling; }
        }

        public void SetValues(IList<double> incoming, bool zeroBased)
        {
            values.Clear();
            if (incoming != null)
            {
                for (int i = 0; i < incoming.Count; i++)
                {
                    values.Add(incoming[i]);
                }
            }

            hasRange = values.Count > 0;

            if (!hasRange)
            {
                floor = 0d;
                ceiling = 1d;
                SetVerticesDirty();
                return;
            }

            double top = double.MinValue;
            double bottom = double.MaxValue;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] > top) top = values[i];
                if (values[i] < bottom) bottom = values[i];
            }

            if (zeroBased && bottom > 0d)
            {
                bottom = 0d;
            }

            if (top - bottom <= 0d)
            {
                double pad = top == 0d ? 1d : System.Math.Abs(top) * 0.05d;
                top += pad;
                bottom -= pad;
            }
            else
            {
                double headroom = (top - bottom) * 0.08d;
                top += headroom;
                bottom -= headroom;
            }

            floor = bottom;
            ceiling = top;
            SetVerticesDirty();
        }

        public void SetColour(Color line, Color fill)
        {
            LineColour = line;
            FillTop = new Color(fill.r, fill.g, fill.b, 0.40f);
            FillBottom = new Color(fill.r, fill.g, fill.b, 0f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            Rect area = GetPixelAdjustedRect();

            for (int row = 0; row <= GridRows; row++)
            {
                float t = GridRows == 0 ? 0f : row / (float)GridRows;
                float y = area.yMin + area.height * t;
                Quad(helper,
                    new Vector2(area.xMin, y - 0.5f),
                    new Vector2(area.xMax, y + 0.5f),
                    GridColour, GridColour);
            }

            if (values.Count == 0)
            {
                return;
            }

            List<Vector2> curve = BuildCurve(area);

            for (int i = 0; i < curve.Count - 1; i++)
            {
                Vector2 a = curve[i];
                Vector2 b = curve[i + 1];

                int start = helper.currentVertCount;
                helper.AddVert(new Vector3(a.x, area.yMin), FillBottom, Vector2.zero);
                helper.AddVert(new Vector3(a.x, a.y), FillTop, Vector2.zero);
                helper.AddVert(new Vector3(b.x, b.y), FillTop, Vector2.zero);
                helper.AddVert(new Vector3(b.x, area.yMin), FillBottom, Vector2.zero);
                helper.AddTriangle(start, start + 1, start + 2);
                helper.AddTriangle(start + 2, start + 3, start);
            }

            float half = LineThickness * 0.5f;
            for (int i = 0; i < curve.Count - 1; i++)
            {
                Vector2 a = curve[i];
                Vector2 b = curve[i + 1];
                Vector2 direction = b - a;
                if (direction.sqrMagnitude < 0.000001f)
                {
                    continue;
                }

                Vector2 normal = new Vector2(-direction.y, direction.x).normalized * half;

                int start = helper.currentVertCount;
                helper.AddVert(new Vector3(a.x + normal.x, a.y + normal.y), LineColour, Vector2.zero);
                helper.AddVert(new Vector3(b.x + normal.x, b.y + normal.y), LineColour, Vector2.zero);
                helper.AddVert(new Vector3(b.x - normal.x, b.y - normal.y), LineColour, Vector2.zero);
                helper.AddVert(new Vector3(a.x - normal.x, a.y - normal.y), LineColour, Vector2.zero);
                helper.AddTriangle(start, start + 1, start + 2);
                helper.AddTriangle(start + 2, start + 3, start);
            }

            Vector2 last = curve[curve.Count - 1];
            float dot = LineThickness * 2f;
            Quad(helper,
                new Vector2(last.x - dot, last.y - dot),
                new Vector2(last.x + dot, last.y + dot),
                LineColour, LineColour);
        }

        private List<Vector2> BuildCurve(Rect area)
        {
            var anchors = new List<Vector2>();
            double span = ceiling - floor;
            if (span <= 0d) span = 1d;

            float step = values.Count == 1 ? 0f : area.width / (values.Count - 1);

            for (int i = 0; i < values.Count; i++)
            {
                double normalised = (values[i] - floor) / span;
                if (normalised < 0d) normalised = 0d;
                if (normalised > 1d) normalised = 1d;

                anchors.Add(new Vector2(
                    area.xMin + step * i,
                    area.yMin + (float)normalised * area.height));
            }

            if (anchors.Count < 3 || SmoothingSteps <= 1)
            {
                if (anchors.Count == 1)
                {
                    anchors.Add(new Vector2(area.xMax, anchors[0].y));
                }
                return anchors;
            }

            var smooth = new List<Vector2>();
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                Vector2 p0 = anchors[i == 0 ? 0 : i - 1];
                Vector2 p1 = anchors[i];
                Vector2 p2 = anchors[i + 1];
                Vector2 p3 = anchors[i + 2 >= anchors.Count ? anchors.Count - 1 : i + 2];

                for (int s = 0; s < SmoothingSteps; s++)
                {
                    float t = s / (float)SmoothingSteps;
                    smooth.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }

            smooth.Add(anchors[anchors.Count - 1]);
            return smooth;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * ((2f * p1) +
                           (-p0 + p2) * t +
                           (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static void Quad(VertexHelper helper, Vector2 min, Vector2 max,
                                 Color bottom, Color top)
        {
            int start = helper.currentVertCount;
            helper.AddVert(new Vector3(min.x, min.y), bottom, Vector2.zero);
            helper.AddVert(new Vector3(min.x, max.y), top, Vector2.zero);
            helper.AddVert(new Vector3(max.x, max.y), top, Vector2.zero);
            helper.AddVert(new Vector3(max.x, min.y), bottom, Vector2.zero);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start + 2, start + 3, start);
        }
    }
}
