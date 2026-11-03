using UnityEngine;
using UnityEngine.UI;

namespace TimeTheft.TitleScreen
{
    // 배경 이미지 원본은 보존하고 UI에서만 검정 그라데이션을 겹친다.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TitleBackgroundShade : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] private float solidUntil = .28f;
        [SerializeField, Range(0f, 1f)] private float clearFrom = .66f;
        [SerializeField, Range(0f, 1f)] private float edgeDarkness = .3f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            const int columns = 24;
            const int rows = 8;
            for (int y = 0; y <= rows; y++)
            {
                float v = y / (float)rows;
                float edge = Mathf.Pow(Mathf.Abs(v * 2f - 1f), 4f) * edgeDarkness;
                for (int x = 0; x <= columns; x++)
                {
                    float u = x / (float)columns;
                    float horizontal = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(solidUntil, Mathf.Max(solidUntil + .001f, clearFrom), u));
                    var tint = color;
                    tint.a *= 1f - (1f - horizontal) * (1f - edge);
                    vh.AddVert(new Vector3(rect.xMin + rect.width * u, rect.yMin + rect.height * v), tint, Vector2.zero);
                    if (x == 0 || y == 0) continue;
                    int i = y * (columns + 1) + x;
                    vh.AddTriangle(i - columns - 2, i - columns - 1, i);
                    vh.AddTriangle(i - columns - 2, i, i - 1);
                }
            }
        }
    }
}
