using System;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    public sealed class TabletopClockView : MonoBehaviour
    {
        [SerializeField] private TabletopFocusTarget focusTarget;
        [SerializeField] private SpriteRenderer caseRenderer, selectionRing;
        [SerializeField] private TextMeshPro valueLabel, ownerLabel;
        [SerializeField] private Transform hand;
        [SerializeField] private GameObject decisionControls;
        [SerializeField] private TabletopFocusTarget keep, swap;
        [SerializeField] private TabletopDecisionObject keepObject, swapObject;
        [Header("교체 가능한 시계 아트와 임시 뚜껑 (FocusPoint는 이 밖에 둠)")]
        [SerializeField] private Transform visual, lidPivot;
        [Header("미세 반응 · Unity 월드 단위")]
        [SerializeField, Min(0)] private float hoverLift = .035f;
        [SerializeField, Min(0)] private float selectedLift = .045f;
        [SerializeField, Min(.1f)] private float motionResponse = 18;
        [Header("임시 공개와 행동 피드백")]
        [SerializeField, Min(.05f)] private float revealDuration = .25f;
        [SerializeField, Range(0, 140)] private float lidOpenDegrees = 108;
        [SerializeField, Min(.05f)] private float actionDuration = .22f;
        [SerializeField, Range(0, .15f)] private float pulseScale = .035f;
        private int index;
        private Action<int> select;
        private Action<ClockAction> choose;
        private bool initialized;
        private Vector3 homePosition, visualPosition, visualScale;
        private Quaternion homeRotation, visualRotation, lidRotation;
        private float lift, revealElapsed, actionElapsed;
        private bool selected, revealed;
        private ClockAction feedbackAction;

        public TabletopFocusTarget FocusTarget => focusTarget;
        public TabletopFocusTarget Keep => keep;
        public TabletopFocusTarget Swap => swap;
        public TextMeshPro ValueLabel => valueLabel;
        public TextMeshPro OwnerLabel => ownerLabel;
        public bool IsRevealed => hand.gameObject.activeSelf;
        public Transform Visual => visual;

        public void Initialize(int clockIndex, Action<int> selectClock, Action<ClockAction> chooseAction)
        {
            if (!focusTarget || !caseRenderer || !selectionRing || !valueLabel || !ownerLabel || !hand || !decisionControls || !keep || !swap || !visual || !lidPivot || !keepObject || !swapObject)
                throw new InvalidOperationException("TabletopClockView의 표시와 조작 참조를 연결하세요.");
            index = clockIndex;
            select = selectClock;
            choose = chooseAction;
            if (initialized) return;
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            visualPosition = visual.localPosition;
            visualRotation = visual.localRotation;
            visualScale = visual.localScale;
            lidRotation = lidPivot.localRotation;
            actionElapsed = actionDuration;
            keepObject.Initialize();
            swapObject.Initialize();
            decisionControls.SetActive(true);
            focusTarget.Clicked += Select;
            keep.Clicked += KeepClock;
            swap.Clicked += SwapClock;
            initialized = true;
        }

        public void Show(int value, bool revealed, string owner, Color tone, bool selectable = false)
        {
            gameObject.SetActive(true);
            if (revealed && !this.revealed) revealElapsed = 0;
            this.revealed = revealed;
            if (!revealed) lidPivot.localRotation = lidRotation;
            // 호버 원색을 복구한 후 단계별 색상을 적용한다.
            focusTarget.SetInputEnabled(false);
            caseRenderer.color = tone;
            selectionRing.color = new Color(tone.r, tone.g, tone.b, selectable ? .28f : .48f);
            selectionRing.enabled = selectable || selected || revealed;
            valueLabel.text = revealed ? value.ToString() : "?";
            valueLabel.color = revealed ? TableView.Cream : TableView.Muted;
            ownerLabel.text = owner;
            ownerLabel.color = tone;
            hand.gameObject.SetActive(revealed);
            hand.localRotation = Quaternion.Euler(0, 0, -value * 30);
            focusTarget.SetInputEnabled(selectable);
            Actions(false);
        }

        public void SetSelected(bool value)
        {
            selected = value;
            if (value)
            {
                // 클릭한 프레임에 반응하되 루트·입력 영역·집중 시점은 움직이지 않는다.
                lift = selectedLift;
                visual.localPosition = visualPosition + Vector3.back * lift;
            }
        }

        public void PlayActionFeedback(ClockAction action)
        {
            feedbackAction = action;
            actionElapsed = 0;
        }

        public void ResetPresentation()
        {
            if (!initialized) return;
            focusTarget.SetInputEnabled(false);
            Actions(false);
            keepObject.ResetPresentation();
            swapObject.ResetPresentation();
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            selected = revealed = false;
            valueLabel.text = "?";
            hand.gameObject.SetActive(false);
            RestoreVisual();
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            bool hovered = focusTarget.InputEnabled && focusTarget.IsHighlighted;
            float targetLift = selected ? selectedLift : hovered ? hoverLift : 0;
            lift = Mathf.Lerp(lift, targetLift, 1 - Mathf.Exp(-motionResponse * Time.unscaledDeltaTime));
            actionElapsed = Mathf.Min(actionDuration, actionElapsed + Time.unscaledDeltaTime);
            float actionPulse = Mathf.Sin(Mathf.Clamp01(actionElapsed / Mathf.Max(.05f, actionDuration)) * Mathf.PI);
            float open = 0;
            if (revealed)
            {
                revealElapsed += Time.unscaledDeltaTime;
                open = Mathf.SmoothStep(0, 1, Mathf.Clamp01(revealElapsed / Mathf.Max(.05f, revealDuration)));
            }
            lidPivot.localRotation = lidRotation * Quaternion.Euler(open * lidOpenDegrees, 0, 0);
            float revealPulse = revealed ? Mathf.Sin(open * Mathf.PI) : 0;
            visual.localPosition = visualPosition + Vector3.back * (lift + actionPulse * .015f);
            visual.localRotation = visualRotation * Quaternion.Euler(0, 0,
                (feedbackAction == ClockAction.Swap ? 3 : 0) * actionPulse);
            visual.localScale = visualScale * (1 + pulseScale * (actionPulse + revealPulse) + (hovered ? .012f : 0));
        }

        private void RestoreVisual()
        {
            lift = 0;
            actionElapsed = actionDuration;
            revealElapsed = 0;
            visual.localPosition = visualPosition;
            visual.localRotation = visualRotation;
            visual.localScale = visualScale;
            lidPivot.localRotation = lidRotation;
        }

        public void Actions(bool visible)
        {
            keepObject.SetVisible(visible);
            swapObject.SetVisible(visible);
        }

        public void Hide()
        {
            focusTarget.SetInputEnabled(false);
            Actions(false);
            valueLabel.text = "?";
            hand.gameObject.SetActive(false);
            revealed = false;
            gameObject.SetActive(false);
        }

        private void Select() { if (isActiveAndEnabled) select?.Invoke(index); }
        private void KeepClock()
        {
            if (!isActiveAndEnabled) return;
            keepObject.Press();
            choose?.Invoke(ClockAction.Keep);
        }

        private void SwapClock()
        {
            if (!isActiveAndEnabled) return;
            swapObject.Press();
            choose?.Invoke(ClockAction.Swap);
        }

        private void OnDisable() { if (initialized) RestoreVisual(); }

        private void OnDestroy()
        {
            if (!initialized) return;
            if (focusTarget) focusTarget.Clicked -= Select;
            if (keep) keep.Clicked -= KeepClock;
            if (swap) swap.Clicked -= SwapClock;
        }
    }
}
