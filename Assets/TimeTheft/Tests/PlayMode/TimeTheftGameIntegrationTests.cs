#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class TimeTheftGameIntegrationTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private TabletopGameController controller;
        private PrototypeSettings settings;
        private Camera camera;
        private TabletopCameraLook look;
        private TabletopFocusCamera focus;
        private Transform counter;
        private Color playerColor, aiColor;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadMainScene()
        {
            previousTimeScale = Time.timeScale;
            TabletopDecisionUITestInput.DisableSceneModules();
            input = new InputTestFixture();
            input.Setup();
            // MCP/Test Runner 창의 포커스에 따라 UI 모듈이 합성 클릭을 버리지 않도록
            // 격리된 Fixture 설정만 고정한다. TearDown이 실제 입력 설정을 복구한다.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("TimeTheftMainSceneMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TabletopGameController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(UnityEngine.Object.FindObjectsByType<TabletopGameController>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            counter = GameObject.Find("Devices/IntegratedTimeCounter").transform;
            foreach (var label in new[] { controller.View.PlayerLife, controller.View.AiLife, controller.View.Pot })
                Assert.That(label.transform.IsChildOf(counter), Is.True, "게임 값이 통합 계수기의 실제 표시부에 연결된다");
            Assert.That(GameObject.Find("Devices/PlayerTimeCounter"), Is.Null);
            Assert.That(GameObject.Find("Devices/OpponentTimeCounter"), Is.Null);
            Assert.That(GameObject.Find("Devices/CentralTimeBank"), Is.Null);
            playerColor = controller.View.PlayerLife.color;
            aiColor = controller.View.AiLife.color;
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.MoveDuration = settings.RevealDuration = settings.SettlementDuration = settings.RoundPause = .05f;
            settings.RandomSeed = 635;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            TabletopResultTestTiming.SetAll(controller, .12f);
            var view = new SerializedObject(controller.View);
            view.FindProperty("allocationDuration").floatValue = .05f;
            view.FindProperty("aiSelectionPause").floatValue = .05f;
            view.ApplyModifiedPropertiesWithoutUndo();
            camera = Camera.main;
            look = camera.GetComponent<TabletopCameraLook>();
            focus = controller.View.FocusCamera;
            Time.timeScale = 0;
            PointAt(camera.pixelRect.max + Vector2.one * 20);
            yield return null;
            SetWindowFocus();
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "최초 시계 선택");
            AssertNumbers();
        }

        [UnityTearDown]
        public IEnumerator RestoreRuntimeOnlyChanges()
        {
            if (controller) controller.enabled = false;
            if (focus) focus.enabled = false;
            TabletopDecisionUITestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = previousTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator IntegratedCounterTracksSettlementPayoutAndRestartThroughRealGameInputs()
        {
            var originalClocks = (TabletopClockView[])controller.View.Clocks.Clone();
            bool sawKeep = false, sawSwap = false, sawExchange = false, sawSettlement = false;
            int rounds = 0;
            while (controller.Match.Stage != RoundStage.GameOver)
            {
                Assert.That(++rounds, Is.LessThanOrEqualTo(settings.Rules.InitialLife));
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "다음 시계 선택");
                AssertNumbers();
                var match = controller.Match;
                int index = (match.Round + 2) % 3;
                if (!match.CanSelectClock(index)) index = (index + 1) % 3;
                yield return ClickWorld(controller.View.Clocks[index].FocusTarget);
                yield return WaitFor(() => match.Stage == RoundStage.Deciding, "유지/교환 선택");
                Assert.That(controller.View.Clocks[index].ValueLabel.text, Is.EqualTo(match.PlayerValue.ToString()));
                Assert.That(controller.View.Clocks[match.AiClock].IsRevealed, Is.False);
                Assert.That(controller.View.Clocks[match.DiscardedClock].gameObject.activeSelf, Is.False);
                var action = match.Round % 2 == 0 ? ClockAction.Keep : ClockAction.Swap;
                sawKeep |= action == ClockAction.Keep;
                sawSwap |= action == ClockAction.Swap;
                yield return TabletopDecisionUITestInput.Click(input, mouse,
                    action == ClockAction.Keep ? controller.View.DecisionUI.KeepButton : controller.View.DecisionUI.SwapButton,
                    () => Assert.That(match.Stage, Is.EqualTo(RoundStage.Deciding), "pointer down은 결정을 확정하지 않는다"),
                    () => Assert.That(match.Stage, Is.EqualTo(RoundStage.Revealing),
                        "pointer up 제출 / " + match.DebugSnapshot() + " pointer=" + mouse.position.ReadValue() +
                        " visible=" + controller.View.DecisionUI.IsVisible));
                Assert.That(match.PlayerAction, Is.EqualTo(action), "실제 화면 버튼 입력이 기존 Controller로 전달된다");
                int round = match.Round;
                Vector3 playerStart = controller.View.PlayerClockSlot.position;
                float deadline = Time.realtimeSinceStartup + 6;
                while (match.Round == round && match.Stage != RoundStage.GameOver && Time.realtimeSinceStartup < deadline)
                {
                    AssertDisplayedTotal();
                    if (match.Stage == RoundStage.Exchanging && match.DidExchange)
                    {
                        sawExchange = true;
                        Assert.That(controller.View.Clocks[match.PlayerClock].gameObject.activeSelf, Is.True);
                    }
                    if (match.Stage == RoundStage.Settling)
                    {
                        sawSettlement = true;
                        if (match.DidExchange)
                            Assert.That(Vector3.Distance(controller.View.Clocks[match.PlayerClock].transform.position, playerStart), Is.LessThan(.001f),
                                "교환된 실제 시계가 플레이어 목적지에 도착한다");
                        if (!controller.View.IsCountingTime) AssertNumbers();
                    }
                    yield return null;
                }
                Assert.That(match.Round != round || match.Stage == RoundStage.GameOver, Is.True, "라운드 진행 시간 초과");
            }
            yield return WaitFor(() => controller.View.IsResultVisible, "정산·판돈 지급 후 종료 결과");
            Assert.That(sawKeep && sawSwap && sawExchange && sawSettlement, Is.True);
            Assert.That(controller.Match.Winner, Is.Not.EqualTo(MatchWinner.Draw), "고정 시드로 판돈 지급 경로 확인");
            Assert.That(controller.Match.Pot, Is.Zero);
            Assert.That(controller.Match.Award, Is.GreaterThan(0));
            AssertNumbers();
            AssertDisplayedTotal();
            var completed = controller.Match;
            yield return ClickWorld(controller.View.Restart);
            yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "결과 전표에서 재시작");
            Assert.That(controller.Match, Is.Not.SameAs(completed));
            Assert.That(controller.Match.Round, Is.EqualTo(1));
            Assert.That(controller.Match.PlayerLife, Is.EqualTo(settings.Rules.InitialLife));
            Assert.That(controller.Match.AiLife, Is.EqualTo(settings.Rules.InitialLife));
            CollectionAssert.AreEqual(originalClocks, controller.View.Clocks);
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator AllThreeClockCollidersRemainSelectableBesideTheIntegratedCounter()
        {
            for (int index = 0; index < 3; index++)
            {
                controller.RestartGame();
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Selecting, "초기 선택 복구");
                yield return ClickWorld(controller.View.Clocks[index].FocusTarget);
                Assert.That(controller.Match.PlayerClock, Is.EqualTo(index));
                yield return WaitFor(() => controller.Match.Stage == RoundStage.Deciding, "선택한 시계 배분 완료");
                Assert.That(Vector3.Distance(controller.View.Clocks[index].transform.position,
                    controller.View.PlayerClockSlot.position), Is.LessThan(.001f));
                Assert.That(controller.View.DecisionUI.IsVisible, Is.True);
                Assert.That(controller.View.Clocks[controller.Match.AiClock].IsRevealed, Is.False);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator ParallaxKeepsClockRaysClearAndCounterInspectionReturnsToOverview()
        {
            Vector3 home = camera.transform.position;
            foreach (var corner in new[] { new Vector2(.001f, .001f), new Vector2(.001f, .999f),
                new Vector2(.999f, .001f), new Vector2(.999f, .999f) })
            {
                float elapsed = 0;
                while (elapsed < .7f)
                {
                    SetWindowFocus();
                    PointAt(camera.pixelRect.min + Vector2.Scale(camera.pixelRect.size, corner));
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
                Assert.That(Vector3.Distance(camera.transform.position, home), Is.GreaterThan(.1f), "실제 마우스 자유 시차 이동");
                foreach (var clock in controller.View.Clocks) AssertFirstHit(clock.FocusTarget);
                foreach (var label in counter.GetComponentsInChildren<TextMeshPro>().Concat(new[] { controller.View.RoundLabel }))
                {
                    label.ForceMeshUpdate();
                    foreach (var glyph in label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(g => g.isVisible))
                    foreach (var vertex in new[] { glyph.bottomLeft, glyph.topLeft, glyph.topRight, glyph.bottomRight })
                    {
                        Vector3 point = camera.WorldToViewportPoint(label.transform.TransformPoint(vertex));
                        Assert.That(point.z, Is.GreaterThan(camera.nearClipPlane));
                        Assert.That(point.x, Is.InRange(0f, 1f), label.name + " 최대 시차 글자 가로 잘림");
                        Assert.That(point.y, Is.InRange(0f, 1f), label.name + " 최대 시차 글자 세로 잘림");
                    }
                }
            }
            var inspection = counter.GetComponent<TabletopFocusTarget>();
            Assert.That(inspection, Is.Not.Null);
            yield return ClickWorld(inspection);
            yield return WaitFor(() => !focus.IsTransitioning, "통합 계수기 집중 완료");
            Assert.That(focus.CurrentTarget, Is.SameAs(inspection));
            foreach (var label in new[] { controller.View.PlayerLife, controller.View.AiLife, controller.View.Pot })
            {
                var point = camera.WorldToViewportPoint(label.transform.position);
                Assert.That(point.z, Is.GreaterThan(camera.nearClipPlane));
                Assert.That(point.x, Is.InRange(0f, 1f), label.name);
                Assert.That(point.y, Is.InRange(0f, 1f), label.name);
            }
            PointAt(camera.pixelRect.max + Vector2.one * 20);
            focus.ReturnToOverview();
            yield return WaitFor(() => !focus.HasCameraControl, "기본 구도 복귀");
            Assert.That(focus.CurrentTarget, Is.Null);
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Selecting));
            AssertNumbers();
        }

        private IEnumerator ClickWorld(TabletopFocusTarget target)
        {
            yield return WaitFor(() => !focus.IsTransitioning, "카메라 이동 종료");
            var collider = target.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger);
            for (int frame = 0; frame < 12; frame++)
            {
                SetWindowFocus();
                Vector3 pointer = camera.WorldToScreenPoint(collider.bounds.center);
                Assert.That(pointer.z, Is.GreaterThan(camera.nearClipPlane));
                Assert.That(camera.pixelRect.Contains(pointer), Is.True, target.name + " 클릭 영역");
                PointAt(pointer);
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(target), target.name + " 실제 호버");
            input.Press(mouse.leftButton);
            yield return null;
            input.Release(mouse.leftButton);
        }

        private void AssertFirstHit(TabletopFocusTarget target)
        {
            var collider = target.GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            Vector3 pointer = camera.WorldToScreenPoint(collider.bounds.center);
            Assert.That(camera.pixelRect.Contains(pointer), Is.True, target.name + " 시차 화면 영역");
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(camera.ScreenPointToRay(pointer), out RaycastHit hit, 30,
                ~0, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(hit.collider.GetComponentInParent<TabletopFocusTarget>(), Is.SameAs(target), "테이블·계수기가 시계 레이를 막지 않는다");
        }

        private void AssertNumbers()
        {
            Assert.That(controller.View.PlayerLife.text, Is.EqualTo(controller.Match.PlayerLife.ToString("00") + ":00"));
            Assert.That(controller.View.AiLife.text, Is.EqualTo(controller.Match.AiLife.ToString("00") + ":00"));
            Assert.That(controller.View.Pot.text, Is.EqualTo(controller.Match.Pot.ToString("00") + ":00"));
            AssertColors();
        }

        private void AssertDisplayedTotal()
        {
            int total = 0;
            foreach (TextMeshPro label in new[] { controller.View.PlayerLife, controller.View.AiLife, controller.View.Pot })
            {
                Assert.That(label.text, Does.Match(@"^\d{2,}:00$"), "시계형 표시만 사용하고 h 글리프를 요구하지 않는다");
                total += int.Parse(label.text.Split(':')[0]);
            }
            Assert.That(total, Is.EqualTo(settings.Rules.InitialLife * 2), "표시 보간 중에도 세 계수기 총량 보존");
            AssertColors();
        }

        private void AssertColors()
        {
            Assert.That(controller.View.PlayerLife.color, Is.EqualTo(playerColor), "통합 표시부의 Player 색 보존");
            Assert.That(controller.View.AiLife.color, Is.EqualTo(aiColor), "통합 표시부의 Opponent 색 보존");
        }

        private IEnumerator WaitFor(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, description + " / " + controller.Match.DebugSnapshot());
        }

        private void SetWindowFocus()
        {
            look.SendMessage("OnApplicationFocus", true);
            focus.SendMessage("OnApplicationFocus", true);
        }

        private void PointAt(Vector2 position)
        {
            mouse.MakeCurrent();
            input.Set(mouse.position, position);
        }
    }
}
#endif
