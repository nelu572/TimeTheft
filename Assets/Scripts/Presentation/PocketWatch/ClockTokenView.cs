using System;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    // 논리 슬롯과 기존 입력을 유지하고, 선택 사항인 H2 표시 어댑터에 공개 상태만 전달한다.
    public sealed class ClockTokenView : MonoBehaviour
    {
        [SerializeField] private GameObject face, lid;
        [SerializeField] private SpriteRenderer dial;
        [SerializeField] private TextMeshPro number, kind;
        [SerializeField] private TabletopFocusTarget selectionTarget;
        [SerializeField] private H2ClockPresentation h2Presentation;
        private Vector3 homePosition, faceScale, lidScale;
        private bool initialized;
        private int index;
        private Action<int> select;
        private Transform visual;
        private Vector3 visualPosition;
        private BoxCollider decisionCollider;
        private bool decisionClickable;
        public bool IsOpen => face.activeSelf;
        public TextMeshPro Number => number;
        public TextMeshPro Kind => kind;
        public TabletopFocusTarget SelectionTarget => selectionTarget;
        public H2ClockPresentation H2Presentation => h2Presentation;
        public bool IsSelected { get; private set; }
        public int SlotIndex => index;

        internal void BindSlot(int slotIndex, Vector3 localHome)
        {
            index = slotIndex; homePosition = localHome;
            transform.localPosition = localHome;
            SetSelected(false); SetSelectable(false);
        }

        public void Initialize(int clockIndex, Action<int> selectClock)
        {
            if (!face || !lid || !dial || !number || !kind || !selectionTarget)
                throw new System.InvalidOperationException("임시 시계의 문자판·덮개·숫자·종류 표시를 연결하세요.");
            index = clockIndex;
            select = selectClock;
            SetSelectable(false);
            if (h2Presentation) h2Presentation.InitializeDisplay();
            if (initialized) return;
            homePosition = transform.localPosition;
            faceScale = face.transform.localScale;
            lidScale = lid.transform.localScale;
            visual = face.transform.parent;
            visualPosition = visual.localPosition;
            selectionTarget.Clicked += Select;
            initialized = true;
        }

        public void Show(ClockState state, bool keepClosedVisual = false)
        {
            SetDecisionClickable(false);
            SetSelectable(false);
            SetSelected(false);
            face.transform.localScale = faceScale;
            lid.transform.localScale = lidScale;
            bool visible = state.Visibility == ClockVisibility.Open || state.Visibility == ClockVisibility.Hidden;
            gameObject.SetActive(visible || keepClosedVisual);
            bool open = state.Face.HasValue;
            face.SetActive(open);
            lid.SetActive(!open);
            number.text = kind.text = "";
            if (h2Presentation && h2Presentation.enabled) h2Presentation.Apply(state);
            if (!open) return;
            ClockFace value = state.Face.Value;
            bool red = value.Kind == ClockKind.Red;
            dial.color = red ? new Color(.55f, .065f, .075f) : new Color(.85f, .8f, .66f);
            number.text = value.Hours + "h";
            kind.text = red ? "빨강" : "일반";
            number.color = kind.color = red ? Color.white : new Color(.08f, .09f, .09f);
        }

        public void CloseFrame(float t)
        {
            t = Mathf.Clamp01(t);
            if (h2Presentation && h2Presentation.isActiveAndEnabled) h2Presentation.CloseFrame(t);
            bool open = t < .5f;
            face.SetActive(open);
            lid.SetActive(!open);
            face.transform.localScale = Vector3.Scale(faceScale, new Vector3(1, 1 - Mathf.Clamp01(t * 2), 1));
            lid.transform.localScale = Vector3.Scale(lidScale, new Vector3(1, Mathf.Clamp01(t * 2 - 1), 1));
            if (open) return;
            number.text = kind.text = "";
        }

        public void SetSelectable(bool value) { if (selectionTarget) selectionTarget.SetInputEnabled(value); }
        public void SetDecisionClickable(bool value)
        {
            decisionClickable = value;
            if (!value) { if (decisionCollider) decisionCollider.enabled = false; return; }
            if (!decisionCollider)
            {
                var go = new GameObject("Selected Clock Click");
                go.transform.SetParent(selectionTarget.transform, false);
                decisionCollider = go.AddComponent<BoxCollider>();
            }
            SetSelectable(true);
            UpdateDecisionCollider();
        }
        private void LateUpdate() => UpdateDecisionCollider();
        private void UpdateDecisionCollider()
        {
            if (!decisionCollider) return;
            bool active = decisionClickable && h2Presentation && selectionTarget.InputEnabled;
            decisionCollider.enabled = active;
            if (!active) return;
            bool found = false; var bounds = new Bounds();
            foreach (var renderer in h2Presentation.MotionRoot.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                var b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = decisionCollider.transform.InverseTransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
            }
            if (found) { decisionCollider.center = bounds.center; decisionCollider.size = bounds.size + Vector3.one * .025f; }
        }
        public void SetSelected(bool value)
        {
            IsSelected = value;
            if (visual) visual.localPosition = visualPosition + (value ? Vector3.back * .055f : Vector3.zero);
        }
        private void Select() { if (isActiveAndEnabled) select?.Invoke(index); }
        private void OnDisable() => SetSelectable(false);
        private void OnDestroy() { if (initialized && selectionTarget) selectionTarget.Clicked -= Select; }

        public void ShuffleFrame(float t)
        {
            // H2는 정식 슬롯 교환 모션을 연결하기 전까지 닫힌 정지 자세를 유지한다.
            // Sprite용 중앙 집결을 적용하면 3D 메시 5개가 같은 자리에서 관통한다.
            if (h2Presentation && h2Presentation.enabled) return;
            float pile = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
            transform.localPosition = Vector3.Lerp(homePosition, new Vector3(0, homePosition.y, homePosition.z), pile)
                + Vector3.up * (pile * .12f) + Vector3.forward * (pile * Mathf.Sin(t * 4 * Mathf.PI + homePosition.x) * .18f);
        }

        public void ResetPose()
        {
            if (!initialized) return;
            transform.localPosition = homePosition;
            SetSelected(false);
        }
    }
}
