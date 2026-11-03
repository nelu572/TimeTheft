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
        public Vector3 SettlementOffset = new Vector3(0, 0, -1.05f);
        public Vector3 SettlementCameraOffset = new Vector3(-1.7f, 2.5f, -3.6f);
        [Tooltip("계수기와 공용 공개 위치의 중간에 더하는 시선 보정")]
        public Vector3 SettlementLookOffset = new Vector3(0, .04f, 0);
        [Min(0), Tooltip("실제 메시 최저점과 펠트 사이의 작은 접촉 여유")]
        public float TableContactGap = .003f;
        public Vector3 TurnCloseOffset = new Vector3(.213586f, -.06064f, -.003712f);
        [SerializeField] private AnimationCurve settlementCameraEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationClip SettlementClip => settlementClip;
        public AnimationClip CloseClip => closeClip;
        private Quaternion tableRotation, publicTableRotation;
        private Vector3 publicTablePosition;
        private MeshFilter[] tableMeshes;
        private Vector3[][] tableVertices;
        private bool onPublicTable;
        public bool IsOnPublicTable => onPublicTable;
        public float PublicTableHeight { get; private set; }

        private void CapturePublicTableBasis()
        {
            onPublicTable = false;
            tableRotation = model.MotionRoot.rotation;
            tableMeshes = model.Watch.GetComponentsInChildren<MeshFilter>();
            tableVertices = new Vector3[tableMeshes.Length][];
            for (int i = 0; i < tableMeshes.Length; i++) tableVertices[i] = tableMeshes[i].sharedMesh.vertices;
        }

        private float LowestMeshPoint()
        {
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < tableMeshes.Length; i++)
            {
                var matrix = tableMeshes[i].transform.localToWorldMatrix;
                foreach (var vertex in tableVertices[i]) lowest = Mathf.Min(lowest, matrix.MultiplyPoint3x4(vertex).y);
            }
            return lowest;
        }

        private void KeepAbovePublicTable()
        {
            float correction = PublicTableHeight + TableContactGap - LowestMeshPoint();
            if (correction > 0) model.MotionRoot.position += Vector3.up * correction;
        }
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
            var offset = SettlementOffset;
            if (IsOpponentTurn) offset.z = -offset.z;
            var target = center + offset;
            var settledRotation = IsOpponentTurn ? Quaternion.AngleAxis(180, Vector3.up) * tableRotation : tableRotation;
            var root = model.MotionRoot;
            var poseStart = root.position; var rotationStart = root.rotation;
            var openedLid = model.Watch.LidPivot.localRotation;
            // 선택 Collider는 논리 슬롯에 남는다. 공용 위치의 실제 펠트만 조회한다.
            PublicTableHeight = IsOpponentTurn ? center.y : poseStart.y - RevealOffset.y;
            Physics.SyncTransforms();
            float surface = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(target + Vector3.up * 3, Vector3.down, 8, ~0, QueryTriggerInteraction.Ignore))
                if (hit.normal.y > .9f && !hit.collider.GetComponentInParent<TabletopFocusTarget>())
                    surface = Mathf.Max(surface, hit.point.y);
            if (!float.IsNegativeInfinity(surface)) PublicTableHeight = surface;
            root.SetPositionAndRotation(target, settledRotation);
            target.y += PublicTableHeight + TableContactGap - LowestMeshPoint();
            root.SetPositionAndRotation(poseStart, rotationStart);
            Configure(settlementClip, target);
            var cameraEnd = center + SettlementCameraOffset;
            var counter = (view.PlayerLife.transform.position + view.AiLife.transform.position + view.Bank.transform.position) / 3;
            var look = Vector3.Lerp(counter, target, .5f) + SettlementLookOffset;
            var cameraEndRotation = Quaternion.LookRotation(look - cameraEnd, Vector3.up);
            Phase = TurnPhase.SettlementTransition; PhaseElapsed = 0;
            while (id == generation && ownsPose && PhaseElapsed < SettlementTransitionDuration)
            {
                yield return null; if (id != generation || !ownsPose) yield break;
                if (skipMotion) break;
                PhaseElapsed = Mathf.Min(SettlementTransitionDuration, PhaseElapsed + Time.unscaledDeltaTime);
                float u = PhaseElapsed / Mathf.Max(.01f, SettlementTransitionDuration); Sample(u);
                float t = Mathf.Clamp01(settlementCameraEase.Evaluate(u));
                root.rotation = Quaternion.Slerp(rotationStart, settledRotation, t);
                model.Watch.LidPivot.localRotation = openedLid;
                KeepAbovePublicTable();
                if (IsOpponentTurn) HoldOverviewCamera();
                else presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(cameraStart, cameraEnd, t), Quaternion.Slerp(cameraRotation, cameraEndRotation, t));
            }
            if (id != generation || !ownsPose) yield break;
            Sample(1); root.SetPositionAndRotation(target, settledRotation);
            model.Watch.LidPivot.localRotation = openedLid; KeepAbovePublicTable();
            publicTablePosition = root.position; publicTableRotation = settledRotation; onPublicTable = true;
            if (IsOpponentTurn) HoldOverviewCamera();
            else presentationCamera.transform.SetPositionAndRotation(cameraEnd, cameraEndRotation);
            SetSettlementPhase(TurnPhase.SettlementLead);
        }

        public IEnumerator CloseAndReturn(bool handoffToRemoval = false)
        {
            if (!isActiveAndEnabled || !ownsPose) yield break;
            int id = generation;
            Configure(closeClip, model.MotionRoot.position + (onPublicTable ? Vector3.zero : TurnCloseOffset));
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
