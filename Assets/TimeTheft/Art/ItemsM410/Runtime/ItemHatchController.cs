using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TimeTheft.ItemPresentation
{
    public enum HatchOperation { SingleDispose, Replace, AddGive }
    public enum HatchPresentationEvent { Started, FloorClear, VisualsRemoved, VisualsGiven, Restored, Cancelled }

    public sealed class ItemHatchRequest
    {
        public readonly string Id;
        public readonly HatchOperation Operation;
        public readonly Transform Location;
        public readonly GameObject[] OldVisuals, NewVisuals;
        public Action<bool> Finished;
        public bool Ready = true;
        public ItemHatchRequest(string id, HatchOperation operation, Transform location, GameObject[] oldVisuals, GameObject[] newVisuals)
        { Id = id; Operation = operation; Location = location; OldVisuals = oldVisuals ?? Array.Empty<GameObject>(); NewVisuals = newVisuals ?? Array.Empty<GameObject>(); }
    }

    [Serializable]
    public sealed class ItemHatchModule
    {
        public HatchOperation Operation;
        public GameObject Root, Model;
        public AnimationClip Clip;
        public Transform Tray;
        public Renderer Floor;
        public Vector2 Aperture;
        public int FloorClearFrame, CleanupFrame, GivenFrame;
        [NonSerialized] public ItemPoseSnapshot Pose;
        [NonSerialized] public Vector3 InitialPosition, InitialScale;
        [NonSerialized] public Quaternion InitialRotation;
    }

    // 하나의 실제 상판에 한 요청만 개방한다. 대기 중에는 물체/표면을 움직이지 않는다.
    public sealed class ItemHatchController : MonoBehaviour
    {
        public ItemHatchModule[] Modules;
        public ItemHatchSurface Surface;
        [Range(1, 16)] public int QueueLimit = 8;
        [Min(.05f)] public float PlaybackSpeed = 1f;
        public bool FitSingleDisposeToVisuals = true;
        public bool FitSupplyToVisuals = true;
        [Min(.01f)] public float AperturePadding = .08f;
        public bool Busy => active != null;
        public int WaitingCount => queue.Count;
        public string ActiveRequestId => active?.Id;
        public Rect ActiveRoomRect => Surface ? Surface.ActiveRoomRect : default;
        public string LastError { get; private set; }
        public event Action<string, HatchPresentationEvent> Presented;
        private readonly Queue<ItemHatchRequest> queue = new Queue<ItemHatchRequest>();
        private readonly HashSet<string> accepted = new HashSet<string>();
        private readonly HashSet<string> cancelled = new HashSet<string>();
        private ItemHatchRequest active;
        private ItemHatchModule module;
        private Coroutine routine;
        private VisualState[] oldState, newState;
        private Transform opening;
        private Vector2 aperture;
        private bool initialized, resetting;

        private sealed class VisualState
        {
            public GameObject Visual;
            private readonly Transform parent;
            private readonly Vector3 position, scale;
            private readonly Quaternion rotation;
            private readonly bool active;
            public Vector3 WorldPosition;
            public VisualState(GameObject go)
            { Visual = go; parent = go.transform.parent; position = go.transform.localPosition; rotation = go.transform.localRotation; scale = go.transform.localScale; WorldPosition = go.transform.position; active = go.activeSelf; }
            public void Restore()
            { if (!Visual) return; Visual.transform.SetParent(parent, false); Visual.transform.localPosition = position; Visual.transform.localRotation = rotation; Visual.transform.localScale = scale; Visual.SetActive(active); }
        }

        public void Initialize()
        {
            if (initialized) return;
            if (Modules == null || Modules.Length == 0) { LastError = "해치 모듈이 없습니다."; return; }
            foreach (var value in Modules)
            {
                if (value == null || !value.Root || !value.Model || !value.Clip || value.Aperture.x <= 0 || value.Aperture.y <= 0)
                { LastError = "해치 모델·클립·개구부 연결을 확인하세요."; return; }
                value.Pose = new ItemPoseSnapshot(value.Model);
                value.InitialPosition = value.Root.transform.localPosition; value.InitialRotation = value.Root.transform.localRotation; value.InitialScale = value.Root.transform.localScale;
                if (!value.Floor)
                    foreach (var renderer in value.Model.GetComponentsInChildren<Renderer>(true))
                        if (renderer.name.IndexOf("LowerPanel", StringComparison.OrdinalIgnoreCase) >= 0) { value.Floor = renderer; break; }
                value.Root.SetActive(false);
            }
            opening = new GameObject("Active item hatch opening").transform;
            opening.SetParent(transform, false);
            initialized = true;
            if (Surface) Surface.RestoreSurface();
        }

        public bool Enqueue(ItemHatchRequest request)
        {
            if (resetting || request == null || string.IsNullOrEmpty(request.Id) || !request.Location || accepted.Contains(request.Id) || queue.Count >= QueueLimit) return false;
            foreach (var visual in request.OldVisuals) if (!visual) return false;
            foreach (var visual in request.NewVisuals) if (!visual) return false;
            Initialize(); if (!initialized) return false;
            // 공급물은 큐 대기 중에도 완성된 상판 위에 먼저 나타나지 않는다.
            foreach (var visual in request.NewVisuals) if (Array.IndexOf(request.OldVisuals, visual) < 0) visual.SetActive(false);
            accepted.Add(request.Id); queue.Enqueue(request); Pump(); return true;
        }
        private void Pump()
        {
            if (resetting || active != null || queue.Count == 0) return;
            var request = queue.Dequeue();
            if (cancelled.Remove(request.Id)) { request.Finished?.Invoke(false); Pump(); return; }
            active = request; module = null;
            foreach (var candidate in Modules) if (candidate.Operation == request.Operation) { module = candidate; break; }
            if (module == null) { LastError = "요청한 해치 모듈이 없습니다."; Complete(false, true); return; }
            oldState = Capture(request.OldVisuals); newState = Capture(request.NewVisuals);
            // Ready=false 동안 원래 닫힌 상판을 유지하고 원본 Exit가 가까운 착지점에 도달하기를 기다린다.
            var running = StartCoroutine(Execute());
            if (active == request) routine = running;
        }
        private static VisualState[] Capture(GameObject[] visuals)
        { var result = new VisualState[visuals.Length]; for (int i = 0; i < visuals.Length; i++) result[i] = new VisualState(visuals[i]); return result; }

        private bool ConfigureOpening()
        {
            if (!active.Location) { LastError = "해치 위치가 사라졌습니다."; return false; }
            if (active.NewVisuals.Length > 0 && !module.Tray)
            { LastError = "공급에는 원본 RisingTray Transform이 필요합니다."; return false; }
            opening.SetPositionAndRotation(active.Location.position, active.Location.rotation);
            aperture = module.Aperture;
            var fittedVisuals = active.OldVisuals;
            if (active.Operation != HatchOperation.SingleDispose)
            {
                fittedVisuals = new GameObject[active.OldVisuals.Length + active.NewVisuals.Length];
                active.OldVisuals.CopyTo(fittedVisuals, 0); active.NewVisuals.CopyTo(fittedVisuals, active.OldVisuals.Length);
            }
            bool fit = active.Operation == HatchOperation.SingleDispose ? FitSingleDisposeToVisuals : active.Operation != HatchOperation.Replace && FitSupplyToVisuals;
            if (active.Operation == HatchOperation.Replace && TryBounds(fittedVisuals, opening, out var commonBounds))
                aperture = new Vector2(Mathf.Max(aperture.x, 2 * Mathf.Max(Mathf.Abs(commonBounds.min.x), Mathf.Abs(commonBounds.max.x)) + AperturePadding),
                    Mathf.Max(aperture.y, 2 * Mathf.Max(Mathf.Abs(commonBounds.min.z), Mathf.Abs(commonBounds.max.z)) + AperturePadding));
            if (fit && TryBounds(fittedVisuals, opening, out var bounds))
            {
                // 실제 착지/보유 몸체의 합집합만 사용한다. 다른 보유품과 원본 모션은 이동시키지 않는다.
                opening.position += opening.right * bounds.center.x + opening.forward * bounds.center.z;
                aperture = new Vector2(Mathf.Max(.24f, bounds.size.x + AperturePadding), Mathf.Max(.24f, bounds.size.z + AperturePadding));
            }
            if (!WithinOpening(active.OldVisuals, opening, aperture) || !WithinOpening(active.NewVisuals, opening, aperture))
            { TryBounds(fittedVisuals, opening, out var failedBounds); LastError = "실제 물체가 요청한 개구부에 들어가지 않습니다: " + failedBounds + " / " + aperture; return false; }
            if (!Surface || !Surface.Configure(opening, aperture))
            { LastError = Surface ? Surface.LastError : "실제 상판 개구부가 연결되지 않았습니다."; return false; }
            foreach (var value in Modules) { value.Pose.Restore(); value.Root.SetActive(value == module); }
            PositionModule();
            module.Clip.SampleAnimation(module.Model, module.Clip.length);
            Surface.UpdatePanelUV(module.Model);
            module.Pose.Restore(); PositionModule();
            if (!module.Floor) { LastError = "실제 LowerPanel Renderer가 연결되지 않았습니다."; return false; }
            return true;
        }
        private void PositionModule()
        {
            module.Root.transform.localScale = Vector3.Scale(module.InitialScale, new Vector3(aperture.x / module.Aperture.x, 1, aperture.y / module.Aperture.y));
            module.Root.transform.SetPositionAndRotation(opening.position, opening.rotation);
        }

        private IEnumerator Execute()
        {
            var request = active;
            var cycle = ExecuteCycle();
            while (true)
            {
                bool next = false;
                Exception failure = null;
                try { next = cycle.MoveNext(); }
                catch (Exception exception) { failure = exception; }
                if (failure != null)
                {
                    LastError = failure.Message;
                    if (active == request) Complete(false, true);
                    Debug.LogException(failure, this);
                    yield break;
                }
                if (!next) yield break;
                yield return cycle.Current;
            }
        }

        private IEnumerator ExecuteCycle()
        {
            var request = active;
            Presented?.Invoke(request.Id, HatchPresentationEvent.Started);
            while (!request.Ready) yield return null;
            // 큐에 들어온 시점이 아니라 Ready 시점의 안정된 레이아웃을 최종 공급 자세로 캡처한다.
            newState = Capture(request.NewVisuals);
            if (!ConfigureOpening()) { Complete(false, true); yield break; }
            foreach (var value in oldState) if (value.Visual) value.WorldPosition = value.Visual.transform.position;
            foreach (var value in newState) if (value.Visual && Array.IndexOf(request.OldVisuals, value.Visual) < 0) value.Visual.SetActive(false);
            module.Clip.SampleAnimation(module.Model, module.Clip.length);
            var finalTray = module.Tray ? module.Tray.position : opening.position;
            module.Pose.Restore(); PositionModule();
            float duration = module.Clip.length, elapsed = 0, clearFrame = 0;
            bool floorClear = false, removed = false, given = false;
            do
            {
                float time = Mathf.Min(elapsed, duration), frame = 1 + time * 30;
                module.Clip.SampleAnimation(module.Model, time);
                // 원본 트레이의 앞뒤 이동은 제거하고 최종 해치 바로 아래에서 같은 높이 곡선으로 상승한다.
                if (module.Tray)
                    module.Tray.position = new Vector3(finalTray.x, module.Tray.position.y, finalTray.z);
                var trayDelta = module.Tray ? module.Tray.position - finalTray : Vector3.zero;
                // 비활성 공급물도 먼저 실제 트레이의 원본 자세에 둔다. 활성화 콜백에 완성 보유 자세를 노출하지 않는다.
                foreach (var value in newState)
                    if (value.Visual && (Array.IndexOf(request.OldVisuals, value.Visual) < 0 || removed)) value.Visual.transform.position = value.WorldPosition + (!given ? trayDelta : Vector3.zero);
                // 지정 프레임과 실제 패널의 하강을 모두 확인한 뒤에만 낙하시킨다.
                if (!floorClear && frame >= module.FloorClearFrame && module.Floor.bounds.max.y <= opening.position.y - .035f)
                { floorClear = true; clearFrame = frame; Presented?.Invoke(request.Id, HatchPresentationEvent.FloorClear); }
                if (floorClear)
                {
                    float fall = Mathf.Clamp01((frame - clearFrame) / Mathf.Max(1, module.CleanupFrame - clearFrame - 1));
                    foreach (var value in oldState) if (value.Visual && !removed) value.Visual.transform.position = value.WorldPosition - opening.up * (4.8f * fall * fall);
                    foreach (var value in newState)
                    {
                        if (!value.Visual) continue;
                        // FloorClear 이전에는 숨김을 유지하고, 이후에만 이미 트레이 위에 있는 물체를 공개한다.
                        if (Array.IndexOf(request.OldVisuals, value.Visual) < 0 || removed) value.Visual.SetActive(true);
                    }
                }
                if (!removed && floorClear && frame >= module.CleanupFrame && AllBelow(request.OldVisuals, opening.position.y - .02f))
                {
                    removed = true;
                    foreach (var value in oldState) if (value.Visual) value.Visual.SetActive(false);
                    if (oldState.Length > 0) Presented?.Invoke(request.Id, HatchPresentationEvent.VisualsRemoved);
                    foreach (var value in newState) if (value.Visual) { value.Visual.transform.position = value.WorldPosition + trayDelta; value.Visual.SetActive(true); }
                }
                // Import 시작 프레임의 오프셋이 있어도 원본 트레이가 실제 최종점에 도착하기 전에 위로 순간이동하지 않는다.
                if (!given && floorClear && frame >= module.GivenFrame && (!module.Tray || trayDelta.sqrMagnitude <= .000004f))
                {
                    given = true;
                    foreach (var value in newState) if (value.Visual) value.Visual.transform.position = value.WorldPosition;
                    if (newState.Length > 0) Presented?.Invoke(request.Id, HatchPresentationEvent.VisualsGiven);
                }
                if (time >= duration) break;
                yield return null;
                // Editor/GPU 정체 뒤 원본 공급 프레임을 한 번에 건너뛰지 않는다. 정상 프레임 속도와 궤적은 동일하다.
                elapsed += Mathf.Min(Mathf.Min(Time.unscaledDeltaTime, .05f) * PlaybackSpeed, .12f);
            } while (true);
            bool success = floorClear && (oldState.Length == 0 || removed) && (newState.Length == 0 || given);
            if (!success) LastError = "패널 통로 또는 완전한 지하 도달이 확인되지 않았습니다.";
            Complete(success, !success);
        }

        private static bool AllBelow(GameObject[] visuals, float height)
        {
            foreach (var visual in visuals)
            {
                if (!visual) continue;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled && renderer.bounds.max.y > height) return false;
            }
            return true;
        }
        private static bool TryBounds(GameObject[] visuals, Transform location, out Bounds bounds)
        {
            bounds = default; bool found = false;
            foreach (var visual in visuals)
            {
                if (!visual) continue;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled) continue;
                    bool visiblePart = true;
                    for (var part = renderer.transform; part && part != visual.transform; part = part.parent)
                        if (!part.gameObject.activeSelf) { visiblePart = false; break; }
                    if (!visiblePart) continue;
                    // Renderer.bounds는 inactive 공급물에서 갱신되지 않을 수 있다. 원본 local bounds를 직접 변환한다.
                    var mesh = renderer.GetComponent<MeshFilter>();
                    var localBounds = mesh && mesh.sharedMesh ? mesh.sharedMesh.bounds : renderer.localBounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = localBounds.center + Vector3.Scale(localBounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var local = location.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                        if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; } else bounds.Encapsulate(local);
                    }
                }
            }
            return found;
        }
        private static bool WithinOpening(GameObject[] visuals, Transform location, Vector2 size)
        { return !TryBounds(visuals, location, out var bounds) || (Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)) <= size.x * .5f + .002f && Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z)) <= size.y * .5f + .002f); }

        private void Complete(bool success, bool restoreVisuals)
        {
            var request = active;
            if (restoreVisuals)
            { if (oldState != null) foreach (var value in oldState) value.Restore(); if (newState != null) foreach (var value in newState) value.Restore(); }
            if (Surface) Surface.RestoreSurface();
            if (Modules != null) foreach (var value in Modules)
            {
                if (value?.Pose == null) continue;
                value.Pose.Restore(); value.Root.transform.localPosition = value.InitialPosition; value.Root.transform.localRotation = value.InitialRotation; value.Root.transform.localScale = value.InitialScale; value.Root.SetActive(false);
            }
            active = null; module = null; routine = null; oldState = newState = null;
            if (request != null)
            { Presented?.Invoke(request.Id, success ? HatchPresentationEvent.Restored : HatchPresentationEvent.Cancelled); request.Finished?.Invoke(success); }
            Pump();
        }
        public bool Cancel(string id)
        {
            if (active != null && active.Id == id)
            { if (routine != null) StopCoroutine(routine); Complete(false, true); return true; }
            foreach (var request in queue) if (request.Id == id && cancelled.Add(id)) return true;
            return false;
        }
        public void ResetHatch()
        {
            resetting = true;
            try
            {
                while (queue.Count > 0) queue.Dequeue().Finished?.Invoke(false);
                if (active != null) { if (routine != null) StopCoroutine(routine); Complete(false, true); }
                else if (Surface) Surface.RestoreSurface();
                accepted.Clear(); cancelled.Clear();
            }
            finally { resetting = false; }
        }
        public void CancelAll() => ResetHatch();
        private void OnDisable() => ResetHatch();
        private void OnDestroy() { ResetHatch(); if (opening) Destroy(opening.gameObject); }
    }
}
