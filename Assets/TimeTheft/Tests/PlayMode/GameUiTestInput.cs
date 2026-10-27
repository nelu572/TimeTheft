#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TimeTheft.Tests
{
    internal static class GameUiTestInput
    {
        public static void ShortenBatchReveal(TimeTheftGameController controller)
        {
            var reveal = controller.View.BatchReveal;
            if (!reveal) return;
            // 규칙/입력 회귀는 실행 중인 씬 사본의 연출만 단축한다. 전용 연출 검사는 실제 시간을 검사한다.
            reveal.PreparationDuration = 0;
            reveal.RevealDuration = .12f; reveal.CameraApproachDuration = .08f;
            reveal.CloseDuration = .08f; reveal.CameraReturnDuration = .1f;
            var turn = controller.View.TurnPresentation;
            if (turn) { turn.SelectDuration = .03f; turn.RevealEnterDuration = .04f; turn.LegacyReturnDuration = .03f; }
            if (turn) { turn.SettlementTransitionDuration = .04f; turn.SettlementLeadDuration = .03f;
                turn.TurnCloseDuration = .04f; turn.TurnCameraReturnDuration = .04f; }
            if (controller.View.ShufflePresentation) controller.View.ShufflePresentation.SecondsPerPass = .01f;
            var lifecycle = controller.View.Lifecycle;
            if (lifecycle) { lifecycle.MoveDuration = lifecycle.HatchCycleDuration = lifecycle.SupplyDuration = .04f;
                lifecycle.BinHoldDuration = lifecycle.DiscardBinHoldDuration = .01f; }
        }
        public static IEnumerator ClockMotionReady(TimeTheftGameController controller)
        {
            float deadline = Time.realtimeSinceStartup + 3;
            while (controller.IsPresentingClock && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.IsPresentingClock, Is.False, "Select 완료 후 기존 결정 입력을 사용한다");
        }
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
            Assert.That(IsVisiblyInteractable(button), Is.True, DescribeButton(button));
            mouse.MakeCurrent();
            Vector2 requested = Position(button);
            input.Set(mouse.position, requested);
            Process();
            var hits = Raycast(mouse.position.ReadValue());
            var first = hits.FirstOrDefault(hit => hit.module is GraphicRaycaster);
            string diagnostic = DescribePointer(button, mouse, requested, hits);
            Assert.That(first.gameObject, Is.Not.Null, "실제 GraphicRaycaster가 버튼 영역을 가리킨다 / " + diagnostic);
            Assert.That(first.gameObject.GetComponentInParent<Button>(), Is.SameAs(button), diagnostic);
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
            var first = hits.FirstOrDefault(hit => hit.module is GraphicRaycaster);
            var topButton = first.gameObject ? first.gameObject.GetComponentInParent<Button>() : null;
            return $"frame={Time.frameCount} requested={requested} actual={mouse.position.ReadValue()} " +
                DescribeButton(button) + $" topGraphic={first.gameObject?.name} topButton={topButton?.name} " +
                $"selected={EventSystem.current?.currentSelectedGameObject?.name} " +
                $"left={mouse.leftButton.isPressed} pressed={mouse.leftButton.wasPressedThisFrame} released={mouse.leftButton.wasReleasedThisFrame} " +
                $"hits=[{string.Join(",", hits.Select(hit => hit.gameObject ? hit.gameObject.name : "null"))}]";
        }

        private static string DescribeButton(Button button)
        {
            if (!button) return "button=null/destroyed";
            var canvas = button.GetComponentInParent<Canvas>();
            var graphic = button.targetGraphic;
            var groups = button.GetComponentsInParent<CanvasGroup>(true);
            return $"rect={ScreenRect(button)} " +
                $"screen={Screen.width}x{Screen.height} button={button.name} active={button.gameObject.activeInHierarchy} " +
                $"enabled={button.isActiveAndEnabled} interactable={button.interactable} effectiveInteractable={button.IsInteractable()} " +
                $"canvas={canvas?.name} active={canvas && canvas.isActiveAndEnabled} mode={canvas?.renderMode} " +
                $"pixelRect={canvas?.pixelRect} scale={canvas?.scaleFactor} " +
                $"graphicDepth={graphic?.depth} raycast={graphic && graphic.raycastTarget} cull={graphic?.canvasRenderer.cull} " +
                $"inheritedAlpha={(graphic ? graphic.canvasRenderer.GetInheritedAlpha() : 0)} " +
                "groups=[" + string.Join(",", groups.Select(group => $"{group.name}(enabled={group.enabled},alpha={group.alpha}," +
                    $"interactable={group.interactable},blocksRaycasts={group.blocksRaycasts},ignoreParentGroups={group.ignoreParentGroups})")) + "]";
        }

        private static List<RaycastResult> Raycast(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            if (EventSystem.current)
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            return hits;
        }

        private static bool IsVisiblyInteractable(Button button)
        {
            if (!button || !button.isActiveAndEnabled || !button.IsInteractable()) return false;
            var graphic = button.targetGraphic;
            return graphic && graphic.isActiveAndEnabled && graphic.depth >= 0 &&
                !graphic.canvasRenderer.cull && graphic.canvasRenderer.GetInheritedAlpha() > .99f;
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
            // Stage/자식 Button은 먼저 열릴 수 있다. 부모 CanvasGroup의 입력 잠금과 fade 완료까지 기다린다.
            float deadline = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < deadline)
            {
                Canvas.ForceUpdateCanvases();
                if (IsVisiblyInteractable(button))
                {
                    // 실제 GraphicRaycaster가 CanvasGroup.blocksRaycasts/ignoreParentGroups와 가림도 판정한다.
                    var first = Raycast(Position(button)).FirstOrDefault(hit => hit.module is GraphicRaycaster);
                    if (first.gameObject && first.gameObject.GetComponentInParent<Button>() == button) yield break;
                }
                yield return null;
            }
            var mouse = Mouse.current;
            string diagnostic = button && mouse != null
                ? DescribePointer(button, mouse, Position(button), Raycast(Position(button))) : DescribeButton(button);
            Assert.Fail("UI 표시/레이캐스트 등록 시간 초과: " + diagnostic);
        }

        public static IEnumerator Click(InputTestFixture input, Mouse mouse, Button button,
            Action afterPress = null, Action afterRelease = null)
        {
            yield return Ready(button);
            Point(input, mouse, button);
            yield return null;
            // 카메라/오브젝트를 따라가는 카드가 위 프레임의 LateUpdate에서 이동했을 수 있다.
            yield return Ready(button);
            Point(input, mouse, button);
            string beforePress = DescribePointer(button, mouse, Position(button), Raycast(mouse.position.ReadValue()));
            int submitted = 0;
            UnityEngine.Events.UnityAction observeClick = () => submitted++;
            button.onClick.AddListener(observeClick);
            try
            {
                input.Press(mouse.leftButton);
                Process();
                InvokeAssertion(afterPress, "press", button, mouse, beforePress);
                yield return null;
                input.Release(mouse.leftButton);
                Process();
                Assert.That(submitted, Is.EqualTo(1), "실제 포인터 press/release가 Button.onClick을 한 번 제출한다 / beforePress=" +
                    beforePress + " / afterRelease=" + DescribePointer(button, mouse, mouse.position.ReadValue(), Raycast(mouse.position.ReadValue())));
                InvokeAssertion(afterRelease, "release", button, mouse, beforePress);
            }
            finally
            {
                if (button) button.onClick.RemoveListener(observeClick);
            }
            yield return null;
        }

        private static void InvokeAssertion(Action assertion, string phase, Button button, Mouse mouse, string beforePress)
        {
            try { assertion?.Invoke(); }
            catch (AssertionException)
            {
                TestContext.WriteLine($"UI {phase} assertion / beforePress={beforePress} / current=" +
                    DescribePointer(button, mouse, mouse.position.ReadValue(), Raycast(mouse.position.ReadValue())));
                throw;
            }
        }
    }
}
#endif
