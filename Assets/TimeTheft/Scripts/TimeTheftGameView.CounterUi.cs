using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView
    {
        private readonly TextMeshProUGUI[] counterDeltaLabels = new TextMeshProUGUI[3];
        private readonly Renderer[] measuringPanes = new Renderer[3];
        private TextMeshPro[] deltaSources, deltaTargets;
        public IReadOnlyList<TextMeshProUGUI> CounterDeltaLabels => System.Array.AsReadOnly(counterDeltaLabels);
        public TextMeshProUGUI BankDeltaLabel => counterDeltaLabels[0];
        public TextMeshProUGUI PlayerDeltaLabel => counterDeltaLabels[1];
        public TextMeshProUGUI OpponentDeltaLabel => counterDeltaLabels[2];

        private static void OutlineText(TextMeshProUGUI text)
        {
            // fontMaterial은 이 텍스트의 런타임 복제본이다. 공유 폰트 재질은 수정하지 않는다.
            text.outlineColor = new Color(.015f, .035f, .035f, 1);
            text.outlineWidth = .18f;
        }

        private void InitializeCounterDeltas()
        {
            deltaSources = new[] { bankDelta, playerDelta, aiDelta };
            deltaTargets = new[] { bank, playerLife, aiLife };
            for (int i = 0; i < 3; i++)
            {
                deltaSources[i].GetComponent<Renderer>().enabled = false;
                // 기존 월드 변화량의 Inset 받침은 새 문자 전용 표시와 중복된다.
                foreach (var sprite in deltaSources[i].GetComponentsInChildren<SpriteRenderer>(true)) sprite.enabled = false;
                counterDeltaLabels[i] = Label(uiRoot, deltaTargets[i].name + " Delta", 28, deltaTargets[i].color,
                    Vector2.zero, new Vector2(190, 44));
                counterDeltaLabels[i].alignment = TextAlignmentOptions.Center;
                counterDeltaLabels[i].gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
                OutlineText(counterDeltaLabels[i]);
                string prefix = deltaTargets[i].name.Replace("Digits", "");
                var pane = deltaTargets[i].transform.parent.Find(prefix + "Pane");
                if (pane) measuringPanes[i] = pane.GetComponent<Renderer>();
            }
            HideCounterDeltas();
        }

        private void RefreshCounterDeltas()
        {
            if (deltaSources == null) return;
            var motion = TurnPresentation;
            bool closing = motion && (motion.Phase == H2TurnPresentation.TurnPhase.Closing ||
                motion.Phase == H2TurnPresentation.TurnPhase.Closed || motion.Phase == H2TurnPresentation.TurnPhase.Returning);
            for (int i = 0; i < 3; i++)
            {
                var source = deltaSources[i]; var label = counterDeltaLabels[i];
                source.GetComponent<Renderer>().enabled = false;
                bool show = !IsHelpVisible && !IsResultVisible && !closing && source.gameObject.activeInHierarchy && source.text.Length > 0;
                if (show)
                {
                    var renderer = measuringPanes[i] ? measuringPanes[i] : deltaTargets[i].GetComponent<Renderer>();
                    Vector2 min, max;
                    show = ScreenBounds(renderer.bounds, out min, out max);
                    if (show)
                    {
                        Vector2 local;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, new Vector2((min.x + max.x) * .5f, max.y), null, out local);
                        var half = label.rectTransform.sizeDelta * .5f; var bounds = uiRoot.rect;
                        local.y += half.y + 12;
                        label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, .5f);
                        label.rectTransform.anchoredPosition = new Vector2(Mathf.Round(Mathf.Clamp(local.x, bounds.xMin + half.x + 12, bounds.xMax - half.x - 12)),
                            Mathf.Round(Mathf.Clamp(local.y, bounds.yMin + half.y + 12, bounds.yMax - half.y - 12)));
                    }
                }
                var group = label.GetComponent<CanvasGroup>();
                group.alpha = Mathf.MoveTowards(group.alpha, show ? 1 : 0, Time.unscaledDeltaTime / .14f);
                label.color = source.color;
                if (show) label.text = source.text.Replace('−', '-');
                else if (group.alpha <= 0) label.text = "";
            }
        }

        private bool ScreenBounds(Bounds bounds, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 8; i++)
            {
                var point = uiCamera.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (point.z <= uiCamera.nearClipPlane) return false;
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return uiCamera.pixelRect.Contains((min + max) * .5f);
        }

        private void HideCounterDeltas()
        {
            foreach (var label in counterDeltaLabels)
                if (label) { label.text = ""; label.GetComponent<CanvasGroup>().alpha = 0; }
        }
    }
}
