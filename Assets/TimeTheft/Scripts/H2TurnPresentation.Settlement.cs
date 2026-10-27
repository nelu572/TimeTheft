using System;
using System.Collections;
using UnityEngine;

namespace TimeTheft
{
    public sealed partial class H2TurnPresentation
    {
        [SerializeField] private AnimationClip settlementClip, closeClip;
        [Header("정산/닫힘 · 관찰 시간은 기존 Settings의 Reveal/Result Hold Duration")]
        [Min(.01f)] public float SettlementTransitionDuration = .8f;
        [Tooltip("Match의 실제 정산 경로/이동량을 보여 준 뒤 계수기 보간을 시작하기까지")]
        [Min(0)] public float SettlementLeadDuration = .25f;
        [Min(.01f)] public float TurnCloseDuration = .45f;
        [Min(.01f)] public float TurnCameraReturnDuration = .55f;
        [Header("정산용 임시 앵커 · 기존 논리 슬롯/계수기 배치는 보존")]
        public Vector3 SettlementOffset = new Vector3(-1.3f, .12f, -.7f);
        public Vector3 SettlementCameraOffset = new Vector3(-2.05f, 2.2f, -3.05f);
        [Tooltip("세 계수기 표시부의 평균 월드 위치에 더하는 시선 보정")]
        public Vector3 SettlementLookOffset = new Vector3(1f, -.25f, 0);
        public Vector3 TurnCloseOffset = new Vector3(.213586f, -.06064f, -.003712f);
        [SerializeField] private AnimationCurve settlementCameraEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationClip SettlementClip => settlementClip;
        public AnimationClip CloseClip => closeClip;
        // Controller가 이 경계 뒤 회수 표현을 시작한다. 정산/턴 전이는 이 이벤트가 실행하지 않는다.
        public event Action<int> CloseCompleted;
        public bool ReleaseClosedPose(int index)
        {
            if (!ownsPose || ClockIndex != index || Phase != TurnPhase.Closed) return false;
            ownsPose = ownsCamera = false;
            return true;
        }
        public bool CanSettle(TimeTheftGameView gameView, int index) => CanPlay(gameView, index) && settlementClip && closeClip;

        public void SetSettlementPhase(TurnPhase phase)
        {
            if (!isActiveAndEnabled || !ownsPose) return;
            Phase = phase; PhaseElapsed = 0;
        }

        public IEnumerator SettlementTransition()
        {
            if (!isActiveAndEnabled || !ownsPose || !ownsCamera) yield break;
            int id = generation; skipMotion = false;
            var cameraStart = presentationCamera.transform.position; var cameraRotation = presentationCamera.transform.rotation;
            var target = center + SettlementOffset;
            Configure(settlementClip, target);
            var cameraEnd = center + SettlementCameraOffset;
            var look = (view.PlayerLife.transform.position + view.AiLife.transform.position + view.Bank.transform.position) / 3 + SettlementLookOffset;
            var cameraEndRotation = Quaternion.LookRotation(look - cameraEnd, Vector3.up);
            Phase = TurnPhase.SettlementTransition; PhaseElapsed = 0;
            while (id == generation && ownsPose && PhaseElapsed < SettlementTransitionDuration)
            {
                yield return null; if (id != generation || !ownsPose) yield break;
                if (skipMotion) break;
                PhaseElapsed = Mathf.Min(SettlementTransitionDuration, PhaseElapsed + Time.unscaledDeltaTime);
                float u = PhaseElapsed / Mathf.Max(.01f, SettlementTransitionDuration); Sample(u);
                float t = Mathf.Clamp01(settlementCameraEase.Evaluate(u));
                presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(cameraStart, cameraEnd, t), Quaternion.Slerp(cameraRotation, cameraEndRotation, t));
            }
            if (id != generation || !ownsPose) yield break;
            Sample(1); presentationCamera.transform.SetPositionAndRotation(cameraEnd, cameraEndRotation);
            SetSettlementPhase(TurnPhase.SettlementLead);
        }

        public IEnumerator CloseAndReturn(bool handoffToRemoval = false)
        {
            if (!isActiveAndEnabled || !ownsPose) yield break;
            int id = generation;
            Configure(closeClip, model.MotionRoot.position + TurnCloseOffset);
            yield return Progress(id, TurnCloseDuration, TurnPhase.Closing);
            if (id != generation || !ownsPose) yield break;
            Phase = TurnPhase.Closed; PhaseElapsed = 0;
            CloseCompleted?.Invoke(ClockIndex);
            if (handoffToRemoval) yield break;
            if (id != generation || !ownsCamera) yield break;
            var p = presentationCamera.transform.position; var q = presentationCamera.transform.rotation;
            Phase = TurnPhase.Returning; PhaseElapsed = 0; skipMotion = false;
            while (id == generation && ownsCamera && PhaseElapsed < TurnCameraReturnDuration)
            {
                yield return null; if (id != generation || !ownsCamera) yield break;
                if (skipMotion) break;
                PhaseElapsed = Mathf.Min(TurnCameraReturnDuration, PhaseElapsed + Time.unscaledDeltaTime);
                float t = Mathf.SmoothStep(0, 1, PhaseElapsed / Mathf.Max(.01f, TurnCameraReturnDuration));
                presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(p, focus.OverviewPosition, t), Quaternion.Slerp(q, focus.OverviewRotation, t));
            }
            if (id != generation || !ownsCamera) yield break;
            focus.EndPresentationCamera(); ownsCamera = false; Phase = TurnPhase.Closed;
        }
    }
}
