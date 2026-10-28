using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TimeTheft.ItemPresentation;
using UnityEngine;

namespace TimeTheft
{
    // Match가 확정한 사용/효과/종료만 제작된 모션에 전달한다.
    public sealed class ItemGamePresentation : MonoBehaviour
    {
        private sealed class Use
        {
            public ItemWorldView Item, Removed;
            public ItemKind Kind;
            public Participant Owner;
            public ItemPresentationAnchors Anchors;
            public Transform AnchorRoot;
            public string Token;
            public int Turn, Batch;
            public bool Activated, Ended, Held, Prepared, Deployed, Applied, Ready, Inspected, EndRequested, FailureReported;
            public float Speed;
            public Quaternion ContactRotation;
        }
        private sealed class Acquisition
        {
            public ItemWorldView Item;
            public Transform Anchors;
            public string Token;
            public int QueuedFrame, Generation;
            public bool Started, Completed, Failed, Reported, Retiring;
        }
        private readonly List<Use> uses = new List<Use>();
        private readonly List<Acquisition> acquisitions = new List<Acquisition>();
        [SerializeField] private ItemHatchController hatch;
        [SerializeField] private Transform opponent;
        private ItemWorldInventoryView inventory;
        private TimeTheftGameView view;
        private TimeTheftGameController game;
        private Transform actors;
        private int serial, generation;
        private bool skip;
        private H2ClockPresentation inspected;
        private Quaternion inspectedHomeRotation;
        private int inspectedIndex = -1;
        public int ActiveCount => uses.Count;
        public bool IsPreparing => uses.Any(u => u.Item && !u.Ready);
        public bool IsAcquiring => acquisitions.Any(a => a.Item && !a.Completed);
        public bool HasAcquisitionFailure => acquisitions.Any(a => a.Item && a.Failed);
        public ItemHatchController Hatch => hatch;
        public Transform Opponent => opponent;
        public bool HasFailedPresentation => uses.Any(u => u.Item && u.Item.ModelPresentation.State == ItemPresentationState.Failed);

        public void ConfigureHatch(ItemHatchController value)
        {
            hatch = value;
            foreach (var use in uses) if (use.Item) use.Item.ModelPresentation.Hatch = hatch;
        }

        public void Initialize(ItemWorldInventoryView world, TimeTheftGameView gameView)
        {
            inventory = world; view = gameView; game = view.GetComponent<TimeTheftGameController>();
            if (!actors) { actors = new GameObject("Used item models").transform; actors.SetParent(transform, false); }
        }
        private Transform Anchor(Transform root, string name, Vector3 position, Quaternion rotation)
        {
            var value = new GameObject(name).transform; value.SetParent(root, false);
            value.SetPositionAndRotation(position, rotation); return value;
        }
        private static bool VisibleBounds(GameObject visual, out Bounds bounds)
        {
            bounds = default; bool found = false;
            if (!visual) return false;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }
        private Bounds BodyBounds(ItemWorldView item)
        {
            var model = item.ModelPresentation; var cables = model.Cables;
            bool found = false; var bounds = new Bounds(item.transform.position, Vector3.zero);
            foreach (var renderer in model.Visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (cables && (cables.CableMeshes.Any(mesh => mesh && mesh.gameObject == renderer.gameObject) ||
                    cables.ClipRoots.Any(root => root && renderer.transform.IsChildOf(root)))) continue;
                if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }
        private float TableHeight(Vector3 point)
        {
            if (hatch && hatch.Surface && hatch.Surface.Surface && hatch.Surface.Surface.sharedMesh)
            {
                var surface = hatch.Surface.Surface;
                var renderer = surface.GetComponent<Renderer>();
                if (renderer) return renderer.bounds.max.y;
            }
            float height = inventory.PlayerAnchor.position.y, highest = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(new Vector3(point.x, Mathf.Max(point.y, height) + 4, point.z), Vector3.down, 12, ~0, QueryTriggerInteraction.Ignore))
                if (hit.normal.y > .9f && !hit.collider.GetComponentInParent<TabletopFocusTarget>()) highest = Mathf.Max(highest, hit.point.y);
            return float.IsNegativeInfinity(highest) ? height : highest;
        }
        private Vector3 CounterFront(Transform digits)
        {
            var normal = Vector3.ProjectOnPlane(digits.forward, Vector3.up).normalized;
            var table = Vector3.zero; foreach (var clock in view.Clocks) table += clock.transform.position / view.Clocks.Length;
            if (Vector3.Dot(normal, table - digits.position) < 0) normal = -normal;
            return normal.sqrMagnitude > .001f ? normal : Vector3.right;
        }
        private Transform FrameContact(Use use, TMPro.TextMeshPro digits, string name)
        {
            bool player = digits == view.PlayerLife;
            var root = CounterRoot();
            string bolt = player ? "ClampBolt_-1_-1" : "ClampBolt_1_-1";
            var hex = root.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "HexHead" && t.parent.name == bolt);
            if (hex) return Anchor(use.AnchorRoot, name, hex.position,
                hex.rotation * (player ? Quaternion.Euler(0, 180, 0) : Quaternion.identity) * Quaternion.Euler(90, 0, 0));
            return Anchor(use.AnchorRoot, name, CounterReferencePoint(new Vector3(.21226f, .3271778f, player ? -.966f : .966f)), root.rotation);
        }
        private Transform CounterRoot()
        {
            for (var value = view.Bank.transform; value; value = value.parent)
                if (value.name == "IntegratedTimeCounter") return value;
            return view.Bank.transform.parent;
        }
        private Vector3 CounterReferencePoint(Vector3 sourceOffset)
        {
            // M4.10 Anchor_Contract: 원본 counter yaw=270, scale=.6의 상대 TR을 현재 counter에 옮긴다.
            return CounterRoot().TransformPoint(Quaternion.Inverse(Quaternion.Euler(0, 270, 0)) * sourceOffset / .6f);
        }
        private Vector3 CounterPlacement(ItemWorldView item)
        {
            return CounterReferencePoint(new Vector3(.9f, .0025f, 0));
        }
        private Vector3 BoundaryTarget(Use use, Transform targetSide)
        {
            var point = targetSide.position; point.x = 0;
            if (use.Kind == ItemKind.RubberDuck) point = new Vector3(use.Owner == Participant.Player ? 0 : 2.7f, point.y, use.Owner == Participant.Player ? 2.5f : -2.5f);
            if (use.Kind == ItemKind.Handcuffs && use.Owner == Participant.Player && opponent && VisibleBounds(opponent.gameObject, out var bounds))
            { point.x = bounds.center.x; point.z = bounds.min.z - BodyBounds(use.Item).extents.z - .12f; }
            else if (use.Kind == ItemKind.Handcuffs) point.z -= .35f;
            point.y = TableHeight(point); return point;
        }
        private void SetHatchLocation(Use use, Bounds body)
        {
            var location = use.Anchors.HatchLocation;
            var position = body.center;
            if (use.EndRequested && use.Kind == ItemKind.RubberDuck) position.z = Mathf.Clamp(position.z, -2.4f, 2.4f);
            // 해제된 수갑은 뒤쪽 테두리에서 안쪽으로 회수한 다음 떨어뜨린다.
            if (use.EndRequested && use.Kind == ItemKind.Handcuffs) position.z += use.Owner == Participant.Player ? -.9f : .9f;
            bool returnToSlot = use.EndRequested && (use.Kind == ItemKind.SpareKey || use.Kind == ItemKind.Screwdriver) && use.Anchors.ItemSlot;
            if (returnToSlot)
            {
                position = use.Anchors.ItemSlot.position;
                // 사용 중인 끝/문자판 접촉 자세 대신 처음 보유했던 평평한 footprint를 쓴다.
                // 이 Collider는 BeginModelUse 뒤 비활성화되므로 bounds 대신 로컬 모서리를 읽는다.
                var box = use.Item.GetComponent<BoxCollider>();
                if (box)
                {
                    var rest = new Bounds(box.transform.TransformPoint(box.center), Vector3.zero);
                    for (int i = 0; i < 8; i++)
                        rest.Encapsulate(box.transform.TransformPoint(box.center + Vector3.Scale(box.size * .5f,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    body = rest;
                    position.x = rest.center.x; position.z = rest.center.z;
                }
                // 소모 후 다시 중심 정렬된 남은 보유품 바로 아래에는 열지 않는다.
                position.z += use.Owner == Participant.Player ? -.55f : .55f;
            }
            position.y = TableHeight(position);
            location.SetPositionAndRotation(position, Quaternion.identity);
            if (!hatch || !hatch.Surface) return;
            var aperture = new Vector2(Mathf.Max(.24f, body.size.x + hatch.AperturePadding), Mathf.Max(.24f, body.size.z + hatch.AperturePadding));
            float direction = use.Owner == Participant.Player ? -1 : 1;
            // 열쇠/드라이버는 보유 슬롯 앞, 나머지는 현재 물체 가까이에서만 찾는다.
            var offsets = new[] { Vector3.zero, new Vector3(0, 0, direction * .55f),
                new Vector3(-.55f, 0, direction * .55f), new Vector3(.55f, 0, direction * .55f),
                new Vector3(0, 0, direction * 1.05f) };
            foreach (var offset in offsets)
            {
                location.position = position + offset;
                if (hatch.Surface.ValidateOpening(location, aperture)) return;
            }
            // 검증 실패는 실제 해치에 전달되어 Failed로 남는다. 임의 위치에서 숨기지 않는다.
            location.position = position;
        }
        public void QueueAcquire(ItemWorldView item)
        {
            if (!item || !item.ModelPresentation || acquisitions.Any(a => a.Item == item)) return;
            // 생성 프레임의 완성된 보유 모델이 먼저 보이지 않게 한다. Renderer는 해치 크기 측정을 위해 유지한다.
            item.ModelPresentation.Visual.SetActive(false);
            item.Target.SetInputEnabled(false);
            acquisitions.Add(new Acquisition { Item = item, QueuedFrame = Time.frameCount, Generation = generation });
        }
        public bool QueueRetire(ItemWorldView item)
        {
            if (!item || !item.ModelPresentation || acquisitions.Any(a => a.Item == item)) return false;
            item.BeginModelUse(actors);
            acquisitions.Add(new Acquisition { Item = item, QueuedFrame = Time.frameCount, Generation = generation, Retiring = true });
            return true;
        }
        private void UpdateAcquisitions()
        {
            bool refreshed = false;
            bool canStart = game && game.Match != null &&
                (game.Match.Stage == TurnStage.AwaitingItems || game.Match.Stage == TurnStage.AwaitingClock) &&
                (!view.FocusCamera || !view.FocusCamera.HasCameraControl);
            for (int i = acquisitions.Count - 1; i >= 0; i--)
            {
                var entry = acquisitions[i];
                if (!entry.Item || entry.Completed && !entry.Failed)
                {
                    if (entry.Item) { if (entry.Retiring) Destroy(entry.Item.gameObject); else entry.Item.CaptureHeldModelPose(); }
                    if (entry.Anchors) Destroy(entry.Anchors.gameObject);
                    acquisitions.RemoveAt(i); refreshed = true; continue;
                }
                entry.Item.Target.SetInputEnabled(false);
                if (entry.Failed && !entry.Reported)
                { entry.Reported = true; Debug.LogError("공통 해치 공급 실패: " + (hatch ? hatch.LastError : "해치 미연결"), this); }
            }
            if (canStart)
                foreach (var owner in new[] { Participant.Player, Participant.Ai })
                {
                    var batch = acquisitions.Where(a => a.Item && a.Item.Owner == owner && !a.Started && !a.Completed &&
                        a.Item.gameObject.activeInHierarchy && Time.frameCount > a.QueuedFrame).ToArray();
                    if (batch.Length == 0) continue;
                    var root = new GameObject("Common supply anchors").transform; root.SetParent(actors, false);
                    var point = owner == Participant.Player ? inventory.PlayerAnchor.position : inventory.OpponentAnchor.position;
                    // M4.9 Batch 원본 크기. 기존 H2 회수구를 피해 필요한 만큼만 왼쪽으로 보정한다.
                    point.x = 0;
                    point.y = TableHeight(point);
                    var location = Anchor(root, "Common supply opening", point, Quaternion.identity);
                    var incoming = batch.Where(a => !a.Retiring).Select(a => a.Item.ModelPresentation.Visual).ToList();
                    var outgoing = batch.Where(a => a.Retiring).Select(a => a.Item.ModelPresentation.Visual).ToList();
                    // 공통 패널 위 기존 보유품도 원본 Batch 하강/상승을 함께 탄다. 논리 보유 상태는 유지한다.
                    var held = owner == Participant.Player ? inventory.PlayerItems : inventory.OpponentItems;
                    foreach (var item in held)
                        if (item.ModelPresentation && !batch.Any(a => a.Item == item) && !acquisitions.Any(a => a.Item == item))
                        { outgoing.Add(item.ModelPresentation.Visual); incoming.Add(item.ModelPresentation.Visual); }
                    string token = "acquire/" + ++serial;
                    int capturedGeneration = generation;
                    var request = new ItemHatchRequest(token, HatchOperation.Replace, location, outgoing.ToArray(), incoming.ToArray());
                    request.Finished = success => {
                        if (capturedGeneration != generation) return;
                        foreach (var entry in batch) { entry.Completed = true; entry.Failed = !success; }
                        if (root) Destroy(root.gameObject);
                    };
                    foreach (var entry in batch) { entry.Started = true; entry.Token = token; }
                    if (!hatch || !hatch.Enqueue(request))
                    { foreach (var entry in batch) { entry.Completed = true; entry.Failed = true; } Destroy(root.gameObject); }
                }
            if (refreshed && game && game.Match != null) view.RefreshInventory(game.Match);
        }
        public bool CanUseVisual(Participant owner, int id) => !IsAcquiring && !acquisitions.Any(a => a.Item && a.Item.Owner == owner && a.Item.ItemId == id);
        public IEnumerator WaitForAcquisitions()
        {
            int id = generation;
            while (id == generation && IsAcquiring) yield return null;
        }
        public bool Play(ItemUseResult result)
        {
            var kind = result.Effect.Value.Source.Kind;
            var item = inventory.TakeModel(result.Actor, result.ItemId, kind);
            if (!item) return false;
            var model = item.ModelPresentation;
            model.Hatch = hatch;
            var use = new Use { Item = item, Kind = kind, Owner = result.Actor, Token = "use/" + ++serial,
                Turn = game.Match.Turn, Batch = game.Match.Batch, Speed = model.PlaybackSpeed,
                ContactRotation = model.Contact ? model.Contact.rotation : model.MotionRoot.rotation };
            use.AnchorRoot = new GameObject("Item anchors " + serial).transform; use.AnchorRoot.SetParent(actors, false);
            var targetSide = result.Actor == Participant.Player ? inventory.OpponentAnchor : inventory.PlayerAnchor;
            var targetPosition = BoundaryTarget(use, targetSide);
            if (result.RemovedItem.HasValue)
            {
                var removed = result.RemovedItem.Value;
                use.Removed = inventory.TakeModel(removed.Owner, removed.Id, removed.Kind);
                if (use.Removed) { targetPosition = use.Removed.transform.position; use.Removed.BeginModelUse(actors); }
            }
            use.Anchors = new ItemPresentationAnchors {
                Opponent = result.Actor == Participant.Ai,
                ItemSlot = Anchor(use.AnchorRoot, "Slot", model.MotionRoot.position, model.MotionRoot.rotation),
                TargetItem = Anchor(use.AnchorRoot, "Target", targetPosition, use.Removed ? use.Removed.ModelPresentation.CutPoseRotation :
                    kind == ItemKind.RubberDuck ? Quaternion.LookRotation(use.Owner == Participant.Player ? Vector3.forward : Vector3.back, Vector3.up) * Quaternion.Euler(90, 0, 0) : Quaternion.identity),
                TargetClock = Anchor(use.AnchorRoot, "Crown contact", targetPosition, use.ContactRotation),
                ClockFocus = Anchor(use.AnchorRoot, "Dial contact", targetPosition, use.ContactRotation),
                CounterContact = kind == ItemKind.TimeBypass ? Anchor(use.AnchorRoot, "Counter body placement", CounterPlacement(item), CounterRoot().rotation) : null,
                PlayerCableContact = kind == ItemKind.TimeBypass ? FrameContact(use, view.PlayerLife, "Player frame contact") : null,
                OpponentCableContact = kind == ItemKind.TimeBypass ? FrameContact(use, view.AiLife, "Opponent frame contact") : null,
                CableContactsAreJaws = kind == ItemKind.TimeBypass,
                RemovedTargetVisual = use.Removed && use.Removed.ModelPresentation ? use.Removed.ModelPresentation.Visual : null,
                CutProxyPrefab = result.RemovedItem.HasValue ? inventory.CutProxy(result.RemovedItem.Value.Kind) : null
            };
            use.Anchors.HatchLocation = Anchor(use.AnchorRoot, "Actual table hatch", model.MotionRoot.position, Quaternion.identity);
            SetHatchLocation(use, BodyBounds(use.Removed ? use.Removed : item));
            item.BeginModelUse(actors);
            model.Presented += (token, signal) => {
                if (token != use.Token) return;
                if (signal == ItemPresentationEvent.Prepared) { use.Prepared = true; if (kind == ItemKind.SpareKey || kind == ItemKind.Screwdriver) use.Ready = true; }
                if (signal == ItemPresentationEvent.Deployed) use.Deployed = true;
                if (signal == ItemPresentationEvent.Held) { use.Held = true; use.Ready = true; }
                if (signal == ItemPresentationEvent.Activated) use.Applied = true;
                if (signal == ItemPresentationEvent.Complete || signal == ItemPresentationEvent.Failed) use.Ready = true;
            };
            uses.Add(use);
            if (!model.UseConfirmed(use.Token, use.Anchors)) { Release(use); uses.Remove(use); }
            return true;
        }
        private void Activate(Use use)
        {
            if (use.Activated || !use.Item) return;
            use.Item.ModelPresentation.EffectActivated(use.Token); use.Activated = true;
        }
        private void End(Use use)
        {
            use.Ended = true;
            if (!use.Item || use.EndRequested) return;
            // 수갑은 같은 논리 프레임에 발동과 해제가 온다. 고유 반응을 마친 뒤 회수한다.
            if (use.Activated && !use.Applied) return;
            use.EndRequested = true;
            SetHatchLocation(use, BodyBounds(use.Item));
            use.Item.ModelPresentation.EffectEnded(use.Token);
        }
        private static Renderer Part(H2ClockPresentation clock, string name) => clock.MotionRoot.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == name);
        private void BindClock(Use use, int index)
        {
            var clock = view.Clocks[index].H2Presentation;
            if (!clock) return;
            var dial = Part(clock, "Dial"); var crown = Part(clock, "Crown");
            if (!dial || !crown) return;
            var normal = dial.transform.forward.normalized;
            var outward = Vector3.ProjectOnPlane(crown.bounds.center - dial.bounds.center, normal).normalized;
            var crownMesh = crown.GetComponent<MeshFilter>().sharedMesh.bounds;
            var localOut = crown.transform.InverseTransformDirection(outward).normalized;
            float support = Vector3.Dot(new Vector3(Mathf.Abs(localOut.x), Mathf.Abs(localOut.y), Mathf.Abs(localOut.z)), crownMesh.extents);
            var crownPoint = crown.transform.TransformPoint(crownMesh.center + localOut * support);
            var keySurface = (outward * .517678f + normal * .855576f).normalized;
            use.Anchors.TargetClock.SetPositionAndRotation(crownPoint + outward * .0018f,
                Quaternion.FromToRotation(use.ContactRotation * Vector3.right, -keySurface) * use.ContactRotation);
            var hub = Part(clock, "HandHub");
            var hubBounds = hub ? hub.GetComponent<MeshFilter>().sharedMesh.bounds : default;
            var hubPoint = hub ? hub.transform.TransformPoint(new Vector3(hubBounds.center.x, hubBounds.center.y, hubBounds.max.z)) : dial.bounds.center;
            use.Anchors.ClockFocus.SetPositionAndRotation(hubPoint + normal * .0018f,
                Quaternion.FromToRotation(use.ContactRotation * Vector3.right, -normal) * use.ContactRotation);
            use.Item.ModelPresentation.SyncContactPose();
        }
        public IEnumerator InspectKey(TimeTheftMatch match, int index)
        {
            if (match.Stage != TurnStage.AwaitingKeyDecision || index != match.CurrentClock || index < 0 || index >= view.Clocks.Length) yield break;
            var use = uses.FirstOrDefault(u => u.Kind == ItemKind.SpareKey && u.Owner == match.ActiveParticipant && u.Turn == match.Turn && u.Batch == match.Batch);
            if (use == null || !use.Item || use.Inspected) yield break;
            use.Inspected = true;
            int id = generation; skip = false; BindClock(use, index); Activate(use);
            var clock = view.Clocks[index].H2Presentation; inspected = clock; inspectedIndex = index;
            inspectedHomeRotation = clock.MotionRoot.rotation;
            var homeRotation = inspectedHomeRotation;
            var faceNormal = Part(clock, "Dial").transform.forward;
            var towardPlayer = Vector3.ProjectOnPlane(view.FocusCamera.transform.position - clock.MotionRoot.position, Vector3.up).normalized;
            var privateRotation = use.Owner == Participant.Player
                ? Quaternion.FromToRotation(faceNormal, (faceNormal + towardPlayer * .3f).normalized) * homeRotation : homeRotation;
            try
            {
                while (id == generation && WaitingForEffect(use)) { BindClock(use, index); yield return null; }
                if (id != generation || !use.Item || !use.Applied) yield break;
                // 공개 Face/UI는 유지한다. AI의 실제 개인값은 렌더러에도 전달하지 않는다.
                if (use.Owner == Participant.Player)
                {
                    var face = match.GetPrivateClock(Participant.Player, index);
                    if (face.HasValue) { clock.Watch.SetTime(face.Value.Hours, 0); clock.Watch.SetRed(face.Value.Kind == ClockKind.Red); clock.Materials.SetC2(clock.Materials.C2Enabled); }
                }
                float elapsed = 0; float angle = use.Owner == Participant.Player ? 38 : 8;
                view.ShowPrivateClockPeek(index, use.Owner);
                while (id == generation && elapsed < .25f) { elapsed += Time.unscaledDeltaTime * (skip ? 30 : 1); float t = Mathf.Clamp01(elapsed / .25f); clock.MotionRoot.rotation = Quaternion.Slerp(homeRotation, privateRotation, t); clock.Watch.SetLidAngle(Mathf.Lerp(0, angle, t)); yield return null; }
                elapsed = 0;
                while (id == generation && elapsed < .65f) { elapsed += Time.unscaledDeltaTime * (skip ? 30 : 1); yield return null; }
                elapsed = 0;
                while (id == generation && elapsed < .22f) { elapsed += Time.unscaledDeltaTime * (skip ? 30 : 1); float t = Mathf.Clamp01(elapsed / .22f); clock.MotionRoot.rotation = Quaternion.Slerp(privateRotation, homeRotation, t); clock.Watch.SetLidAngle(Mathf.Lerp(angle, 0, t)); yield return null; }
                End(use);
                // 확인 뒤 선택을 돌려주기 전에 원본 Exit와 실제 개구부 복구까지 끝낸다.
                while (id == generation && use.Item && use.Item.ModelPresentation.State != ItemPresentationState.Complete &&
                    use.Item.ModelPresentation.State != ItemPresentationState.Failed) yield return null;
                // 완료 모델의 소유자 정리도 끝난 다음 최종 시계 클릭을 허용한다.
                if (id == generation && use.Item && use.Item.ModelPresentation.State == ItemPresentationState.Complete) yield return null;
            }
            finally
            {
                if (id == generation && clock) { clock.MotionRoot.rotation = homeRotation; clock.HoldLidClosed(); clock.Apply(match.GetClock(index)); }
                if (inspected == clock) { inspected = null; inspectedIndex = -1; }
            }
        }
        private static bool WaitingForEffect(Use use) => use.Item && !use.Applied &&
            use.Item.ModelPresentation.State != ItemPresentationState.Failed && use.Item.ModelPresentation.State != ItemPresentationState.Complete;
        public IEnumerator PresentHeldClockEffects(TimeTheftMatch match)
        {
            if (match.Action != TimeAction.Collect || !match.HasScrewdriverProtection || match.CurrentClock < 0) yield break;
            var face = match.GetClock(match.CurrentClock).Face;
            if (!face.HasValue || face.Value.Kind != ClockKind.Red) yield break;
            var use = uses.FirstOrDefault(u => u.Kind == ItemKind.Screwdriver && u.Owner == match.ActiveParticipant && u.Turn == match.Turn && u.Batch == match.Batch);
            if (use == null || !use.Item || use.Activated) yield break;
            int id = generation; BindClock(use, match.CurrentClock); Activate(use);
            while (id == generation && WaitingForEffect(use)) { BindClock(use, match.CurrentClock); yield return null; }
            if (id == generation && use.Item && use.Applied)
            {
                float hold = 0;
                while (id == generation && hold < .5f)
                { hold += Time.unscaledDeltaTime * (skip ? 30 : 1); yield return null; }
                if (id == generation) End(use);
            }
        }
        public IEnumerator PresentSettlementEffects(TimeTheftMatch match)
        {
            int id = generation; var active = new List<Use>();
            foreach (var effect in match.LastItemEffects)
            {
                var use = uses.FirstOrDefault(u => u.Owner == effect.Source.Owner && u.Kind == effect.Source.Kind && u.Batch == match.Batch && u.Turn == match.Turn);
                if (use == null || !use.Item || (use.Kind != ItemKind.Screwdriver && use.Kind != ItemKind.TimeBypass)) continue;
                if (use.Kind == ItemKind.Screwdriver && use.Activated) continue;
                if (use.Kind == ItemKind.Screwdriver) BindClock(use, match.CurrentClock);
                if (effect.Triggered) { if (use.Kind == ItemKind.TimeBypass) view.BeginBypassCue(effect, use.Item.ModelPresentation.Cables); Activate(use); if (!use.Applied) active.Add(use); }
                else End(use);
            }
            while (id == generation && active.Any(WaitingForEffect))
            { foreach (var use in active) if (WaitingForEffect(use) && use.Kind == ItemKind.Screwdriver) BindClock(use, match.CurrentClock); yield return null; }
        }
        public void EndSettlementEffects(TimeTheftMatch match)
        {
            view.EndBypassCue();
            foreach (var use in uses)
                if ((use.Kind == ItemKind.Screwdriver || use.Kind == ItemKind.TimeBypass) && use.Batch == match.Batch && use.Turn == match.Turn) End(use);
        }
        private void LateUpdate()
        {
            if (!game || game.Match == null || game.Match.CurrentClock < 0) return;
            foreach (var use in uses)
                if (use.Item && use.Activated && !use.Ended && use.Owner == game.Match.ActiveParticipant &&
                    (use.Kind == ItemKind.SpareKey || use.Kind == ItemKind.Screwdriver)) BindClock(use, game.Match.CurrentClock);
        }
        private void Update()
        {
            if (!game || game.Match == null) return;
            UpdateAcquisitions();
            if (view.IsResultVisible) { ResetAll(); return; }
            var match = game.Match;
            for (int i = uses.Count - 1; i >= 0; i--)
            {
                var use = uses[i];
                if (!use.Item) { Release(use); uses.RemoveAt(i); continue; }
                var model = use.Item.ModelPresentation;
                if (model.State == ItemPresentationState.Complete)
                { Release(use); uses.RemoveAt(i); continue; }
                if (model.State == ItemPresentationState.Failed)
                {
                    use.Ready = true;
                    if (!use.FailureReported)
                    {
                        use.FailureReported = true;
                        Debug.LogError($"{use.Kind} 표현 실패 ({use.Token}): {(hatch ? hatch.LastError : "실제 해치 미연결")}. 물체는 안전 자세를 유지합니다.", this);
                    }
                    continue;
                }
                if (use.Ready && model.State == ItemPresentationState.Holding && (!use.Activated || use.Applied)) model.PlaybackSpeed = use.Speed;
                if (use.Activated && (use.Kind == ItemKind.SpareKey || use.Kind == ItemKind.Screwdriver) && match.ActiveParticipant == use.Owner && match.CurrentClock >= 0 && !use.Ended) BindClock(use, match.CurrentClock);
                foreach (var signal in match.TurnItemEvents)
                    if ((match.Batch != use.Batch || match.Turn != use.Turn) && signal.Source.Owner == use.Owner && signal.Source.Kind == use.Kind)
                    {
                        if (signal.Kind == ItemTurnEventKind.ItemsBlocked || signal.Kind == ItemTurnEventKind.TurnSkipped) Activate(use);
                        if (signal.Kind != ItemTurnEventKind.ItemsBlocked) use.Ended = true;
                    }
                if ((match.Stage == TurnStage.GameOver && (use.Kind == ItemKind.RubberDuck || use.Kind == ItemKind.Handcuffs)) ||
                    ((use.Kind == ItemKind.SpareKey || use.Kind == ItemKind.Screwdriver || use.Kind == ItemKind.TimeBypass) && (match.Batch != use.Batch || match.Turn != use.Turn))) use.Ended = true;
                if (use.Ended && (use.Held || model.State == ItemPresentationState.Holding && !use.Activated)) End(use);
            }
        }
        public void SkipMotion() { skip = true; foreach (var use in uses) if (use.Item) use.Item.ModelPresentation.PlaybackSpeed = Mathf.Max(30, use.Speed); }
        private void Release(Use use)
        {
            if (use.Item) { use.Item.ModelPresentation.ResetPresentation(); use.Item.gameObject.SetActive(false); Destroy(use.Item.gameObject); }
            if (use.Removed) { use.Removed.gameObject.SetActive(false); Destroy(use.Removed.gameObject); }
            if (use.AnchorRoot) Destroy(use.AnchorRoot.gameObject);
        }
        public void ResetAll()
        {
            generation++; skip = false; if (view) view.EndBypassCue();
            if (hatch) hatch.CancelAll();
            if (inspected) { inspected.MotionRoot.rotation = inspectedHomeRotation; inspected.HoldLidClosed(); if (game && game.Match != null && inspectedIndex >= 0) inspected.Apply(game.Match.GetClock(inspectedIndex)); }
            inspected = null; inspectedIndex = -1;
            foreach (var use in uses) Release(use); uses.Clear();
            foreach (var entry in acquisitions) { if (entry.Anchors) Destroy(entry.Anchors.gameObject); if (entry.Retiring && entry.Item) Destroy(entry.Item.gameObject); } acquisitions.Clear();
        }
        private void OnDisable() => ResetAll();
    }
}
