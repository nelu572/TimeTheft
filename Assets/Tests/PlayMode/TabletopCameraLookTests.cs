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
        private Quaternion baseWorldRotation;
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
            baseWorldRotation = child.transform.rotation;
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
                Assert.That(Quaternion.Angle(baseRotation, camera.transform.localRotation), Is.LessThan(11.51f));
                yield return new WaitForSecondsRealtime(1);
                Vector3 offset = camera.transform.localPosition - basePosition;
                Assert.That(offset.x * Mathf.Sign(corner.x - .5f), Is.InRange(.20f, .221f));
                Assert.That(offset.y * Mathf.Sign(corner.y - .5f), Is.InRange(.11f, .121f));
                Assert.That(offset.z, Is.EqualTo(0).Within(.0001f));
                AssertWorldUpRotation();
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
                Assert.That(Vector3.Distance(basePosition, camera.transform.localPosition), Is.GreaterThan(.23f),
                    $"반복 {i}, viewport {camera.pixelRect}, pointer {mouse.position.ReadValue()}");
                AssertWorldUpRotation();
                look.enabled = false;
                Assert.That(Quaternion.Angle(baseRotation, camera.transform.localRotation), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(basePosition, camera.transform.localPosition), Is.LessThan(.0001f));
                look.enabled = true;
            }
        }

        [UnityTest]
        public IEnumerator DeadZoneIsStillAtCenterContinuousAtItsEdgeAndBoundedAtCorners()
        {
            Time.timeScale = 0;
            yield return HoldPointer(new Vector2(.11f, -.11f));
            Assert.That(Vector3.Distance(camera.transform.localPosition, basePosition), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(camera.transform.localRotation, baseRotation), Is.LessThan(.01f));
            yield return HoldPointer(new Vector2(.12f, 0));
            Assert.That(Vector3.Distance(camera.transform.localPosition, basePosition), Is.LessThan(.00001f));

            yield return HoldPointer(new Vector2(.13f, 0));
            float positive = camera.transform.localPosition.x - basePosition.x;
            Assert.That(positive, Is.InRange(.000001f, .001f), "dead zone 밖에서 점프 없이 조금씩 움직인다");
            AssertWorldUpRotation();
            yield return HoldPointer(new Vector2(-.13f, 0));
            float negative = camera.transform.localPosition.x - basePosition.x;
            Assert.That(negative, Is.EqualTo(-positive).Within(.00001f), "양쪽 경계가 대칭이다");

            yield return HoldPointer(new Vector2(.999f, .999f));
            Vector3 offset = camera.transform.localPosition - basePosition;
            Assert.That(offset.x, Is.EqualTo(.22f).Within(.002f));
            Assert.That(offset.y, Is.EqualTo(.12f).Within(.002f));
            Assert.That(offset.z, Is.EqualTo(0).Within(.00001f));
            AssertWorldUpRotation();
        }

        [UnityTest]
        public IEnumerator MagneticHoverBlendsSettlesSwitchesAndReturnsToTheMovingFreePose()
        {
            Time.timeScale = 0;
            yield return HoldPointerFor(new Vector2(.7f, .3f), .2f);
            Vector3 startPosition = camera.transform.position;
            Quaternion startRotation = camera.transform.rotation;
            Vector3 firstPosition = startPosition + camera.transform.right * .06f + camera.transform.up * .03f;
            Quaternion firstRotation = Quaternion.AngleAxis(2, Vector3.up) * startRotation * Quaternion.AngleAxis(-1, Vector3.right);
            look.MoveToHoverPose(firstPosition, firstRotation, .3f);
            Assert.That(look.IsHoverLocked, Is.True);
            AssertPose(startPosition, startRotation);
            yield return HoldPointerFor(new Vector2(-.7f, -.3f), .1f);
            AssertBetween(startPosition, firstPosition);
            yield return HoldPointerFor(new Vector2(-.7f, -.3f), .3f);
            AssertPose(firstPosition, firstRotation);
            yield return HoldPointerFor(new Vector2(.9f, .8f), .1f);
            AssertPose(firstPosition, firstRotation);

            Vector3 secondPosition = firstPosition - camera.transform.right * .04f + camera.transform.up * .02f;
            Quaternion secondRotation = Quaternion.AngleAxis(-2, Vector3.up) * firstRotation;
            look.MoveToHoverPose(secondPosition, secondRotation, .3f);
            AssertPose(firstPosition, firstRotation);
            yield return HoldPointerFor(new Vector2(.9f, .8f), .1f);
            AssertBetween(firstPosition, secondPosition);
            yield return HoldPointerFor(new Vector2(.9f, .8f), .3f);
            AssertPose(secondPosition, secondRotation);

            look.ReleaseHover(.3f);
            Assert.That(look.IsHoverLocked, Is.False);
            AssertPose(secondPosition, secondRotation);
            yield return HoldPointerFor(new Vector2(.8f, .6f), .1f);
            Assert.That(Vector3.Distance(camera.transform.position, secondPosition), Is.GreaterThan(.001f), "복귀도 시간에 따라 이동한다");
            yield return HoldPointer(new Vector2(-.8f, -.6f));
            Assert.That(camera.transform.localPosition.x, Is.EqualTo(basePosition.x - .22f * OutsideDeadZone(.8f)).Within(.002f),
                "복귀 시작 때 캡처한 위치가 아니라 바뀐 포인터의 free pose로 이어진다");
            Assert.That(camera.transform.localPosition.y, Is.EqualTo(basePosition.y - .12f * OutsideDeadZone(.6f)).Within(.002f));
            AssertWorldUpRotation();

            look.MoveToHoverPose(firstPosition, firstRotation, .3f);
            yield return HoldPointerFor(Vector2.zero, .1f);
            look.SetSuspended(true);
            Assert.That(look.IsHoverLocked, Is.False);
            Vector3 focusedPosition = camera.transform.position + new Vector3(.3f, .2f, .1f);
            Quaternion focusedRotation = Quaternion.Euler(17, 23, 0);
            camera.transform.SetPositionAndRotation(focusedPosition, focusedRotation);
            look.MoveToHoverPose(secondPosition, secondRotation, .1f);
            yield return HoldPointerFor(new Vector2(.9f, .9f), .2f);
            AssertPose(focusedPosition, focusedRotation);
            look.ReleaseHover(.1f);
            yield return HoldPointerFor(Vector2.zero, .2f);
            AssertPose(focusedPosition, focusedRotation);
        }

        private IEnumerator HoldPointer(Vector2 normalized) => HoldPointerFor(normalized, 1);

        private IEnumerator HoldPointerFor(Vector2 normalized, float seconds)
        {
            float elapsed = 0;
            float deadline = Time.realtimeSinceStartup + 3;
            while (elapsed < seconds && Time.realtimeSinceStartup < deadline)
            {
                look.SendMessage("OnApplicationFocus", true);
                PointAt(camera.pixelRect.center + Vector2.Scale(camera.pixelRect.size * .5f, normalized));
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(seconds), "실제 카메라 업데이트 시간 확보");
        }

        private void AssertBetween(Vector3 start, Vector3 end)
        {
            float distance = Vector3.Distance(start, end);
            Assert.That(Vector3.Distance(camera.transform.position, start), Is.InRange(.001f, distance - .001f), "자석 진입/전환은 순간 이동하지 않는다");
            Assert.That(Vector3.Distance(camera.transform.position, end), Is.GreaterThan(.001f));
        }

        private static float OutsideDeadZone(float value) => Mathf.SmoothStep(0, 1, (value - .12f) / .88f);

        private void AssertWorldUpRotation()
        {
            Vector3 offset = camera.transform.localPosition - basePosition;
            Quaternion expected = Quaternion.AngleAxis(offset.x / .22f * 7, Vector3.up) * baseWorldRotation *
                Quaternion.AngleAxis(-offset.y / .12f * 4.5f, Vector3.right);
            Assert.That(Quaternion.Angle(camera.transform.rotation, expected), Is.LessThan(.03f), "월드 수직축 yaw와 기준 자세의 local-X pitch");
            Assert.That(camera.transform.right.y, Is.EqualTo((baseWorldRotation * Vector3.right).y).Within(.0001f),
                "시차는 기준 자세에 추가 roll을 만들지 않는다");
        }

        private void AssertPose(Vector3 position, Quaternion rotation)
        {
            Assert.That(Vector3.Distance(camera.transform.position, position), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, rotation), Is.LessThan(.01f));
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
