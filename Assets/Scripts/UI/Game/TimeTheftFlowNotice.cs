using System.Collections;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    // 진행 코루틴이 직접 기다리는 중앙 안내. 기존 픽셀 프레임과 런타임 폰트를 재사용한다.
    public sealed class TimeTheftFlowNotice : MonoBehaviour
    {
        [Min(.1f)] public float PlaybackSpeed = 1;
        public bool IsVisible { get; private set; }
        public string Message => title ? title.text : "";
        private RectTransform root;
        private CanvasGroup group;
        private TextMeshProUGUI title, detail;
        private int generation;

        public void Initialize(TimeTheftGameView view)
        {
            root = new GameObject("Flow Notices", typeof(RectTransform), typeof(CanvasGroup),
                typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
            root.SetParent(view.PhaseLabel.canvas.transform, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            group = root.GetComponent<CanvasGroup>();
            var shield = root.GetComponent<UnityEngine.UI.Image>();
            shield.color = new Color(.02f, .035f, .04f, .22f);
            shield.raycastTarget = true;
            var card = new GameObject("Central Notice", typeof(RectTransform), typeof(UiIndexGraphic)).GetComponent<RectTransform>();
            card.SetParent(root, false);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
            card.sizeDelta = new Vector2(760, 176);
            var frame = card.GetComponent<UiIndexGraphic>();
            frame.Layout = UiIndexGraphic.FrameLayout.Compact;
            frame.Accent = new Color(.78f, .65f, .40f);
            frame.raycastTarget = false;
            title = Text(card, "Announcement", view.PhaseLabel, 42, new Vector2(0, 20), new Vector2(700, 68));
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(.91f, .88f, .78f);
            detail = Text(card, "Context", view.PhaseLabel, 22, new Vector2(0, -38), new Vector2(700, 42));
            detail.color = new Color(.69f, .76f, .70f);
            Cancel();
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, TextMeshProUGUI source,
            float size, Vector2 position, Vector2 extent)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.font = source.font; label.fontSharedMaterial = source.fontSharedMaterial;
            label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, .5f);
            label.rectTransform.anchoredPosition = position; label.rectTransform.sizeDelta = extent;
            return label;
        }

        public IEnumerator Show(string message, string context, float readingSeconds)
        {
            int id = ++generation;
            IsVisible = true;
            title.text = message; detail.text = context;
            root.SetAsLastSibling(); root.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            try
            {
                float elapsed = 0;
                float duration = .12f + readingSeconds + .12f;
                while (id == generation && elapsed < duration)
                {
                    group.alpha = Mathf.Min(Mathf.Clamp01(elapsed / .12f), Mathf.Clamp01((duration - elapsed) / .12f));
                    yield return null;
                    elapsed += Time.unscaledDeltaTime * Mathf.Max(.1f, PlaybackSpeed);
                }
                // 안내가 닫히는 프레임의 누른 입력을 다음 행동으로 넘기지 않는다.
                if (id == generation) { group.alpha = 0; yield return null; }
            }
            finally { if (id == generation) Cancel(); }
        }

        public static string BatchName(int batch)
        {
            string[] ordinal = { "첫", "두", "세", "네", "다섯", "여섯", "일곱", "여덟", "아홉", "열" };
            return (batch > 0 && batch <= ordinal.Length ? ordinal[batch - 1] : batch.ToString()) + " 번째 시계 묶음";
        }

        public void Cancel()
        {
            generation++; IsVisible = false;
            if (group) { group.alpha = 0; group.blocksRaycasts = false; }
            if (root) root.gameObject.SetActive(false);
        }
        private void OnDisable() => Cancel();
        private void OnDestroy() { if (root) Destroy(root.gameObject); }
    }
}
