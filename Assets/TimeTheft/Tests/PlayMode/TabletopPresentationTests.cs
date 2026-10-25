using TimeTheft.Legacy;
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
    [Category("Legacy")]
    public sealed class TabletopPresentationTests
    {
        private InputTestFixture input;
        private TabletopGameController controller;
        private TabletopGameView view;
        private PrototypeSettings settings;
        private Vector3[] homePositions, visualPositions, visualScales;
        private Quaternion[] homeRotations, visualRotations, lidRotations;
        private Transform[] lids;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadGame()
        {
            previousTimeScale = Time.timeScale;
            TabletopDecisionUITestInput.DisableSceneModules();
            input = new InputTestFixture();
            input.Setup();
            var mouse = InputSystem.AddDevice<Mouse>("PresentationMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TabletopGame.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TabletopGameController>();
            Assert.That(controller, Is.Not.Null);
            view = controller.View;
            Assert.That(view.Clocks, Has.Length.EqualTo(3));
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.RandomSeed = 635;
            settings.MoveDuration = .4f;
            settings.RevealDuration = .8f;
            settings.SettlementDuration = .8f;
            settings.RoundPause = .4f;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            TabletopResultTestTiming.SetAll(controller, .05f);
            serialized.Update();
            serialized.FindProperty("actionReadDuration").floatValue = .8f;
            serialized.FindProperty("exchangeDuration").floatValue = .6f;
            serialized.FindProperty("finalValueDuration").floatValue = .52f;
            serialized.FindProperty("timeTransferDuration").floatValue = .8f;
            serialized.FindProperty("resultHoldDuration").floatValue = .4f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("allocationDuration").floatValue = .4f;
            serializedView.FindProperty("aiSelectionPause").floatValue = .05f;
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            mouse.MakeCurrent();
            input.Set(mouse.position, Camera.main.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "최초 시계 선택 대기");
            homePositions = new Vector3[3];
            homeRotations = new Quaternion[3];
            visualPositions = new Vector3[3];
            visualRotations = new Quaternion[3];
            visualScales = new Vector3[3];
            lids = new Transform[3];
            lidRotations = new Quaternion[3];
            for (int i = 0; i < 3; i++)
            {
                var clock = view.Clocks[i];
                Assert.That(clock.Visual, Is.Not.Null);
                homePositions[i] = clock.transform.localPosition;
                homeRotations[i] = clock.transform.localRotation;
                visualPositions[i] = clock.Visual.localPosition;
                visualRotations[i] = clock.Visual.localRotation;
                visualScales[i] = clock.Visual.localScale;
                lids[i] = (Transform)new SerializedObject(clock).FindProperty("lidPivot").objectReferenceValue;
                Assert.That(lids[i], Is.Not.Null);
                lidRotations[i] = lids[i].localRotation;
            }
        }

        [UnityTearDown]
        public IEnumerator RestoreGame()
        {
            if (controller) controller.enabled = false;
            if (view && view.FocusCamera) view.FocusCamera.enabled = false;
            TabletopDecisionUITestInput.DisableSceneModules();
            Time.timeScale = previousTimeScale;
            if (settings) UnityEngine.Object.Destroy(settings);
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator HoverMovesArtworkAndAllocationMovesOnlyChosenClockBeforePrivateReveal()
        {
            var clock = view.Clocks[0];
            Vector3 rootPosition = clock.transform.position;
            Quaternion rootRotation = clock.transform.rotation;
            Vector3 focusPosition = clock.FocusTarget.FocusPoint.position;
            Quaternion focusRotation = clock.FocusTarget.FocusPoint.rotation;
            Vector3 localFocusPosition = clock.FocusTarget.FocusPoint.localPosition;
            Quaternion localFocusRotation = clock.FocusTarget.FocusPoint.localRotation;
            float artworkHeight = clock.Visual.position.y;
            AssertPrivate(clock);
            clock.FocusTarget.SetHighlighted(true);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(clock.Visual.position.y, Is.GreaterThan(artworkHeight + .01f), "호버 시 아트만 살짝 상승");
            AssertPose(clock.transform, rootPosition, rootRotation);
            AssertPose(clock.FocusTarget.FocusPoint, focusPosition, focusRotation);
            AssertPrivate(clock);
            Assert.That(Quaternion.Angle(lidRotations[0], lids[0].localRotation), Is.LessThan(.01f));
            clock.FocusTarget.SetHighlighted(false);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(Vector3.Distance(clock.Visual.localPosition, visualPositions[0]), Is.LessThan(.001f));

            controller.SelectClock(0);
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Allocating));
            Assert.That(clock.Visual.localPosition.z, Is.LessThan(visualPositions[0].z - .02f), "클릭한 프레임에 선택 상승");
            Assert.That(controller.Match.AiClock, Is.EqualTo(-1));
            Assert.That(controller.Match.DiscardedClock, Is.EqualTo(-1));
            Assert.That(view.FocusCamera.CurrentTarget, Is.Null, "가져오는 경로를 전체 구도에서 보여 준다");
            AssertPrivate(clock);
            controller.SelectClock(1);
            Assert.That(controller.Match.PlayerClock, Is.Zero, "1차 이동 중 중복 선택 차단");
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Allocating));
            Assert.That(controller.Match.AiClock, Is.EqualTo(-1));
            AssertMovingBetween(clock.transform.position, rootPosition, view.PlayerClockSlot.position);
            for (int i = 0; i < view.Clocks.Length; i++)
            {
                var watch = view.Clocks[i];
                AssertPrivate(watch);
                Assert.That(watch.FocusTarget.InputEnabled, Is.False, "배분 중 전체 선택 잠금");
                AssertDecisionObjectsHidden(watch);
                if (i == 0) continue;
                Assert.That(Vector3.Distance(watch.transform.localPosition, homePositions[i]), Is.LessThan(.001f), "미선택 시계는 아직 중앙에 남는다");
                Assert.That(watch.gameObject.activeSelf, Is.True, "마지막 시계도 배분 완료 전에는 퇴장하지 않는다");
            }
            yield return WaitFor(() => clock.IsRevealed, "배분 이후 자기 시계 공개");
            int ai = controller.Match.AiClock, discarded = controller.Match.DiscardedClock;
            Assert.That(clock.ValueLabel.text, Is.EqualTo(controller.Match.PlayerValue.ToString()));
            AssertPrivate(view.Clocks[ai]);
            AssertPrivate(view.Clocks[discarded]);
            Assert.That(view.Clocks[discarded].gameObject.activeSelf, Is.False);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "공개 연출 뒤 행동 선택");
            Assert.That(Quaternion.Angle(lidRotations[0], lids[0].localRotation), Is.GreaterThan(100), "공개 시 임시 뚜껑 열림");
            AssertAllocationEndpoints();
            Assert.That(Vector3.Distance(clock.FocusTarget.FocusPoint.localPosition, localFocusPosition), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(clock.FocusTarget.FocusPoint.localRotation, localFocusRotation), Is.LessThan(.01f));
            AssertPrivate(view.Clocks[ai]);
            AssertPrivate(view.Clocks[discarded]);
        }

        [UnityTest]
        public IEnumerator EveryTakePathClearsTheUnselectedClocks()
        {
            for (int selected = 0; selected < 3; selected++)
            {
                if (selected > 0)
                {
                    controller.RestartGame();
                    yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "이동 경로별 선택 준비");
                }
                controller.SelectClock(selected);
                float deadline = Time.realtimeSinceStartup + 3;
                bool sawMovement = false;
                while (controller.Match.Stage == RoundStage.Allocating && controller.Match.AiClock < 0 &&
                    Time.realtimeSinceStartup < deadline)
                {
                    Vector3 position = view.Clocks[selected].transform.position;
                    sawMovement |= Vector3.Distance(position, view.PlayerClockSlot.position) > .1f &&
                        Vector3.Distance(position, view.Clocks[selected].transform.parent.TransformPoint(homePositions[selected])) > .1f;
                    for (int other = 0; other < 3; other++)
                    {
                        if (other == selected) continue;
                        Vector3 separation = position - view.Clocks[other].transform.position;
                        separation.y = 0;
                        Assert.That(separation.magnitude, Is.GreaterThan(1.1f), "공용 시계의 원형 본체끼리 겹치지 않는다");
                        AssertPrivate(view.Clocks[other]);
                    }
                    yield return null;
                }
                Assert.That(sawMovement, Is.True);
                Assert.That(Vector3.Distance(view.Clocks[selected].transform.position, view.PlayerClockSlot.position), Is.LessThan(.001f));
            }
        }

        [UnityTest]
        public IEnumerator AiFirstAllocationLeavesTwoUnmovedChoicesForPlayerInNextRound()
        {
            controller.SelectClock(0);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "첫 라운드 배분");
            settings.MoveDuration = settings.RevealDuration = settings.SettlementDuration = settings.RoundPause = .1f;
            controller.ChooseAction(ClockAction.Keep);
            yield return WaitFor(() => controller.Match.Round == 2 && controller.Match.Stage == RoundStage.Selecting,
                "다음 라운드의 AI 선공 배분 완료");
            Assert.That(controller.Match.PlayerSelectsFirst, Is.False);
            Assert.That(controller.Match.PlayerClock, Is.EqualTo(-1));
            Assert.That(controller.Match.DiscardedClock, Is.EqualTo(-1));
            int ai = controller.Match.AiClock, choices = 0;
            Assert.That(ai, Is.InRange(0, 2));
            for (int i = 0; i < view.Clocks.Length; i++)
            {
                var clock = view.Clocks[i];
                AssertPrivate(clock);
                Assert.That(clock.FocusTarget.InputEnabled, Is.EqualTo(i != ai));
                AssertDecisionObjectsHidden(clock);
                if (i == ai) continue;
                choices++;
                Assert.That(Vector3.Distance(clock.transform.localPosition, homePositions[i]), Is.LessThan(.001f));
            }
            Assert.That(choices, Is.EqualTo(2));
            Assert.That(Vector3.Distance(view.Clocks[ai].transform.position, view.AiClockSlot.position), Is.LessThan(.001f));
            controller.SelectClock(ai);
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Selecting));
            Assert.That(controller.Match.PlayerClock, Is.EqualTo(-1));
            controller.SelectClock((ai + 1) % 3);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "남은 시계 중 플레이어 선택");
            Assert.That(controller.Match.AiClock, Is.EqualTo(ai));
            AssertAllocationEndpoints();
        }

        [UnityTest]
        public IEnumerator RestartCancelsEachAllocationAndSkipAppliesOwnedAndDiscardEndpoints()
        {
            var originalClocks = (TabletopClockView[])view.Clocks.Clone();
            for (int phase = 0; phase < 3; phase++)
            {
                controller.SelectClock(0);
                if (phase == 1)
                    yield return WaitFor(() => controller.Match.AiClock >= 0 && controller.Match.Stage == RoundStage.Allocating,
                        "AI 시계 이동 진입");
                else if (phase == 2)
                    yield return WaitFor(() => controller.Match.Stage == RoundStage.Inspecting, "남은 시계 퇴장 진입");
                yield return new WaitForSecondsRealtime(.1f);
                foreach (var clock in view.Clocks) AssertPrivate(clock);
                controller.RestartGame();
                AssertResetPoseAndPrivacy();
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "배분 중 재시작 후 선택 복원");
                AssertResetPoseAndPrivacy();
                CollectionAssert.AreEqual(originalClocks, view.Clocks);
            }
            controller.SelectClock(0);
            float deadline = Time.realtimeSinceStartup + 5;
            while (controller.Match.Stage != RoundStage.Deciding && Time.realtimeSinceStartup < deadline)
            {
                if (controller.Match.Stage == RoundStage.Allocating)
                    foreach (var clock in view.Clocks) AssertPrivate(clock);
                controller.SkipAnimation();
                yield return null;
            }
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Deciding));
            AssertAllocationEndpoints();
            AssertPrivate(view.Clocks[controller.Match.AiClock]);
            AssertPrivate(view.Clocks[controller.Match.DiscardedClock]);
            Assert.That(view.Clocks[controller.Match.DiscardedClock].gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator PhysicalExchangeAndTimeCountingFinishAtExactRuleValues()
        {
            controller.SelectClock(0);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "행동 선택 대기");
            var match = controller.Match;
            int oldPlayer = match.PlayerClock, oldAi = match.AiClock;
            Vector3 playerStart = view.Clocks[oldPlayer].transform.position;
            Vector3 aiStart = view.Clocks[oldAi].transform.position;
            Quaternion playerRotation = view.Clocks[oldPlayer].transform.rotation;
            Quaternion aiRotation = view.Clocks[oldAi].transform.rotation;
            int previousPlayer = match.PlayerLife, previousAi = match.AiLife, previousPot = match.Pot;
            // 시드 635의 첫 선택 0에서는 AI가 유지하므로 교환을 선택하면 실물 교환된다.
            controller.ChooseAction(ClockAction.Swap);
            Assert.That(match.DidExchange, Is.True);
            yield return WaitFor(() => match.Stage == RoundStage.Exchanging, "실물 교환 시작");
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(match.Stage, Is.EqualTo(RoundStage.Exchanging));
            AssertMovingBetween(view.Clocks[oldPlayer].transform.position, playerStart, aiStart);
            AssertMovingBetween(view.Clocks[oldAi].transform.position, aiStart, playerStart);
            AssertPrivate(view.Clocks[oldPlayer]);
            AssertPrivate(view.Clocks[oldAi]);

            yield return WaitFor(() => match.Stage == RoundStage.Settling, "정산 시작");
            Assert.That(match.PlayerClock, Is.EqualTo(oldAi));
            Assert.That(match.AiClock, Is.EqualTo(oldPlayer));
            Assert.That(Vector3.Distance(view.Clocks[oldPlayer].transform.position, aiStart), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(view.Clocks[oldAi].transform.position, playerStart), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(view.Clocks[oldPlayer].transform.rotation, aiRotation), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(view.Clocks[oldAi].transform.rotation, playerRotation), Is.LessThan(.01f));
            Assert.That(view.Clocks[match.PlayerClock].ValueLabel.text, Is.EqualTo(match.PlayerValue.ToString()));
            Assert.That(view.Clocks[match.AiClock].ValueLabel.text, Is.EqualTo(match.AiValue.ToString()));
            Assert.That(view.IsCountingTime, Is.True);
            bool sawIntermediateValue = false;
            int fromPlayer = previousPlayer, fromAi = previousAi, fromPot = previousPot;
            float deadline = Time.realtimeSinceStartup + 3;
            while (view.IsCountingTime && Time.realtimeSinceStartup < deadline)
            {
                int player = Number(view.PlayerLife.text), ai = Number(view.AiLife.text), pot = Number(view.Pot.text);
                Assert.That(player, Is.InRange(match.PlayerLife, previousPlayer));
                Assert.That(ai, Is.InRange(match.AiLife, previousAi));
                Assert.That(pot, Is.InRange(previousPot, match.Pot));
                Assert.That(player, Is.LessThanOrEqualTo(fromPlayer));
                Assert.That(ai, Is.LessThanOrEqualTo(fromAi));
                Assert.That(pot, Is.GreaterThanOrEqualTo(fromPot));
                Assert.That(player + ai + pot, Is.EqualTo(previousPlayer + previousAi + previousPot));
                sawIntermediateValue |= pot > previousPot && pot < match.Pot;
                fromPlayer = player;
                fromAi = ai;
                fromPot = pot;
                yield return null;
            }
            Assert.That(view.IsCountingTime, Is.False);
            Assert.That(sawIntermediateValue, Is.True, "계수기가 시작 값에서 끝 값으로 보간된다");
            AssertNumbers();
            Assert.That(view.IsResultVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator RestartCancelsExchangeAndCountingAndSkipAppliesExactEndpoints()
        {
            var originalClocks = (TabletopClockView[])view.Clocks.Clone();
            foreach (bool restartWhileCounting in new[] { false, true })
            {
                controller.SelectClock(0);
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "행동 선택 대기");
                controller.ChooseAction(ClockAction.Swap);
                Assert.That(controller.Match.DidExchange, Is.True);
                yield return WaitFor(() => restartWhileCounting ? view.IsCountingTime
                    : controller.Match.Stage == RoundStage.Exchanging, "재시작할 연출 진입");
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(restartWhileCounting ? view.IsCountingTime : controller.Match.Stage == RoundStage.Exchanging, Is.True);
                controller.RestartGame();
                Assert.That(view.IsCountingTime, Is.False);
                AssertResetPoseAndPrivacy();
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "재시작 후 선택 복원");
                yield return new WaitForSecondsRealtime(.25f);
                AssertResetPoseAndPrivacy();
                AssertNumbers();
                CollectionAssert.AreEqual(originalClocks, view.Clocks);
            }

            controller.SelectClock(0);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "스킵할 라운드 행동 선택");
            int oldPlayer = controller.Match.PlayerClock, oldAi = controller.Match.AiClock;
            Vector3 playerStart = view.Clocks[oldPlayer].transform.position;
            Vector3 aiStart = view.Clocks[oldAi].transform.position;
            controller.ChooseAction(ClockAction.Swap);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Exchanging, "스킵할 교환 진입");
            float deadline = Time.realtimeSinceStartup + 3;
            while (controller.Match.Stage != RoundStage.Settling && Time.realtimeSinceStartup < deadline)
            {
                controller.SkipAnimation();
                yield return null;
            }
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Settling));
            Assert.That(Vector3.Distance(view.Clocks[oldPlayer].transform.position, aiStart), Is.LessThan(.001f), "교환 스킵도 도착 위치 확정");
            Assert.That(Vector3.Distance(view.Clocks[oldAi].transform.position, playerStart), Is.LessThan(.001f));
            deadline = Time.realtimeSinceStartup + 3;
            while (view.IsCountingTime && Time.realtimeSinceStartup < deadline)
            {
                controller.SkipAnimation();
                yield return null;
            }
            Assert.That(view.IsCountingTime, Is.False);
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator DecisionUiIsGatedToDecisionAndRestartOrDisableRemovesAllActionInputs()
        {
            var ui = view.DecisionUI;
            Assert.That(ui, Is.Not.Null);
            AssertDecisionUi(false);
            controller.SelectClock(0);
            float deadline = Time.realtimeSinceStartup + 5;
            while (controller.Match.Stage != RoundStage.Deciding && Time.realtimeSinceStartup < deadline)
            {
                AssertDecisionUi(false);
                yield return null;
            }
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Deciding));
            AssertDecisionUi(true);
            ui.KeepButton.onClick.Invoke();
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Revealing));
            Assert.That(controller.Match.PlayerAction, Is.EqualTo(ClockAction.Keep));
            AssertDecisionUi(false);
            ui.SwapButton.onClick.Invoke();
            Assert.That(controller.Match.PlayerAction, Is.EqualTo(ClockAction.Keep), "숨긴 직후의 중복 제출 차단");
            controller.RestartGame();
            AssertDecisionUi(false);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "결정 제출 중 재시작 후 선택 복원");
            AssertResetPoseAndPrivacy();
            controller.SelectClock(0);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "비활성화할 결정 단계");
            AssertDecisionUi(true);
            var stoppedMatch = controller.Match;
            controller.enabled = false;
            AssertDecisionUi(false);
            ui.SwapButton.onClick.Invoke();
            Assert.That(controller.Match, Is.SameAs(stoppedMatch));
            Assert.That(stoppedMatch.Stage, Is.EqualTo(RoundStage.Deciding));
            controller.enabled = true;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "재활성화 후 새 게임");
            AssertDecisionUi(false);
            AssertResetPoseAndPrivacy();
        }

        private void AssertDecisionUi(bool visible)
        {
            Assert.That(view.DecisionUI.IsVisible, Is.EqualTo(visible));
            Assert.That(view.DecisionUI.KeepButton.interactable, Is.EqualTo(visible));
            Assert.That(view.DecisionUI.SwapButton.interactable, Is.EqualTo(visible));
            foreach (var clock in view.Clocks) AssertDecisionObjectsHidden(clock);
        }

        private IEnumerator WaitFor(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, description);
        }

        private void AssertAllocationEndpoints()
        {
            var match = controller.Match;
            Assert.That(Vector3.Distance(view.Clocks[match.PlayerClock].transform.position, view.PlayerClockSlot.position), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(view.Clocks[match.AiClock].transform.position, view.AiClockSlot.position), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(view.Clocks[match.DiscardedClock].transform.position, view.DiscardClockSlot.position), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(view.Clocks[match.PlayerClock].transform.rotation, view.PlayerClockSlot.rotation), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(view.Clocks[match.AiClock].transform.rotation, view.AiClockSlot.rotation), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(view.Clocks[match.DiscardedClock].transform.rotation, view.DiscardClockSlot.rotation), Is.LessThan(.01f));
            Assert.That(view.Clocks[match.DiscardedClock].gameObject.activeSelf, Is.False);
        }

        private void AssertResetPoseAndPrivacy()
        {
            Assert.That(controller.Match.Round, Is.EqualTo(1));
            Assert.That(controller.Match.PlayerLife, Is.EqualTo(settings.Rules.InitialLife));
            Assert.That(controller.Match.AiLife, Is.EqualTo(settings.Rules.InitialLife));
            Assert.That(controller.Match.Pot, Is.Zero);
            Assert.That(view.IsResultVisible, Is.False);
            AssertDecisionUi(false);
            for (int i = 0; i < 3; i++)
            {
                var clock = view.Clocks[i];
                Assert.That(clock.gameObject.activeSelf, Is.True);
                Assert.That(Vector3.Distance(clock.transform.localPosition, homePositions[i]), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(clock.transform.localRotation, homeRotations[i]), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(clock.Visual.localPosition, visualPositions[i]), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(clock.Visual.localRotation, visualRotations[i]), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(clock.Visual.localScale, visualScales[i]), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(lids[i].localRotation, lidRotations[i]), Is.LessThan(.01f));
                Assert.That(clock.Keep.InputEnabled, Is.False);
                Assert.That(clock.Swap.InputEnabled, Is.False);
                AssertDecisionObjectsHidden(clock);
                AssertPrivate(clock);
            }
        }

        private static TabletopDecisionObject DecisionObject(TabletopClockView clock, string property)
        {
            return (TabletopDecisionObject)new SerializedObject(clock).FindProperty(property).objectReferenceValue;
        }

        private static void AssertDecisionObjectsHidden(TabletopClockView clock)
        {
            foreach (string property in new[] { "keepObject", "swapObject" })
            {
                var decision = DecisionObject(clock, property);
                Assert.That(decision.IsVisible, Is.False, clock.name + " 숨긴 선택 물건");
                Assert.That(decision.Artwork.gameObject.activeSelf, Is.False);
            }
            AssertDecisionColliders(clock, false);
        }

        private static void AssertDecisionColliders(TabletopClockView clock, bool enabled)
        {
            foreach (var target in new[] { clock.Keep, clock.Swap })
            {
                var colliders = target.GetComponentsInChildren<Collider>(true);
                Assert.That(colliders, Is.Not.Empty, target.name);
                foreach (var collider in colliders) Assert.That(collider.enabled, Is.EqualTo(enabled), target.name);
            }
        }

        private static void AssertMovingBetween(Vector3 current, Vector3 start, Vector3 end)
        {
            Assert.That(Vector3.Distance(current, start), Is.GreaterThan(.01f), "이동 중 시작 위치를 떠난다");
            Assert.That(Vector3.Distance(current, end), Is.GreaterThan(.01f), "이동 중 도착 위치로 순간 이동하지 않는다");
        }

        private static void AssertPose(Transform transform, Vector3 position, Quaternion rotation)
        {
            Assert.That(Vector3.Distance(transform.position, position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(transform.rotation, rotation), Is.LessThan(.01f));
        }

        private static void AssertPrivate(TabletopClockView clock)
        {
            Assert.That(clock.ValueLabel.text, Is.EqualTo("?"));
            Assert.That(clock.IsRevealed, Is.False, clock.name + " 비공개 바늘");
        }

        private void AssertNumbers()
        {
            Assert.That(view.PlayerLife.text, Is.EqualTo(controller.Match.PlayerLife + "h"));
            Assert.That(view.AiLife.text, Is.EqualTo(controller.Match.AiLife + "h"));
            Assert.That(view.Pot.text, Is.EqualTo(controller.Match.Pot + "h"));
        }

        private static int Number(string text) => int.Parse(text.TrimEnd('h'));
    }
}
#endif
