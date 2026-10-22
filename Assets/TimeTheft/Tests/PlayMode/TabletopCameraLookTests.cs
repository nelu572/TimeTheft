using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class TabletopCameraLookTests
    {
        private InputTestFixture input;
        private GameObject rig;
        private Camera camera;
        private TabletopCameraLook look;
        private Mouse mouse;
        private Quaternion baseRotation;
        private Vector3 basePosition;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator CreateCamera()
        {
            input = new InputTestFixture();
            input.Setup();
            Assert.That(InputSystem.devices, Is.Empty);
            previousTimeScale = Time.timeScale;
            mouse = InputSystem.AddDevice<Mouse>("TabletopCameraTestMouse");
            rig = new GameObject("TestCameraRig");
            rig.transform.SetPositionAndRotation(new Vector3(0, 4, -5), Quaternion.Euler(34, 0, 0));
            var child = new GameObject("TestCamera");
            child.transform.SetParent(rig.transform, false);
            child.transform.localPosition = new Vector3(.03f, -.02f, .04f);
            child.transform.localRotation = Quaternion.Euler(3, 7, 2);
            camera = child.AddComponent<Camera>();
            camera.enabled = false;
            camera.rect = new Rect(.1f, .1f, .8f, .8f);
            baseRotation = child.transform.localRotation;
            basePosition = child.transform.localPosition;
            look = child.AddComponent<TabletopCameraLook>();
            PointAt(camera.pixelRect.center);
            yield return null;
            // Unity의 최초 focus 콜백 이후 테스트할 창 상태를 지정한다.
            look.SendMessage("OnApplicationFocus", true);
        }

        [UnityTearDown]
        public IEnumerator DestroyCamera()
        {
            Time.timeScale = previousTimeScale;
            Object.Destroy(rig);
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator PointerLimitsCenterAndFocusWorkWhileTimeIsPaused()
        {
            Time.timeScale = 0;
            Assert.That(Quaternion.Angle(baseRotation, camera.transform.localRotation), Is.LessThan(.01f));
            foreach (var corner in new[] { new Vector2(.01f, .01f), new Vector2(.99f, .01f),
                new Vector2(.01f, .99f), new Vector2(.99f, .99f) })
            {
                Rect rect = camera.pixelRect;
                PointAt(rect.min + Vector2.Scale(rect.size, corner));
                yield return null;
                Assert.That(Quaternion.Angle(baseRotation, camera.transform.localRotation), Is.LessThan(8.4f));
                yield return new WaitForSecondsRealtime(1);
                Vector3 angles = (Quaternion.Inverse(baseRotation) * camera.transform.localRotation).eulerAngles;
                float yaw = Mathf.DeltaAngle(0, angles.y), pitch = Mathf.DeltaAngle(0, angles.x);
                Assert.That(yaw * Mathf.Sign(corner.x - .5f), Is.InRange(6.5f, 7.01f));
                Assert.That(-pitch * Mathf.Sign(corner.y - .5f), Is.InRange(4.1f, 4.51f));
                Vector3 offset = camera.transform.localPosition - basePosition;
                Assert.That(offset.x * Mathf.Sign(corner.x - .5f), Is.InRange(.20f, .221f));
                Assert.That(offset.y * Mathf.Sign(corner.y - .5f), Is.InRange(.11f, .121f));
                Assert.That(offset.z, Is.EqualTo(0).Within(.0001f));
            }

            PointAt(camera.pixelRect.center);
            yield return new WaitForSecondsRealtime(1);
            AssertAtRest();
            PointAt(camera.pixelRect.max - Vector2.one);
            yield return new WaitForSecondsRealtime(1);
            look.SendMessage("OnApplicationFocus", false);
            yield return new WaitForSecondsRealtime(1);
            AssertAtRest();
            look.SendMessage("OnApplicationFocus", true);
            PointAt(new Vector2(camera.pixelRect.xMax + 20, camera.pixelRect.yMax + 20));
            yield return new WaitForSecondsRealtime(1);
            AssertAtRest();
            PointAt(camera.pixelRect.max - Vector2.one);
            yield return new WaitForSecondsRealtime(1);
            InputSystem.RemoveDevice(mouse);
            yield return new WaitForSecondsRealtime(1);
            AssertAtRest();
        }

        [UnityTest]
        public IEnumerator ReenablingRestoresAuthoredPoseWithoutAccumulation()
        {
            for (int i = 0; i < 3; i++)
            {
                // 재활성화의 기준 pose를 검사하므로 외부 창 포커스와 GameView 크기를
                // 매 프레임 제어한다. 응답 시간도 실제 LateUpdate에 공급된 시간으로 센다.
                float elapsed = 0;
                float deadline = Time.realtimeSinceStartup + 3;
                while (elapsed < 1 && Time.realtimeSinceStartup < deadline)
                {
                    look.SendMessage("OnApplicationFocus", true);
                    PointAt(camera.pixelRect.max - Vector2.one);
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
                Assert.That(elapsed, Is.GreaterThanOrEqualTo(1), "테스트 업데이트 시간 부족");
                Assert.That(Quaternion.Angle(baseRotation, camera.transform.localRotation), Is.InRange(7.9f, 8.4f),
                    $"반복 {i}, viewport {camera.pixelRect}, pointer {mouse.position.ReadValue()}");
                look.enabled = false;
                Assert.That(Quaternion.Angle(baseRotation, camera.transform.localRotation), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(basePosition, camera.transform.localPosition), Is.LessThan(.0001f));
                look.enabled = true;
            }
        }

        private void AssertAtRest()
        {
            Assert.That(Quaternion.Angle(baseRotation, camera.transform.localRotation), Is.LessThan(.05f));
            Assert.That(Vector3.Distance(basePosition, camera.transform.localPosition), Is.LessThan(.002f));
        }

        private void PointAt(Vector2 position)
        {
            mouse.MakeCurrent();
            input.Set(mouse.position, position);
        }
    }
}
