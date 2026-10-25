using UnityEngine;

namespace TimeTheft
{
    // 외부 스프라이트 없이 시계 테두리·다이얼·테이블을 그린다.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TableShape : UnityEngine.UI.MaskableGraphic
    {
        public bool Ring;
        public float Thickness = 3f;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            const int segments = 80;
            if (!Ring)
            {
                vh.AddVert(Vector3.zero, color, Vector2.zero);
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    vh.AddVert(new Vector3(Mathf.Cos(angle) * r.width / 2, Mathf.Sin(angle) * r.height / 2), color, Vector2.zero);
                    if (i > 0) vh.AddTriangle(0, i, i + 1);
                }
            }
            else
            {
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle));
                    vh.AddVert(Vector3.Scale(direction, new Vector3(r.width / 2, r.height / 2)), color, Vector2.zero);
                    vh.AddVert(Vector3.Scale(direction, new Vector3(r.width / 2 - Thickness, r.height / 2 - Thickness)), color, Vector2.zero);
                    if (i > 0)
                    {
                        int n = i * 2;
                        vh.AddTriangle(n - 2, n, n - 1);
                        vh.AddTriangle(n, n + 1, n - 1);
                    }
                }
            }
        }
    }
}
