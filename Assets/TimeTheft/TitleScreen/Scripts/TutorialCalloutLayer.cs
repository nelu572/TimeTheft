using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeTheft.TitleScreen
{
    public sealed class TutorialCalloutLayer : MonoBehaviour
    {
        [SerializeField] private Image screenshot;
        [SerializeField] private Vector2 panelSize = new Vector2(340f, 144f);
        [SerializeField] private float dashLength = 10f;
        [SerializeField] private float dashGap = 7f;

        private sealed class CalloutView
        {
            public RectTransform Root, Lines, Dot, Panel;
            public Image Background, DotOuter, DotInner, Badge;
            public Image[] Border;
            public CanvasGroup DotGroup, PanelGroup;
            public TMP_Text Number, Title, Description;
            public readonly List<Image> Dashes = new List<Image>();
            public int UsedDashes;
        }

        private readonly List<CalloutView> views = new List<CalloutView>();
        private TutorialCallout[] currentData;
        private Sprite currentSprite;
        private bool presentationEnabled, paused;
        private float elapsed;
        private int animationFrame = -1;
        public int VisibleCount { get; private set; }

        public void BeginPage() => ResetPresentation(true);
        public void PausePresentation() => paused = true;

        public void ResetPresentation(bool enabled)
        {
            presentationEnabled = enabled;
            paused = false;
            elapsed = 0f;
            animationFrame = Time.frameCount;
            currentData = null;
            currentSprite = null;
            VisibleCount = 0;
            foreach (var view in views)
            {
                view.Root.gameObject.SetActive(false);
                view.DotGroup.alpha = view.PanelGroup.alpha = 0f;
                view.Dot.localScale = Vector3.one;
                foreach (var dash in view.Dashes) dash.gameObject.SetActive(false);
            }
        }

        private void OnDisable() => ResetPresentation(false);

        // Image.preserveAspect와 같은 계산이며, 결과는 주석 레이어의 로컬 좌표다.
        public static Rect GetDisplayedImageRect(Image image, RectTransform overlay)
        {
            if (!image || !image.sprite || !overlay) return Rect.zero;
            var rect = image.GetPixelAdjustedRect();
            var size = image.sprite.rect.size;
            if (size.x <= 0 || size.y <= 0 || rect.width <= 0 || rect.height <= 0) return Rect.zero;
            if (image.preserveAspect)
            {
                float ratio = size.x / size.y;
                if (ratio > rect.width / rect.height)
                {
                    float height = rect.width / ratio;
                    rect.y += (rect.height - height) * image.rectTransform.pivot.y;
                    rect.height = height;
                }
                else
                {
                    float width = rect.height * ratio;
                    rect.x += (rect.width - width) * image.rectTransform.pivot.x;
                    rect.width = width;
                }
            }
            Vector2 min = overlay.InverseTransformPoint(image.rectTransform.TransformPoint(rect.min));
            Vector2 max = overlay.InverseTransformPoint(image.rectTransform.TransformPoint(rect.max));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public void Refresh(TutorialCallout[] callouts, TitleScreenTheme theme)
        {
            VisibleCount = 0;
            var overlay = (RectTransform)transform;
            var imageRect = GetDisplayedImageRect(screenshot, overlay);
            bool canShow = presentationEnabled && screenshot && screenshot.sprite && gameObject.activeInHierarchy && theme && imageRect.width > 0;
            if (canShow && (currentData != callouts || currentSprite != screenshot.sprite))
            {
                elapsed = 0f;
                animationFrame = Time.frameCount;
                currentData = callouts;
                currentSprite = screenshot.sprite;
            }
            if (!canShow) elapsed = 0f;
            else if (!paused && animationFrame != Time.frameCount)
            {
                elapsed += Time.unscaledDeltaTime;
                animationFrame = Time.frameCount;
            }
            if (canShow && callouts != null)
                for (int i = 0; i < callouts.Length; i++)
                {
                    var data = callouts[i];
                    if (data == null || !data.Visible) continue;
                    if (VisibleCount == views.Count) views.Add(CreateView(VisibleCount));
                    var view = views[VisibleCount++];
                    view.Root.gameObject.SetActive(true);
                    UpdateView(view, data, i + 1, VisibleCount - 1, theme, imageRect, overlay.rect);
                }
            for (int i = VisibleCount; i < views.Count; i++) views[i].Root.gameObject.SetActive(false);
        }

        private void UpdateView(CalloutView view, TutorialCallout data, int number, int order, TitleScreenTheme theme, Rect imageRect, Rect bounds)
        {
            float time = elapsed - order * .14f;
            float dotProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / .12f));
            float lineProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - .12f) / .38f));
            float panelProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - .5f) / .18f));
            Vector2 point = imageRect.min + Vector2.Scale(imageRect.size,
                new Vector2(Mathf.Clamp01(data.HighlightPosition.x), Mathf.Clamp01(data.HighlightPosition.y)));
            Vector2 center = imageRect.min + Vector2.Scale(imageRect.size, data.PanelPosition);
            Vector2 size = Vector2.Min(panelSize, bounds.size - Vector2.one * 24f);
            center.x = Mathf.Clamp(center.x, bounds.xMin + size.x * .5f + 12, bounds.xMax - size.x * .5f - 12);
            center.y = Mathf.Clamp(center.y, bounds.yMin + size.y * .5f + 12, bounds.yMax - size.y * .5f - 12);
            view.Dot.anchoredPosition = point;
            view.Dot.localScale = Vector3.one * Mathf.Lerp(.65f, 1f, dotProgress) * (1f + .07f * Mathf.Sin(Mathf.Max(0f, time) * 2.3f));
            view.DotGroup.alpha = dotProgress;
            view.Panel.anchoredPosition = center + Vector2.down * (10f * (1f - panelProgress));
            view.PanelGroup.alpha = panelProgress;
            view.Panel.sizeDelta = size;
            view.Background.color = theme.Panel;
            foreach (var edge in view.Border)
                edge.color = new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, .58f);
            view.DotOuter.color = theme.Accent;
            view.DotInner.color = theme.Background;
            view.Badge.color = new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, .13f);
            view.Number.font = view.Title.font = view.Description.font = theme.BodyFont;
            view.Number.color = theme.Accent;
            view.Title.color = theme.Text;
            view.Description.color = theme.MutedText;
            view.Number.text = number.ToString("00");
            view.Title.text = data.Title;
            view.Description.text = data.Description;

            Vector2 difference = point - center;
            Vector2 anchor, elbow;
            if (Mathf.Abs(difference.x) >= Mathf.Abs(difference.y))
            {
                anchor = center + new Vector2(Mathf.Sign(difference.x) * size.x * .5f, 0);
                elbow = new Vector2(point.x, anchor.y);
            }
            else
            {
                anchor = center + new Vector2(0, Mathf.Sign(difference.y) * size.y * .5f);
                elbow = new Vector2(anchor.x, point.y);
            }
            view.UsedDashes = 0;
            float firstLength = Vector2.Distance(point, elbow);
            float drawnLength = (firstLength + Vector2.Distance(elbow, anchor)) * lineProgress;
            DrawDashes(view, point, elbow, theme.Accent, drawnLength);
            DrawDashes(view, elbow, anchor, theme.Accent, drawnLength - firstLength);
            for (int i = view.UsedDashes; i < view.Dashes.Count; i++) view.Dashes[i].gameObject.SetActive(false);
        }

        private void DrawDashes(CalloutView view, Vector2 from, Vector2 to, Color accent, float drawnLength)
        {
            float length = Vector2.Distance(from, to);
            float visibleLength = Mathf.Min(length, drawnLength);
            if (length < 1f || visibleLength <= 0f) return;
            Vector2 direction = (to - from) / length;
            float dash = Mathf.Max(2f, dashLength), stride = dash + Mathf.Max(2f, dashGap);
            for (float at = 0; at < visibleLength; at += stride)
            {
                if (view.UsedDashes == view.Dashes.Count) view.Dashes.Add(CreateImage(view.Lines, "Dash"));
                var image = view.Dashes[view.UsedDashes++];
                image.gameObject.SetActive(true);
                image.color = new Color(accent.r, accent.g, accent.b, .72f);
                var rt = image.rectTransform;
                float segment = Mathf.Min(dash, visibleLength - at);
                rt.sizeDelta = new Vector2(segment, 1.7f);
                rt.anchoredPosition = from + direction * (at + segment * .5f);
                rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            }
        }

        private CalloutView CreateView(int index)
        {
            var view = new CalloutView();
            view.Root = CreateRect(transform, "Callout " + (index + 1));
            view.Root.sizeDelta = Vector2.zero;
            view.Lines = CreateRect(view.Root, "Dotted Connector");
            view.DotOuter = CreateImage(view.Root, "Highlight");
            view.Dot = view.DotOuter.rectTransform;
            view.Dot.sizeDelta = new Vector2(13, 13);
            view.Dot.localRotation = Quaternion.Euler(0, 0, 45);
            view.DotGroup = view.Dot.gameObject.AddComponent<CanvasGroup>();
            view.DotGroup.blocksRaycasts = view.DotGroup.interactable = false;
            view.DotGroup.alpha = 0f;
            view.DotInner = CreateImage(view.Dot, "Highlight Center");
            view.DotInner.rectTransform.sizeDelta = new Vector2(5, 5);
            view.Background = CreateImage(view.Root, "Explanation Panel");
            view.Panel = view.Background.rectTransform;
            view.PanelGroup = view.Panel.gameObject.AddComponent<CanvasGroup>();
            view.PanelGroup.blocksRaycasts = view.PanelGroup.interactable = false;
            view.PanelGroup.alpha = 0f;
            // Outline의 겹친 사각형은 Fade 중 패널 안쪽까지 금색으로 비친다.
            // 기존 두께의 네 변만 그려 어두운 패널이 그대로 나타나게 한다.
            view.Border = new Image[4];
            for (int i = 0; i < view.Border.Length; i++)
            {
                var edge = view.Border[i] = CreateImage(view.Panel, "Panel Border");
                var rt = edge.rectTransform;
                bool horizontal = i < 2;
                float side = i % 2;
                rt.anchorMin = horizontal ? new Vector2(0, side) : new Vector2(side, 0);
                rt.anchorMax = horizontal ? new Vector2(1, side) : new Vector2(side, 1);
                rt.sizeDelta = horizontal ? new Vector2(0, 1.2f) : new Vector2(1.2f, 0);
            }
            view.Badge = CreateImage(view.Panel, "Number Badge");
            SetTopLeft(view.Badge.rectTransform, 22, 20, 36, 34);
            view.Number = CreateText(view.Badge.transform, "Number", 23);
            view.Number.alignment = TextAlignmentOptions.Center;
            Stretch(view.Number.rectTransform);
            view.Title = CreateText(view.Panel, "Title", 26);
            SetTopLeft(view.Title.rectTransform, 72, 20, 246, 36);
            view.Title.fontStyle = FontStyles.Bold;
            view.Description = CreateText(view.Panel, "Explanation", 23);
            SetTopLeft(view.Description.rectTransform, 22, 69, 296, 60);
            return view;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            return rt;
        }

        private static Image CreateImage(Transform parent, string name)
        {
            var rt = CreateRect(parent, name);
            rt.gameObject.AddComponent<CanvasRenderer>();
            var image = rt.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateText(Transform parent, string name, float fontSize)
        {
            var rt = CreateRect(parent, name);
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static void SetTopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
