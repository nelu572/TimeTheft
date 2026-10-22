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

            input = new InputTestFixture();
            input.Setup();
            var mouse = InputSystem.AddDevice<Mouse>("PresentationMouse");
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
            Time.timeScale = previousTimeScale;
            if (settings) UnityEngine.Object.Destroy(settings);
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator HoverSelectionAndLidAnimateOnlyArtworkWithoutExposingOtherValues()
        {
            var clock = view.Clocks[0];
            Vector3 rootPosition = clock.transform.position;
            Quaternion rootRotation = clock.transform.rotation;
            Vector3 focusPosition = clock.FocusTarget.FocusPoint.position;
            Quaternion focusRotation = clock.FocusTarget.FocusPoint.rotation;
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
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Inspecting));
            Assert.That(clock.Visual.position.y, Is.GreaterThan(artworkHeight + .02f), "클릭한 프레임에 선택 상승");
            AssertPose(clock.transform, rootPosition, rootRotation);
            AssertPose(clock.FocusTarget.FocusPoint, focusPosition, focusRotation);
            AssertPrivate(clock);
            int ai = controller.Match.AiClock, discarded = controller.Match.DiscardedClock;
            yield return WaitFor(() => clock.IsRevealed, "배분 이후 자기 시계 공개");
            Assert.That(clock.ValueLabel.text, Is.EqualTo(controller.Match.PlayerValue.ToString()));
            AssertPrivate(view.Clocks[ai]);
            AssertPrivate(view.Clocks[discarded]);
            Assert.That(view.Clocks[discarded].gameObject.activeSelf, Is.False);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "공개 연출 뒤 행동 선택");
            Assert.That(Quaternion.Angle(lidRotations[0], lids[0].localRotation), Is.GreaterThan(100), "공개 시 임시 뚜껑 열림");
            AssertPose(clock.transform, rootPosition, rootRotation);
            AssertPose(clock.FocusTarget.FocusPoint, focusPosition, focusRotation);
            AssertPrivate(view.Clocks[ai]);
            AssertPrivate(view.Clocks[discarded]);
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
        public IEnumerator DecisionObjectsEnterExitWithoutKeepingInvisibleClickAreasAfterRestart()
        {
            var clock = view.Clocks[0];
            var keep = DecisionObject(clock, "keepObject");
            var swap = DecisionObject(clock, "swapObject");
            Assert.That(keep, Is.Not.Null);
            Assert.That(swap, Is.Not.Null);
            AssertDecisionObjectsHidden(clock);
            controller.SelectClock(0);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "선택 물건 등장 시작");
            Assert.That(clock.Keep.InputEnabled && clock.Swap.InputEnabled, Is.True);
            Assert.That(keep.Artwork.gameObject.activeInHierarchy, Is.True);
            Assert.That(swap.Artwork.gameObject.activeInHierarchy, Is.True);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(keep.IsVisible && swap.IsVisible, Is.True);
            Vector3 keepShownPosition = keep.Artwork.localPosition;
            Vector3 swapShownPosition = swap.Artwork.localPosition;
            Vector3 keepShownScale = keep.Artwork.localScale;
            Vector3 swapShownScale = swap.Artwork.localScale;

            clock.Actions(false);
            Assert.That(clock.Keep.InputEnabled || clock.Swap.InputEnabled, Is.False, "퇴장 시작과 동시에 입력 차단");
            AssertDecisionColliders(clock, false);
            Assert.That(keep.IsVisible && swap.IsVisible, Is.True, "입력 차단 후 아트는 잠시 남아 퇴장한다");
            Assert.That(keep.Artwork.gameObject.activeInHierarchy, Is.True);
            Assert.That(swap.Artwork.gameObject.activeInHierarchy, Is.True);
            yield return WaitFor(() => !keep.IsVisible && !swap.IsVisible, "선택 물건 퇴장 완료");
            AssertDecisionObjectsHidden(clock);
            Assert.That(Vector3.Distance(keep.Artwork.localPosition, keepShownPosition), Is.GreaterThan(.01f));
            Assert.That(Vector3.Distance(swap.Artwork.localPosition, swapShownPosition), Is.GreaterThan(.01f));
            Assert.That(keep.Artwork.localScale.magnitude, Is.LessThan(keepShownScale.magnitude));
            Assert.That(swap.Artwork.localScale.magnitude, Is.LessThan(swapShownScale.magnitude));

            clock.Actions(true);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(keep.IsVisible && swap.IsVisible, Is.True);
            AssertDecisionColliders(clock, true);
            Assert.That(Vector3.Distance(keep.Artwork.localPosition, keepShownPosition), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(swap.Artwork.localPosition, swapShownPosition), Is.LessThan(.001f));
            clock.Actions(false);
            yield return null;
            controller.RestartGame();
            foreach (var watch in view.Clocks) AssertDecisionObjectsHidden(watch);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "퇴장 중 재시작 후 선택 복원");
            yield return new WaitForSecondsRealtime(.25f);
            foreach (var watch in view.Clocks) AssertDecisionObjectsHidden(watch);
            AssertResetPoseAndPrivacy();
        }

        private IEnumerator WaitFor(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, description);
        }

        private void AssertResetPoseAndPrivacy()
        {
            Assert.That(controller.Match.Round, Is.EqualTo(1));
            Assert.That(controller.Match.PlayerLife, Is.EqualTo(settings.Rules.InitialLife));
            Assert.That(controller.Match.AiLife, Is.EqualTo(settings.Rules.InitialLife));
            Assert.That(controller.Match.Pot, Is.Zero);
            Assert.That(view.IsResultVisible, Is.False);
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
            Assert.That(Vector3.Distance(current, start), Is.GreaterThan(.01f), "교환 중 시작 위치를 떠난다");
            Assert.That(Vector3.Distance(current, end), Is.GreaterThan(.01f), "교환 중 도착 위치로 순간 이동하지 않는다");
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
