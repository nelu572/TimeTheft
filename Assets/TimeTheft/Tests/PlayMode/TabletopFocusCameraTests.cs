#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class TabletopFocusCameraTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private Keyboard keyboard;
        private Camera camera;
        private TabletopCameraLook look;
        private TabletopFocusCamera focus;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private float homeFov;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadStudy()
        {
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TabletopStudy.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            input = new InputTestFixture();
            input.Setup();
            mouse = InputSystem.AddDevice<Mouse>("StudyFocusMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("StudyFocusKeyboard");
            camera = Camera.main;
            look = camera.GetComponent<TabletopCameraLook>();
            focus = camera.GetComponent<TabletopFocusCamera>();
            // 입력 테스트 전에 씬에 저장된 기준 위치로 되돌린다.
            look.enabled = false;
            look.enabled = true;
            homePosition = camera.transform.position;
            homeRotation = camera.transform.rotation;
            homeFov = camera.fieldOfView;
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0;
            PointAt(camera.pixelRect.center);
            yield return null;
            look.SendMessage("OnApplicationFocus", true);
            focus.SendMessage("OnApplicationFocus", true);
            Assert.That(Object.FindObjectsByType<PrototypeController>(FindObjectsSortMode.None), Is.Empty);
        }

        [UnityTearDown]
        public IEnumerator RestoreInput()
        {
            if (focus) focus.enabled = false;
            Time.timeScale = previousTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator FiveAuthoredTargetsHoverClickFocusAndReturnThroughRealInput()
        {
            var targets = Object.FindObjectsByType<TabletopFocusTarget>(FindObjectsSortMode.None)
                .OrderBy(t => t.transform.GetInstanceID()).ToArray();
            Assert.That(targets, Has.Length.EqualTo(5));
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                PointAt(camera.pixelRect.max + Vector2.one * 10);
                yield return new WaitForSecondsRealtime(.85f);
                Assert.That(focus.HoveredTarget, Is.Null);
                var renderer = target.name == "CentralTimeBank"
                    ? target.transform.Find("Face/Trim").GetComponent<SpriteRenderer>()
                    : target.name == "ClockPlaceholder" ? target.transform.Find("Case").GetComponent<SpriteRenderer>()
                    : target.transform.Find("SlotOutline").GetComponent<SpriteRenderer>();
                Color originalColor = renderer.color;
                // 추적 카메라가 움직이는 동안에도 같은 물체의 실제 영역을 가리킨다.
                Vector3 aim = target.name == "CentralTimeBank" ? target.transform.Find("Cabinet").position
                    : target.transform.position;
                for (int sample = 0; sample < 20; sample++)
                {
                    PointAt(camera.WorldToScreenPoint(aim));
                    yield return null;
                }
                Assert.That(focus.HoveredTarget, Is.SameAs(target), target.name);
                Assert.That(focus.HasCameraControl, Is.False);
                Assert.That(renderer.color.maxColorComponent, Is.GreaterThan(originalColor.maxColorComponent));
                Vector3 before = camera.transform.position;
                input.Press(mouse.leftButton);
                yield return null;
                input.Release(mouse.leftButton);
                Assert.That(focus.CurrentTarget, Is.SameAs(target), target.name);
                Assert.That(Vector3.Distance(before, camera.transform.position), Is.LessThan(.3f), "클릭 시 순간 이동");
                Assert.That(renderer.color, Is.EqualTo(originalColor));
                yield return new WaitForSecondsRealtime(target.TransitionDuration + .1f);
                Assert.That(Vector3.Distance(camera.transform.position, target.FocusPoint.position), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, target.FocusPoint.rotation), Is.LessThan(.02f));
                Assert.That(camera.fieldOfView, Is.EqualTo(target.FieldOfView).Within(.01f));
                PointAt(camera.pixelRect.max - Vector2.one);
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That(Vector3.Distance(camera.transform.position, target.FocusPoint.position), Is.LessThan(.001f), "집중 중 추적 중지");
                PointAt(camera.pixelRect.center);
                if (i % 2 == 0) input.Press(mouse.rightButton);
                else input.Press(keyboard.escapeKey);
                yield return null;
                input.Release(mouse.rightButton);
                input.Release(keyboard.escapeKey);
                yield return new WaitForSecondsRealtime(.85f);
                AssertHome();
            }

            PointAt(camera.pixelRect.max - Vector2.one);
            yield return new WaitForSecondsRealtime(1);
            Assert.That(Quaternion.Angle(homeRotation, camera.transform.rotation), Is.InRange(7.9f, 8.4f));
            Assert.That(Vector3.Distance(homePosition, camera.transform.position), Is.GreaterThan(.23f));
            PointAt(camera.pixelRect.center);
            yield return new WaitForSecondsRealtime(1);
            AssertHome();
        }

        [UnityTest]
        public IEnumerator InterruptedTransitionsInvalidTargetsAndDisableReleaseTheCamera()
        {
            var target = GameObject.Find("ClockSlots/ClockSlot1/ClockPlaceholder").GetComponent<TabletopFocusTarget>();
            Assert.That(focus.Focus(target), Is.True);
            yield return new WaitForSecondsRealtime(.2f);
            var before = camera.transform.position;
            focus.ReturnToOverview();
            Assert.That(camera.transform.position, Is.EqualTo(before), "복귀 명령은 pose를 즉시 바꾸지 않는다");
            PointAt(camera.pixelRect.center);
            yield return new WaitForSecondsRealtime(.85f);
            AssertHome();

            focus.Focus(target);
            yield return new WaitForSecondsRealtime(.85f);
            before = camera.transform.position;
            look.enabled = false;
            look.enabled = true;
            Assert.That(camera.transform.position, Is.EqualTo(before));
            target.enabled = false;
            yield return new WaitForSecondsRealtime(.85f);
            AssertHome();
            target.enabled = true;
            focus.Focus(target);
            yield return new WaitForSecondsRealtime(.2f);
            focus.enabled = false;
            AssertHome();
            focus.enabled = true;
            focus.SendMessage("OnApplicationFocus", true);
            focus.Focus(target);
            Object.Destroy(target.FocusPoint.gameObject);
            yield return new WaitForSecondsRealtime(.85f);
            AssertHome();
            Assert.That(focus.Focus(target), Is.False);
        }

        private void PointAt(Vector2 position)
        {
            mouse.MakeCurrent();
            input.Set(mouse.position, position);
        }

        private void AssertHome()
        {
            Assert.That(focus.HasCameraControl, Is.False);
            Assert.That(focus.CurrentTarget, Is.Null);
            Assert.That(Vector3.Distance(camera.transform.position, homePosition), Is.LessThan(.003f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, homeRotation), Is.LessThan(.05f));
            Assert.That(camera.fieldOfView, Is.EqualTo(homeFov).Within(.01f));
        }
    }
}
#endif
