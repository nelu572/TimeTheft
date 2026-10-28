using System;
using System.Collections;
using UnityEngine;

namespace TimeTheft
{
    // 시각적 생명주기만 소유한다. Match·Face·정산에는 접근하지 않는다.
    [DisallowMultipleComponent]
    public sealed class H2ClockLifecyclePresentation : MonoBehaviour
    {
        public enum LifecyclePhase { Idle, Supplying, MovingToHatch, HatchCycle, BinHold }
        [SerializeField] private H2TableHatchView hatch;
        [SerializeField] private GameObject carrier;
        [SerializeField] private AnimationClip moveClip, discardMoveClip, dropClip, carrierPrepare, carrierNext;
        [SerializeField] private AnimationClip[] prepareClips = new AnimationClip[5], nextClips = new AnimationClip[5];
        [Header("회수·공급 · 원본 상대 타이밍 유지")]
        [Min(.01f)] public float MoveDuration = 16f / 24;
        [Min(.01f), Tooltip("개방·낙하·닫힘을 함께 늘이거나 줄인다")]
        public float HatchCycleDuration = 1;
        [Min(0)] public float BinHoldDuration = .25f;
        [Min(0)] public float DiscardBinHoldDuration = .5f;
        [Min(.01f)] public float SupplyDuration = 3;
        [Tooltip("원본 개구부 앵커의 월드 보정. 크라운·고리 여유 포함")]
        public Vector3 ApproachOffset = new Vector3(-.1484952f, .04704f, -.0344701f);
        public H2TableHatchView Hatch => hatch;
        public GameObject Carrier => carrier;
        public LifecyclePhase Phase { get; private set; }
        public float PhaseElapsed { get; private set; }
        public int ClockIndex { get; private set; } = -1;
        public int ClosedClockIndex { get; private set; } = -1;
        public bool IsDiscarding { get; private set; }
        public bool IsRunning => Phase != LifecyclePhase.Idle;
        public bool IsSupplying => Phase == LifecyclePhase.Supplying;
        public event Action<int, bool> RemoveStarted, RemoveCompleted;
        public event Action SupplyCompleted;
        private TimeTheftGameView view;
        private H2TurnPresentation turn;
        private ClockTokenView removing;
        private Renderer[] dropRenderers;
        private ClockTokenView[] supplying;
        private Collider[] colliders;
        private bool[] colliderEnabled;
        private TabletopFocusCamera focus;
        private bool ownsCamera, skip;
        private int generation;

        public bool CanPlay(TimeTheftGameView gameView)
        {
            if (!isActiveAndEnabled || !hatch || !hatch.IsReady || !carrier || !moveClip || !discardMoveClip || !dropClip ||
                !carrierPrepare || !carrierNext || !gameView || !gameView.FocusCamera || prepareClips.Length != 5 || nextClips.Length != 5) return false;
            var clocks = gameView.Clocks;
            if (clocks.Length != 5) return false;
            for (int i = 0; i < 5; i++) if (!clocks[i] || !clocks[i].H2Presentation || !clocks[i].H2Presentation.enabled || !prepareClips[i] || !nextClips[i]) return false;
            return true;
        }
        private void OnEnable()
        {
            turn = GetComponent<H2TurnPresentation>();
            if (turn) turn.CloseCompleted += OnClosed;
        }
        private void OnClosed(int index) => ClosedClockIndex = index;
        private void Lock(TimeTheftGameView gameView)
        {
            view = gameView; focus = view.FocusCamera; view.LockLifecycleInput();
            if (!focus.IsPresentationCameraOwned) focus.BeginPresentationCamera();
            ownsCamera = true;
        }
        private void DisableColliders(ClockTokenView[] clocks)
        {
            colliders = new Collider[clocks.Length]; colliderEnabled = new bool[clocks.Length];
            for (int i = 0; i < clocks.Length; i++)
            { colliders[i] = clocks[i].GetComponent<Collider>(); if (colliders[i]) { colliderEnabled[i] = colliders[i].enabled; colliders[i].enabled = false; } }
        }
        private void RestoreColliders()
        {
            if (colliders != null) for (int i = 0; i < colliders.Length; i++) if (colliders[i]) colliders[i].enabled = colliderEnabled[i];
            colliders = null; colliderEnabled = null;
        }
        public IEnumerator Supply(TimeTheftGameView gameView, bool nextBatch)
        {
            if (!CanPlay(gameView)) yield break;
            Cancel(); int id = ++generation; Lock(gameView); skip = false; hatch.ResetClosed();
            supplying = view.Clocks; DisableColliders(supplying);
            view.PhaseLabel.text = "새 묶음 · 시계를 공급하는 중";
            foreach (var clock in supplying) { clock.CloseFrame(1); clock.H2Presentation.BeginBatchPose(); }
            var clips = nextBatch ? nextClips : prepareClips; var trayClip = nextBatch ? carrierNext : carrierPrepare;
            var camera = focus.GetComponent<Camera>(); var cameraStart = camera.transform.position; var cameraRotation = camera.transform.rotation;
            carrier.SetActive(true); Phase = LifecyclePhase.Supplying; PhaseElapsed = 0;
            SampleSupply(clips, trayClip, 0);
            while (id == generation && PhaseElapsed < SupplyDuration)
            {
                yield return null; if (id != generation) yield break;
                if (skip) break;
                PhaseElapsed = Mathf.Min(SupplyDuration, PhaseElapsed + Time.unscaledDeltaTime);
                float u = PhaseElapsed / Mathf.Max(.01f, SupplyDuration);
                SampleSupply(clips, trayClip, u);
                float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(u * 3));
                camera.transform.SetPositionAndRotation(Vector3.Lerp(cameraStart, focus.OverviewPosition, t), Quaternion.Slerp(cameraRotation, focus.OverviewRotation, t));
            }
            if (id != generation) yield break;
            SampleSupply(clips, trayClip, 1); carrier.SetActive(false);
            camera.transform.SetPositionAndRotation(focus.OverviewPosition, focus.OverviewRotation);
            foreach (var clock in supplying) clock.H2Presentation.EndBatchPose();
            supplying = null; Finish(); SupplyCompleted?.Invoke();
        }
        private void SampleSupply(AnimationClip[] clips, AnimationClip tray, float u)
        {
            for (int i = 0; i < supplying.Length; i++) supplying[i].H2Presentation.SampleBatchClip(clips[i], u);
            H2AnimationSampling.Sample(tray, carrier, u * tray.length); carrier.SetActive(u < 69f / 72);
            Physics.SyncTransforms();
        }
        public IEnumerator Remove(TimeTheftGameView gameView, int index, bool discarded)
        {
            if (!CanPlay(gameView) || index < 0 || index >= 5) yield break;
            int id = ++generation; IsDiscarding = discarded; ClockIndex = index; removing = gameView.Clocks[index];
            var model = removing.H2Presentation;
            dropRenderers = model.Watch.GetComponentsInChildren<Renderer>();
            if (!discarded && (!turn || !turn.ReleaseClosedPose(index))) { removing = null; ClockIndex = -1; yield break; }
            if (discarded) model.BeginBatchPose();
            Lock(gameView); DisableColliders(new[] { removing }); skip = false;
            view.PhaseLabel.text = discarded ? "마지막 시계 · 열지 않고 폐기합니다" : "사용한 시계를 회수하는 중";
            removing.CloseFrame(1);
            var root = model.MotionRoot; var visiblePosition = root.position; var visibleRotation = root.rotation;
            removing.SetSelected(false); root.SetPositionAndRotation(visiblePosition, visibleRotation); hatch.ResetClosed();
            var start = root.position; var rotation = root.localRotation;
            var clip = discarded ? discardMoveClip : moveClip;
            model.SampleBatchClip(clip, 0); var sourceStart = root.localPosition; var sourceRotation = root.localRotation;
            model.SampleBatchClip(clip, 1); var sourceEnd = root.localPosition;
            root.position = start; root.localRotation = rotation;
            var target = hatch.Anchor.position + ApproachOffset;
            var camera = focus.GetComponent<Camera>(); var cameraStart = camera.transform.position; var cameraRotation = camera.transform.rotation;
            Phase = LifecyclePhase.MovingToHatch; PhaseElapsed = 0; RemoveStarted?.Invoke(index, discarded);
            while (id == generation && PhaseElapsed < MoveDuration)
            {
                yield return null; if (id != generation) yield break;
                if (skip) break;
                PhaseElapsed = Mathf.Min(MoveDuration, PhaseElapsed + Time.unscaledDeltaTime);
                SampleMove(model, clip, PhaseElapsed / Mathf.Max(.01f, MoveDuration), start, target, rotation, sourceStart, sourceEnd, sourceRotation);
                float t = Mathf.SmoothStep(0, 1, PhaseElapsed / Mathf.Max(.01f, MoveDuration));
                camera.transform.SetPositionAndRotation(Vector3.Lerp(cameraStart, focus.OverviewPosition, t), Quaternion.Slerp(cameraRotation, focus.OverviewRotation, t));
            }
            if (id != generation) yield break;
            SampleMove(model, clip, 1, start, target, rotation, sourceStart, sourceEnd, sourceRotation);
            camera.transform.SetPositionAndRotation(focus.OverviewPosition, focus.OverviewRotation);
            KeepInsideAperture(root, DropBounds());
            var dropStart = root.position; var dropRotation = root.localRotation;
            model.SampleBatchClip(dropClip, 0); var dropSource = root.localPosition; var dropSourceRotation = root.localRotation;
            root.position = dropStart; root.localRotation = dropRotation;
            Phase = LifecyclePhase.HatchCycle; PhaseElapsed = 0;
            while (id == generation && PhaseElapsed < HatchCycleDuration)
            {
                yield return null; if (id != generation) yield break;
                if (skip) break;
                PhaseElapsed = Mathf.Min(HatchCycleDuration, PhaseElapsed + Time.unscaledDeltaTime);
                SampleDrop(model, PhaseElapsed / Mathf.Max(.01f, HatchCycleDuration), dropStart, dropRotation, dropSource, dropSourceRotation);
            }
            if (id != generation) yield break;
            SampleDrop(model, 1, dropStart, dropRotation, dropSource, dropSourceRotation);
            Phase = LifecyclePhase.BinHold; PhaseElapsed = 0;
            float hold = discarded ? DiscardBinHoldDuration : BinHoldDuration;
            while (id == generation && PhaseElapsed < hold && !skip)
            { yield return null; PhaseElapsed += Time.unscaledDeltaTime; }
            if (id != generation) yield break;
            removing.gameObject.SetActive(false); model.EndBatchPose(); removing = null;
            Finish(); RemoveCompleted?.Invoke(index, discarded);
        }
        private static void SampleMove(H2ClockPresentation model, AnimationClip clip, float u, Vector3 start, Vector3 end, Quaternion rotation, Vector3 sourceStart, Vector3 sourceEnd, Quaternion sourceRotation)
        {
            model.SampleBatchClip(clip, u); var root = model.MotionRoot;
            var delta = sourceEnd - sourceStart;
            float travel = Mathf.Clamp01(Vector3.Dot(root.localPosition - sourceStart, delta) / delta.sqrMagnitude);
            var residual = root.parent.TransformVector(root.localPosition - Vector3.Lerp(sourceStart, sourceEnd, travel));
            var sourceAxis = root.parent.TransformVector(delta); sourceAxis.y = 0;
            var targetAxis = end - start; targetAxis.y = 0;
            if (sourceAxis.sqrMagnitude > .00001f && targetAxis.sqrMagnitude > .00001f)
                residual = Quaternion.FromToRotation(sourceAxis, targetAxis) * residual;
            root.position = Vector3.Lerp(start, end, travel) + residual;
            root.localRotation = rotation * Quaternion.Inverse(sourceRotation) * root.localRotation;
            Physics.SyncTransforms();
        }
        private void SampleDrop(H2ClockPresentation model, float u, Vector3 start, Quaternion rotation, Vector3 source, Quaternion sourceRotation)
        {
            model.SampleBatchClip(dropClip, Mathf.Clamp01(u / (19f / 24))); var root = model.MotionRoot;
            root.position = start + root.parent.TransformVector(root.localPosition - source);
            root.localRotation = rotation * Quaternion.Inverse(sourceRotation) * root.localRotation;
            // FBX 바운스의 최저점도 실제 수납부 바닥 아래로 내려가지 않게 접촉을 보정한다.
            var bounds = dropRenderers[0].bounds;
            foreach (var part in dropRenderers) bounds.Encapsulate(part.bounds);
            if (bounds.min.y < hatch.FloorHeight) root.position += Vector3.up * (hatch.FloorHeight - bounds.min.y);
            if (bounds.min.y <= hatch.Anchor.position.y + .12f && bounds.max.y >= hatch.Anchor.position.y)
                KeepInsideAperture(root, bounds);
            hatch.SampleCycle(u);
        }
        private Bounds DropBounds()
        {
            var bounds = dropRenderers[0].bounds;
            foreach (var part in dropRenderers) bounds.Encapsulate(part.bounds);
            return bounds;
        }
        private void KeepInsideAperture(Transform root, Bounds bounds)
        {
            var center = hatch.Anchor.position; var half = hatch.ClearApertureSize * .5f;
            float x = Mathf.Max(0, center.x - half.x - bounds.min.x) + Mathf.Min(0, center.x + half.x - bounds.max.x);
            float z = Mathf.Max(0, center.z - half.y - bounds.min.z) + Mathf.Min(0, center.z + half.y - bounds.max.z);
            root.position += new Vector3(x, 0, z);
        }
        private void Finish()
        {
            dropRenderers = null; RestoreColliders(); if (carrier) carrier.SetActive(false); if (hatch) hatch.ResetClosed();
            if (ownsCamera && focus) focus.EndPresentationCamera(); ownsCamera = false;
            Phase = LifecyclePhase.Idle; ClockIndex = -1; IsDiscarding = false; skip = false;
        }
        public void SkipMotion() { if (IsRunning) skip = true; }
        public void Cancel()
        {
            generation++;
            if (removing) { removing.gameObject.SetActive(false); removing.H2Presentation.EndBatchPose(); removing = null; }
            if (supplying != null) { foreach (var clock in supplying) if (clock) clock.H2Presentation.EndBatchPose(); supplying = null; }
            Finish(); ClosedClockIndex = -1;
        }
        private void OnDisable()
        {
            if (turn) turn.CloseCompleted -= OnClosed;
            Cancel();
        }
    }
}
