#if UNITY_EDITOR
using System;
using System.Collections;
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
    public sealed class M410EventIntegrationTests
    {
        private TimeTheftGameController game;
        private TimeTheftSettings settings;
        private float oldTimeScale;
        private InputTestFixture input;
        private Mouse mouse;
        private Camera camera;
        private TabletopFocusCamera focus;
        private ItemWorldInventoryView World => game.View.WorldItems;

        [UnitySetUp]
        public IEnumerator Load()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("M410EventMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            focus = game.View.FocusCamera; camera = focus.GetComponent<Camera>();
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            settings = UnityEngine.Object.Instantiate(game.Settings);
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.EnableAiItemStrategy = false;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.PreviewDuration = .1f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = .06f;
            settings.AiThinkDuration = 10;
            var serialized = new SerializedObject(game);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(game);
            Time.timeScale = 0;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game) { World.ModelUses?.ResetAll(); game.enabled = false; }
            if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator SixAcquisitionsFinishAtProfileSizedHoldingPosesAndBlockPrematureUse()
        {
            game.RestartGame();
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
                TimeTheftItemTestSetup.Give(game.Match, (int)kind < 3 ? Participant.Player : Participant.Ai, kind);
            yield return Wait(() => World.PlayerItems.Count == 3 && World.OpponentItems.Count == 3);
            var waiting = World.PlayerItems.First(item => !World.ModelUses.CanUseVisual(Participant.Player, item.ItemId.Value));
            game.UseItem(waiting.ItemId.Value);
            Assert.That(game.Match.GetInventory(Participant.Player).Count, Is.EqualTo(3), "획득 연출 중 사용 입력을 거절한다");
            Assert.That(World.ModelUses.ActiveCount, Is.Zero);
            yield return AcquisitionsReady();
            var widths = new[] { .9f, 1.3f, 1.25f, 1.5f, 1.15f, 1.8f };
            foreach (var item in World.PlayerItems.Concat(World.OpponentItems))
            {
                var model = item.ModelPresentation;
                Assert.That(model, Is.Not.Null);
                Assert.That(model.MotionRoot, Is.Not.Null);
                Assert.That(model.Profile.InventoryWidth, Is.EqualTo(widths[(int)item.Kind.Value]).Within(.001f));
                Assert.That(model.State == ItemPresentationState.Idle || model.State == ItemPresentationState.Complete, Is.True);
                var bounds = Bounds(model.Visual);
                float expected = widths[(int)item.Kind.Value] * Mathf.Abs(item.transform.lossyScale.x);
                Assert.That(Mathf.Max(bounds.size.x, bounds.size.z), Is.EqualTo(expected).Within(.08f), item.Kind + " 획득 완료 크기");
                var collider = item.GetComponent<Collider>();
                Assert.That(collider.enabled, Is.True);
                var click = collider.bounds; click.Expand(.04f);
                Assert.That(click.Contains(bounds.min) && click.Contains(bounds.max), Is.True, item.Kind + " 실제 모델 클릭 범위");
                Assert.That(item.Target.FocusPoint, Is.Not.Null);
                Assert.That(item.Target.FocusOnClick, Is.False);
                if (item.Kind == ItemKind.Screwdriver) Assert.That(bounds.size.y, Is.LessThan(.3f), "드라이버는 수평으로 놓인다");
            }
            Assert.That(World.OpponentItems.All(item => !item.ItemId.HasValue), Is.True, "상대 모델에 비공개 Source ID를 전달하지 않는다");
            game.RestartGame();
            yield return null;
            AssertClean();
        }

        [UnityTest]
        public IEnumerator KeyInspectsTheActualPrivateHandWithoutPublicFaceThenAllowsKeepAndReselect()
        {
            foreach (bool keep in new[] { true, false })
            {
                game.RestartGame();
                int key = TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.SpareKey);
                yield return Wait(() => World.PlayerItems.Count == 1);
                yield return AcquisitionsReady();
                var keyModel = World.PlayerItems.Single().ModelPresentation;
                game.UseItem(key);
                yield return Wait(() => !game.IsPresentingItem);
                Assert.That(game.Match.GetInventory(Participant.Player), Is.Empty);
                var clock = game.View.Clocks[0].H2Presentation;
                var centered = clock.MotionRoot.position; var centeredRotation = clock.MotionRoot.rotation;
                var hatch = keyModel.Hatch;
                Assert.That(hatch, Is.Not.Null, "열쇠의 실제 폐기 해치가 연결돼 있다");
                bool hatchRestored = false;
                string requestId = keyModel.Token + "/dispose";
                Action<string, HatchPresentationEvent> onHatch = (id, signal) =>
                { if (id == requestId && signal == HatchPresentationEvent.Restored) hatchRestored = true; };
                hatch.Presented += onHatch;
                try
                {
                    yield return ClickWorldClock(0);
                    yield return Wait(() => game.IsPresentingItem && Quaternion.Angle(clock.Watch.LidPivot.localRotation, Quaternion.identity) > 8);
                    Assert.That(game.Match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
                    Assert.That(clock.MotionRoot.position, Is.EqualTo(centered));
                    Assert.That(Quaternion.Angle(clock.MotionRoot.rotation, centeredRotation), Is.GreaterThan(1f), "개인 열람만 플레이어 쪽으로 약간 기울인다");
                    var face = game.Match.GetPrivateClock(Participant.Player, 0);
                    Assert.That(face.HasValue, Is.True);
                    Assert.That(Quaternion.Angle(clock.Watch.HourHand.localRotation,
                        Quaternion.Euler(0, 0, (face.Value.Hours % 12) * 30)), Is.LessThan(.02f));
                    AssertPrivate();
                    game.View.ShowPrivateClockPeek(0, Participant.Ai);
                    Canvas.ForceUpdateCanvases();
                    Assert.That(game.View.ClockValueLabels[0].text, Is.EqualTo("?"), "AI 열람 표시는 실제 개인값 대신 물음표만 쓴다");
                    game.View.ShowPrivateClockPeek(0, Participant.Player);
                    game.SelectClock(1);
                    Assert.That(game.Match.CurrentClock, Is.Zero, "열람 중 조기 재선택을 거절한다");
                    Assert.That(game.Match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
                    yield return Wait(() => !game.IsPresentingItem && !game.IsPresentingClock);
                    Assert.That(hatchRestored, Is.True, "해치 복원 완료 후 최종 시계 입력을 허용한다");
                    Assert.That(hatch.Busy, Is.False); Assert.That(World.ModelUses.ActiveCount, Is.Zero);
                    Assert.That(clock.MotionRoot.position, Is.EqualTo(centered));
                    Assert.That(Quaternion.Angle(clock.MotionRoot.rotation, centeredRotation), Is.LessThan(.01f), "닫은 뒤 원래 자세로 복귀한다");
                    Assert.That(Quaternion.Angle(clock.Watch.LidPivot.localRotation, Quaternion.identity), Is.LessThan(.02f));
                    Assert.That(game.View.KeepClockButton, Is.Null);
                    AssertPrivate();
                    int selected = keep ? 0 : 1;
                    yield return ClickWorldClock(selected);
                    yield return GameUiTestInput.ClockMotionReady(game);
                    Assert.That(game.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
                    Assert.That(game.Match.CurrentClock, Is.EqualTo(selected));
                    Assert.That(Vector3.Distance(game.View.Clocks[selected].H2Presentation.MotionRoot.position,
                        game.View.Clocks[selected].transform.position), Is.GreaterThan(.1f), "최종 월드 클릭 뒤에 기존 H2 선택 모션을 수행한다");
                    AssertPrivate();
                }
                finally { hatch.Presented -= onHatch; }
                game.RestartGame();
                yield return null;
                AssertClean();
            }
        }

        [UnityTest]
        public IEnumerator ScissorsContinueForwardAfterImpactAndPiecesWaitForActualHatchFloorClear()
        {
            game.RestartGame();
            int scissors = TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.Scissors);
            TimeTheftItemTestSetup.Give(game.Match, Participant.Ai, ItemKind.Screwdriver);
            yield return Wait(() => World.PlayerItems.Count == 1 && World.OpponentItems.Count == 1);
            yield return AcquisitionsReady();
            var tool = World.PlayerItems.Single();
            var victim = World.OpponentItems.Single();
            var victimBounds = Bounds(victim.ModelPresentation.Visual);
            var cut = tool.ModelPresentation.Cut;
            var model = tool.ModelPresentation;
            Assert.That(cut, Is.Not.Null);
            Assert.That(victim.ItemId, Is.Null);
            model.PlaybackSpeed = 1;
            game.UseItem(scissors);
            var hatch = model.Hatch;
            Assert.That(hatch, Is.Not.Null);
            hatch.PlaybackSpeed = 1;
            Assert.That(game.Match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(game.Match.GetInventory(Participant.Ai), Is.Empty, "유일한 상대 대상을 Match가 한 번 제거한다");
            bool floorClear = false, restored = false, sawFall = false, sawForwardExit = false;
            bool sawImpact = false;
            var impactPoint = Vector3.zero;
            string requestId = model.Token + "/cut";
            Action<string, HatchPresentationEvent> onHatch = (id, signal) =>
            {
                if (id != requestId) return;
                floorClear |= signal == HatchPresentationEvent.FloorClear;
                restored |= signal == HatchPresentationEvent.Restored;
            };
            Action<string, ItemPresentationEvent> onTool = (token, signal) =>
            {
                if (signal == ItemPresentationEvent.CutImpact) { sawImpact = true; impactPoint = model.Contact.position; }
                if (signal == ItemPresentationEvent.VisualRemoved && sawImpact)
                    sawForwardExit |= model.SourceSeconds > 71f / 30f && Vector3.Distance(model.Contact.position, impactPoint) > .2f;
            };
            hatch.Presented += onHatch;
            model.Presented += onTool;
            try
            {
                yield return Wait(() => cut && cut.Pieces, false);
                var pieces = cut.Pieces;
                Assert.That(pieces.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(2));
                Assert.That(victim, Is.Not.Null, "논리 제거 뒤에도 Cut 완료까지 피해 모델 소유자를 보존한다");
                Assert.That(Bounds(pieces).size.y, Is.LessThan(victimBounds.size.y + .08f), "실제 피해 대상의 자세를 조각에도 전달한다");
                var before = pieces.GetComponentsInChildren<Renderer>().Select(r => r.bounds.center).ToArray();
                yield return new WaitForSecondsRealtime(.1f);
                var after = pieces.GetComponentsInChildren<Renderer>().Select(r => r.bounds.center).ToArray();
                Assert.That(after.Where((point, i) => Vector3.Distance(point, before[i]) > .001f).Count(), Is.EqualTo(2));
                float deadline = Time.realtimeSinceStartup + 20;
                while ((World.ModelUses.ActiveCount > 0 || game.IsPresentingItem) && Time.realtimeSinceStartup < deadline)
                {
                    if (model && model.Visual.activeInHierarchy && model.SourceSeconds > 71f / 30f)
                        sawForwardExit |= Vector3.Distance(model.Contact.position, impactPoint) > .2f;
                    if (pieces && pieces.activeInHierarchy)
                    {
                        float bottom = Bounds(pieces).min.y;
                        if (!floorClear) Assert.That(bottom, Is.GreaterThanOrEqualTo(victimBounds.min.y - .02f), "FloorClear 전에는 상판을 관통하지 않는다");
                        else sawFall |= bottom < victimBounds.min.y - .1f;
                    }
                    yield return null;
                }
                Assert.That(floorClear && restored && sawFall, Is.True, "실제 해치의 통로 확보·낙하·상판 복원을 관찰한다");
                Assert.That(sawForwardExit, Is.True, "절단 뒤에도 원래 전진 퇴장 경로를 재생한다");
                Assert.That(hatch.Busy, Is.False); Assert.That(World.ModelUses.ActiveCount, Is.Zero);
                yield return null;
                Assert.That(tool == null && victim == null && pieces == null, Is.True);
                AssertClean();
            }
            finally
            {
                hatch.Presented -= onHatch;
                if (model) model.Presented -= onTool;
            }
            game.RestartGame();
            yield return null;
            AssertClean();
        }

        [UnityTest]
        public IEnumerator RedCollectDriverActivatesAtTheHeldClockBeforeLoweringAndSettlement()
        {
            settings.RandomSeed = 635;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.Rules.InitialLifeHours = 100;
            game.RestartGame();
            int driver = TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.Screwdriver);
            yield return Wait(() => World.PlayerItems.Count == 1);
            yield return AcquisitionsReady();
            var model = World.PlayerItems.Single().ModelPresentation;
            yield return Wait(() => !game.IsFlowBlocked && game.Match.Stage == TurnStage.AwaitingItems);
            game.UseItem(driver);
            yield return Wait(() => !game.IsPresentingItem && !game.IsFlowBlocked);
            int red = ReferenceClock(ClockKind.Red);
            game.SelectClock(red);
            yield return GameUiTestInput.ClockMotionReady(game);
            bool activated = false, beforeLowering = false, exactContact = false;
            Action<string, ItemPresentationEvent> onItem = (token, signal) =>
            {
                if (signal != ItemPresentationEvent.Activated) return;
                activated = true;
                beforeLowering = game.Match.UsedCount == 0 && !game.View.TurnPresentation.IsOnPublicTable &&
                    game.View.TurnPresentation.Phase == H2TurnPresentation.TurnPhase.ResultPose;
                exactContact = model.Contact && Vector3.Distance(model.Contact.position, model.Anchors.ClockFocus.position) < .01f;
            };
            model.Presented += onItem;
            try
            {
                game.ChooseAction(TimeAction.Collect);
                yield return Wait(() => activated);
                Assert.That(beforeLowering, Is.True, "드라이버는 시계를 든 공개 자세에서 실제 정산·내려놓기 전에 발동한다");
                Assert.That(exactContact, Is.True, "실제 시계 contact anchor에 팁이 도달한다");
                yield return Wait(() => game.Match.UsedCount == 1);
                Assert.That(game.Match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.Screwdriver).Triggered, Is.True);
            }
            finally { if (model) model.Presented -= onItem; }
        }

        private IEnumerator AcquisitionsReady()
        {
            yield return Wait(() => World.PlayerItems.Concat(World.OpponentItems).All(item => item.ModelPresentation &&
                (item.ModelPresentation.State == ItemPresentationState.Idle || item.ModelPresentation.State == ItemPresentationState.Complete)) &&
                World.PlayerItems.All(item => World.ModelUses.CanUseVisual(Participant.Player, item.ItemId.Value)));
            yield return World.ModelUses.WaitForAcquisitions();
            yield return null;
        }
        private IEnumerator Wait(Func<bool> condition, bool fastModels = true)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                if (fastModels)
                    foreach (var item in UnityEngine.Object.FindObjectsByType<ItemWorldView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (item.ModelPresentation) item.ModelPresentation.PlaybackSpeed = 50;
                yield return null;
            }
            Assert.That(condition(), Is.True, "M410 이벤트 통합 상태 대기 시간 초과: " + game.Match.Stage +
                ", flow=" + game.IsFlowBlocked + ", item=" + game.IsPresentingItem + ", clock=" + game.IsPresentingClock +
                ", preparing=" + World.ModelUses.IsPreparing + ", acquiring=" + World.ModelUses.IsAcquiring);
        }
        private IEnumerator ClickWorldClock(int index)
        {
            var clock = game.View.Clocks[index];
            yield return Wait(() => clock.SelectionTarget.InputEnabled && !game.IsPresentingClock && !World.ModelUses.IsAcquiring);
            Assert.That(clock.SelectionTarget.InputEnabled, Is.True);
            for (int frame = 0; frame < 8; frame++)
            {
                Physics.SyncTransforms();
                input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(clock.GetComponent<Collider>().bounds.center));
                GameUiTestInput.Process(); focus.SendMessage("Update");
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(clock.SelectionTarget));
            input.Press(mouse.leftButton); GameUiTestInput.Process(); focus.SendMessage("Update"); yield return null;
            input.Release(mouse.leftButton); GameUiTestInput.Process(); focus.SendMessage("Update"); yield return null;
        }
        private int ReferenceClock(ClockKind kind)
        {
            for (int index = 0; index < TimeTheftMatch.ClocksPerBatch; index++)
            {
                var reference = new TimeTheftMatch(settings.Rules, new System.Random(settings.RandomSeed));
                reference.BeginBatch(); reference.CloseAndShuffle(); reference.CompleteShuffle();
                if (reference.Stage == TurnStage.AwaitingItems) reference.EndItemPhase(Participant.Player);
                reference.SelectClock(Participant.Player, index); reference.CommitAction(Participant.Player, TimeAction.Collect);
                if (reference.GetClock(index).Face.Value.Kind == kind) return index;
            }
            throw new InvalidOperationException("재현용 시계 종류가 없습니다.");
        }
        private static Bounds Bounds(GameObject visual)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            Assert.That(renderers, Is.Not.Empty);
            var result = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) result.Encapsulate(renderer.bounds);
            return result;
        }
        private void AssertPrivate()
        {
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < 5; i++)
            {
                Assert.That(game.Match.GetClock(i).Face, Is.Null);
                bool privatePeek = game.Match.Stage == TurnStage.AwaitingKeyDecision && game.Match.CurrentClock == i;
                var known = privatePeek ? game.Match.GetPrivateClock(Participant.Player, i) : (ClockFace?)null;
                Assert.That(game.View.ClockValueLabels[i].text, Is.EqualTo(known.HasValue ? known.Value.Hours.ToString("00") + ":00" : ""));
                Assert.That(game.View.ClockValueLabels[i].gameObject.activeInHierarchy, Is.EqualTo(known.HasValue));
            }
        }
        private void AssertClean()
        {
            Assert.That(World.ModelUses.ActiveCount, Is.Zero);
            Assert.That(World.ModelUses.IsPreparing, Is.False);
            Assert.That(World.ModelUses.transform.Find("Used item models").childCount, Is.Zero);
        }
    }
}
#endif
