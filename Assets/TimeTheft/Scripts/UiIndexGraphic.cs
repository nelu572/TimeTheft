using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimeTheft
{
    // 정보 카드 위의 우클릭은 이 UI에서만 소비한다. 월드 포인터의 UI 차단은 유지한다.
    public sealed class UiContextCancel : MonoBehaviour, IPointerClickHandler
    {
        public System.Action Cancel;
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Right) Cancel?.Invoke();
        }
    }

    // 고정 크기 픽셀 모서리와 가변 본문을 분리한다. Nine-slice처럼 늘어나되 아트 텍스처에 의존하지 않는다.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiIndexGraphic : MaskableGraphic, IPointerEnterHandler, IPointerExitHandler
    {
        public enum FrameLayout { Detail, Compact, Action, Guide }
        public enum FrameSymbol { None, Collect, Ration }
        private bool emphasized, pointerInside;
        private FrameLayout layout;
        private FrameSymbol symbol;
        private Color accent = new Color(.69f, .59f, .38f);
        private float header = 110, footer = 136, highlight;
        public FrameLayout Layout { get => layout; set { if (layout == value) return; layout = value; SetVerticesDirty(); } }
        public FrameSymbol Symbol { get => symbol; set { if (symbol == value) return; symbol = value; SetVerticesDirty(); } }
        public Color Accent { get => accent; set { if (accent == value) return; accent = value; SetVerticesDirty(); } }
        public void SetBands(float top, float bottom)
        {
            if (Mathf.Approximately(header, top) && Mathf.Approximately(footer, bottom)) return;
            header = top; footer = bottom; SetVerticesDirty();
        }
        public bool Emphasized
        {
            get => emphasized;
            set { if (emphasized == value) return; emphasized = value; SetVerticesDirty(); }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            var edge = Color.Lerp(new Color(.25f, .37f, .35f), accent, emphasized ? .85f : .35f + highlight * .65f);
            var ink = new Color(.055f, .09f, .092f, .985f);
            Shape(vh, r, edge);
            Shape(vh, new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), ink);
            if (layout == FrameLayout.Compact)
            {
                Quad(vh, new Rect(r.x + 8, r.y + 14, 3, r.height - 28), accent);
                Quad(vh, new Rect(r.xMax - 24, r.y + 12, 10, 2), edge);
                Quad(vh, new Rect(r.xMax - 16, r.y + 12, 2, 10), edge);
                return;
            }
            if (layout == FrameLayout.Action)
            {
                Quad(vh, new Rect(r.x + 8, r.y + 14, 4, r.height - 28), accent);
                Quad(vh, new Rect(r.x + 16, r.y + 14, 44, r.height - 28), new Color(.09f, .15f, .15f));
                Arrow(vh, new Vector2(r.x + 38, r.center.y), symbol == FrameSymbol.Ration, accent);
                for (int i = 0; i < 3; i++) Quad(vh, new Rect(r.xMax - 26 - i * 6, r.yMax - 13, 2, 4), edge);
                Quad(vh, new Rect(r.x + 72, r.y + 12, r.width - 94, 2), Color.Lerp(edge, accent, highlight));
                return;
            }

            float head = Mathf.Min(header, r.height - 24);
            Quad(vh, new Rect(r.x + 8, r.yMax - head, r.width - 16, head - 8), new Color(.078f, .125f, .125f));
            Quad(vh, new Rect(r.x + 18, r.yMax - head + 18, 3, head - 36), accent);
            ClockMark(vh, new Vector2(r.x + 45, r.yMax - 51), accent);
            Rule(vh, r.x + 28, r.yMax - head, r.width - 56, edge);
            // 눈금은 고정 간격이며, 패널을 늘려도 모서리·선·장식 두께를 유지한다.
            for (int i = 0; i < 5; i++) Quad(vh, new Rect(r.xMax - 30 - i * 8, r.yMax - 22, 2, i == 4 ? 12 : 5), edge);
            if (footer > 0 && r.height > head + footer)
            {
                Quad(vh, new Rect(r.x + 8, r.y + 22, r.width - 16, footer - 22), new Color(.065f, .112f, .11f));
                Rule(vh, r.x + 28, r.y + footer, r.width - 56, edge);
                MouseMark(vh, new Vector2(r.x + 43, r.y + 57), accent);
            }
            for (float x = r.x + 28; x < r.xMax - 28; x += 12)
            {
                int i = Mathf.RoundToInt((x - r.x - 28) / 12);
                Quad(vh, new Rect(x, r.y + 12, 2, i % 5 == 0 ? 6 : 2), new Color(edge.r, edge.g, edge.b, .65f));
            }
            Quad(vh, new Rect(r.x + 28, r.yMax - 4, emphasized ? 88 : 52, 2), accent);
        }
        private void Shape(VertexHelper vh, Rect r, Color tint)
        {
            // 비대칭 계단형 실루엣을 평면의 직교 픽셀로 그린다.
            Quad(vh, new Rect(r.x + 16, r.yMax - 4, Mathf.Max(0, r.width - 68), 4), tint);
            Quad(vh, new Rect(r.x + 8, r.yMax - 8, r.width - 24, 4), tint);
            Quad(vh, new Rect(r.x + 4, r.yMax - 16, r.width - 8, 8), tint);
            Quad(vh, new Rect(r.x, r.y + 12, r.width, r.height - 28), tint);
            Quad(vh, new Rect(r.x + 8, r.y + 4, r.width - 16, 8), tint);
            Quad(vh, new Rect(r.x + 16, r.y, r.width - 40, 4), tint);
        }
        private void Rule(VertexHelper vh, float x, float y, float width, Color tint)
        {
            Quad(vh, new Rect(x, y, 30, 2), accent);
            Quad(vh, new Rect(x + 36, y, Mathf.Max(0, width - 36), 1), tint);
        }
        private void ClockMark(VertexHelper vh, Vector2 p, Color tint)
        {
            Quad(vh, new Rect(p.x - 8, p.y + 12, 16, 2), tint);
            Quad(vh, new Rect(p.x - 8, p.y - 14, 16, 2), tint);
            Quad(vh, new Rect(p.x - 14, p.y - 8, 2, 16), tint);
            Quad(vh, new Rect(p.x + 12, p.y - 8, 2, 16), tint);
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2)
                Quad(vh, new Rect(p.x + sx * 10 - 2, p.y + sy * 10 - 2, 4, 4), tint);
            Quad(vh, new Rect(p.x - 1, p.y - 1, 2, 10), tint);
            Quad(vh, new Rect(p.x - 1, p.y - 1, 8, 2), tint);
            Quad(vh, new Rect(p.x - 4, p.y + 17, 8, 3), tint);
        }
        private void MouseMark(VertexHelper vh, Vector2 p, Color tint)
        {
            Quad(vh, new Rect(p.x - 8, p.y - 10, 2, 18), tint);
            Quad(vh, new Rect(p.x + 6, p.y - 10, 2, 18), tint);
            Quad(vh, new Rect(p.x - 4, p.y + 10, 8, 2), tint);
            Quad(vh, new Rect(p.x - 4, p.y - 14, 8, 2), tint);
            Quad(vh, new Rect(p.x - 1, p.y, 2, 10), tint);
            Quad(vh, new Rect(p.x - 6, p.y + 1, 5, 7), tint);
        }
        private void Arrow(VertexHelper vh, Vector2 p, bool down, Color tint)
        {
            int sign = down ? -1 : 1;
            Quad(vh, new Rect(p.x - 2, p.y - 11, 4, 22), tint);
            for (int i = 0; i < 4; i++)
            {
                float y = p.y + sign * (11 - i * 3);
                Quad(vh, new Rect(p.x - 2 - i * 3, y - 2, 4, 4), tint);
                Quad(vh, new Rect(p.x - 2 + i * 3, y - 2, 4, 4), tint);
            }
        }
        private void Quad(VertexHelper vh, Rect r, Color tint)
        {
            if (r.width <= 0 || r.height <= 0) return;
            float scale = canvas ? canvas.scaleFactor : 1;
            r = Rect.MinMaxRect(Mathf.Round(r.xMin * scale) / scale, Mathf.Round(r.yMin * scale) / scale,
                Mathf.Round(r.xMax * scale) / scale, Mathf.Round(r.yMax * scale) / scale);
            tint *= color;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), tint, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), tint, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), tint, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
        }
        public void OnPointerEnter(PointerEventData data)
        {
            var button = GetComponentInParent<Button>();
            if (!button || !button.IsInteractable()) return;
            pointerInside = true; SetVerticesDirty();
        }
        public void OnPointerExit(PointerEventData data) { pointerInside = false; SetVerticesDirty(); }
        private void Update()
        {
            float next = Mathf.MoveTowards(highlight, pointerInside ? 1 : 0, Time.unscaledDeltaTime / .09f);
            if (Mathf.Approximately(next, highlight)) return;
            highlight = next; SetVerticesDirty();
        }
        protected override void OnDisable() { pointerInside = false; highlight = 0; base.OnDisable(); }
    }
}
