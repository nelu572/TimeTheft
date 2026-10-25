using TimeTheft.Legacy;
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
using UnityEngine.UI;

namespace TimeTheft.Tests
{
    [Category("Legacy")]
    public sealed class TabletopGameFlowTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private Keyboard keyboard;
        private TabletopGameController controller;
        private PrototypeSettings settings;
        private Camera camera;
        private TabletopCameraLook look;
        private TabletopFocusCamera focus;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadGame()
        {
            previousTimeScale = Time.timeScale;
            TabletopDecisionUITestInput.DisableSceneModules();
            input = new InputTestFixture();
            input.Setup();
            mouse = InputSystem.AddDevice<Mouse>("TabletopGameMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("TabletopGameKeyboard");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TabletopGame.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TabletopGameController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.View, Is.Not.Null);
            Assert.That(UnityEngine.Object.FindObjectsByType<PrototypeController>(FindObjectsSortMode.None), Is.Empty);
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.MoveDuration = settings.RevealDuration = settings.SettlementDuration = settings.RoundPause = .05f;
            settings.RandomSeed = 635;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            TabletopResultTestTiming.SetAll(controller, .05f);
            var serializedView = new SerializedObject(controller.View);
            serializedView.FindProperty("allocationDuration").floatValue = .05f;
            serializedView.FindProperty("aiSelectionPause").floatValue = .05f;
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            look = camera.GetComponent<TabletopCameraLook>();
            focus = controller.View.FocusCamera;
            Time.timeScale = 0;
            PointAt(camera.pixelRect.center);
            yield return null;
            look.SendMessage("OnApplicationFocus", true);
            focus.SendMessage("OnApplicationFocus", true);
            controller.RestartGame();
            yield return WaitFor(RoundStage.Selecting);
        }

        [UnityTearDown]
        public IEnumerator RestoreGame()
        {
            if (controller) controller.enabled = false;
            if (focus) focus.enabled = false;
            TabletopDecisionUITestInput.DisableSceneModules();
            Time.timeScale = previousTimeScale;
            if (settings) UnityEngine.Object.Destroy(settings);
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator WorldClicksPreservePrivacyAndCompleteTwoSeededGames()
        {
            bool sawExchange = false, sawNoExchange = false;
            int[] seeds = { 635, 261022 };
            var originalClocks = (TabletopClockView[])controller.View.Clocks.Clone();
            for (int game = 0; game < seeds.Length; game++)
            {
                AssertInitialTable();
                int completedRounds = 0;
                while (controller.Match.Stage != RoundStage.GameOver)
                {
                    Assert.That(++completedRounds, Is.LessThanOrEqualTo(settings.Rules.InitialLife));
                    yield return WaitFor(RoundStage.Selecting);
                    AssertAllClocksHidden();
                    AssertInGamePresentation();
                    Assert.That(controller.Match.PlayerSelectsFirst, Is.EqualTo(controller.Match.Round % 2 == 1));
                    Assert.That(controller.Match.AiClock >= 0, Is.EqualTo(!controller.Match.PlayerSelectsFirst),
                        "AI 선공 라운드는 상대 배분이 끝난 뒤 잔여 시계를 선택한다");
                    Assert.That(controller.View.RoundLabel.text, Does.Contain(controller.Match.Round.ToString("00")));
                    int index = (controller.Match.Round + Math.Abs(seeds[game] % 3)) % 3;
                    if (!controller.Match.CanSelectClock(index)) index = (index + 1) % 3;
                    var selected = controller.View.Clocks[index];
                    yield return Click(selected.FocusTarget, () =>
                    {
                        Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Allocating));
                        Assert.That(controller.Match.PlayerClock, Is.EqualTo(index));
                        Assert.That(focus.CurrentTarget, Is.Null, "시계가 테이블에서 이동하는 동안 전체 구도 유지");
                        Assert.That(selected.ValueLabel.text, Is.EqualTo("?"));
                        Assert.That(selected.IsRevealed, Is.False, "배분 완료 전에는 바늘도 숨긴다");
                        Assert.That(controller.View.HelpVisible, Is.False, "시계를 선택하면 초회 도움말을 닫는다");
                        int ai = controller.Match.AiClock;
                        controller.SelectClock((index + 1) % 3);
                        Assert.That(controller.Match.PlayerClock, Is.EqualTo(index), "중복 선택 무시");
                        Assert.That(controller.Match.AiClock, Is.EqualTo(ai));
                    });
                    yield return WaitFor(RoundStage.Deciding);
                    var match = controller.Match;
                    int discarded = match.DiscardedClock;
                    Assert.That(selected.ValueLabel.text, Is.EqualTo(match.PlayerValue.ToString()));
                    Assert.That(selected.IsRevealed, Is.True);
                    AssertPrivateClock(controller.View.Clocks[match.AiClock]);
                    AssertPrivateClock(controller.View.Clocks[discarded]);
                    Assert.That(controller.View.Clocks[discarded].gameObject.activeSelf, Is.False);
                    Assert.That(controller.View.Clocks[match.AiClock].OwnerLabel.text,
                        Is.EqualTo("상대 선택 중"), "행동 확정 전 상대 행동 비공개");
                    Assert.That(selected.OwnerLabel.text, Is.EqualTo("내 시계"));
                    Assert.That(Vector3.Distance(selected.transform.position, controller.View.PlayerClockSlot.position), Is.LessThan(.001f));
                    Assert.That(Vector3.Distance(controller.View.Clocks[match.AiClock].transform.position,
                        controller.View.AiClockSlot.position), Is.LessThan(.001f));
                    AssertInGamePresentation();
                    Assert.That(controller.View.DecisionUI.IsVisible, Is.True);
                    Assert.That(selected.Keep.InputEnabled || selected.Swap.InputEnabled, Is.False);

                    int round = match.Round;
                    int oldPlayer = match.PlayerClock, oldAi = match.AiClock;
                    int previousPlayer = match.PlayerLife, previousAi = match.AiLife, previousPot = match.Pot;
                    ClockAction action = round % 2 == 0 ? ClockAction.Keep : ClockAction.Swap;
                    yield return Click(action == ClockAction.Keep ? controller.View.DecisionUI.KeepButton : controller.View.DecisionUI.SwapButton, () =>
                    {
                        Assert.That(match.Stage, Is.EqualTo(RoundStage.Revealing));
                        Assert.That(match.PlayerAction, Is.EqualTo(action));
                        Assert.That(controller.View.Clocks[oldPlayer].OwnerLabel.text,
                            Is.EqualTo("나 · " + TableView.ActionName(action)));
                        Assert.That(controller.View.Clocks[oldAi].OwnerLabel.text,
                            Is.EqualTo("상대 · " + TableView.ActionName(match.AiAction)));
                        AssertInGamePresentation();
                        controller.ChooseAction(action == ClockAction.Keep ? ClockAction.Swap : ClockAction.Keep);
                        Assert.That(match.PlayerAction, Is.EqualTo(action), "중복 행동 확정 무시");
                        Assert.That(selected.Keep.InputEnabled, Is.False);
                        Assert.That(selected.Swap.InputEnabled, Is.False);
                    });

                    bool sawExchanging = false, sawSettlement = false, sawCompletedCount = false;
                    int settledPlayer = -1, settledAi = -1, settledPot = -1;
                    float deadline = Time.realtimeSinceStartup + 5;
                    while (match.Round == round && match.Stage != RoundStage.GameOver && Time.realtimeSinceStartup < deadline)
                    {
                        AssertInGamePresentation();
                        AssertPrivateClock(controller.View.Clocks[discarded]);
                        Assert.That(match.PlayerLife + match.AiLife + match.Pot,
                            Is.EqualTo(settings.Rules.InitialLife * 2));
                        if (match.Stage == RoundStage.Exchanging && !sawExchanging)
                        {
                            sawExchanging = true;
                            if (match.DidExchange)
                            {
                                sawExchange = true;
                                AssertPrivateClock(controller.View.Clocks[match.PlayerClock]);
                                AssertPrivateClock(controller.View.Clocks[match.AiClock]);
                            }
                            else sawNoExchange = true;
                        }
                        if (match.Stage == RoundStage.Settling && !sawSettlement)
                        {
                            sawSettlement = true;
                            Assert.That(match.PlayerLoss, Is.EqualTo(Math.Min(previousPlayer, match.PlayerValue)));
                            Assert.That(match.AiLoss, Is.EqualTo(Math.Min(previousAi, match.AiValue)));
                            Assert.That(match.PlayerLife, Is.EqualTo(previousPlayer - match.PlayerLoss));
                            Assert.That(match.AiLife, Is.EqualTo(previousAi - match.AiLoss));
                            Assert.That(match.Pot, Is.EqualTo(previousPot + match.PlayerLoss + match.AiLoss));
                            AssertFinalClock(controller.View.Clocks[match.PlayerClock], match.PlayerValue);
                            AssertFinalClock(controller.View.Clocks[match.AiClock], match.AiValue);
                            Assert.That(controller.View.IsResultVisible, Is.False, "정산 확인 후 결과를 표시한다");
                            settledPlayer = match.PlayerLife;
                            settledAi = match.AiLife;
                            settledPot = match.Pot;
                        }
                        if (match.Stage == RoundStage.Settling)
                        {
                            AssertDisplayedSettlementRange(previousPlayer, previousAi, previousPot, match);
                            if (!controller.View.IsCountingTime)
                            {
                                AssertNumbers();
                                sawCompletedCount = true;
                            }
                        }
                        yield return null;
                    }
                    Assert.That(sawExchanging, Is.True, "교환 판단 단계 확인");
                    Assert.That(sawSettlement, Is.True, "수명 및 판돈 정산 단계 확인");
                    Assert.That(sawCompletedCount, Is.True, "표시 보간 완료 후 정확한 정산 수치 확인");
                    Assert.That(match.Round != round || match.Stage == RoundStage.GameOver, Is.True, "라운드 진행 시간 초과");
                    if (match.Stage == RoundStage.GameOver)
                    {
                        deadline = Time.realtimeSinceStartup + 3;
                        while (!controller.View.IsResultVisible && Time.realtimeSinceStartup < deadline)
                        {
                            AssertInGamePresentation();
                            yield return null;
                        }
                        Assert.That(controller.View.IsResultVisible, Is.True);
                        Assert.That(controller.View.IsCountingTime, Is.False, "배당 완료 후 결과를 표시한다");
                        Assert.That(controller.View.Status.gameObject.activeInHierarchy, Is.True);
                        Assert.That(controller.View.Detail.gameObject.activeInHierarchy, Is.True);
                        Assert.That(controller.View.Restart.InputEnabled, Is.True);
                        if (match.Winner == MatchWinner.Draw)
                        {
                            Assert.That(match.Award, Is.Zero);
                            Assert.That(match.Pot, Is.EqualTo(settledPot));
                        }
                        else
                        {
                            Assert.That(match.Award, Is.EqualTo(settledPot));
                            Assert.That(match.Pot, Is.Zero);
                            Assert.That(match.PlayerLife, Is.EqualTo(settledPlayer +
                                (match.Winner == MatchWinner.Player ? match.Award : 0)));
                            Assert.That(match.AiLife, Is.EqualTo(settledAi +
                                (match.Winner == MatchWinner.Ai ? match.Award : 0)));
                        }
                        AssertNumbers();
                        Assert.That(match.PlayerLife + match.AiLife + match.Pot,
                            Is.EqualTo(settings.Rules.InitialLife * 2));
                    }
                }

                settings.RandomSeed = seeds[(game + 1) % seeds.Length];
                var completedMatch = controller.Match;
                yield return Click(controller.View.Restart);
                yield return WaitFor(RoundStage.Selecting);
                Assert.That(controller.Match, Is.Not.SameAs(completedMatch));
                CollectionAssert.AreEqual(originalClocks, controller.View.Clocks, "시계 인스턴스 재사용");
                AssertInitialTable();
            }
            Assert.That(sawExchange, Is.True);
            Assert.That(sawNoExchange, Is.True);
        }

        [UnityTest]
        public IEnumerator HelpKeyTogglesInstructionsAndClockSelectionDismissesThem()
        {
            AssertInGamePresentation();
            bool before = controller.View.HelpVisible;
            yield return ToggleHelpKey();
            Assert.That(controller.View.HelpVisible, Is.EqualTo(!before));
            yield return ToggleHelpKey();
            Assert.That(controller.View.HelpVisible, Is.EqualTo(before));
            if (!controller.View.HelpVisible) yield return ToggleHelpKey();
            Assert.That(controller.View.HelpVisible, Is.True);
            var match = controller.Match;
            controller.View.Restart.Activate();
            Assert.That(controller.Match, Is.SameAs(match), "일반 플레이에서 종료 전 다시하기 입력 차단");
            Assert.That(match.Stage, Is.EqualTo(RoundStage.Selecting));
            yield return Click(controller.View.Clocks[0].FocusTarget, () =>
                Assert.That(controller.View.HelpVisible, Is.False, "시계 선택 시 도움말 닫기"));
            yield return WaitFor(RoundStage.Deciding);
            AssertInGamePresentation();
            yield return ToggleHelpKey();
            Assert.That(controller.View.HelpVisible, Is.True, "선택 후에도 H로 도움말 재확인");
            yield return ToggleHelpKey();
            Assert.That(controller.View.HelpVisible, Is.False);
            Assert.That(controller.Match, Is.SameAs(match));
            Assert.That(match.Stage, Is.EqualTo(RoundStage.Deciding));
        }

        [UnityTest]
        public IEnumerator SimultaneousEliminationKeepsUnawardedTimeOnTheWorldDevice()
        {
            settings.Rules.InitialLife = 1;
            controller.RestartGame();
            yield return WaitFor(RoundStage.Selecting);
            var selected = controller.View.Clocks[1];
            yield return Click(selected.FocusTarget);
            yield return WaitFor(RoundStage.Deciding);
            yield return Click(controller.View.DecisionUI.KeepButton);
            yield return WaitFor(RoundStage.GameOver);
            float deadline = Time.realtimeSinceStartup + 3;
            while (!controller.View.IsResultVisible && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.View.IsResultVisible, Is.True);
            Assert.That(controller.Match.Winner, Is.EqualTo(MatchWinner.Draw));
            Assert.That(controller.Match.Award, Is.Zero);
            Assert.That(controller.Match.Pot, Is.EqualTo(2));
            Assert.That(controller.View.Restart.InputEnabled, Is.True);
            Assert.That(controller.View.Status.text, Does.Contain("무승부"));
            Assert.That(controller.View.Detail.text, Does.Contain("획득자 없음"));
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator RestartKeyCancelsInspectionRevealAndSettlementWithoutStaleInput()
        {
            TabletopResultTestTiming.SetAll(controller, .25f);
            settings.MoveDuration = settings.RevealDuration = settings.SettlementDuration = .25f;
            var serializedView = new SerializedObject(controller.View);
            serializedView.FindProperty("allocationDuration").floatValue = .25f;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
            var clocks = (TabletopClockView[])controller.View.Clocks.Clone();
            foreach (RoundStage stage in new[] { RoundStage.Allocating, RoundStage.Inspecting, RoundStage.Revealing, RoundStage.Settling })
            {
                yield return WaitFor(RoundStage.Selecting);
                var selected = controller.View.Clocks[(int)stage % 3];
                yield return Click(selected.FocusTarget);
                if (stage == RoundStage.Inspecting) yield return WaitFor(stage);
                else if (stage != RoundStage.Allocating)
                {
                    yield return WaitFor(RoundStage.Deciding);
                    yield return Click(controller.View.DecisionUI.SwapButton);
                    if (stage == RoundStage.Settling) yield return WaitFor(stage);
                }
                Assert.That(controller.Match.Stage, Is.EqualTo(stage));
                var oldMatch = controller.Match;
                input.Press(keyboard.rKey);
                yield return null;
                input.Release(keyboard.rKey);
                Assert.That(controller.Match, Is.Not.SameAs(oldMatch), "R 키 새 게임");
                controller.ChooseAction(ClockAction.Swap);
                yield return WaitFor(RoundStage.Selecting);
                PointAt(camera.pixelRect.center);
                yield return WaitForCamera();
                yield return new WaitForSecondsRealtime(.3f);
                AssertInitialTable();
                Assert.That(focus.CurrentTarget, Is.Null);
                Assert.That(focus.HasCameraControl, Is.False);
                CollectionAssert.AreEqual(clocks, controller.View.Clocks);
                Assert.That(UnityEngine.Object.FindObjectsByType<TabletopClockView>(FindObjectsSortMode.None), Has.Length.EqualTo(3));
            }
            var stoppedMatch = controller.Match;
            controller.enabled = false;
            controller.View.Clocks[0].FocusTarget.Activate();
            controller.View.Restart.Activate();
            controller.ChooseAction(ClockAction.Keep);
            Assert.That(controller.Match, Is.SameAs(stoppedMatch), "비활성 컨트롤러는 클릭을 처리하지 않는다");
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Selecting));
            controller.enabled = true;
            controller.RestartGame();
            yield return WaitFor(RoundStage.Selecting);
            AssertInitialTable();
        }

        private IEnumerator Click(TabletopFocusTarget target, Action afterPress = null)
        {
            yield return WaitForCamera();
            Assert.That(target, Is.Not.Null);
            Assert.That(target.IsAvailable && target.InputEnabled, Is.True, target.name);
            var collider = target.GetComponentsInChildren<Collider>()
                .FirstOrDefault(c => c.enabled && !c.isTrigger && c.GetComponentInParent<TabletopFocusTarget>() == target);
            Assert.That(collider, Is.Not.Null, target.name + " 선택 Collider");
            // 기본 마우스 추적이 움직이는 중에도 실제 콜라이더 위치를 가리킨다.
            for (int i = 0; i < 12; i++)
            {
                Vector3 screen = camera.WorldToScreenPoint(collider.bounds.center);
                Assert.That(screen.z, Is.GreaterThan(camera.nearClipPlane), target.name);
                Assert.That(camera.pixelRect.Contains(screen), Is.True,
                    $"{target.name} 화면 내 선택 영역 / R{controller.Match.Round} {controller.Match.Stage} / " +
                    $"대상 {target.transform.position} Collider {collider.bounds.center} Screen {screen} Rect {camera.pixelRect} / " +
                    $"카메라 {camera.transform.position} {camera.transform.eulerAngles} Focus {focus.CurrentTarget}");
                PointAt(screen);
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(target), target.name + " 실제 호버 레이캐스트");
            input.Press(mouse.leftButton);
            yield return null;
            afterPress?.Invoke();
            input.Release(mouse.leftButton);
        }

        private IEnumerator Click(Button button, Action afterRelease = null)
        {
            yield return TabletopDecisionUITestInput.Click(input, mouse, button, () =>
            {
                Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Deciding), "UI 행동은 pointer down에서 확정하지 않는다");
                Assert.That(controller.View.DecisionUI.IsVisible, Is.True);
            }, afterRelease);
        }

        private IEnumerator WaitFor(RoundStage stage)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (controller.Match.Stage != stage && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.Match.Stage, Is.EqualTo(stage), controller.Match.DebugSnapshot());
        }

        private IEnumerator WaitForCamera()
        {
            float deadline = Time.realtimeSinceStartup + 3;
            while (focus.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(focus.IsTransitioning, Is.False);
        }

        private void AssertInitialTable()
        {
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Selecting));
            Assert.That(controller.Match.Round, Is.EqualTo(1));
            Assert.That(controller.Match.PlayerLife, Is.EqualTo(settings.Rules.InitialLife));
            Assert.That(controller.Match.AiLife, Is.EqualTo(settings.Rules.InitialLife));
            Assert.That(controller.Match.Pot, Is.Zero);
            Assert.That(controller.Match.Award, Is.Zero);
            Assert.That(controller.View.IsResultVisible, Is.False);
            AssertInGamePresentation();
            Assert.That(controller.View.RoundLabel.text, Does.Contain("01"));
            AssertAllClocksHidden();
            AssertNumbers();
        }

        private void AssertAllClocksHidden()
        {
            Assert.That(controller.View.Clocks, Has.Length.EqualTo(3));
            for (int i = 0; i < controller.View.Clocks.Length; i++)
            {
                var clock = controller.View.Clocks[i];
                Assert.That(clock.gameObject.activeSelf, Is.True);
                AssertPrivateClock(clock);
                Assert.That(clock.FocusTarget.InputEnabled, Is.EqualTo(controller.Match.CanSelectClock(i)));
                Assert.That(clock.FocusTarget.FocusOnClick, Is.False);
                Assert.That(clock.Keep.InputEnabled, Is.False);
                Assert.That(clock.Swap.InputEnabled, Is.False);
            }
        }

        private static void AssertPrivateClock(TabletopClockView clock)
        {
            Assert.That(clock.ValueLabel.text, Is.EqualTo("?"), clock.name);
            Assert.That(clock.IsRevealed, Is.False, clock.name + " 비공개 바늘");
        }

        private static void AssertFinalClock(TabletopClockView clock, int value)
        {
            Assert.That(clock.ValueLabel.text, Is.EqualTo(value.ToString()), clock.name);
            Assert.That(clock.IsRevealed, Is.True);
        }

        private void AssertNumbers()
        {
            Assert.That(controller.View.PlayerLife.text, Is.EqualTo(controller.Match.PlayerLife + "h"));
            Assert.That(controller.View.AiLife.text, Is.EqualTo(controller.Match.AiLife + "h"));
            Assert.That(controller.View.Pot.text, Is.EqualTo(controller.Match.Pot + "h"));
        }

        private void AssertDisplayedSettlementRange(int previousPlayer, int previousAi, int previousPot, LegacyMatch match)
        {
            int player = int.Parse(controller.View.PlayerLife.text.TrimEnd('h'));
            int ai = int.Parse(controller.View.AiLife.text.TrimEnd('h'));
            int pot = int.Parse(controller.View.Pot.text.TrimEnd('h'));
            Assert.That(player, Is.InRange(match.PlayerLife, previousPlayer));
            Assert.That(ai, Is.InRange(match.AiLife, previousAi));
            Assert.That(pot, Is.InRange(previousPot, match.Pot));
            Assert.That(player + ai + pot, Is.EqualTo(previousPlayer + previousAi + previousPot),
                "표시 보간 중에도 수명과 판돈 총량 보존");
        }

        private void AssertInGamePresentation()
        {
            Assert.That(controller.View.IsResultVisible, Is.False);
            Assert.That(controller.View.Status.gameObject.activeInHierarchy, Is.False, "일반 플레이에서 종료 결과 숨김");
            Assert.That(controller.View.Detail.gameObject.activeInHierarchy, Is.False, "일반 플레이에서 종료 상세 숨김");
            Assert.That(controller.View.Restart.InputEnabled, Is.False, "다시하기는 종료 후에만 입력 허용");
            bool deciding = controller.Match.Stage == RoundStage.Deciding;
            Assert.That(controller.View.DecisionUI.IsVisible, Is.EqualTo(deciding), "행동 UI는 결정 단계에서만 표시한다");
            Assert.That(controller.View.DecisionUI.KeepButton.interactable, Is.EqualTo(deciding));
            Assert.That(controller.View.DecisionUI.SwapButton.interactable, Is.EqualTo(deciding));
            foreach (var clock in controller.View.Clocks)
                Assert.That(clock.Keep.InputEnabled || clock.Swap.InputEnabled, Is.False, "기본 플레이에서 기존 월드 장치를 잠근다");
        }

        private IEnumerator ToggleHelpKey()
        {
            input.Press(keyboard.hKey);
            yield return null;
            input.Release(keyboard.hKey);
            yield return null;
        }

        private void PointAt(Vector2 position)
        {
            mouse.MakeCurrent();
            input.Set(mouse.position, position);
        }
    }
}
#endif
