using TimeTheft.Legacy;
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TimeTheft.Tests
{
    [Category("Legacy")]
    public sealed class TabletopDecisionUITests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private TabletopGameController controller;
        private PrototypeSettings settings;
        private Camera camera;
        private TabletopFocusCamera focus;
        private TabletopCameraLook look;
        private GameObject behindUi;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadGame()
        {
            previousTimeScale = Time.timeScale;
            TabletopDecisionUITestInput.DisableSceneModules();
            input = new InputTestFixture();
            input.Setup();
            mouse = InputSystem.AddDevice<Mouse>("DecisionUiMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TabletopGame.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TabletopGameController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.View.DecisionUI, Is.Not.Null);
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.MoveDuration = settings.SettlementDuration = settings.RoundPause = .05f;
            settings.RevealDuration = .3f;
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
            focus = controller.View.FocusCamera;
            look = camera.GetComponent<TabletopCameraLook>();
            Time.timeScale = 0;
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            yield return null;
            focus.SendMessage("OnApplicationFocus", true);
            look.SendMessage("OnApplicationFocus", true);
            controller.RestartGame();
            yield return WaitFor(RoundStage.Selecting);
        }

        [UnityTearDown]
        public IEnumerator RestoreGame()
        {
            if (controller) controller.enabled = false;
            if (focus) focus.enabled = false;
            TabletopDecisionUITestInput.DisableSceneModules();
            if (behindUi) UnityEngine.Object.Destroy(behindUi);
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = previousTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator OverlayBlocksWorldHoverAndSubmissionCannotClickThroughAfterItHides()
        {
            int worldClicks = 0;
            var target = CreateBehindUiTarget();
            target.Clicked += () => worldClicks++;
            foreach (bool groupedEdges in new[] { false, true })
            {
                controller.RestartGame();
                yield return WaitFor(RoundStage.Selecting);
                controller.SelectClock(0);
                yield return WaitFor(RoundStage.Deciding);
                var ui = controller.View.DecisionUI;
                yield return TabletopDecisionUITestInput.Ready(ui.KeepButton);
                // 두 번째 case도 이전 버튼 위치에 이미 정착한 카메라를 재사용하지 않는다.
                // 반대편 입력에 실제 프레임 동안 적응한 뒤 버튼으로 옮겨 새 시차 목표를 만든다.
                Vector2 away = new Vector2(camera.pixelRect.center.x, camera.pixelRect.yMax - 20);
                float awayElapsed = 0;
                float awayDeadline = Time.realtimeSinceStartup + 3;
                while (awayElapsed < .2f && Time.realtimeSinceStartup < awayDeadline)
                {
                    focus.SendMessage("OnApplicationFocus", true);
                    look.SendMessage("OnApplicationFocus", true);
                    input.Set(mouse.position, away);
                    TabletopDecisionUITestInput.Process();
                    yield return null;
                    awayElapsed += Time.unscaledDeltaTime;
                }
                Assert.That(awayElapsed, Is.GreaterThanOrEqualTo(.2f), "반대편 포인터의 실제 시차 업데이트 시간 확보");
                Vector2 pointer = TabletopDecisionUITestInput.Position(ui.KeepButton);
                behindUi.transform.SetPositionAndRotation(camera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, 2)),
                    camera.transform.rotation);
                Physics.SyncTransforms();
                Assert.That(Physics.Raycast(camera.ScreenPointToRay(pointer), out RaycastHit hit, 30, 1 << 31,
                    QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(hit.collider.GetComponent<TabletopFocusTarget>(), Is.SameAs(target));
                TabletopDecisionUITestInput.Point(input, mouse, ui.KeepButton);
                focus.SendMessage("Update");
                Assert.That(focus.HoveredTarget, Is.Null, "UI 위 포인터로 뒤의 실제 Collider를 호버하지 않는다");
                Assert.That(focus.MagneticTarget, Is.Null);
                Assert.That(look.IsHoverLocked, Is.False);
                Vector3 beforeLook = camera.transform.position;
                look.SendMessage("LateUpdate");
                Assert.That(Vector3.Distance(camera.transform.position, beforeLook), Is.GreaterThan(.00001f),
                    "UI가 월드 입력을 막는 동안에도 자유 시차는 계속 움직인다");
                behindUi.transform.position = camera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, 2));
                Physics.SyncTransforms();

                if (groupedEdges)
                {
                    input.Press(mouse.leftButton, queueEventOnly: true);
                    input.Release(mouse.leftButton, queueEventOnly: true);
                    TabletopDecisionUITestInput.Process();
                }
                else
                {
                    input.Press(mouse.leftButton);
                    TabletopDecisionUITestInput.Process();
                    focus.SendMessage("Update");
                    Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Deciding));
                    Assert.That(ui.IsVisible, Is.True);
                    input.Release(mouse.leftButton);
                    TabletopDecisionUITestInput.Process();
                }
                Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Revealing));
                Assert.That(controller.Match.PlayerAction, Is.EqualTo(ClockAction.Keep));
                Assert.That(ui.IsVisible, Is.False);
                Assert.That(ui.KeepButton.interactable || ui.SwapButton.interactable, Is.False);
                // UI를 먼저 닫은 EventSystem 뒤에서 카메라 Update가 실행되는 순서도 보호한다.
                focus.SendMessage("Update");
                Assert.That(worldClicks, Is.Zero, groupedEdges ? "같은 InputUpdate의 down/up도 월드로 통과하지 않는다" : "UI 숨김 직후 같은 프레임의 월드 클릭 차단");
                Assert.That(focus.HoveredTarget, Is.Null);
                ui.SwapButton.onClick.Invoke();
                Assert.That(controller.Match.PlayerAction, Is.EqualTo(ClockAction.Keep), "같은 프레임의 다른 버튼 제출 무시");
            }
        }

        [UnityTest]
        public IEnumerator DecisionButtonsSubmitDuringFocusTransitionAndWhileAnInspectionIsFocused()
        {
            foreach (bool duringTransition in new[] { true, false })
            {
                controller.RestartGame();
                yield return WaitFor(RoundStage.Selecting);
                controller.SelectClock(0);
                yield return WaitFor(RoundStage.Deciding);
                var inspections = new SerializedObject(controller.View).FindProperty("inspectionTargets");
                var inspection = (TabletopFocusTarget)inspections.GetArrayElementAtIndex(0).objectReferenceValue;
                Assert.That(focus.Focus(inspection), Is.True);
                if (!duringTransition)
                {
                    float deadline = Time.realtimeSinceStartup + 3;
                    while (focus.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(focus.IsTransitioning, Is.False);
                }
                Assert.That(focus.HasCameraControl, Is.True);
                Assert.That(focus.CurrentTarget, Is.SameAs(inspection));
                Assert.That(controller.View.DecisionUI.IsVisible, Is.True);
                yield return TabletopDecisionUITestInput.Click(input, mouse, controller.View.DecisionUI.SwapButton, () =>
                {
                    Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Deciding));
                    Assert.That(focus.CurrentTarget, Is.SameAs(inspection), "우클릭 복귀 없이 UI를 누른다");
                }, () =>
                {
                    Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Revealing));
                    Assert.That(controller.Match.PlayerAction, Is.EqualTo(ClockAction.Swap));
                    Assert.That(controller.View.DecisionUI.IsVisible, Is.False);
                });
            }
        }

        [UnityTest]
        public IEnumerator CompactBottomOverlayFitsTheViewportAndButtonsShowHoverAndPressFeedback()
        {
            controller.SelectClock(0);
            yield return WaitFor(RoundStage.Deciding);
            var ui = controller.View.DecisionUI;
            Canvas.ForceUpdateCanvases();
            Rect keep = TabletopDecisionUITestInput.ScreenRect(ui.KeepButton);
            Rect swap = TabletopDecisionUITestInput.ScreenRect(ui.SwapButton);
            var canvas = ui.GetComponentInParent<Canvas>();
            float scale = canvas.scaleFactor;
            foreach (var button in new[] { ui.KeepButton, ui.SwapButton })
            {
                Rect rect = TabletopDecisionUITestInput.ScreenRect(button);
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(Screen.width));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.yMax, Is.LessThan(Screen.height * .25f), "시계 판독 영역을 가리는 중앙 HUD가 되지 않는다");
                Assert.That(rect.width, Is.GreaterThanOrEqualTo(90 * scale));
                Assert.That(rect.height, Is.GreaterThanOrEqualTo(44 * scale));
                Assert.That(button.GetComponentInChildren<TextMeshProUGUI>().text, Is.Not.Empty);
            }
            Assert.That(keep.Overlaps(swap), Is.False);
            Assert.That((Mathf.Min(keep.xMin, swap.xMin) + Mathf.Max(keep.xMax, swap.xMax)) * .5f,
                Is.EqualTo(Screen.width * .5f).Within(Screen.width * .03f));
            Assert.That(Mathf.Max(keep.xMax, swap.xMax) - Mathf.Min(keep.xMin, swap.xMin), Is.LessThan(Screen.width * .5f));

            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            TabletopDecisionUITestInput.Process();
            yield return new WaitForSecondsRealtime(.2f);
            Color normal = ui.KeepButton.targetGraphic.canvasRenderer.GetColor();
            TabletopDecisionUITestInput.Point(input, mouse, ui.KeepButton);
            yield return new WaitForSecondsRealtime(.2f);
            Color hovered = ui.KeepButton.targetGraphic.canvasRenderer.GetColor();
            Assert.That(Vector4.Distance(normal, hovered), Is.GreaterThan(.01f), "hover를 눈에 보이는 색 변화로 전달한다");
            input.Press(mouse.leftButton);
            TabletopDecisionUITestInput.Process();
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(Vector4.Distance(hovered, ui.KeepButton.targetGraphic.canvasRenderer.GetColor()), Is.GreaterThan(.01f),
                "누르는 동안 별도의 피드백을 표시한다");
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Deciding));
            input.Release(mouse.leftButton);
            TabletopDecisionUITestInput.Process();
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Revealing));
        }

        private TabletopFocusTarget CreateBehindUiTarget()
        {
            behindUi = new GameObject("ColliderBehindDecisionUi");
            behindUi.layer = 31;
            behindUi.AddComponent<BoxCollider>().size = Vector3.one * .2f;
            var point = new GameObject("FocusPoint").transform;
            point.SetParent(behindUi.transform, false);
            var target = behindUi.AddComponent<TabletopFocusTarget>();
            var serialized = new SerializedObject(target);
            serialized.FindProperty("focusPoint").objectReferenceValue = point;
            serialized.FindProperty("focusOnClick").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var cameraSettings = new SerializedObject(focus);
            cameraSettings.FindProperty("interactionLayers").intValue = 1 << 31;
            cameraSettings.ApplyModifiedPropertiesWithoutUndo();
            return target;
        }

        private IEnumerator WaitFor(RoundStage stage)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (controller.Match.Stage != stage && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.Match.Stage, Is.EqualTo(stage), controller.Match.DebugSnapshot());
        }
    }

    internal static class TabletopDecisionUITestInput
    {
        public static Vector2 Position(Button button)
        {
            Canvas.ForceUpdateCanvases();
            return RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        }

        public static Rect ScreenRect(Button button)
        {
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(null, corner);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        public static void Point(InputTestFixture input, Mouse mouse, Button button)
        {
            Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True);
            mouse.MakeCurrent();
            Vector2 requested = Position(button);
            input.Set(mouse.position, requested);
            Process();
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() }, hits);
            var first = hits.FirstOrDefault(hit => hit.module is GraphicRaycaster);
            string diagnostic = DescribePointer(button, mouse, requested, hits);
            Assert.That(first.gameObject, Is.Not.Null, "실제 GraphicRaycaster가 버튼 영역을 가리킨다 / " + diagnostic);
            Assert.That(first.gameObject.transform.IsChildOf(button.transform), Is.True, diagnostic);
        }

        public static void DisableSceneModules()
        {
            // Fixture가 InputSystem 전역 상태를 교체하기 전에 씬 UI의 활성 Action binding을 해제한다.
            foreach (var module in UnityEngine.Object.FindObjectsByType<InputSystemUIInputModule>(FindObjectsInactive.Include,
                FindObjectsSortMode.None))
                module.enabled = false;
        }

        private static string DescribePointer(Button button, Mouse mouse, Vector2 requested, List<RaycastResult> hits)
        {
            var canvas = button.GetComponentInParent<Canvas>();
            var graphic = button.targetGraphic;
            var ui = button.GetComponentInParent<TabletopDecisionUI>();
            return $"frame={Time.frameCount} requested={requested} actual={mouse.position.ReadValue()} rect={ScreenRect(button)} " +
                $"screen={Screen.width}x{Screen.height} button={button.name} active={button.gameObject.activeInHierarchy} " +
                $"interactable={button.interactable} visible={ui && ui.IsVisible} " +
                $"canvas={canvas?.name} active={canvas && canvas.isActiveAndEnabled} mode={canvas?.renderMode} " +
                $"pixelRect={canvas?.pixelRect} scale={canvas?.scaleFactor} " +
                $"graphicDepth={graphic?.depth} raycast={graphic && graphic.raycastTarget} cull={graphic?.canvasRenderer.cull} " +
                $"left={mouse.leftButton.isPressed} pressed={mouse.leftButton.wasPressedThisFrame} released={mouse.leftButton.wasReleasedThisFrame} " +
                $"hits=[{string.Join(",", hits.Select(hit => hit.gameObject ? hit.gameObject.name : "null"))}]";
        }

        public static void Process()
        {
            InputSystem.Update();
            Assert.That(EventSystem.current, Is.Not.Null);
            var module = EventSystem.current.currentInputModule as InputSystemUIInputModule;
            Assert.That(module, Is.Not.Null, "실제 InputSystem UI 입력 모듈 사용");
            module.Process();
        }

        public static IEnumerator Ready(Button button)
        {
            // Stage는 코루틴에서 먼저 열린다. 실제 표시/레이캐스트 등록이 끝난 프레임부터 누른다.
            float deadline = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < deadline)
            {
                Canvas.ForceUpdateCanvases();
                if (button.gameObject.activeInHierarchy && button.interactable && button.targetGraphic.depth >= 0 &&
                    !button.targetGraphic.canvasRenderer.cull) yield break;
                yield return null;
            }
            Assert.Fail($"UI 표시/레이캐스트 등록 시간 초과: {button.name} active={button.gameObject.activeInHierarchy} " +
                $"depth={button.targetGraphic.depth} cull={button.targetGraphic.canvasRenderer.cull}");
        }

        public static IEnumerator Click(InputTestFixture input, Mouse mouse, Button button,
            Action afterPress = null, Action afterRelease = null)
        {
            yield return Ready(button);
            Point(input, mouse, button);
            yield return null;
            input.Press(mouse.leftButton);
            Process();
            afterPress?.Invoke();
            yield return null;
            input.Release(mouse.leftButton);
            Process();
            afterRelease?.Invoke();
            yield return null;
        }
    }
}
#endif
