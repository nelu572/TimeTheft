#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimeTheft.ItemPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class M410ResidualTests
    {
        private TimeTheftGameController game;
        private TimeTheftSettings settings;
        private InputTestFixture input;
        private float oldTimeScale;
        private GameRules normalRules;
        private ItemWorldInventoryView World => game.View.WorldItems;

        [UnitySetUp]
        public IEnumerator Load()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.AddDevice<Mouse>("M410ResidualMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            game = Object.FindFirstObjectByType<TimeTheftGameController>();
            normalRules = JsonUtility.FromJson<GameRules>(JsonUtility.ToJson(game.Settings.Rules));
            settings = Object.Instantiate(game.Settings);
            settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.EnableAiItemStrategy = true;
            settings.Rules.AiDecisionNoise = settings.Rules.AiItemConservation = 0;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.Rules.ExtraRedProbability = 1;
            settings.PreviewDuration = .1f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = .08f;
            settings.AiThinkDuration = .4f;
            var serialized = new SerializedObject(game);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(game);
            Time.timeScale = 0;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game) { game.View.WorldItems.ModelUses?.ResetAll(); game.enabled = false; }
            GameUiTestInput.DisableSceneModules();
            if (settings) Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator NormalAiKeyCoroutineShowsOnlyQuestionAndNeutralMeshUntilPublicReveal()
        {
            game.RestartGame();
            var match = game.Match;
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.SpareKey);
            bool sawPrivate = false, sawQuestion = false, sawFinalChoice = false;
            Quaternion[] neutralHands = null;
            Material[] neutralDials = null;
            float deadline = Time.realtimeSinceStartup + 25;
            while (match.UsedCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                foreach (var item in Object.FindObjectsByType<ItemWorldView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (item.ModelPresentation) item.ModelPresentation.PlaybackSpeed = 40;
                if (neutralHands == null && (match.Stage == TurnStage.AwaitingItems || match.Stage == TurnStage.AwaitingClock))
                {
                    neutralHands = game.View.Clocks.Select(clock => clock.H2Presentation.Watch.HourHand.localRotation).ToArray();
                    neutralDials = game.View.Clocks.Select(clock => Dial(clock.H2Presentation).sharedMaterial).ToArray();
                }
                if (match.Stage == TurnStage.AwaitingKeyDecision)
                {
                    sawPrivate = true;
                    Assert.That(neutralHands, Is.Not.Null);
                    Assert.That(match.ObserveItems(Participant.Ai).KnownClocks.Count, Is.EqualTo(1));
                    Assert.That(match.ObserveItems(Participant.Player).KnownClocks, Is.Empty);
                    Assert.That(game.View.PhaseLabel.text, Does.Not.Contain("나만 확인").And.Not.Contain("8h"));
                    Canvas.ForceUpdateCanvases();
                    for (int i = 0; i < TimeTheftMatch.ClocksPerBatch; i++)
                    {
                        Assert.That(match.GetClock(i).Face, Is.Null);
                        var label = game.View.ClockValueLabels[i];
                        Assert.That(label.text, Is.EqualTo("").Or.EqualTo("?"), "AI 개인 확인의 실제 숫자는 UI에 쓰지 않는다");
                        if (label.text == "?" && label.gameObject.activeInHierarchy) sawQuestion = true;
                        var h2 = game.View.Clocks[i].H2Presentation;
                        Assert.That(h2.DisplayedHours, Is.Null);
                        Assert.That(h2.IsRed, Is.False);
                        Assert.That(Quaternion.Angle(h2.Watch.HourHand.localRotation, neutralHands[i]), Is.LessThan(.01f), "AI 개인값으로 실제 시침을 바꾸지 않는다");
                        Assert.That(Dial(h2).sharedMaterial, Is.SameAs(neutralDials[i]), "AI 개인 색상을 실제 문자판에 전달하지 않는다");
                    }
                }
                if (match.Stage == TurnStage.AwaitingAction)
                {
                    sawFinalChoice = true;
                    Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
                    Assert.That(match.ObserveItems(Participant.Player).KnownClocks, Is.Empty);
                    Assert.That(game.View.Clocks.All(clock => !clock.IsOpen), Is.True);
                }
                yield return null;
            }
            Assert.That(sawPrivate, Is.True, "실제 AI ChooseItem과 열쇠 확인 코루틴을 거친다");
            Assert.That(sawQuestion, Is.True, "AI private 열람에는 물음표만 표시한다");
            Assert.That(sawFinalChoice, Is.True, "AI ChooseKeyClock 이후 행동 결정까지 자동 진행한다");
            Assert.That(match.UsedCount, Is.EqualTo(1), "실제 정산까지 마친다");
            int usedIndex = match.CurrentClock;
            Assert.That(usedIndex, Is.InRange(0, TimeTheftMatch.ClocksPerBatch - 1));
            deadline = Time.realtimeSinceStartup + 10;
            while (game.View.Clocks[usedIndex].gameObject.activeInHierarchy && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(game.View.Clocks[usedIndex].gameObject.activeInHierarchy, Is.False, "AI 사용 시계의 실제 회수까지 완료한다");
        }

        [UnityTest]
        public IEnumerator AutomaticInitialAddGiveUsesTheRealTrayAndUnusedModelsStayVisibleThroughATurn()
        {
            ConfigureNormalGrants();
            var hatch = World.ModelUses.Hatch;
            using (var supply = new SupplyProbe(this))
            {
                game.RestartGame();
                yield return WaitFor(() => PlayerReady() && HeldItems().Length == 4, supply);
                Assert.That(supply.GivenCount, Is.EqualTo(2));
                Assert.That(supply.ClearCount, Is.EqualTo(2));
                Assert.That(supply.RestoredCount, Is.EqualTo(2));
                var held = HeldItems();
                AssertHeld(held);
                game.EndItemPhase();
                yield return WaitFor(() => game.Match.Stage == TurnStage.AwaitingClock, supply, held);
                int selected = game.Match.GetAvailableClockIndices().First();
                game.SelectClock(selected);
                yield return WaitFor(() => !game.IsPresentingClock, supply, held);
                game.ChooseAction(TimeAction.Ration);
                yield return WaitFor(() => game.Match.UsedCount == 1 && !game.View.Lifecycle.IsRunning &&
                    !game.View.Clocks[selected].gameObject.activeInHierarchy, supply, held);
                AssertHeld(held);
                Assert.That(hatch.LastError, Is.Null.Or.Empty);
            }
        }

        [UnityTest]
        public IEnumerator RestartDuringNativeAddGiveRestoresSurfaceAndColliderThenSuppliesAgain()
        {
            ConfigureNormalGrants();
            var hatch = World.ModelUses.Hatch;
            var surface = hatch.Surface;
            var mesh = surface.Surface.sharedMesh;
            var collision = surface.CollisionSurface;
            var collisionMesh = collision ? collision.sharedMesh : null;
            bool collisionEnabled = collision && collision.enabled;
            var solids = surface.SolidSurfaceColliders.Where(value => value).ToArray();
            var enabledStates = solids.Select(value => value.enabled).ToArray();
            using (var supply = new SupplyProbe(this))
            {
                game.RestartGame();
                yield return WaitFor(() => hatch.Busy && surface.HasOpening, supply);
                Assert.That(hatch.ActiveRequestId, Does.StartWith("acquire/"));
                Assert.That(surface.Surface.sharedMesh, Is.Not.SameAs(mesh));
                game.RestartGame();
                Assert.That(hatch.Busy, Is.False);
                Assert.That(hatch.WaitingCount, Is.Zero);
                Assert.That(surface.HasOpening, Is.False);
                Assert.That(surface.Surface.sharedMesh, Is.SameAs(mesh));
                Assert.That(surface.CollisionSurface.sharedMesh, Is.SameAs(collisionMesh));
                Assert.That(surface.CollisionSurface.enabled, Is.EqualTo(collisionEnabled));
                for (int i = 0; i < solids.Length; i++) Assert.That(solids[i].enabled, Is.EqualTo(enabledStates[i]));
                Assert.That(supply.CancelledCount, Is.GreaterThanOrEqualTo(1));
                int before = supply.GivenCount;
                yield return WaitFor(() => PlayerReady() && HeldItems().Length == 4, supply);
                Assert.That(supply.GivenCount - before, Is.EqualTo(2), "재시작한 양측 공통 공급 두 요청이 재개된다");
                AssertHeld(HeldItems());
                Assert.That(hatch.Busy, Is.False); Assert.That(hatch.WaitingCount, Is.Zero);
                Assert.That(surface.HasOpening, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator NextBatchAutomaticGrantRunsNativeAddGiveWithoutHidingPreviouslyHeldModels()
        {
            ConfigureNormalGrants();
            using (var supply = new SupplyProbe(this))
            {
                game.RestartGame();
                yield return WaitFor(() => PlayerReady() && HeldItems().Length == 4, supply);
                var held = HeldItems();
                int initialGiven = supply.GivenCount;
                float deadline = Time.realtimeSinceStartup + 25;
                while (!(game.Match.Batch == 2 && PlayerReady() && HeldItems().Length == 6) && Time.realtimeSinceStartup < deadline)
                {
                    supply.CheckFrame(); AssertHeld(held);
                    if (game.Match.ActiveParticipant == Participant.Player && !game.IsPresentingClock)
                    {
                        if (game.Match.Stage == TurnStage.AwaitingItems) game.EndItemPhase();
                        else if (game.Match.Stage == TurnStage.AwaitingClock) game.SelectClock(game.Match.GetAvailableClockIndices().First());
                        else if (game.Match.Stage == TurnStage.AwaitingAction) game.ChooseAction(TimeAction.Ration);
                    }
                    yield return null;
                }
                Assert.That(game.Match.Batch, Is.EqualTo(2));
                Assert.That(PlayerReady(), Is.True);
                Assert.That(supply.GivenCount - initialGiven, Is.EqualTo(2), "두 번째 묶음의 양측 한 개 지급도 실제 AddGive를 사용한다");
                Assert.That(game.Match.LastItemGrants.All(grant => grant.GrantedItems.Count == 1), Is.True);
                AssertHeld(held); AssertHeld(HeldItems());
                Assert.That(World.ModelUses.Hatch.Busy, Is.False);
                Assert.That(World.ModelUses.Hatch.WaitingCount, Is.Zero);
            }
        }

        private void ConfigureNormalGrants()
        {
            settings.Rules = JsonUtility.FromJson<GameRules>(JsonUtility.ToJson(normalRules));
            settings.Rules.InitialItemCount = 2; settings.Rules.ItemsPerBatch = 1;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.Rules.EnableAiItemStrategy = false;
            settings.AiThinkDuration = settings.DiscardDuration = .04f;
            World.ModelUses.Hatch.PlaybackSpeed = 20;
        }

        [UnityTest]
        public IEnumerator BypassUsesTheNativeGripBasisOnTheActualBoltsAndStoresBeforeHatchExit()
        {
            ConfigureNormalGrants();
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.AiThinkDuration = 10;
            game.RestartGame();
            int source = Enumerable.Range(0, 5).First(i => game.Match.GetClock(i).Face.Value.Kind == ClockKind.Normal);
            int id = TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.TimeBypass);
            using (var supply = new SupplyProbe(this))
            {
                yield return WaitFor(() => PlayerReady() && World.ModelUses.CanUseVisual(Participant.Player, id), supply);
                var model = World.PlayerItems.Single().ModelPresentation;
                model.PlaybackSpeed = 20;
                game.UseItem(id);
                yield return WaitFor(() => !game.IsPresentingItem && model.State == ItemPresentationState.Holding, supply);
                var cables = model.Cables;
                Assert.That(cables.HasTargetAnchors, Is.True);
                Assert.That(cables.AttachmentWeight, Is.GreaterThanOrEqualTo(.9999f));
                Assert.That(cables.MaxAttachedTargetError, Is.LessThan(.002f));
                for (int i = 0; i < 2; i++)
                {
                    var target = i == 0 ? model.Anchors.PlayerCableContact : model.Anchors.OpponentCableContact;
                    var boltName = i == 0 ? "ClampBolt_-1_-1" : "ClampBolt_1_-1";
                    var hex = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                        .Single(t => t.name == "HexHead" && t.parent.name == boltName);
                    Assert.That(Vector3.Distance(target.position, hex.position), Is.LessThan(.001f));
                    var nativeRotation = hex.rotation * (i == 0 ? Quaternion.Euler(0, 180, 0) : Quaternion.identity) * Quaternion.Euler(90, 0, 0);
                    Assert.That(Quaternion.Angle(cables.ClipRoots[i].rotation, nativeRotation), Is.LessThan(.1f));
                    var jaws = cables.ClipRoots[i].GetComponentsInChildren<Renderer>().Where(r => r.name.Contains("Jaw")).ToArray();
                    Assert.That(jaws.Length, Is.GreaterThanOrEqualTo(2));
                    var jawBounds = jaws[0].bounds; foreach (var jaw in jaws.Skip(1)) jawBounds.Encapsulate(jaw.bounds);
                    var boltBounds = hex.GetComponent<Renderer>().bounds; boltBounds.Expand(.01f);
                    Assert.That(jawBounds.Intersects(boltBounds), Is.True, "실제 두 턱의 영역과 원본 계수기 볼트가 접한다");
                    Assert.That(jawBounds.size.magnitude, Is.LessThan(.6f), "FBX 100배 단위가 중복되지 않는다");
                }
                bool storedBeforeHatch = false;
                System.Action<string, HatchPresentationEvent> onHatch = (token, signal) => {
                    if (token.EndsWith("/dispose") && signal == HatchPresentationEvent.Started)
                        storedBeforeHatch = model.SourceSeconds >= 236f / 30f - .01f;
                };
                World.ModelUses.Hatch.Presented += onHatch;
                try
                {
                    game.SelectClock(game.Match.LastShuffle.SourceToDestination[source]);
                    yield return WaitFor(() => !game.IsPresentingClock, supply);
                    game.ChooseAction(TimeAction.Collect);
                    bool sawBypassCue = false;
                    yield return WaitFor(() => { sawBypassCue |= game.View.IsBypassCueActive; return game.Match.UsedCount == 1 && World.ModelUses.ActiveCount == 0; }, supply);
                    Assert.That(sawBypassCue, Is.True, "실제 발동 정산 중 계수기/케이블 강조를 표시한다");
                    Assert.That(game.View.IsBypassCueActive, Is.False, "정산 종료 후 원래 Renderer property block으로 복원한다");
                    Assert.That(game.Match.LastItemEffects.Single(e => e.Source.Kind == ItemKind.TimeBypass).Triggered, Is.True);
                    Assert.That(storedBeforeHatch, Is.True);
                    Assert.That(World.ModelUses.HasFailedPresentation, Is.False);
                }
                finally { World.ModelUses.Hatch.Presented -= onHatch; }
            }
        }

        [UnityTest]
        public IEnumerator ThreePlayerHandcuffsCompleteNativeSupplyWithoutChangingTheProtectedH2OpeningOrRows()
        {
            ConfigureNormalGrants();
            settings.Rules.InitialItemCount = settings.Rules.MaxHeldItems = 3;
            settings.Rules.ItemWeights = new[] { 0, 0, 0, 0, 0, 1 };
            var hatch = World.ModelUses.Hatch;
            var surface = hatch.Surface;
            var originalMesh = surface.Surface.sharedMesh;
            var rows = new[] { World.PlayerAnchor, World.OpponentAnchor };
            var positions = rows.Select(row => row.localPosition).ToArray();
            var rotations = rows.Select(row => row.localRotation).ToArray();
            var scales = rows.Select(row => row.localScale).ToArray();
            var protectedOpenings = surface.ProtectedOpenings.Where(value => value).ToArray();
            Assert.That(protectedOpenings, Is.Not.Empty, "실제 메인 H2 회수구가 보호 대상으로 연결되어 있다");
            var holePositions = protectedOpenings.Select(value => value.transform.localPosition).ToArray();
            var holeRotations = protectedOpenings.Select(value => value.transform.localRotation).ToArray();
            var holeScales = protectedOpenings.Select(value => value.transform.localScale).ToArray();
            var holeEnabled = protectedOpenings.Select(value => value.enabled).ToArray();
            var holeMeshes = protectedOpenings.Select(value => value is MeshCollider mesh ? mesh.sharedMesh : null).ToArray();
            var holeBounds = (Bounds[])surface.ProtectedWorldBounds.Clone();
            using (var supply = new SupplyProbe(this))
            {
                game.RestartGame();
                yield return WaitFor(() => PlayerReady() && HeldItems().Length == 6, supply);
                Assert.That(World.PlayerItems.Count, Is.EqualTo(3));
                Assert.That(World.PlayerItems.All(item => item.Kind == ItemKind.Handcuffs), Is.True);
                Assert.That(supply.GivenCount, Is.EqualTo(2), "소유자마다 공통 Batch를 한 번만 실행한다");
                Assert.That(supply.ClearCount, Is.EqualTo(2));
                Assert.That(supply.RestoredCount, Is.EqualTo(2));
                Assert.That(World.ModelUses.HasAcquisitionFailure, Is.False);
                Assert.That(hatch.LastError, Is.Null.Or.Empty);
                Assert.That(hatch.Busy, Is.False); Assert.That(hatch.WaitingCount, Is.Zero);
                Assert.That(surface.HasOpening, Is.False);
                Assert.That(surface.Surface.sharedMesh, Is.SameAs(originalMesh));
                AssertHeld(HeldItems());
                for (int i = 0; i < rows.Length; i++)
                {
                    Assert.That(rows[i].localPosition, Is.EqualTo(positions[i]));
                    Assert.That(Quaternion.Angle(rows[i].localRotation, rotations[i]), Is.LessThan(.01f));
                    Assert.That(rows[i].localScale, Is.EqualTo(scales[i]));
                }
                for (int i = 0; i < protectedOpenings.Length; i++)
                {
                    var hole = protectedOpenings[i];
                    Assert.That(hole.transform.localPosition, Is.EqualTo(holePositions[i]));
                    Assert.That(Quaternion.Angle(hole.transform.localRotation, holeRotations[i]), Is.LessThan(.01f));
                    Assert.That(hole.transform.localScale, Is.EqualTo(holeScales[i]));
                    Assert.That(hole.enabled, Is.EqualTo(holeEnabled[i]));
                    if (hole is MeshCollider mesh) Assert.That(mesh.sharedMesh, Is.SameAs(holeMeshes[i]));
                }
                Assert.That(surface.ProtectedWorldBounds, Is.EqualTo(holeBounds));
            }
        }

        private ItemWorldView[] HeldItems() => World.PlayerItems.Concat(World.OpponentItems).ToArray();
        private bool PlayerReady() => game.Match.ActiveParticipant == Participant.Player &&
            (game.Match.Stage == TurnStage.AwaitingItems || game.Match.Stage == TurnStage.AwaitingClock) &&
            !World.ModelUses.IsAcquiring && !World.ModelUses.Hatch.Busy && !game.View.Lifecycle.IsRunning;

        private IEnumerator WaitFor(System.Func<bool> ready, SupplyProbe supply, ItemWorldView[] stable = null)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!ready() && Time.realtimeSinceStartup < deadline)
            {
                supply.CheckFrame();
                if (stable != null) AssertHeld(stable);
                yield return null;
            }
            supply.CheckFrame();
            Assert.That(ready(), Is.True, game.Match.Stage + " / supply=" + World.ModelUses.IsAcquiring);
        }

        private void AssertHeld(ItemWorldView[] items)
        {
            bool commonTransfer = World.ModelUses.Hatch.Busy && World.ModelUses.Hatch.ActiveRequestId.StartsWith("acquire/");
            foreach (var item in items)
            {
                Assert.That(item && item.gameObject.activeInHierarchy, Is.True);
                if (commonTransfer) continue; // 공통 Batch의 실제 지하 회수/상승 구간은 별도 FloorClear 검사로 확인한다.
                Assert.That(item.ModelPresentation.Visual.activeInHierarchy, Is.True, "공급 종료 및 H2 연출 중 보유품은 표시를 유지한다");
                Assert.That(BodyBottom(item), Is.EqualTo(TableHeight()).Within(.01f), item.Kind + " 보유 몸체의 바닥 높이");
            }
        }

        private float TableHeight() => World.ModelUses.Hatch.Surface.Surface.GetComponent<Renderer>().bounds.max.y;

        private static float BodyBottom(ItemWorldView item)
        {
            var model = item.ModelPresentation;
            var cables = model.Cables;
            float bottom = float.PositiveInfinity;
            foreach (var renderer in model.Visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || (cables && (cables.CableMeshes.Any(mesh => mesh && mesh.gameObject == renderer.gameObject) ||
                    cables.ClipRoots.Any(root => root && renderer.transform.IsChildOf(root))))) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var bounds = filter && filter.sharedMesh ? filter.sharedMesh.bounds : renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    bottom = Mathf.Min(bottom, renderer.transform.TransformPoint(corner).y);
                }
            }
            Assert.That(float.IsInfinity(bottom), Is.False);
            return bottom;
        }

        private sealed class SupplyProbe : System.IDisposable
        {
            private readonly M410ResidualTests owner;
            private readonly ItemHatchController hatch;
            private readonly HashSet<string> cleared = new HashSet<string>();
            public int ClearCount, GivenCount, RestoredCount, CancelledCount;
            public SupplyProbe(M410ResidualTests fixture)
            {
                owner = fixture;
                hatch = owner.World.ModelUses.Hatch;
                Assert.That(hatch.Modules.Single(module => module.Operation == HatchOperation.AddGive).Clip, Is.Not.Null);
                hatch.Presented += OnPresented;
            }
            private void OnPresented(string id, HatchPresentationEvent signal)
            {
                if (!id.StartsWith("acquire/", System.StringComparison.Ordinal)) return;
                if (signal == HatchPresentationEvent.FloorClear) { cleared.Add(id); ClearCount++; }
                if (signal == HatchPresentationEvent.VisualsGiven)
                {
                    Assert.That(cleared.Contains(id), Is.True, "FloorClear 뒤에만 실제 공급 완료를 알린다");
                    var given = owner.HeldItems().Where(item => item.ModelPresentation.Visual.activeInHierarchy &&
                        (item.Owner != Participant.Player || !hatch.Busy || !owner.World.ModelUses.CanUseVisual(item.Owner, item.ItemId.Value))).ToArray();
                    Assert.That(given, Is.Not.Empty);
                    foreach (var item in given) Assert.That(BodyBottom(item), Is.EqualTo(owner.TableHeight()).Within(.01f));
                    GivenCount++;
                }
                if (signal == HatchPresentationEvent.Restored) RestoredCount++;
                if (signal == HatchPresentationEvent.Cancelled) CancelledCount++;
            }
            public void CheckFrame()
            {
                if (!hatch.Busy || cleared.Contains(hatch.ActiveRequestId)) return;
                foreach (var item in owner.HeldItems())
                    if (item.ModelPresentation.Visual.activeInHierarchy)
                        Assert.That(BodyBottom(item), Is.GreaterThanOrEqualTo(owner.TableHeight() - .01f), "FloorClear 전 공급물은 지하에 보이지 않는다");
            }
            public void Dispose() => hatch.Presented -= OnPresented;
        }

        private static Renderer Dial(H2ClockPresentation clock) => clock.Watch.GetComponentsInChildren<Renderer>()
            .First(renderer => renderer.name == "Dial");
    }
}
#endif
