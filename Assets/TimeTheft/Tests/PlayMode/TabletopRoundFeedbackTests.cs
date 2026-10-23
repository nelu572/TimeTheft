#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class TabletopRoundFeedbackTests
    {
        private InputTestFixture input;
        private TabletopGameController controller;
        private TabletopGameView view;
        private TabletopRoundFeedback feedback;
        private PrototypeSettings settings;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadGame()
        {
            previousTimeScale = Time.timeScale;
            TabletopDecisionUITestInput.DisableSceneModules();
            input = new InputTestFixture();
            input.Setup();
            var mouse = InputSystem.AddDevice<Mouse>("RoundFeedbackMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TabletopGame.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TabletopGameController>();
            Assert.That(controller, Is.Not.Null);
            view = controller.View;
            feedback = view.RoundFeedback;
            Assert.That(feedback, Is.Not.Null);
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.MoveDuration = settings.RevealDuration = settings.SettlementDuration = settings.RoundPause = .05f;
            settings.RandomSeed = 635;
            settings.Rules.AiSwapAtMinimum = settings.Rules.AiSwapAtMaximum = 0;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            TabletopResultTestTiming.SetAll(controller, .2f);
            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("allocationDuration").floatValue = .05f;
            serializedView.FindProperty("aiSelectionPause").floatValue = .05f;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
            mouse.MakeCurrent();
            input.Set(mouse.position, Camera.main.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "최초 선택");
            AssertCleared();
        }

        [UnityTearDown]
        public IEnumerator RestoreGame()
        {
            if (controller) controller.enabled = false;
            if (view && view.FocusCamera) view.FocusCamera.enabled = false;
            TabletopDecisionUITestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = previousTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator ActionsVerdictFinalValuesAndSettlementAppearInOrderWithoutLeakingHiddenClocks()
        {
            controller.SelectClock(0);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "행동 선택");
            var match = controller.Match;
            int oldPlayer = match.PlayerClock, oldAi = match.AiClock, discarded = match.DiscardedClock;
            AssertCleared(true);
            controller.ChooseAction(ClockAction.Swap);
            Assert.That(feedback.Phase, Is.EqualTo(RoundPresentationPhase.Actions));
            Assert.That(feedback.PlayerAction.text, Does.Contain("교환"));
            Assert.That(feedback.AiAction.text, Does.Contain("유지"));
            Assert.That(feedback.PlayerAction.gameObject.activeInHierarchy, Is.True);
            Assert.That(feedback.AiAction.gameObject.activeInHierarchy, Is.True);
            Assert.That(feedback.Verdict.gameObject.activeInHierarchy, Is.False);
            AssertDeltasHidden();
            AssertPrivate(oldAi);
            AssertPrivate(discarded);
            Assert.That(match.PlayerLife, Is.EqualTo(30));
            Assert.That(view.DecisionUI.IsVisible, Is.False);
            Vector3 playerPlate = feedback.PlayerAction.transform.position;
            Vector3 aiPlate = feedback.AiAction.transform.position;

            yield return WaitPhase(RoundPresentationPhase.Exchange);
            Assert.That(feedback.Verdict.text, Does.Contain("시계 교환"));
            Assert.That(match.Stage, Is.EqualTo(RoundStage.Revealing), "판정 문구를 먼저 읽은 뒤 물리 교환한다");
            Assert.That(match.PlayerClock, Is.EqualTo(oldPlayer));
            Assert.That(match.AiClock, Is.EqualTo(oldAi));
            AssertPrivate(oldAi);
            AssertDeltasHidden();
            yield return WaitFor(() => match.Stage == RoundStage.Exchanging, "실물 교환 시작");
            Assert.That(match.PlayerClock, Is.EqualTo(oldAi));
            Assert.That(match.AiClock, Is.EqualTo(oldPlayer));
            AssertPrivate(oldPlayer);
            AssertPrivate(oldAi);
            AssertPrivate(discarded);

            yield return WaitPhase(RoundPresentationPhase.Values);
            Assert.That(match.Stage, Is.EqualTo(RoundStage.Exchanging));
            Assert.That(view.Clocks[match.PlayerClock].ValueLabel.text, Is.EqualTo(match.PlayerValue.ToString()));
            Assert.That(view.Clocks[match.AiClock].ValueLabel.text, Is.EqualTo(match.AiValue.ToString()));
            Assert.That(match.PlayerLife, Is.EqualTo(30));
            Assert.That(match.AiLife, Is.EqualTo(30));
            Assert.That(match.Pot, Is.Zero, "최종 숫자 관찰 중에는 아직 정산하지 않는다");
            AssertDeltasHidden();
            Assert.That(Vector3.Distance(feedback.PlayerAction.transform.position, playerPlate), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(feedback.AiAction.transform.position, aiPlate), Is.LessThan(.0001f));
            Assert.That(feedback.PlayerAction.text, Does.Contain("교환"), "행동 표식은 시계와 함께 주인이 바뀌지 않는다");
            Assert.That(feedback.AiAction.text, Does.Contain("유지"));

            yield return WaitPhase(RoundPresentationPhase.Settlement);
            AssertDeltas(match.PlayerLoss, match.AiLoss);
            bool sawTransfers = false;
            float deadline = Time.realtimeSinceStartup + 3;
            while (feedback.Phase == RoundPresentationPhase.Settlement && Time.realtimeSinceStartup < deadline)
            {
                AssertDisplayedTotal(60);
                AssertPrivate(discarded);
                sawTransfers |= feedback.PlayerTransfer.gameObject.activeInHierarchy && feedback.AiTransfer.gameObject.activeInHierarchy;
                yield return null;
            }
            Assert.That(sawTransfers, Is.True, "두 실제 손실을 계수기에서 Time Bank로 전달한다");
            Assert.That(feedback.Phase, Is.EqualTo(RoundPresentationPhase.ResultHold));
            Assert.That(view.IsCountingTime, Is.False);
            AssertNumbers();
            AssertTransfersHidden();
            Assert.That(match.Stage, Is.EqualTo(RoundStage.Settling));
            Assert.That(feedback.PlayerAction.gameObject.activeInHierarchy, Is.True);
            Assert.That(feedback.Verdict.gameObject.activeInHierarchy, Is.True);
            Assert.That(view.Clocks[match.PlayerClock].IsRevealed && view.Clocks[match.AiClock].IsRevealed, Is.True);
            AssertDeltas(match.PlayerLoss, match.AiLoss);
            yield return WaitPhase(RoundPresentationPhase.Preparing);
            Assert.That(feedback.Verdict.text, Does.Contain("다음 라운드"));
            AssertDeltasHidden();
            AssertTransfersHidden();
            yield return WaitFor(() => match.Round == 2 && match.Stage == RoundStage.Selecting, "다음 라운드 선택");
            AssertCleared();
        }

        [UnityTest]
        public IEnumerator CappedLossIsShownInsteadOfClockValueAndDrawNeverStartsAnAward()
        {
            settings.Rules.InitialLife = 3;
            settings.Rules.MinimumValue = settings.Rules.MaximumValue = 9;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "차감 상한 게임");
            controller.SelectClock(0);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "고정 9h 시계 선택");
            var match = controller.Match;
            Vector3 playerPosition = view.Clocks[match.PlayerClock].transform.position;
            Vector3 aiPosition = view.Clocks[match.AiClock].transform.position;
            controller.ChooseAction(ClockAction.Keep);
            yield return WaitPhase(RoundPresentationPhase.Exchange);
            Assert.That(feedback.Verdict.text, Does.Contain("교환 없음"));
            yield return WaitPhase(RoundPresentationPhase.Values);
            Assert.That(match.PlayerValue, Is.EqualTo(9));
            Assert.That(match.AiValue, Is.EqualTo(9));
            Assert.That(match.PlayerLife, Is.EqualTo(3));
            Assert.That(Vector3.Distance(view.Clocks[match.PlayerClock].transform.position, playerPosition), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(view.Clocks[match.AiClock].transform.position, aiPosition), Is.LessThan(.0001f));
            yield return WaitPhase(RoundPresentationPhase.Settlement);
            Assert.That(match.PlayerLoss, Is.EqualTo(3));
            Assert.That(match.AiLoss, Is.EqualTo(3));
            AssertDeltas(3, 3);
            yield return WaitPhase(RoundPresentationPhase.ResultHold);
            Assert.That(match.Winner, Is.EqualTo(MatchWinner.Draw));
            Assert.That(match.PlayerLife, Is.Zero);
            Assert.That(match.AiLife, Is.Zero);
            Assert.That(match.Pot, Is.EqualTo(6));
            Assert.That(view.IsResultVisible, Is.False, "정산 결과를 유지한 뒤 종료 전표를 연다");
            AssertDeltas(3, 3);
            AssertNumbers();
            float deadline = Time.realtimeSinceStartup + 3;
            while (!view.IsResultVisible && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(feedback.Phase, Is.Not.EqualTo(RoundPresentationPhase.Award));
                AssertTransfersHidden();
                yield return null;
            }
            Assert.That(view.IsResultVisible, Is.True);
            Assert.That(match.Award, Is.Zero);
            Assert.That(match.Pot, Is.EqualTo(6));
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator RestartClearsEveryResultPhaseAndSkipStillFinishesAtExactAwardValues()
        {
            var clocks = (TabletopClockView[])view.Clocks.Clone();
            var homePositions = new Vector3[3];
            var homeRotations = new Quaternion[3];
            for (int i = 0; i < clocks.Length; i++)
            {
                homePositions[i] = clocks[i].transform.localPosition;
                homeRotations[i] = clocks[i].transform.localRotation;
            }
            foreach (var phase in new[] { RoundPresentationPhase.Actions, RoundPresentationPhase.Exchange,
                RoundPresentationPhase.Values, RoundPresentationPhase.Settlement, RoundPresentationPhase.ResultHold,
                RoundPresentationPhase.Preparing, RoundPresentationPhase.Award })
            {
                ConfigureRound(phase == RoundPresentationPhase.Award);
                controller.RestartGame();
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "검사할 결과 라운드");
                controller.SelectClock(0);
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "결과 재시작 전 결정");
                controller.ChooseAction(phase == RoundPresentationPhase.Award ? ClockAction.Keep : ClockAction.Swap);
                yield return WaitPhase(phase);
                var previousMatch = controller.Match;
                controller.RestartGame();
                Assert.That(controller.Match, Is.Not.SameAs(previousMatch));
                AssertCleared();
                Assert.That(view.IsCountingTime, Is.False);
                for (int i = 0; i < clocks.Length; i++)
                {
                    AssertPrivate(i);
                    Assert.That(Vector3.Distance(clocks[i].transform.localPosition, homePositions[i]), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(clocks[i].transform.localRotation, homeRotations[i]), Is.LessThan(.01f));
                }
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "재시작 후 선택 복원");
                AssertCleared();
                AssertNumbers();
                CollectionAssert.AreEqual(clocks, view.Clocks);
            }

            ConfigureRound(true);
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "스킵 배당 라운드");
            controller.SelectClock(0);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "스킵할 결정");
            controller.ChooseAction(ClockAction.Keep);
            bool sawAward = false;
            float deadline = Time.realtimeSinceStartup + 5;
            while (!view.IsResultVisible && Time.realtimeSinceStartup < deadline)
            {
                AssertDisplayedTotal(10);
                sawAward |= feedback.Phase == RoundPresentationPhase.Award;
                controller.SkipAnimation();
                yield return null;
            }
            Assert.That(view.IsResultVisible, Is.True);
            Assert.That(sawAward, Is.True);
            Assert.That(controller.Match.Winner, Is.EqualTo(MatchWinner.Player));
            Assert.That(controller.Match.Award, Is.EqualTo(8));
            Assert.That(controller.Match.PlayerLife, Is.EqualTo(10));
            Assert.That(controller.Match.AiLife, Is.Zero);
            Assert.That(controller.Match.Pot, Is.Zero);
            Assert.That(view.IsCountingTime, Is.False);
            Assert.That(feedback.PlayerDelta.text, Is.EqualTo("+8h"));
            Assert.That(feedback.AiDelta.text, Is.EqualTo("0h"));
            Assert.That(feedback.PotDelta.text, Is.EqualTo("−8h"));
            AssertNumbers();
            AssertTransfersHidden();
        }

        private void ConfigureRound(bool award)
        {
            settings.Rules.InitialLife = award ? 5 : 30;
            settings.Rules.MinimumValue = 1;
            settings.Rules.MaximumValue = 12;
            settings.RandomSeed = award ? 1 : 635;
            TabletopResultTestTiming.SetAll(controller, .25f);
        }

        private IEnumerator WaitPhase(RoundPresentationPhase phase) => WaitFor(() => feedback.Phase == phase, phase.ToString());

        private IEnumerator WaitFor(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, description);
        }

        private void AssertCleared(bool decisionVisible = false)
        {
            Assert.That(feedback.Phase, Is.EqualTo(RoundPresentationPhase.None));
            Assert.That(feedback.IsVisible, Is.False);
            Assert.That(feedback.PlayerAction.gameObject.activeInHierarchy || feedback.AiAction.gameObject.activeInHierarchy, Is.False);
            Assert.That(feedback.Verdict.gameObject.activeInHierarchy, Is.False);
            AssertDeltasHidden();
            AssertTransfersHidden();
            Assert.That(view.DecisionUI.IsVisible, Is.EqualTo(decisionVisible));
        }

        private void AssertDeltasHidden()
        {
            foreach (var label in new[] { feedback.PlayerDelta, feedback.AiDelta, feedback.PotDelta })
                Assert.That(label.gameObject.activeInHierarchy, Is.False);
        }

        private void AssertDeltas(int player, int ai)
        {
            Assert.That(feedback.PlayerDelta.text, Is.EqualTo("−" + player + "h"));
            Assert.That(feedback.AiDelta.text, Is.EqualTo("−" + ai + "h"));
            Assert.That(feedback.PotDelta.text, Is.EqualTo("+" + (player + ai) + "h"));
            foreach (var label in new[] { feedback.PlayerDelta, feedback.AiDelta, feedback.PotDelta })
                Assert.That(label.gameObject.activeInHierarchy, Is.True);
        }

        private void AssertTransfersHidden()
        {
            Assert.That(feedback.PlayerTransfer.gameObject.activeInHierarchy || feedback.AiTransfer.gameObject.activeInHierarchy, Is.False);
        }

        private void AssertPrivate(int index)
        {
            Assert.That(view.Clocks[index].ValueLabel.text, Is.EqualTo("?"));
            Assert.That(view.Clocks[index].IsRevealed, Is.False);
        }

        private void AssertDisplayedTotal(int total)
        {
            Assert.That(Number(view.PlayerLife.text) + Number(view.AiLife.text) + Number(view.Pot.text), Is.EqualTo(total));
        }

        private void AssertNumbers()
        {
            Assert.That(view.PlayerLife.text, Is.EqualTo(controller.Match.PlayerLife + "h"));
            Assert.That(view.AiLife.text, Is.EqualTo(controller.Match.AiLife + "h"));
            Assert.That(view.Pot.text, Is.EqualTo(controller.Match.Pot + "h"));
        }

        private static int Number(string text) => int.Parse(text.TrimEnd('h'));
    }

    internal static class TabletopResultTestTiming
    {
        public static void SetAll(TabletopGameController controller, float duration)
        {
            var serialized = new SerializedObject(controller);
            foreach (string field in new[] { "actionReadDuration", "exchangeVerdictDuration", "exchangeDuration",
                "finalValueDuration", "timeTransferDuration", "resultHoldDuration", "roundCloseDuration" })
                serialized.FindProperty(field).floatValue = duration;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
