using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimeTheft.TitleScreen
{
    // 실제 Hover만 강조하며 클릭 확정 전에 짧은 눌림 피드백을 보장한다.
    public sealed class TitleMenuButton : Button
    {
        [SerializeField] private CanvasGroup border;
        [SerializeField] private RectTransform arrow;
        private CanvasGroup arrowGroup;
        private TMP_Text[] labels;
        private RectTransform labelRect;
        private Vector2 labelRest, arrowRest;
        private Vector3 scaleRest;
        private bool cached, pointerInside, clickPending, clickFeedback;
        private float hover, press, hoverTarget, pressTarget;
        private TitleScreenTheme theme;
        private Coroutine clickRoutine;

        public void SetTheme(TitleScreenTheme theme)
        {
            this.theme = theme;
            labels = GetComponentsInChildren<TMP_Text>(true);
            if (!cached)
            {
                scaleRest = transform.localScale;
                foreach (var label in labels)
                    if (label.name == "Label") { labelRect = label.rectTransform; labelRest = labelRect.anchoredPosition; }
                if (arrow)
                {
                    arrowRest = arrow.anchoredPosition;
                    arrowGroup = arrow.GetComponent<CanvasGroup>();
                    if (!arrowGroup) arrowGroup = arrow.gameObject.AddComponent<CanvasGroup>();
                    arrowGroup.blocksRaycasts = arrowGroup.interactable = false;
                    arrow.gameObject.SetActive(true);
                }
                cached = true;
            }
            if (border)
                foreach (var edge in border.GetComponentsInChildren<Image>(true)) edge.color = theme.Accent;
            DoStateTransition(currentSelectionState, true);
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
            base.OnPointerEnter(eventData);
            DoStateTransition(currentSelectionState, false);
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            base.OnPointerExit(eventData);
            DoStateTransition(currentSelectionState, false);
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) QueueClick();
        }

        public override void OnSubmit(BaseEventData eventData) => QueueClick();

        private void QueueClick()
        {
            if (!IsActive() || !IsInteractable() || clickPending) return;
            clickPending = clickFeedback = true;
            DoStateTransition(currentSelectionState, false);
            clickRoutine = StartCoroutine(ConfirmClick());
        }

        private IEnumerator ConfirmClick()
        {
            float time = 0f;
            while (time < .1f)
            {
                yield return null;
                time += Time.unscaledDeltaTime;
            }
            clickFeedback = false;
            // PointerEventData는 InputModule이 재사용하므로 지연 중 보관하지 않는다.
            if (IsActive() && IsInteractable()) onClick.Invoke();
            clickPending = false;
            clickRoutine = null;
            if (IsActive()) DoStateTransition(currentSelectionState, false);
        }

        protected override void InstantClearState()
        {
            if (clickRoutine != null) StopCoroutine(clickRoutine);
            clickRoutine = null;
            pointerInside = clickPending = clickFeedback = false;
            base.InstantClearState();
            DoStateTransition(SelectionState.Normal, true);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) InstantClearState();
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            bool pressed = state == SelectionState.Pressed || clickFeedback;
            state = !IsInteractable() ? SelectionState.Disabled : pointerInside ?
                (pressed ? SelectionState.Pressed : SelectionState.Highlighted) : SelectionState.Normal;
            base.DoStateTransition(state, instant);
            hoverTarget = pointerInside && IsInteractable() ? 1f : 0f;
            pressTarget = IsInteractable() && (clickFeedback || (pointerInside && pressed)) ? 1f : 0f;
            if (instant)
            {
                hover = hoverTarget; press = pressTarget; ApplyMotion();
            }
        }

        private void Update()
        {
            if (!cached) return;
            hover = Mathf.MoveTowards(hover, hoverTarget, Time.unscaledDeltaTime / .15f);
            press = Mathf.MoveTowards(press, pressTarget, Time.unscaledDeltaTime / .09f);
            ApplyMotion();
        }

        private void ApplyMotion()
        {
            if (!cached || !theme) return;
            float h = Mathf.SmoothStep(0f, 1f, hover), p = Mathf.SmoothStep(0f, 1f, press);
            foreach (var label in labels)
                label.color = !IsInteractable() ? theme.MutedText : Color.Lerp(theme.Text, theme.Background, h);
            if (labelRect) labelRect.anchoredPosition = labelRest + Vector2.right * (6f * h);
            transform.localScale = scaleRest * Mathf.Lerp(1f, .985f, p);
            if (border) border.alpha = !IsInteractable() ? .08f : Mathf.Lerp(.24f, .9f, h) + .1f * p;
            if (arrow && arrowGroup)
            {
                arrow.anchoredPosition = arrowRest + Vector2.right * (Mathf.Lerp(-6f, 4f, h) + 3f * p);
                arrowGroup.alpha = h;
            }
        }
    }
}
