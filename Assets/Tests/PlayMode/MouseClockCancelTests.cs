#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class MouseClockCancelTests
    {
        private TimeTheftGameController game;
        private TimeTheftSettings settings;
        private TabletopFocusCamera focus;
        private Camera camera;
        private Mouse mouse;
        private InputTestFixture input;
        private float oldTimeScale;

        [UnitySetUp]
        public IEnumerator Load()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("MouseClockCancel");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(game.Settings);
            settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.Rules.EnableAiItemStrategy = false;
            settings.Rules.InitialLifeHours = 100;
            settings.PreviewDuration = .1f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = .1f;
            settings.AiThinkDuration = 10;
            var serialized = new SerializedObject(game);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(game);
            game.View.TurnPresentation.SelectDuration = .35f;
            focus = game.View.FocusCamera; camera = focus.GetComponent<Camera>();
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
            game.RestartGame();
            yield return Wait(() => game.Match.Stage == TurnStage.AwaitingClock);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game) game.enabled = false;
            if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator ClickingTheMovedClosedWatchAndRightClickBothReturnOnlyTheUncommittedSelection()
        {
            var match = game.Match;
            var first = game.View.Clocks[0];
            var home = first.H2Presentation.MotionRoot.position;
            var homeRotation = first.H2Presentation.MotionRoot.rotation;
            var balances = Balances();
            yield return ClickWatch(0);
            yield return GameUiTestInput.ClockMotionReady(game);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(Vector3.Distance(first.H2Presentation.MotionRoot.position, home), Is.GreaterThan(.1f));
            AssertPrivate();

            // 논리 슬롯의 예전 Collider가 아니라 실제 이동한 H2 외함을 가리킨다.
            yield return ClickWatch(0);
            yield return Wait(() => !game.IsPresentingClock && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(Vector3.Distance(first.H2Presentation.MotionRoot.position, home), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(first.H2Presentation.MotionRoot.rotation, homeRotation), Is.LessThan(.01f));
            Assert.That(first.IsSelected, Is.False);
            Assert.That(match.UsedCount, Is.Zero); Assert.That(match.Turn, Is.EqualTo(1));
            Assert.That(Balances(), Is.EqualTo(balances)); AssertPrivate();

            var second = game.View.Clocks[1];
            var secondHome = second.H2Presentation.MotionRoot.position;
            yield return ClickWatch(1);
            yield return GameUiTestInput.ClockMotionReady(game);
            yield return RightClick();
            yield return RightClick();
            yield return Wait(() => !game.IsPresentingClock && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(Vector3.Distance(second.H2Presentation.MotionRoot.position, secondHome), Is.LessThan(.001f));
            Assert.That(game.View.CollectButton.gameObject.activeInHierarchy, Is.False);
            Assert.That(game.View.RationButton.gameObject.activeInHierarchy, Is.False);
            Assert.That(match.UsedCount, Is.Zero); Assert.That(Balances(), Is.EqualTo(balances)); AssertPrivate();
        }

        [UnityTest]
        public IEnumerator RightClickDuringSelectionOrAfterCommitCannotCancelOrSettleTwice()
        {
            game.View.TurnPresentation.SelectDuration = .8f;
            settings.RevealDuration = 1;
            yield return ClickWatch(0);
            Assert.That(game.IsPresentingClock, Is.True);
            yield return RightClick();
            Assert.That(game.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(game.Match.CurrentClock, Is.Zero);
            yield return GameUiTestInput.ClockMotionReady(game);
            Assert.That(game.Match.CancelClockSelection(Participant.Ai), Is.False);
            yield return GameUiTestInput.Click(input, mouse, game.View.CollectButton);
            var match = game.Match;
            Assert.That(match.Stage, Is.EqualTo(TurnStage.Revealing));
            Assert.That(match.CancelClockSelection(Participant.Player), Is.False);
            yield return RightClick();
            Assert.That(match.Action, Is.EqualTo(TimeAction.Collect));
            yield return Wait(() => match.UsedCount == 1);
            var balances = Balances();
            yield return RightClick();
            yield return null;
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(Balances(), Is.EqualTo(balances));
        }

        [UnityTest]
        public IEnumerator KeyInspectionAndItsFinalChoiceCannotBeCancelledToPeekAtAnotherWatch()
        {
            game.RestartGame();
            int key = TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.SpareKey);
            yield return Wait(() => game.Match.Stage == TurnStage.AwaitingItems && game.View.WorldItems.PlayerItems.Count == 1 &&
                game.View.WorldItems.ModelUses.CanUseVisual(Participant.Player, key));
            game.UseItem(key);
            yield return Wait(() => !game.IsPresentingItem);
            var match = game.Match;
            yield return ClickWatch(0);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
            yield return RightClick();
            Assert.That(match.CurrentClock, Is.Zero);
            Assert.That(match.CancelClockSelection(Participant.Player), Is.False);
            yield return GameUiTestInput.ClockMotionReady(game);
            Assert.That(match.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
            Assert.That(match.GetPrivateClock(Participant.Player, 1), Is.Null);
            AssertPrivate();
            yield return ClickWatch(1);
            yield return GameUiTestInput.ClockMotionReady(game);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(match.CurrentClock, Is.EqualTo(1));
            yield return RightClick();
            Assert.That(match.CancelClockSelection(Participant.Player), Is.False, "열쇠 최종 선택 취소로 첫 private 선택을 반복할 수 없다");
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(match.CurrentClock, Is.EqualTo(1));
            Assert.That(match.SelectClock(Participant.Player, 2), Is.False);
            Assert.That(match.GetPrivateClock(Participant.Player, 1), Is.Null);
            Assert.That(match.GetPrivateClock(Participant.Player, 2), Is.Null);
            Assert.That(match.UsedCount, Is.Zero); AssertPrivate();
        }

        private IEnumerator ClickWatch(int index)
        {
            var clock = game.View.Clocks[index];
            yield return Wait(() => clock.SelectionTarget.InputEnabled && !game.IsPresentingClock && !game.View.WorldItems.ModelUses.IsAcquiring);
            var body = clock.H2Presentation.Watch.GetComponentsInChildren<Renderer>().First(renderer => renderer.name == "Case");
            Assert.That(clock.SelectionTarget.InputEnabled, Is.True);
            for (int frame = 0; frame < 8; frame++)
            {
                Physics.SyncTransforms();
                input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(body.bounds.center));
                GameUiTestInput.Process(); focus.SendMessage("Update");
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(clock.SelectionTarget), "현재 H2 외함 위치에서 실제 선택 Collider를 맞힌다");
            input.Press(mouse.leftButton); GameUiTestInput.Process(); focus.SendMessage("Update"); yield return null;
            input.Release(mouse.leftButton); GameUiTestInput.Process(); focus.SendMessage("Update"); yield return null;
        }

        private IEnumerator RightClick()
        {
            input.Set(mouse.position, camera.pixelRect.min + Vector2.one * 5);
            input.Press(mouse.rightButton); GameUiTestInput.Process(); focus.SendMessage("Update"); yield return null;
            input.Release(mouse.rightButton); GameUiTestInput.Process(); focus.SendMessage("Update"); yield return null;
        }

        private int[] Balances() => new[] { game.Match.PlayerLifeMinutes, game.Match.AiLifeMinutes, game.Match.BankMinutes };

        private void AssertPrivate()
        {
            Canvas.ForceUpdateCanvases();
            foreach (var clock in Enumerable.Range(0, TimeTheftMatch.ClocksPerBatch))
            {
                Assert.That(game.Match.GetClock(clock).Face, Is.Null);
                var known = game.Match.Stage == TurnStage.AwaitingKeyDecision && game.Match.CurrentClock == clock
                    ? game.Match.GetPrivateClock(Participant.Player, clock) : (ClockFace?)null;
                Assert.That(game.View.ClockValueLabels[clock].text, Is.EqualTo(known.HasValue ? known.Value.Hours.ToString("00") + ":00" : ""));
            }
        }

        private IEnumerator Wait(Func<bool> ready)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (!ready() && Time.realtimeSinceStartup < deadline)
            {
                foreach (var item in UnityEngine.Object.FindObjectsByType<ItemWorldView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (item.ModelPresentation) item.ModelPresentation.PlaybackSpeed = 40;
                yield return null;
            }
            Assert.That(ready(), Is.True, game.Match.Stage + " / " + game.View.TurnPresentation.Phase);
        }
    }
}
#endif
