using System.Collections;
using UnityEngine;

namespace TimeTheft
{
    // Match를 참조하지 않는 개별 시계 표시부. Controller가 입력/규칙 전이를 소유한다.
    [DisallowMultipleComponent]
    public sealed partial class H2TurnPresentation : MonoBehaviour
    {
        public enum TurnPhase { Idle, Selecting, DecisionHold, RevealEnter, ResultPose, LegacyReturn,
            SettlementTransition, SettlementLead, CountingTime, SettlementHold, Closing, Closed, Returning }
        [SerializeField] private AnimationClip selectClip, revealClip;
        [Header("개별 턴 · Decision Hold는 입력까지 무기한")]
        [Min(.01f)] public float SelectDuration = .875f;
        [Min(.01f)] public float RevealEnterDuration = .84f;
        [Tooltip("정식 Settlement Transition 전까지 기존 계수기 화면으로 넘기는 임시 복귀")]
        [Min(.01f)] public float LegacyReturnDuration = .35f;
        [Tooltip("닫힌 이웃 시계를 넘는 동안의 임시 여유 높이. 시작/결정 자세에는 적용하지 않는다.")]
        [Min(0)] public float SelectClearance = .1f;
        [Header("임시 확인 앵커 · 기존 슬롯 중심에 대한 월드 오프셋")]
        public Vector3 DecisionOffset = new Vector3(.1f, 0, -.9f);
        public Vector3 RevealOffset = new Vector3(.33f, .77f, -1.865f);
        public Vector3 RevealCameraOffset = new Vector3(-.28f, .97f, -1.58f);
        public Vector3 RevealLookOffset = new Vector3(0, .05f, 0);
        [SerializeField] private AnimationCurve cameraEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        private TimeTheftGameView view;
        private H2ClockPresentation model;
        private TabletopFocusCamera focus;
        private Camera presentationCamera;
        private bool ownsPose, ownsCamera;
        private bool skipMotion;
        private int generation;
        private AnimationClip track;
        private Vector3 start, end, sourceStart, sourceEnd, center;
        private Quaternion rotation, sourceRotation, residualBasis;
        public TurnPhase Phase { get; private set; }
        public float PhaseElapsed { get; private set; }
        public int ClockIndex { get; private set; } = -1;
        public bool IsSelecting => Phase == TurnPhase.Selecting;
        public bool IsBusy => IsSelecting || Phase == TurnPhase.RevealEnter || Phase == TurnPhase.LegacyReturn ||
            Phase == TurnPhase.SettlementTransition || Phase == TurnPhase.Closing || Phase == TurnPhase.Returning;
        public AnimationClip SelectClip => selectClip;
        public AnimationClip RevealClip => revealClip;

        public bool CanPlay(TimeTheftGameView gameView, int index) => isActiveAndEnabled && selectClip && revealClip &&
            gameView && gameView.FocusCamera && index >= 0 && index < gameView.Clocks.Length &&
            gameView.Clocks[index].H2Presentation && gameView.Clocks[index].H2Presentation.isActiveAndEnabled;

        public IEnumerator Select(TimeTheftGameView gameView, int index)
        {
            int id = ++generation; Phase = TurnPhase.Selecting; PhaseElapsed = 0; skipMotion = false;
            // 열쇠 재선택 시 앞 시계를 안전하게 원래 슬롯으로 돌린 뒤 새 시계를 가져온다.
            if (ownsPose && model)
            {
                model.CaptureBatchReturnPose();
                var parts = model.Watch.GetComponentsInChildren<Renderer>(); var bounds = parts[0].bounds;
                foreach (var part in parts) bounds.Encapsulate(part.bounds);
                float clearance = bounds.size.y + SelectClearance;
                float time = 0;
                while (id == generation && time < LegacyReturnDuration)
                {
                    yield return null; time += Time.unscaledDeltaTime;
                    if (id != generation) yield break;
                    if (skipMotion) break;
                    float u = Mathf.Clamp01(time / Mathf.Max(.01f, LegacyReturnDuration));
                    model.ReturnBatchPose(Mathf.SmoothStep(0, 1, u));
                    model.MotionRoot.position += Vector3.up * (clearance * Mathf.SmoothStep(0, 1, Mathf.Min(u / .18f, (1 - u) / .18f)));
                }
                model.EndBatchPose(); ownsPose = false;
            }
            if (id != generation) yield break;
            view = gameView; ClockIndex = index; model = view.Clocks[index].H2Presentation;
            focus = view.FocusCamera; presentationCamera = focus.GetComponent<Camera>();
            center = Vector3.zero; foreach (var c in view.Clocks) center += c.transform.position / view.Clocks.Length;
            model.BeginBatchPose(); ownsPose = true;
            var target = center + DecisionOffset; target.y = model.MotionRoot.position.y + DecisionOffset.y;
            Configure(selectClip, target);
            if (skipMotion) Sample(1);
            else yield return Progress(id, SelectDuration, TurnPhase.Selecting);
            if (id != generation || !ownsPose) yield break;
            Phase = TurnPhase.DecisionHold; PhaseElapsed = 0;
        }

        public IEnumerator RevealEnter(TimeTheftGameView gameView, int index)
        {
            if (!ownsPose || model != gameView.Clocks[index].H2Presentation)
            {
                Cancel(); view = gameView; ClockIndex = index; model = view.Clocks[index].H2Presentation;
                focus = view.FocusCamera; presentationCamera = focus.GetComponent<Camera>();
                center = Vector3.zero; foreach (var c in view.Clocks) center += c.transform.position / view.Clocks.Length;
                model.BeginBatchPose(); ownsPose = true;
            }
            int id = ++generation;
            skipMotion = false;
            var cameraStart = presentationCamera.transform.position; var cameraRotation = presentationCamera.transform.rotation;
            float cameraFov = presentationCamera.fieldOfView;
            var target = center + RevealOffset;
            Configure(revealClip, target);
            focus.BeginPresentationCamera(); ownsCamera = true;
            var cameraEnd = target + RevealCameraOffset;
            var cameraEndRotation = Quaternion.LookRotation(target + RevealLookOffset - cameraEnd, Vector3.up);
            Phase = TurnPhase.RevealEnter; PhaseElapsed = 0; Sample(0);
            while (id == generation && PhaseElapsed < RevealEnterDuration)
            {
                yield return null; if (id != generation || !ownsPose) yield break;
                if (skipMotion) break;
                PhaseElapsed = Mathf.Min(RevealEnterDuration, PhaseElapsed + Time.unscaledDeltaTime);
                float u = PhaseElapsed / Mathf.Max(.01f, RevealEnterDuration); Sample(u);
                float t = Mathf.Clamp01(cameraEase.Evaluate(u));
                presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(cameraStart, cameraEnd, t), Quaternion.Slerp(cameraRotation, cameraEndRotation, t));
                presentationCamera.fieldOfView = Mathf.Lerp(cameraFov, focus.OverviewFieldOfView, t);
            }
            if (id != generation || !ownsPose) yield break;
            Sample(1); presentationCamera.transform.SetPositionAndRotation(cameraEnd, cameraEndRotation);
            presentationCamera.fieldOfView = focus.OverviewFieldOfView;
            Phase = TurnPhase.ResultPose; PhaseElapsed = 0;
        }

        // 새 Settlement/Close 모션은 재생하지 않는다. 기존 계수기 표시를 읽을 수 있도록 카메라만 반납한다.
        public IEnumerator ReturnToLegacy()
        {
            if (!ownsCamera) yield break;
            int id = generation; var p = presentationCamera.transform.position; var q = presentationCamera.transform.rotation;
            skipMotion = false;
            Phase = TurnPhase.LegacyReturn; PhaseElapsed = 0;
            while (id == generation && PhaseElapsed < LegacyReturnDuration)
            {
                yield return null; if (id != generation || !ownsCamera) yield break;
                if (skipMotion) break;
                PhaseElapsed = Mathf.Min(LegacyReturnDuration, PhaseElapsed + Time.unscaledDeltaTime);
                float t = Mathf.SmoothStep(0, 1, PhaseElapsed / Mathf.Max(.01f, LegacyReturnDuration));
                presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(p, focus.OverviewPosition, t), Quaternion.Slerp(q, focus.OverviewRotation, t));
            }
            if (id != generation) yield break;
            focus.EndPresentationCamera(); ownsCamera = false; Phase = TurnPhase.ResultPose;
        }

        private void Configure(AnimationClip clip, Vector3 worldTarget)
        {
            track = clip; var root = model.MotionRoot; start = root.localPosition; rotation = root.localRotation;
            var lid = model.Watch.LidPivot.localRotation;
            model.SampleBatchClip(track, 0); sourceStart = root.localPosition; sourceRotation = root.localRotation;
            model.SampleBatchClip(track, 1); sourceEnd = root.localPosition;
            root.localPosition = start; root.localRotation = rotation; model.Watch.LidPivot.localRotation = lid;
            end = root.parent.InverseTransformPoint(worldTarget);
            var sourceDirection = root.parent.TransformVector(sourceEnd - sourceStart); sourceDirection.y = 0;
            var runtimeDirection = worldTarget - root.parent.TransformPoint(start); runtimeDirection.y = 0;
            residualBasis = sourceDirection.sqrMagnitude > .000001f && runtimeDirection.sqrMagnitude > .000001f
                ? Quaternion.FromToRotation(sourceDirection, runtimeDirection) : Quaternion.identity;
            Sample(0);
        }
        private void Sample(float u)
        {
            model.SampleBatchClip(track, u); var root = model.MotionRoot;
            var residual = root.parent.TransformVector(root.localPosition - Vector3.Lerp(sourceStart, sourceEnd, u));
            residual = residualBasis * residual;
            if (track == selectClip)
                residual += Vector3.up * (SelectClearance * Mathf.SmoothStep(0, 1, Mathf.Min(u / .18f, (1 - u) / .18f)));
            var sampledRotation = root.localRotation;
            root.localPosition = Vector3.Lerp(start, end, u) + root.parent.InverseTransformVector(residual);
            root.localRotation = rotation * Quaternion.Inverse(sourceRotation) * sampledRotation;
        }
        private IEnumerator Progress(int id, float duration, TurnPhase phase)
        {
            Phase = phase; PhaseElapsed = 0; skipMotion = false; Sample(0);
            while (id == generation && PhaseElapsed < duration)
            {
                yield return null; if (id != generation || !ownsPose) yield break;
                if (skipMotion) break;
                PhaseElapsed = Mathf.Min(duration, PhaseElapsed + Time.unscaledDeltaTime);
                Sample(PhaseElapsed / Mathf.Max(.01f, duration));
            }
            if (id == generation && ownsPose) Sample(1);
        }
        public void Cancel()
        {
            generation++;
            if (ownsPose && model) { model.CloseFrame(1); model.EndBatchPose(); }
            if (ownsCamera && focus) focus.EndPresentationCamera();
            else if (ownsPose && focus && focus.HasCameraControl)
            { focus.BeginPresentationCamera(); focus.EndPresentationCamera(); }
            ownsPose = ownsCamera = skipMotion = false; Phase = TurnPhase.Idle; PhaseElapsed = 0; ClockIndex = -1;
        }
        public void SkipMotion() { if (IsBusy) skipMotion = true; }
        private void OnDisable()
        {
            int index = Phase == TurnPhase.Closed || Phase == TurnPhase.Returning ? -1 : ClockIndex; Cancel();
            var controller = GetComponent<TimeTheftGameController>();
            if (controller) controller.RestoreClockPresentationAfterDisable(index);
        }
    }
}
