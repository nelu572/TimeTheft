#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class TimeTheftGameIntegrationTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private Keyboard keyboard;
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private Camera camera;
        private TabletopFocusCamera focus;
        private float oldTimeScale;
        private Color playerColor, aiColor;

        [UnitySetUp]
        public IEnumerator LoadMainScene()
        {
            oldTimeScale = Time.timeScale;
            TabletopDecisionUITestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("TimeTheftV03Mouse");
            keyboard = InputSystem.AddDevice<Keyboard>("TimeTheftV03Keyboard");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            Assert.That(controller, Is.Not.Null);
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.RandomSeed = 635;
            settings.PreviewDuration = .2f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.AiThinkDuration =
                settings.ResultHoldDuration = settings.DiscardDuration = .08f;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            playerColor = controller.View.PlayerLife.color; aiColor = controller.View.AiLife.color;
            camera = Camera.main;
            focus = controller.View.FocusCamera;
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
            controller.RestartGame();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false;
            if (focus) focus.enabled = false;
            TabletopDecisionUITestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator MainSceneCompletesThroughActionButtonsAndActualClockClicks()
        {
            Assert.That(UnityEngine.Object.FindObjectsByType<TimeTheftGameController>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(UnityEngine.Object.FindFirstObjectByType<TabletopGameController>(), Is.Null);
            Assert.That(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(GameObject.Find("Action UI/Controls/Ready"), Is.Null);
            var counter = GameObject.Find("Devices/IntegratedTimeCounter").transform;
            foreach (var label in new[] { controller.View.PlayerLife, controller.View.AiLife, controller.View.Bank })
                Assert.That(label.transform.IsChildOf(counter), Is.True);
            Assert.That(controller.View.Clocks, Has.Length.EqualTo(5));
            AssertNumbers();
            int initialClockId = controller.View.Clocks[0].GetInstanceID();
            int acted = 0, batches = 0, lastBatch = 0;
            bool sawRation = false, sawCollect = false, sawTransfer = false, sawNonSequential = false;
            float deadline = Time.realtimeSinceStartup + 120;
            while (!controller.View.IsResultVisible && Time.realtimeSinceStartup < deadline)
            {
                var match = controller.Match;
                if (match.Batch != lastBatch) { batches++; lastBatch = match.Batch; }
                if (match.Stage == TurnStage.AwaitingClock && match.ActiveParticipant == Participant.Player)
                {
                    bool ration = acted++ == 0;
                    sawRation |= ration; sawCollect |= !ration;
                    var available = match.GetAvailableClockIndices();
                    int index = available[(acted * 3) % available.Length];
                    sawNonSequential |= index != match.UsedCount;
                    yield return ClickClock(index);
                    yield return ChooseAction(ration ? TimeAction.Ration : TimeAction.Collect);
                }
                if (controller.View.IsCountingTime)
                {
                    sawTransfer = true;
                    Assert.That(Read(controller.View.PlayerLife) + Read(controller.View.AiLife) + Read(controller.View.Bank), Is.EqualTo(3600));
                    Assert.That(Read(controller.View.Bank), Is.GreaterThanOrEqualTo(0));
                }
                else if (match.Stage == TurnStage.AwaitingClock || match.Stage == TurnStage.BatchPreview || controller.View.IsResultVisible) AssertNumbers();
                yield return null;
            }
            Assert.That(controller.View.IsResultVisible, Is.True, "고정 시드 게임 완주");
            Assert.That(controller.Match.Winner, Is.Not.EqualTo(GameWinner.None));
            Assert.That(sawCollect && sawRation && sawTransfer && sawNonSequential, Is.True);
            Assert.That(batches, Is.GreaterThanOrEqualTo(2), "다음 묶음 자동 진행");
            Assert.That(Math.Min(controller.Match.PlayerLifeMinutes, controller.Match.AiLifeMinutes), Is.Zero);
            AssertNumbers();
            controller.RestartGame();
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview));
            Assert.That(controller.View.IsResultVisible, Is.False);
            Assert.That(controller.View.Clocks[0].GetInstanceID(), Is.EqualTo(initialClockId));
            Assert.That(controller.Match.BankMinutes, Is.EqualTo(1200));
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator PreviewDurationAutomaticallyClosesAndShufflesForEitherFirstParticipant()
        {
            foreach (var first in new[] { Participant.Player, Participant.Ai })
            {
                settings.Rules.FirstParticipant = first;
                settings.PreviewDuration = first == Participant.Player ? .45f : .7f;
                settings.AiThinkDuration = .3f;
                controller.RestartGame();
                float started = Time.realtimeSinceStartup;
                controller.SelectClock(4);
                controller.ChooseAction(TimeAction.Collect);
                yield return new WaitForSecondsRealtime(settings.PreviewDuration * .4f);
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview));
                Assert.That(controller.View.Clocks.All(c => c.IsOpen && c.Number.text != "" && !c.SelectionTarget.InputEnabled), Is.True);
                Assert.That(controller.View.CollectButton.gameObject.activeSelf, Is.False);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.Shuffling);
                Assert.That(Time.realtimeSinceStartup - started, Is.GreaterThanOrEqualTo(settings.PreviewDuration - .06f));
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                Assert.That(controller.View.Clocks.All(c => !c.IsOpen && c.Number.text == "" && c.Kind.text == ""), Is.True);
                Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
                if (first == Participant.Ai)
                {
                    yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingAction);
                    Assert.That(controller.Match.CurrentClock, Is.InRange(0, 4));
                    Assert.That(controller.View.Clocks.All(c => !c.IsOpen), Is.True, "AI도 먼저 고른 닫힌 시계를 유지한다");
                    Assert.That(Enumerable.Range(0, 5).All(i => !controller.Match.GetClock(i).Face.HasValue), Is.True);
                    yield return WaitFor(() => controller.Match.UsedCount > 0);
                    Assert.That(controller.Match.CurrentClock, Is.InRange(0, 4));
                    Assert.That(controller.View.CollectButton.gameObject.activeSelf, Is.False);
                }
            }
        }

        [UnityTest]
        public IEnumerator NonSequentialSelectionStaysPrivateUntilActionAndCannotBeReused()
        {
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            controller.ChooseAction(TimeAction.Ration);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock), "시계 선택 전 행동 확정 거부");
            Assert.That(controller.View.CollectButton.interactable || controller.View.RationButton.interactable, Is.False);
            yield return ClickClock(4);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen && !c.SelectionTarget.InputEnabled), Is.True);
            Assert.That(controller.View.Clocks[4].IsSelected, Is.True);
            controller.SelectClock(0);
            controller.View.Clocks[0].SelectionTarget.Activate();
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(4), "선택 후 연타로 다른 시계를 고르지 않는다");
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.Match.Turn == 3 && controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.GetClock(4).Visibility, Is.EqualTo(ClockVisibility.Used));
            Assert.That(controller.View.Clocks[4].gameObject.activeSelf, Is.False);
            controller.SelectClock(4);
            controller.View.Clocks[4].SelectionTarget.Activate();
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator LastTwoRemainSelectableThenUnchosenClockIsDiscardedUnopened()
        {
            settings.Rules.InitialLifeHours = 100;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.DiscardDuration = .3f;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Turn == 2 && controller.Match.Stage == TurnStage.AwaitingClock);
            yield return ClickClock(controller.Match.GetAvailableClockIndices().Last());
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.Match.Turn == 4 && controller.Match.Stage == TurnStage.AwaitingClock);
            var remaining = controller.Match.GetAvailableClockIndices();
            Assert.That(remaining, Has.Length.EqualTo(2));
            Assert.That(remaining.All(i => controller.View.Clocks[i].SelectionTarget.InputEnabled), Is.True);
            int chosen = remaining.Last(), discarded = remaining.First();
            yield return ClickClock(chosen);
            Assert.That(controller.Match.GetClock(chosen).Face.HasValue, Is.False);
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.BatchComplete);
            Assert.That(controller.Match.UsedCount, Is.EqualTo(4));
            Assert.That(controller.Match.GetClock(discarded).Visibility, Is.EqualTo(ClockVisibility.Discarded));
            Assert.That(controller.Match.GetClock(discarded).Face.HasValue, Is.False);
            Assert.That(controller.View.Clocks[discarded].gameObject.activeSelf, Is.False);
            Assert.That(controller.View.Clocks[discarded].Number.text, Is.Empty);
            yield return WaitFor(() => controller.Match.Batch == 2 && controller.Match.Stage == TurnStage.BatchPreview);
            Assert.That(controller.View.Clocks.All(c => c.IsOpen), Is.True);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen), Is.True);
            yield return WaitFor(() => controller.Match.Turn == 6 && controller.Match.Stage == TurnStage.AwaitingClock);
        }

        [UnityTest]
        public IEnumerator RestartCancelsPreviewSelectionAndPresentationWithoutStaleInput()
        {
            settings.PreviewDuration = .4f;
            foreach (var stage in new[] { TurnStage.BatchPreview, TurnStage.Shuffling, TurnStage.AwaitingClock,
                TurnStage.AwaitingAction, TurnStage.Revealing, TurnStage.Settling })
            {
                settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = .2f;
                controller.RestartGame();
                if (stage == TurnStage.Shuffling) yield return WaitFor(() => controller.Match.Stage == stage);
                else if (stage != TurnStage.BatchPreview)
                {
                    yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                    if (stage != TurnStage.AwaitingClock)
                    {
                        controller.SelectClock(4);
                        if (stage != TurnStage.AwaitingAction) controller.ChooseAction(TimeAction.Collect);
                        yield return WaitFor(() => controller.Match.Stage == stage);
                    }
                }
                controller.RestartGame();
                controller.SelectClock(0);
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview));
                Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
                Assert.That(controller.Match.UsedCount, Is.Zero);
                Assert.That(controller.View.Clocks.All(c => c.IsOpen && !c.SelectionTarget.InputEnabled), Is.True);
                AssertNumbers();
            }
            controller.enabled = false;
            controller.SelectClock(4);
            Assert.That(controller.View.Clocks.All(c => !c.SelectionTarget.InputEnabled), Is.True);
            controller.enabled = true;
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview));
            input.Press(keyboard.rKey); InputSystem.Update(); controller.SendMessage("Update"); input.Release(keyboard.rKey); InputSystem.Update();
            Assert.That(controller.Match.Batch, Is.EqualTo(1));
            // 씬 종료 시 UI가 Controller보다 먼저 파괴되는 순서도 안전해야 한다.
            UnityEngine.Object.Destroy(controller.View.CollectButton.GetComponentInParent<Canvas>().gameObject);
            yield return null;
            controller.enabled = false;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BothRationFacesShowActualRecipientAndExactMinutesBeyondTwentyFourHours()
        {
            settings.Rules.InitialLifeHours = 40;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 7;
            settings.TransferDuration = .3f;
            settings.ResultHoldDuration = .35f;
            foreach (var kind in new[] { ClockKind.Normal, ClockKind.Red })
            {
                controller.RestartGame();
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                Assert.That(controller.View.PlayerLife.text, Is.EqualTo("30:00"));
                int index = -1;
                for (int i = 0; i < 5; i++)
                {
                    // 같은 시드의 독립 모델을 오라클로 사용한다. 런타임 비공개 API는 확장하지 않는다.
                    var reference = new TimeTheftMatch(settings.Rules, new System.Random(settings.RandomSeed));
                    reference.BeginBatch(); reference.CloseAndShuffle(); reference.CompleteShuffle();
                    reference.SelectClock(Participant.Player, i); reference.CommitAction(Participant.Player, TimeAction.Ration);
                    if (reference.GetClock(i).Face.Value.Kind == kind) { index = i; break; }
                }
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                yield return ClickClock(index);
                yield return ChooseAction(TimeAction.Ration);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.Settling);
                var match = controller.Match;
                bool playerReceives = kind == ClockKind.Normal;
                Assert.That(match.TransferParticipant, Is.EqualTo(playerReceives ? Participant.Player : Participant.Ai));
                Assert.That(match.TransferredMinutes, Is.EqualTo(140));
                Assert.That(match.BankMinutes, Is.EqualTo(1060));
                Assert.That(match.PlayerLifeMinutes, Is.EqualTo(playerReceives ? 1940 : 1800));
                Assert.That(match.AiLifeMinutes, Is.EqualTo(playerReceives ? 1800 : 1940));
                Assert.That(controller.View.PhaseLabel.text, Does.Contain(playerReceives ? "Bank → 나" : "Bank → 상대"));
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("02:20"));
                var serialized = new SerializedObject(controller.View);
                var targetDelta = (TextMeshPro)serialized.FindProperty(playerReceives ? "playerDelta" : "aiDelta").objectReferenceValue;
                var bankDelta = (TextMeshPro)serialized.FindProperty("bankDelta").objectReferenceValue;
                Assert.That(targetDelta.text, Is.EqualTo("+02:20"));
                Assert.That(bankDelta.text, Is.EqualTo("−02:20"));
                yield return WaitFor(() => !controller.View.IsCountingTime);
                AssertNumbers();
                Assert.That(playerReceives ? controller.View.PlayerLife.text : controller.View.AiLife.text, Is.EqualTo("32:20"));
                Assert.That(controller.View.Bank.text, Is.EqualTo("17:40"));
            }
        }

        [UnityTest]
        public IEnumerator LargeMinuteBalancesKeepUnchangedLifeExactDuringTransfer()
        {
            settings.Rules.InitialLifeHours = int.MaxValue / (2 * GameTime.MinutesPerHour);
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 7;
            settings.TransferDuration = .4f;
            settings.ResultHoldDuration = .3f;
            controller.RestartGame();
            int initialLife = controller.Match.PlayerLifeMinutes;
            int total = initialLife + controller.Match.AiLifeMinutes + controller.Match.BankMinutes;
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            yield return ClickClock(4);
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.View.IsCountingTime);
            while (controller.View.IsCountingTime)
            {
                var unchanged = controller.Match.TransferParticipant == Participant.Player ? controller.View.AiLife : controller.View.PlayerLife;
                Assert.That(Read(unchanged), Is.EqualTo(initialLife), "수치가 큰 경우에도 변하지 않는 수명을 float로 반올림하지 않는다");
                Assert.That(Read(controller.View.PlayerLife) + Read(controller.View.AiLife) + Read(controller.View.Bank), Is.EqualTo(total));
                Assert.That(Read(controller.View.Bank), Is.GreaterThanOrEqualTo(0));
                yield return null;
            }
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator CollectionStillCapsAtLifeAndCounterInspectionReturnsToClockSelection()
        {
            settings.Rules.InitialLifeHours = 11;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 12;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            yield return ClickClock(4);
            var target = GameObject.Find("Devices/IntegratedTimeCounter").GetComponent<TabletopFocusTarget>();
            Assert.That(focus.Focus(target), Is.True);
            yield return ChooseAction(TimeAction.Collect);
            yield return WaitFor(() => controller.View.IsResultVisible);
            Assert.That(controller.Match.BankMinutes, Is.EqualTo(1260));
            Assert.That(controller.Match.TransferredMinutes, Is.EqualTo(60));
            Assert.That(Math.Min(controller.Match.PlayerLifeMinutes, controller.Match.AiLifeMinutes), Is.Zero);
            AssertNumbers();
        }

        private IEnumerator ChooseAction(TimeAction action)
        {
            var match = controller.Match;
            int selected = match.CurrentClock;
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(selected, Is.InRange(0, 4));
            yield return TabletopDecisionUITestInput.Click(input, mouse,
                action == TimeAction.Ration ? controller.View.RationButton : controller.View.CollectButton, null, () =>
                {
                    Assert.That(match.Stage, Is.EqualTo(TurnStage.Revealing));
                    Assert.That(match.CurrentClock, Is.EqualTo(selected));
                    AssertRevealed(selected);
                    controller.View.RationButton.onClick.Invoke();
                    controller.View.CollectButton.onClick.Invoke();
                    Assert.That(match.Action, Is.EqualTo(action));
                });
        }

        private IEnumerator ClickClock(int index, Action afterPress = null)
        {
            yield return WaitFor(() => !focus.IsTransitioning);
            var token = controller.View.Clocks[index];
            var target = token.SelectionTarget;
            Assert.That(target.IsAvailable && target.InputEnabled, Is.True, "선택 가능한 시계 " + index);
            var collider = token.GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            for (int frame = 0; frame < 10; frame++)
            {
                mouse.MakeCurrent();
                input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(collider.bounds.center));
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(target), "실제 Collider 레이캐스트로 클릭한 시계를 선택");
            input.Press(mouse.leftButton);
            yield return null;
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(index));
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen && c.Number.text == "" && c.Kind.text == ""), Is.True);
            Assert.That(Enumerable.Range(0, 5).All(i => !controller.Match.GetClock(i).Face.HasValue), Is.True);
            Assert.That(controller.View.Clocks.Count(c => c.IsSelected), Is.EqualTo(1));
            Assert.That(token.IsSelected, Is.True);
            Assert.That(token.transform.Find("Visual").localPosition.z, Is.LessThan(0));
            Assert.That(controller.View.CollectButton.interactable && controller.View.RationButton.interactable, Is.True);
            afterPress?.Invoke();
            input.Release(mouse.leftButton);
            yield return null;
        }

        private void AssertRevealed(int index)
        {
            var match = controller.Match;
            var face = match.GetClock(index).Face;
            Assert.That(face.HasValue, Is.True);
            Assert.That(controller.View.Clocks[index].IsOpen, Is.True);
            Assert.That(controller.View.Clocks[index].Number.text, Is.EqualTo(face.Value.Hours + "h"));
            Assert.That(controller.View.Clocks[index].Kind.text, Is.EqualTo(face.Value.Kind == ClockKind.Red ? "빨강" : "일반"));
            Assert.That(Enumerable.Range(0, 5).Where(i => i != index).All(i => !match.GetClock(i).Face.HasValue && !controller.View.Clocks[i].IsOpen), Is.True);
        }
        private void AssertNumbers()
        {
            Assert.That(Read(controller.View.PlayerLife), Is.EqualTo(controller.Match.PlayerLifeMinutes));
            Assert.That(Read(controller.View.AiLife), Is.EqualTo(controller.Match.AiLifeMinutes));
            Assert.That(Read(controller.View.Bank), Is.EqualTo(controller.Match.BankMinutes));
            Assert.That(controller.View.PlayerLife.color, Is.EqualTo(playerColor));
            Assert.That(controller.View.AiLife.color, Is.EqualTo(aiColor));
        }
        private static int Read(TextMeshPro label)
        {
            var parts = label.text.Split(':');
            return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
        }
        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "진행 제한시간");
        }
    }
}
#endif
