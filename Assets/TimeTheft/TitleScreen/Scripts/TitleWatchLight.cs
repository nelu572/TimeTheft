using UnityEngine;
using UnityEngine.UI;

namespace TimeTheft.TitleScreen
{
    // 원본 픽셀과 UV는 그대로 두고 시계 주변의 밝기만 부드럽게 보간한다.
    public sealed class TitleWatchLight : BaseMeshEffect
    {
        private float dimming;

        public void SetTime(float time)
        {
            float next = .15f * (.5f - .5f * Mathf.Cos(time * Mathf.PI * 2f / 4.5f));
            if (next == dimming || (next > 0f && Mathf.Abs(next - dimming) < .0001f)) return;
            dimming = next;
            if (graphic) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount != 4 || dimming <= 0f) return;
            var bottomLeft = UIVertex.simpleVert;
            var topLeft = bottomLeft; var topRight = bottomLeft; var bottomRight = bottomLeft;
            vh.PopulateUIVertex(ref bottomLeft, 0); vh.PopulateUIVertex(ref topLeft, 1);
            vh.PopulateUIVertex(ref topRight, 2); vh.PopulateUIVertex(ref bottomRight, 3);
            vh.Clear();
            const int columns = 20, rows = 12;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    float u = x / (float)columns, v = y / (float)rows;
                    var vertex = bottomLeft;
                    vertex.position = Vector3.Lerp(Vector3.Lerp(bottomLeft.position, bottomRight.position, u),
                        Vector3.Lerp(topLeft.position, topRight.position, u), v);
                    vertex.uv0 = Vector4.Lerp(Vector4.Lerp(bottomLeft.uv0, bottomRight.uv0, u),
                        Vector4.Lerp(topLeft.uv0, topRight.uv0, u), v);
                    float face = SoftArea(u, v, .55f, .40f, .25f, .31f);
                    float lid = SoftArea(u, v, .35f, .71f, .18f, .31f);
                    float brightness = 1f - dimming * Mathf.Max(face, lid);
                    Color tint = Color.Lerp(Color.Lerp(bottomLeft.color, bottomRight.color, u),
                        Color.Lerp(topLeft.color, topRight.color, u), v);
                    tint.r *= brightness; tint.g *= brightness; tint.b *= brightness;
                    vertex.color = tint;
                    vh.AddVert(vertex);
                    if (x == 0 || y == 0) continue;
                    int i = y * (columns + 1) + x;
                    vh.AddTriangle(i - columns - 2, i - columns - 1, i);
                    vh.AddTriangle(i - columns - 2, i, i - 1);
                }
        }

        private static float SoftArea(float u, float v, float x, float y, float width, float height)
        {
            float dx = (u - x) / width, dy = (v - y) / height;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.15f, 1f, dx * dx + dy * dy));
        }
    }
}
