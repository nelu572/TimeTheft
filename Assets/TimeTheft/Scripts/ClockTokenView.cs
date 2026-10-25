using System;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    // 임시 표시부. 완성 모델 연결 시 이 컴포넌트의 표시 참조만 교체한다.
    public sealed class ClockTokenView : MonoBehaviour
    {
        [SerializeField] private GameObject face, lid;
        [SerializeField] private SpriteRenderer dial;
        [SerializeField] private TextMeshPro number, kind;
        [SerializeField] private TabletopFocusTarget selectionTarget;
        private Vector3 homePosition, faceScale, lidScale;
        private bool initialized;
        private int index;
        private Action<int> select;
        private Transform visual;
        private Vector3 visualPosition;
        public bool IsOpen => face.activeSelf;
        public TextMeshPro Number => number;
        public TextMeshPro Kind => kind;
        public TabletopFocusTarget SelectionTarget => selectionTarget;
        public bool IsSelected { get; private set; }

        public void Initialize(int clockIndex, Action<int> selectClock)
        {
            if (!face || !lid || !dial || !number || !kind || !selectionTarget)
                throw new System.InvalidOperationException("임시 시계의 문자판·덮개·숫자·종류 표시를 연결하세요.");
            index = clockIndex;
            select = selectClock;
            SetSelectable(false);
            if (initialized) return;
            homePosition = transform.localPosition;
            faceScale = face.transform.localScale;
            lidScale = lid.transform.localScale;
            visual = face.transform.parent;
            visualPosition = visual.localPosition;
            selectionTarget.Clicked += Select;
            initialized = true;
        }

        public void Show(ClockState state)
        {
            SetSelectable(false);
            SetSelected(false);
            face.transform.localScale = faceScale;
            lid.transform.localScale = lidScale;
            bool visible = state.Visibility == ClockVisibility.Open || state.Visibility == ClockVisibility.Hidden;
            gameObject.SetActive(visible);
            bool open = state.Face.HasValue;
            face.SetActive(open);
            lid.SetActive(!open);
            number.text = kind.text = "";
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
            bool open = t < .5f;
            face.SetActive(open);
            lid.SetActive(!open);
            face.transform.localScale = Vector3.Scale(faceScale, new Vector3(1, 1 - Mathf.Clamp01(t * 2), 1));
            lid.transform.localScale = Vector3.Scale(lidScale, new Vector3(1, Mathf.Clamp01(t * 2 - 1), 1));
            if (open) return;
            number.text = kind.text = "";
        }

        public void SetSelectable(bool value) { if (selectionTarget) selectionTarget.SetInputEnabled(value); }
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
