using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TimeTheft.ItemPresentation
{
    public sealed class ItemPresentationController : MonoBehaviour
    {
        public ItemPresentationProfile Profile;
        public GameObject Visual;
        public GameObject AnimationModel;
        public ItemHatchController Hatch;
        public ItemCutPresentation Cut;
        public BypassCablePresentation Cables;
        [Min(.05f)] public float PlaybackSpeed = 1f;

        public ItemPresentationState State { get; private set; }
        public string Token { get; private set; }
        public float SourceSeconds { get; private set; }
        public ItemPresentationAnchors Anchors { get; private set; }
        public Transform MotionRoot { get { Initialize(); return motionRoot; } }
        public Transform Contact { get { Initialize(); return contact; } }
        public Quaternion CutPoseRotation { get { Initialize(); return motionRoot.rotation * Quaternion.Inverse(canonicalRootRotation); } }
        public bool IsUseReady => used && (State == ItemPresentationState.Holding ||
            State == ItemPresentationState.Complete || State == ItemPresentationState.Failed);
        public event Action<string, ItemPresentationEvent> Presented;

        private ItemPoseSnapshot pose;
        private Transform motionRoot, contact;
        private Transform contactAnchor;
        private Quaternion contactAnchorRotation;
        private Vector3 slotVisualPosition;
        private Quaternion slotVisualRotation;
        private Quaternion canonicalRootRotation;
        private float restSeconds;
        private Coroutine sequence;
        private bool trigger, endRequested, activationDone, used, cutSent, cutFailed;
        private uint sentEvents;
        private readonly HashSet<string> acquiredTokens=new HashSet<string>();
        private readonly HashSet<string> usedTokens=new HashSet<string>();
        private AnimationClip Clip => Anchors != null && Anchors.Opponent && Profile.OpponentClip != null ? Profile.OpponentClip : Profile.Clip;

        private void Awake() => Initialize();

        public void Initialize()
        {
            if (pose != null || Visual == null || AnimationModel == null || Profile == null) return;
            pose = new ItemPoseSnapshot(Visual);
            motionRoot = AnimationModel.transform.Find(Profile.RootPath);
            contact = string.IsNullOrEmpty(Profile.ContactPath) ? null : AnimationModel.transform.Find(Profile.ContactPath);
            if (motionRoot == null) throw new InvalidOperationException("Missing motion root: " + Profile.RootPath);
            canonicalRootRotation = Quaternion.Inverse(Visual.transform.rotation) * motionRoot.rotation;
            if (Cables != null) Cables.Initialize();
        }

        // Acquire 끝 자세와 외부 배치/정규화가 끝난 뒤 호출한다. 공유 Prefab/Clip은 바꾸지 않는다.
        public void SetRestPose()
        {
            Initialize();
            if (sequence != null) throw new InvalidOperationException("Cannot capture an item rest pose during playback.");
            if (Visual != null) pose = new ItemPoseSnapshot(Visual);
            if (Profile != null && Profile.TryGet(ItemPresentationPhase.Acquire, out var acquire))
                restSeconds = (acquire.LastFrame - 1) / 30f;
            SourceSeconds = restSeconds;
        }

        public bool Acquire(string token, ItemPresentationAnchors anchors)
        {
            if (!CanBegin(token) || !acquiredTokens.Add(token)) return false;
            Begin(token, anchors);
            sequence = StartCoroutine(AcquireSequence());
            return true;
        }

        public bool UseConfirmed(string token, ItemPresentationAnchors anchors)
        {
            if (usedTokens.Contains(token)) return false;
            if (State != ItemPresentationState.Idle && State != ItemPresentationState.Complete && State != ItemPresentationState.Failed) return false;
            if (string.IsNullOrEmpty(token) || anchors == null) return false;
            Begin(token, anchors);
            usedTokens.Add(token);
            used = true;
            sequence = StartCoroutine(UseSequence());
            return true;
        }

        private bool CanBegin(string token) => !string.IsNullOrEmpty(token) && token != Token && (State == ItemPresentationState.Idle || State == ItemPresentationState.Complete || State == ItemPresentationState.Failed);

        private void Begin(string token, ItemPresentationAnchors anchors)
        {
            ResetPresentation();
            Initialize();
            Token = token;
            Anchors = anchors ?? new ItemPresentationAnchors();
            Visual.SetActive(true);
            if (Anchors.ItemSlot != null)
            {
                // ItemSlot은 Visual 원점이 아니라 정규화된 motion root의 보유 world pose다.
                var rebase = Anchors.ItemSlot.rotation * Quaternion.Inverse(motionRoot.rotation);
                Visual.transform.rotation = rebase * Visual.transform.rotation;
                Visual.transform.position += Anchors.ItemSlot.position - motionRoot.position;
            }
            slotVisualPosition = Visual.transform.position;
            slotVisualRotation = Visual.transform.rotation;
            trigger = endRequested = activationDone = used = cutSent = cutFailed = false;
            sentEvents = 0;
            if (Cables != null) Cables.Bind(Anchors.PlayerCableContact, Anchors.OpponentCableContact, Anchors.CableContactsAreJaws);
        }

        // 확정된 게임 결과만 받는다. 사용 가능 여부와 효과량은 판단하지 않는다.
        public bool EffectActivated(string token)
        {
            if (token != Token || !used || activationDone || trigger || endRequested || State == ItemPresentationState.Complete) return false;
            trigger = true;
            return true;
        }

        public bool EffectNotActivated(string token) => EffectEnded(token);

        public bool EffectEnded(string token)
        {
            if (token != Token || !used || endRequested || State == ItemPresentationState.Complete) return false;
            endRequested = true;
            return true;
        }

        private IEnumerator AcquireSequence()
        {
            State = ItemPresentationState.Acquiring;
            if (Anchors.ExitLocation != null)
            {
                if (Profile.Item == PresentedItem.TimeBypass)
                    Visual.transform.position += Anchors.ExitLocation.position - motionRoot.position;
                else motionRoot.SetPositionAndRotation(Anchors.ExitLocation.position, Anchors.ExitLocation.rotation);
            }
            yield return Play(ItemPresentationPhase.Acquire);
            State = ItemPresentationState.Complete;
            sequence = null;
            Emit(ItemPresentationEvent.Acquired);
        }

        private IEnumerator UseSequence()
        {
            State = ItemPresentationState.Preparing;
            yield return Play(ItemPresentationPhase.Prepare);
            Emit(ItemPresentationEvent.Prepared);
            if (!Profile.DeployOnUse)
            {
                State = ItemPresentationState.Holding;
                while (!trigger && !endRequested) yield return null;
            }
            if (!endRequested || Profile.DeployOnUse)
            {
                State = ItemPresentationState.Deploying;
                yield return Play(ItemPresentationPhase.Deploy);
                Emit(ItemPresentationEvent.Deployed);
            }
            if (Profile.Item == PresentedItem.Scissors)
            {
                Visual.SetActive(false);
                Emit(ItemPresentationEvent.VisualRemoved);
                State = ItemPresentationState.WaitingForHatch;
                if (Cut != null) while (Cut.IsBusy) yield return null;
                if (cutFailed || Cut == null || Cut.Failed)
                {
                    State = ItemPresentationState.Failed;
                    sequence = null;
                    Emit(ItemPresentationEvent.Failed);
                    yield break;
                }
                State = ItemPresentationState.Complete;
                sequence = null;
                Emit(ItemPresentationEvent.Complete);
                yield break;
            }
            if (Profile.ActivateOnUse)
            {
                State = ItemPresentationState.Activating;
                yield return Play(ItemPresentationPhase.Activate);
                Emit(ItemPresentationEvent.UseReaction);
            }
            if (trigger)
            {
                State = ItemPresentationState.Activating;
                yield return Play(Profile.ActivateOnUse ? ItemPresentationPhase.EffectReaction : ItemPresentationPhase.Activate);
                activationDone = true;
                Emit(ItemPresentationEvent.Activated);
            }
            State = ItemPresentationState.Holding;
            Emit(ItemPresentationEvent.Held);
            // Hold 길이는 FullTake의 데모 시간으로 정하지 않는다.
            while (!endRequested)
            {
                if (trigger && !activationDone)
                {
                    State = ItemPresentationState.Activating;
                    yield return Play(Profile.ActivateOnUse ? ItemPresentationPhase.EffectReaction : ItemPresentationPhase.Activate);
                    activationDone = true;
                    Emit(ItemPresentationEvent.Activated);
                    State = ItemPresentationState.Holding;
                }
                yield return null;
            }
            State = ItemPresentationState.Releasing;
            yield return Play(ItemPresentationPhase.Release);
            Emit(ItemPresentationEvent.Released);
            if (Hatch != null && Anchors.HatchLocation != null)
            {
                State = ItemPresentationState.WaitingForHatch;
                var request = new ItemHatchRequest(Token + "/dispose", HatchOperation.SingleDispose, Anchors.HatchLocation, new[] { Visual }, Array.Empty<GameObject>());
                request.Ready = false;
                bool finished = false, succeeded = false;
                request.Finished = success => { succeeded = success; finished = true; };
                if (Hatch.Enqueue(request))
                {
                    while (Hatch.ActiveRequestId != request.Id && !finished) yield return null;
                    if (!finished)
                    {
                        State = ItemPresentationState.Exiting;
                        yield return Play(ItemPresentationPhase.Exit);
                        State = ItemPresentationState.WaitingForHatch;
                        request.Ready = true;
                    }
                    while (!finished) yield return null;
                }
                if (!succeeded)
                {
                    State = ItemPresentationState.Failed;
                    Emit(ItemPresentationEvent.Failed);
                    sequence = null;
                    yield break; // 실패하면 안전한 안착 자세를 보존한다.
                }
            }
            else
            {
                // 실제 개구부 없이 상판 위에서 지우거나 테이블 밖으로 우회하지 않는다.
                State = ItemPresentationState.Failed;
                sequence = null;
                Emit(ItemPresentationEvent.Failed);
                yield break;
            }
            Visual.SetActive(false);
            Emit(ItemPresentationEvent.VisualRemoved);
            State = ItemPresentationState.Complete;
            sequence = null;
            Emit(ItemPresentationEvent.Complete);
        }

        private IEnumerator Play(ItemPresentationPhase phase)
        {
            if (!Profile.TryGet(phase, out var segment)) yield break;
            if (phase == ItemPresentationPhase.Release || phase == ItemPresentationPhase.Exit) contactAnchor = null;
            if (Profile.Item == PresentedItem.Scissors && phase == ItemPresentationPhase.Deploy)
            {
                yield return FlyScissors(segment);
                yield break;
            }
            if (Profile.Item == PresentedItem.TimeBypass)
            {
                var from = Visual.transform.position;
                var fromRotation = Visual.transform.rotation;
                var destination = phase == ItemPresentationPhase.Exit ? Anchors.HatchLocation :
                    phase == ItemPresentationPhase.Acquire ? Anchors.ItemSlot :
                    phase == ItemPresentationPhase.Deploy ? Anchors.CounterContact : null;
                var destinationPosition = phase == ItemPresentationPhase.Acquire ? slotVisualPosition : destination != null ? destination.position : from;
                var destinationRotation = phase == ItemPresentationPhase.Acquire ? slotVisualRotation : fromRotation;
                if (phase == ItemPresentationPhase.Deploy && destination != null)
                {
                    var forward = Vector3.ProjectOnPlane(Visual.transform.forward, Vector3.up);
                    var targetForward = Vector3.ProjectOnPlane(destination.forward, Vector3.up);
                    var yaw = forward.sqrMagnitude > .000001f && targetForward.sqrMagnitude > .000001f
                        ? Quaternion.FromToRotation(forward, targetForward) : Quaternion.identity;
                    destinationRotation = yaw * fromRotation;
                    var bodyBounds = VisibleBounds(Visual, true);
                    var bottom = new Vector3(bodyBounds.center.x, bodyBounds.min.y, bodyBounds.center.z);
                    destinationPosition = destination.position - yaw * (bottom - from);
                }
                if (phase == ItemPresentationPhase.Exit && destination != null)
                {
                    var bounds = VisibleBounds(Visual);
                    destinationPosition -= new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)-Visual.transform.position;
                }
                float clock = 0f;
                do
                {
                    float u = Mathf.Clamp01(clock / segment.Duration);
                    SourceSeconds = Mathf.Lerp(segment.FirstFrame - 1, segment.LastFrame - 1, u) / 30f;
                    Clip.SampleAnimation(AnimationModel, SourceSeconds);
                    // 61f부터 케이블이 연결되기 시작하므로 본체의 접근은 그 전에 끝낸다.
                    float travel = phase == ItemPresentationPhase.Deploy
                        ? Mathf.InverseLerp((segment.FirstFrame - 1) / 30f, 60f / 30f, SourceSeconds) : u;
                    travel = travel * travel * (3f - 2f * travel);
                    if (destination != null)
                    {
                        Visual.transform.SetPositionAndRotation(Vector3.Lerp(from, destinationPosition, travel),
                            Quaternion.Slerp(fromRotation, destinationRotation, travel));
                    }
                    if (phase == ItemPresentationPhase.Exit && Anchors.HatchLocation)
                        Visual.transform.position += Vector3.up * Mathf.Max(0, Anchors.HatchLocation.position.y - VisibleBounds(Visual).min.y);
                    if (Cables != null) Cables.Sample(SourceSeconds);
                    if (u >= 1f) break;
                    yield return null;
                    clock += Time.unscaledDeltaTime * PlaybackSpeed;
                } while (true);
                yield break;
            }
            bool lockedContact = contactAnchor != null && contact != null &&
                (phase == ItemPresentationPhase.Activate || phase == ItemPresentationPhase.EffectReaction);
            if (lockedContact) FollowContact();
            var rootStartWorld = motionRoot.position;
            var rotationStartWorld = motionRoot.rotation;
            var tipStartWorld = contact != null ? contact.position : rootStartWorld;
            var anchorRotationStart = lockedContact ? contactAnchor.rotation : Quaternion.identity;
            Clip.SampleAnimation(AnimationModel, (segment.FirstFrame - 1) / 30f);
            var sourceStart = motionRoot.localPosition;
            var sourceStartRotation = motionRoot.localRotation;
            var sourceTipStart = contact != null ? motionRoot.InverseTransformPoint(contact.position) : Vector3.zero;
            var tipVectorBasis = Matrix4x4.TRS(Vector3.zero, rotationStartWorld, motionRoot.lossyScale);
            var sourceBasis = rotationStartWorld * Quaternion.Inverse(motionRoot.rotation);
            Clip.SampleAnimation(AnimationModel, (segment.LastFrame - 1) / 30f);
            var sourceEnd = motionRoot.localPosition;
            var sourceEndRotation = motionRoot.localRotation;
            var sourceTipEnd = contact != null ? motionRoot.InverseTransformPoint(contact.position) : Vector3.zero;
            // Prepare는 보유 위치를 기준으로 원래 이동량을 수행한다. 슬롯 원점으로 다시 당기지 않는다.
            var anchor = phase == ItemPresentationPhase.Prepare ? null : Anchors.Get(segment.Destination);
            bool tipDestination = contact != null && anchor != null && phase == ItemPresentationPhase.Deploy;
            bool keyTipRoute = tipDestination && Profile.Item == PresentedItem.SpareKey;
            var sourceTipDelta = sourceTipEnd - sourceTipStart;
            var endOffset = Vector3.zero;
            var endContactRotation = Quaternion.identity;
            if (tipDestination)
            {
                endOffset = Quaternion.Inverse(motionRoot.rotation) * (contact.position - motionRoot.position);
                endContactRotation = Quaternion.Inverse(motionRoot.rotation) * contact.rotation;
            }
            if (phase == ItemPresentationPhase.Exit || phase == ItemPresentationPhase.Deploy && !tipDestination &&
                (Profile.Item == PresentedItem.RubberDuck || Profile.Item == PresentedItem.Handcuffs))
            {
                var bounds=VisibleBounds(Visual);
                endOffset = Quaternion.Inverse(motionRoot.rotation) *
                    (new Vector3(bounds.center.x,bounds.min.y,bounds.center.z) - motionRoot.position);
            }
            var delta = sourceEnd - sourceStart;
            var sourceWorldDelta = sourceBasis * motionRoot.parent.TransformVector(delta);
            var naturalEndRotation = rotationStartWorld * Quaternion.Inverse(sourceStartRotation) * sourceEndRotation;
            float elapsed = 0f;
            do
            {
                float u = Mathf.Clamp01(elapsed / segment.Duration);
                SourceSeconds = Mathf.Lerp(segment.FirstFrame - 1, segment.LastFrame - 1, u) / 30f;
                Clip.SampleAnimation(AnimationModel, SourceSeconds);
                var sourceNow = motionRoot.localPosition;
                float progress = delta.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(sourceNow - sourceStart, delta) / delta.sqrMagnitude) : u * u * (3f - 2f * u);
                var residual = sourceNow - Vector3.Lerp(sourceStart, sourceEnd, progress);
                var sourceRotation = motionRoot.localRotation;
                var naturalRotation = rotationStartWorld * Quaternion.Inverse(sourceStartRotation) * sourceRotation;
                var sourceTipNow = keyTipRoute ? motionRoot.InverseTransformPoint(contact.position) : Vector3.zero;
                if (lockedContact)
                {
                    motionRoot.rotation = contactAnchor.rotation * Quaternion.Inverse(anchorRotationStart) * naturalRotation;
                    motionRoot.position += contactAnchor.position - contact.position;
                    contactAnchorRotation = contactAnchor.rotation;
                }
                else if (anchor != null)
                {
                    var targetRotation = tipDestination ? anchor.rotation * Quaternion.Inverse(endContactRotation) :
                        phase == ItemPresentationPhase.Acquire ? anchor.rotation :
                        phase == ItemPresentationPhase.Deploy && Profile.Item == PresentedItem.RubberDuck ? anchor.rotation :
                        phase == ItemPresentationPhase.Exit && Anchors.ItemSlot &&
                        (Profile.Item == PresentedItem.SpareKey || Profile.Item == PresentedItem.Screwdriver || Profile.Item == PresentedItem.Handcuffs)
                            ? Anchors.ItemSlot.rotation : naturalEndRotation;
                    var target = anchor.position - targetRotation * endOffset;
                    var sourceDirection = Vector3.ProjectOnPlane(sourceWorldDelta, Vector3.up);
                    var destinationDirection = Vector3.ProjectOnPlane(target - rootStartWorld, Vector3.up);
                    var route = sourceDirection.sqrMagnitude > .000001f && destinationDirection.sqrMagnitude > .000001f
                        ? Quaternion.FromToRotation(sourceDirection, destinationDirection) : Quaternion.identity;
                    motionRoot.position = Vector3.Lerp(rootStartWorld, target, progress) +
                        route * (sourceBasis * motionRoot.parent.TransformVector(residual));
                    var correction = targetRotation * Quaternion.Inverse(naturalEndRotation);
                    motionRoot.rotation = Quaternion.Slerp(Quaternion.identity, correction, u * u * (3f - 2f * u)) * naturalRotation;
                    if (keyTipRoute)
                    {
                        // FBX의 최상위 TT_Key_Root 대신 실제 애니메이션된 Tip 곡선을 재대상화한다.
                        // 원본 곡선의 높이/잔차와 회전은 유지하고, 접촉점 이동만 선택한 Crown에 맞춘다.
                        float tipProgress = sourceTipDelta.sqrMagnitude > .0000000001f
                            ? Mathf.Clamp01(Vector3.Dot(sourceTipNow - sourceTipStart, sourceTipDelta) / sourceTipDelta.sqrMagnitude)
                            : u * u * (3f - 2f * u);
                        var tipResidual = sourceTipNow - Vector3.Lerp(sourceTipStart, sourceTipEnd, tipProgress);
                        var nativeDirection = Vector3.ProjectOnPlane(tipVectorBasis.MultiplyVector(sourceTipDelta), Vector3.up);
                        var actualDirection = Vector3.ProjectOnPlane(anchor.position - tipStartWorld, Vector3.up);
                        var tipRoute = nativeDirection.sqrMagnitude > .000001f && actualDirection.sqrMagnitude > .000001f
                            ? Quaternion.FromToRotation(nativeDirection, actualDirection) : Quaternion.identity;
                        var desiredTip = Vector3.Lerp(tipStartWorld, anchor.position, tipProgress) + tipRoute * tipVectorBasis.MultiplyVector(tipResidual);
                        motionRoot.position += desiredTip - contact.position;
                    }
                    if (u >= 1f && tipDestination) motionRoot.position += anchor.position - contact.position;
                }
                else
                {
                    motionRoot.position = rootStartWorld + sourceBasis * motionRoot.parent.TransformVector(sourceNow - sourceStart);
                    motionRoot.rotation = naturalRotation;
                }
                // Native Exit는 원래 해치 낙하까지 포함한다. 실제 FloorClear 전에는 안착 경로만 재사용한다.
                if (phase == ItemPresentationPhase.Exit && Anchors.HatchLocation)
                    motionRoot.position += Vector3.up * Mathf.Max(0, Anchors.HatchLocation.position.y - VisibleBounds(Visual).min.y);
                else if (Anchors.TargetItem && (Profile.Item == PresentedItem.RubberDuck || Profile.Item == PresentedItem.Handcuffs))
                    motionRoot.position += Vector3.up * Mathf.Max(0, Anchors.TargetItem.position.y - VisibleBounds(Visual).min.y);
                if (Cables != null) Cables.Sample(SourceSeconds);
                if (u >= 1f) break;
                yield return null;
                elapsed += Time.unscaledDeltaTime * PlaybackSpeed;
            } while (true);
            if (tipDestination)
            {
                contactAnchor = anchor;
                contactAnchorRotation = anchor.rotation;
            }
        }

        private void FollowContact()
        {
            if (contactAnchor == null || contact == null) return;
            motionRoot.rotation = contactAnchor.rotation * Quaternion.Inverse(contactAnchorRotation) * motionRoot.rotation;
            motionRoot.position += contactAnchor.position - contact.position;
            contactAnchorRotation = contactAnchor.rotation;
        }

        public void SyncContactPose()
        {
            if (used && State == ItemPresentationState.Holding) FollowContact();
        }
        private void LateUpdate() => SyncContactPose();

        private IEnumerator FlyScissors(ItemPresentationSegment segment)
        {
            var tip = contact != null ? contact : motionRoot;
            var startWorld = tip.position;
            var startRotation = motionRoot.rotation;
            Clip.SampleAnimation(AnimationModel, (segment.FirstFrame - 1) / 30f);
            var sourceStart = AnimationModel.transform.InverseTransformPoint(tip.position);
            // Prepare의 마지막 실제 자세를 비행의 기준으로 삼는다. 원본 절대 회전/yaw를 다시 적용하지 않는다.
            var rotationBasis = startRotation * Quaternion.Inverse(motionRoot.rotation);
            Clip.SampleAnimation(AnimationModel, 71f / 30f);
            var sourceImpact = AnimationModel.transform.InverseTransformPoint(tip.position);
            var sourceDirection = sourceImpact - sourceStart;
            var target = Anchors.TargetItem != null ? Anchors.TargetItem.position : startWorld + Vector3.forward;
            float firstSeconds = (segment.FirstFrame - 1) / 30f;
            float lastSeconds = Mathf.Max((segment.LastFrame - 1) / 30f, Clip.length);
            float duration = segment.Duration * (lastSeconds - firstSeconds) /
                Mathf.Max(1f / 30f, (segment.LastFrame - segment.FirstFrame) / 30f);
            float elapsed = 0;
            do
            {
                float u = Mathf.Clamp01(elapsed / duration);
                float seconds = Mathf.Lerp(firstSeconds, lastSeconds, u);
                if (!cutSent && seconds >= 71f/30f)
                {
                    PlaceScissors(71f/30f,tip,startWorld,sourceStart,sourceDirection,target,rotationBasis);
                    cutSent = true;
                    cutFailed = Cut == null || !Cut.Impact(Token,Anchors.RemovedTargetVisual,Anchors.CutProxyPrefab,Anchors.TargetItem,Anchors.HatchLocation,Hatch);
                    State = ItemPresentationState.Exiting;
                    Emit(ItemPresentationEvent.CutImpact);
                }
                PlaceScissors(seconds,tip,startWorld,sourceStart,sourceDirection,target,rotationBasis);
                if (u>=1f) break;
                yield return null; elapsed += Time.unscaledDeltaTime*PlaybackSpeed;
            } while(true);
        }

        private void PlaceScissors(float seconds,Transform tip,Vector3 startWorld,Vector3 sourceStart,Vector3 sourceDirection,Vector3 target,Quaternion rotationBasis)
        {
            SourceSeconds=seconds;
            Clip.SampleAnimation(AnimationModel,seconds);
            var current = AnimationModel.transform.InverseTransformPoint(tip.position);
            float progress=Vector3.Dot(current-sourceStart,sourceDirection)/Mathf.Max(.000001f,sourceDirection.sqrMagnitude);
            motionRoot.rotation=rotationBasis*motionRoot.rotation;
            motionRoot.position += Vector3.LerpUnclamped(startWorld,target,progress)-tip.position;
        }

        private void Emit(ItemPresentationEvent signal)
        {
            uint bit = 1u << (int)signal;
            if ((sentEvents & bit) != 0) return;
            sentEvents |= bit;
            Presented?.Invoke(Token, signal);
        }

        private Bounds VisibleBounds(GameObject visual, bool bodyOnly = false)
        {
            bool found=false;var result=new Bounds(visual.transform.position,Vector3.zero);
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if(!renderer.enabled || !renderer.gameObject.activeInHierarchy)continue;
                if (bodyOnly && Cables != null)
                {
                    bool cablePart = false;
                    foreach (var clip in Cables.ClipRoots)
                        if (clip != null && renderer.transform.IsChildOf(clip)) { cablePart = true; break; }
                    foreach (var mesh in Cables.CableMeshes)
                        if (mesh != null && renderer.transform.IsChildOf(mesh.transform)) { cablePart = true; break; }
                    if (cablePart) continue;
                }
                if(!found){result=renderer.bounds;found=true;}else result.Encapsulate(renderer.bounds);
            }
            return result;
        }

        public void ResetPresentation()
        {
            if (sequence != null) StopCoroutine(sequence);
            sequence = null;
            contactAnchor = null;
            if (Hatch != null && !string.IsNullOrEmpty(Token)) Hatch.Cancel(Token + "/dispose");
            if (Cut != null) Cut.Cancel();
            pose?.Restore();
            if (Visual != null) Visual.SetActive(true);
            if (Cables != null) { Cables.ResetPose(); Cables.Sample(restSeconds); }
            Token = null;
            used = false;
            SourceSeconds = restSeconds;
            State = ItemPresentationState.Idle;
        }

        private void OnDisable() { if (sequence != null) ResetPresentation(); }
    }
}
