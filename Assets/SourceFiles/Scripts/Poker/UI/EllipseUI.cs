using UnityEngine;
using UnityEngine.UI;

namespace Poker.UI
{
    [ExecuteAlways]
    [AddComponentMenu("UI/Ellipse UI")]
    [RequireComponent(typeof(CanvasRenderer))]
    public class EllipseUI : MaskableGraphic
    {
        [Range(32, 128)]
        public int segments = 72;
        public bool useGradient;
        public Color centerColor = Color.white;
        public Color edgeColor   = Color.gray;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r  = rectTransform.rect;
            float cx = r.x + r.width  * 0.5f;
            float cy = r.y + r.height * 0.5f;
            float rx = r.width  * 0.5f;
            float ry = r.height * 0.5f;

            Color mid = useGradient ? centerColor : color;
            Color rim = useGradient ? edgeColor   : color;

            AddVert(vh, cx, cy, mid);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                AddVert(vh, cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, rim);
                if (i > 0) vh.AddTriangle(0, i, i + 1);
            }
        }

        static void AddVert(VertexHelper vh, float x, float y, Color c)
        {
            var v = UIVertex.simpleVert;
            v.color    = c;
            v.position = new Vector3(x, y, 0f);
            vh.AddVert(v);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
            SetMaterialDirty();
        }
#endif
    }
}
