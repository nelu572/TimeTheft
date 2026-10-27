using UnityEngine;

namespace TimeTheft
{
    // 표시만 소유한다. Match/개인 지식/정산에는 접근하지 않는다.
    [DisallowMultipleComponent]
    public sealed class H2ClockPresentation : MonoBehaviour
    {
        [SerializeField] private H2PocketWatchView watch;
        [SerializeField] private H2C2MaterialBinding materials;
        [SerializeField] private Transform mount;
        [SerializeField] private TabletopFocusTarget hoverTarget;
        [SerializeField] private Renderer[] temporaryRenderers;
        [Tooltip("12h 초과 설정에서도 정확한 기존 Xh 값을 표시하는 보조 숫자. 기본 1~12h에는 표시하지 않는다.")]
        [SerializeField] private Renderer extendedHoursLabel;
        [SerializeField, Range(0, 115)] private float openAngle = 115;
        [SerializeField, Min(0)] private float hoverLift = .012f;
        private Vector3 mountHome;
        private bool initialized;
        private Transform motionRoot;
        private Vector3 motionHome, returnPosition, labelHome, labelOffset;
        private Quaternion motionRotation, returnRotation, labelRotation, labelBasis;
        private bool batchPose;

        public H2PocketWatchView Watch => watch;
        public H2C2MaterialBinding Materials => materials;
        public bool IsOpen { get; private set; }
        public int? DisplayedHours { get; private set; }
        public bool IsRed { get; private set; }
        public ClockVisibility Visibility { get; private set; }
        public Transform MotionRoot => motionRoot ? motionRoot : (motionRoot = watch.transform.Find("H2_Watch/MotionRoot"));

        public void BeginBatchPose()
        {
            EndBatchPose();
            motionHome = MotionRoot.localPosition; motionRotation = MotionRoot.localRotation;
            if (extendedHoursLabel)
            {
                labelHome = extendedHoursLabel.transform.localPosition; labelRotation = extendedHoursLabel.transform.localRotation;
                labelOffset = MotionRoot.InverseTransformPoint(extendedHoursLabel.transform.position);
                labelBasis = Quaternion.Inverse(MotionRoot.rotation) * extendedHoursLabel.transform.rotation;
            }
            batchPose = true;
        }

        public void SampleBatchClip(AnimationClip clip, float progress)
        {
            var scale = MotionRoot.localScale;
            clip.SampleAnimation(watch.gameObject, Mathf.Clamp01(progress) * clip.length);
            MotionRoot.localScale = scale;
            IsOpen = Quaternion.Angle(Quaternion.identity, watch.LidPivot.localRotation) >= 85;
            if (extendedHoursLabel) extendedHoursLabel.enabled = IsOpen && DisplayedHours > 12;
        }

        public void CaptureBatchReturnPose() { returnPosition = MotionRoot.localPosition; returnRotation = MotionRoot.localRotation; }
        public void ReturnBatchPose(float progress)
        {
            MotionRoot.localPosition = Vector3.Lerp(returnPosition, motionHome, progress);
            MotionRoot.localRotation = Quaternion.Slerp(returnRotation, motionRotation, progress);
        }
        public void EndBatchPose()
        {
            if (!batchPose) return;
            MotionRoot.localPosition = motionHome; MotionRoot.localRotation = motionRotation;
            if (extendedHoursLabel) { extendedHoursLabel.transform.localPosition = labelHome; extendedHoursLabel.transform.localRotation = labelRotation; }
            batchPose = false;
        }

        public void InitializeDisplay()
        {
            // Inspector에서 Play 전에 어댑터를 꺼 둔 경우도 원래 표시로 시작한다.
            if (enabled) OnEnable(); else OnDisable();
        }

        private void OnEnable()
        {
            if (!watch || !materials || !mount) return;
            if (!initialized) { mountHome = mount.localPosition; initialized = true; }
            foreach (var renderer in temporaryRenderers) if (renderer) renderer.enabled = false;
            watch.gameObject.SetActive(true);
        }

        public void Apply(ClockState state)
        {
            Visibility = state.Visibility;
            bool visible = state.Visibility == ClockVisibility.Open || state.Visibility == ClockVisibility.Hidden;
            IsOpen = visible && state.Face.HasValue;
            DisplayedHours = IsOpen ? state.Face.Value.Hours : (int?)null;
            IsRed = IsOpen && state.Face.Value.Kind == ClockKind.Red;
            if (extendedHoursLabel) extendedHoursLabel.enabled = IsOpen && DisplayedHours > 12;
            // 닫힌/사용/폐기 상태에는 이전 공개값도 보관하지 않는다.
            watch.SetTime(DisplayedHours ?? 12, 0);
            watch.SetRed(IsRed);
            materials.SetC2(materials.C2Enabled);
            watch.SetLidAngle(IsOpen ? openAngle : 0);
        }

        public void CloseFrame(float progress)
        {
            progress = Mathf.Clamp01(progress);
            watch.SetLidAngle(openAngle * (1 - progress));
            if (progress < 1) return;
            IsOpen = false;
            DisplayedHours = null;
            IsRed = false;
            if (extendedHoursLabel) extendedHoursLabel.enabled = false;
            watch.SetTime(12, 0);
            watch.SetRed(false);
            materials.SetC2(materials.C2Enabled);
        }

        private void LateUpdate()
        {
            if (batchPose && extendedHoursLabel)
                extendedHoursLabel.transform.SetPositionAndRotation(MotionRoot.TransformPoint(labelOffset), MotionRoot.rotation * labelBasis);
            // 기존 자석 Hover/선택 입력을 관찰할 뿐 Collider와 FocusPoint는 움직이지 않는다.
            if (mount) mount.localPosition = mountHome + (hoverTarget && hoverTarget.IsHighlighted
                ? Vector3.back * hoverLift : Vector3.zero);
        }

        private void OnDisable()
        {
            EndBatchPose();
            if (mount && initialized) mount.localPosition = mountHome;
            if (watch) watch.gameObject.SetActive(false);
            // 이 어댑터를 끄면 보존한 Sprite/TMP 표시로 복귀한다.
            if (temporaryRenderers != null)
                foreach (var renderer in temporaryRenderers) if (renderer) renderer.enabled = true;
        }
    }
}
