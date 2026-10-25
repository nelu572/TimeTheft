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

            return $"frame={Time.frameCount} requested={requested} actual={mouse.position.ReadValue()} rect={ScreenRect(button)} " +
                $"screen={Screen.width}x{Screen.height} button={button.name} active={button.gameObject.activeInHierarchy} " +
                $"interactable={button.interactable} " +
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
