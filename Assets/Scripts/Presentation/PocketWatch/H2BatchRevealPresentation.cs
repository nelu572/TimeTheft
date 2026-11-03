using System;
using System.Collections;
using UnityEngine;

namespace TimeTheft
{
    // 공개된 모델의 표시/카메라만 제어한다. 관찰 타이머와 Match 전이는 Controller에 남긴다.
    [DisallowMultipleComponent]
    public sealed class H2BatchRevealPresentation : MonoBehaviour
    {
        public enum RevealPhase { Idle, Preparing, Revealing, Observing, Closing, Returning }
        [SerializeField] private AnimationClip[] revealClips = new AnimationClip[5];
        [SerializeField] private AnimationClip[] closeClips = new AnimationClip[5];
        [Header("초기 공개 시간 · 관찰 시간은 TimeTheftSettings의 Preview Duration")]
        [Min(0)] public float PreparationDuration = .15f;
        [Min(.01f)] public float RevealDuration = 2f;
        [Min(.01f)] public float CameraApproachDuration = 1.1f;
        [Min(.01f)] public float CloseDuration = .6f;
        [Min(.01f)] public float CameraReturnDuration = .95f;
        [Header("임시 관찰 카메라 · 기본 카메라/FOV는 보존")]
        public Vector3 CameraOffset = new Vector3(0, 2.2f, -1.55f);
        public Vector3 LookOffset = new Vector3(-.15f, 0, .2f);
        [Header("공개 중 가독성 · 원본 모션에 더하는 임시 자세")]
        [Range(0, 35)] public float ObservationTiltDegrees = 20;
        [Min(0)] public float ObservationLift = .14f;
        [SerializeField] private AnimationCurve cameraEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        private ClockTokenView[] clocks;
        private TimeTheftGameView gameView;
        private TabletopFocusCamera focus;
        private Camera presentationCamera;
        private Vector3 startPosition, nearPosition;
        private Quaternion startRotation, nearRotation;
        private bool ownsPresentation;
        public RevealPhase Phase { get; private set; }
        public float PhaseElapsed { get; private set; }
        public float ReadableSince { get; private set; }
        public float ClosingStarted { get; private set; }
        public bool IsRunning => ownsPresentation;
        public AnimationClip[] RevealClips => (AnimationClip[])revealClips.Clone();
        public AnimationClip[] CloseClips => (AnimationClip[])closeClips.Clone();

        public bool CanPlay(TimeTheftGameView view)
        {
            if (!isActiveAndEnabled || !view || !view.FocusCamera || revealClips.Length != 5 || closeClips.Length != 5) return false;
            var tokens = view.Clocks;
            for (int i = 0; i < 5; i++)
                if (!revealClips[i] || !closeClips[i] || !tokens[i].H2Presentation || !tokens[i].H2Presentation.enabled) return false;
            return true;
        }

        public void Prepare(TimeTheftGameView view)
        {
            Cancel(); gameView = view; clocks = view.Clocks; focus = view.FocusCamera;
            presentationCamera = focus.GetComponent<Camera>();
            startPosition = presentationCamera.transform.position; startRotation = presentationCamera.transform.rotation;
            focus.BeginPresentationCamera(); ownsPresentation = true;
            Vector3 center = Vector3.zero;
            foreach (var token in clocks)
            {
                token.SetSelectable(false); token.SetSelected(false);
                token.H2Presentation.BeginBatchPose(); center += token.transform.position / clocks.Length;
            }
            nearPosition = center + CameraOffset;
            nearRotation = Quaternion.LookRotation(center + LookOffset - nearPosition, Vector3.up);
            for (int i = 0; i < 5; i++) SampleReveal(i, 0);
            Phase = RevealPhase.Preparing; PhaseElapsed = 0;
            gameView.PhaseLabel.text = "새 묶음 · 시계를 공개합니다";
        }

        public IEnumerator Reveal()
        {
            if (!ownsPresentation) yield break;
            yield return Progress(PreparationDuration, RevealPhase.Preparing, null);
            if (!ownsPresentation) yield break;
            float duration = Mathf.Max(RevealDuration, CameraApproachDuration);
            yield return Progress(duration, RevealPhase.Revealing, elapsed =>
            {
                for (int i = 0; i < 5; i++) SampleReveal(i, Mathf.Clamp01(elapsed / Mathf.Max(.01f, RevealDuration)));
                float t = Ease(elapsed / Mathf.Max(.01f, CameraApproachDuration));
                presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(startPosition, nearPosition, t), Quaternion.Slerp(startRotation, nearRotation, t));
            });
            if (!ownsPresentation) yield break;
            presentationCamera.transform.SetPositionAndRotation(nearPosition, nearRotation);
            Phase = RevealPhase.Observing; PhaseElapsed = 0; ReadableSince = Time.realtimeSinceStartup;
            gameView.PhaseLabel.text = "숫자와 종류를 기억하세요";
        }

        public IEnumerator CloseAndReturn()
        {
            if (!ownsPresentation) yield break;
            ClosingStarted = Time.realtimeSinceStartup;
            gameView.PhaseLabel.text = "덮개를 닫는 중";
            yield return Progress(CloseDuration, RevealPhase.Closing, elapsed =>
            {
                for (int i = 0; i < 5; i++) clocks[i].H2Presentation.SampleBatchClip(closeClips[i], Mathf.Clamp01(elapsed / Mathf.Max(.01f, CloseDuration)));
            });
            if (!ownsPresentation) yield break;
            foreach (var token in clocks) { token.CloseFrame(1); token.H2Presentation.CaptureBatchReturnPose(); }
            gameView.PhaseLabel.text = "테이블로 돌아가는 중";
            yield return Progress(CameraReturnDuration, RevealPhase.Returning, elapsed =>
            {
                float t = Ease(elapsed / Mathf.Max(.01f, CameraReturnDuration));
                foreach (var token in clocks) token.H2Presentation.ReturnBatchPose(t);
                presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(nearPosition, focus.OverviewPosition, t), Quaternion.Slerp(nearRotation, focus.OverviewRotation, t));
            });
            Release();
        }

        private void SampleReveal(int index, float progress)
        {
            var model = clocks[index].H2Presentation;
            model.SampleBatchClip(revealClips[index], progress);
            float weight = Mathf.SmoothStep(0, 1, progress);
            model.MotionRoot.localRotation = Quaternion.Euler(ObservationTiltDegrees * weight, 0, 0) * model.MotionRoot.localRotation;
            model.MotionRoot.position += Vector3.up * (ObservationLift * weight);
        }

        private float Ease(float value) => Mathf.Clamp01(cameraEase.Evaluate(Mathf.Clamp01(value)));
        private IEnumerator Progress(float duration, RevealPhase phase, Action<float> frame)
        {
            Phase = phase; PhaseElapsed = 0; duration = Mathf.Max(0, duration); frame?.Invoke(0);
            while (ownsPresentation && PhaseElapsed < duration)
            {
                yield return null;
                if (!ownsPresentation) yield break;
                PhaseElapsed = Mathf.Min(duration, PhaseElapsed + Time.unscaledDeltaTime); frame?.Invoke(PhaseElapsed);
            }
        }

        public void Cancel()
        {
            if (!ownsPresentation) return;
            if (clocks != null) foreach (var token in clocks) if (token) token.CloseFrame(1);
            Release();
        }
        private void Release()
        {
            if (clocks != null) foreach (var token in clocks) if (token && token.H2Presentation) token.H2Presentation.EndBatchPose();
            if (focus) focus.EndPresentationCamera();
            ownsPresentation = false; Phase = RevealPhase.Idle; PhaseElapsed = 0;
        }
        private void OnDisable() => Cancel();
    }
}

