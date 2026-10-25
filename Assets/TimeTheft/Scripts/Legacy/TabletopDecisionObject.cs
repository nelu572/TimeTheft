using System;
using UnityEngine;

namespace TimeTheft
{
    // 선택 의미와 게임 규칙은 ClockView가 소유하고, 이 컴포넌트는 물건의 반응만 담당한다.
    public sealed class TabletopDecisionObject : MonoBehaviour
    {
        [SerializeField] private TabletopFocusTarget target;
        [SerializeField] private Collider selectionCollider;
        [SerializeField] private Transform artwork, mechanism;
        [Header("등장 · 퇴장과 호버")]
        [SerializeField, Min(.05f)] private float transitionDuration = .18f;
        [SerializeField, Min(0)] private float entranceLift = .06f;
        [SerializeField, Min(0)] private float hoverLift = .015f;
        [SerializeField, Min(.1f)] private float response = 18;
        [Header("누름 · 회전 반응 (각 물건별 Inspector 설정)")]
        [SerializeField] private Vector3 pressedOffset = new Vector3(0, 0, .025f);
        [SerializeField] private Vector3 pressedEuler;
        private Vector3 artworkPosition, artworkScale, mechanismPosition;
        private Quaternion mechanismRotation;
        private float progress, hover, press;
        private bool visible, initialized;

        public Transform Artwork => artwork;
        public bool IsVisible => progress > 0;

        public void Initialize()
        {
            if (initialized) return;
            if (!target || !selectionCollider || !artwork || !mechanism)
                throw new InvalidOperationException("선택 물건의 입력 영역과 아트를 연결하세요.");
            artworkPosition = artwork.localPosition;
            artworkScale = artwork.localScale;
            mechanismPosition = mechanism.localPosition;
            mechanismRotation = mechanism.localRotation;
            initialized = true;
            ResetPresentation();
        }

        public void SetVisible(bool value)
        {
            if (!target || !selectionCollider || !artwork || !mechanism) return;
            if (!initialized) Initialize();
            visible = value;
            target.SetInputEnabled(value);
            selectionCollider.enabled = value;
            if (value) artwork.gameObject.SetActive(true);
        }

        public void Press() { press = 1; }

        public void ResetPresentation()
        {
            if (!initialized || !target || !selectionCollider || !artwork || !mechanism) return;
            visible = false;
            progress = hover = press = 0;
            target.SetInputEnabled(false);
            selectionCollider.enabled = false;
            artwork.localPosition = artworkPosition;
            artwork.localScale = artworkScale;
            mechanism.localPosition = mechanismPosition;
            mechanism.localRotation = mechanismRotation;
            artwork.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            progress = Mathf.MoveTowards(progress, visible ? 1 : 0, Time.unscaledDeltaTime / Mathf.Max(.05f, transitionDuration));
            hover = Mathf.Lerp(hover, visible && target.IsHighlighted ? 1 : 0, 1 - Mathf.Exp(-response * Time.unscaledDeltaTime));
            press = Mathf.MoveTowards(press, 0, Time.unscaledDeltaTime / Mathf.Max(.05f, transitionDuration));
            float blend = Mathf.SmoothStep(0, 1, progress);
            artwork.localPosition = artworkPosition + Vector3.back * ((1 - blend) * entranceLift + hover * hoverLift);
            artwork.localScale = artworkScale * Mathf.Lerp(.8f, 1, blend);
            mechanism.localPosition = mechanismPosition + pressedOffset * press;
            mechanism.localRotation = mechanismRotation * Quaternion.Euler(pressedEuler * press);
            if (progress <= 0) artwork.gameObject.SetActive(false);
        }

        private void OnDisable() { if (initialized) ResetPresentation(); }
    }
}
