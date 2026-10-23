#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
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
        private GameObject hoverFixtures;

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
            if (hoverFixtures) Object.Destroy(hoverFixtures);
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
                Assert.That(look.IsHoverLocked, Is.False, "클릭 집중이 호버 잠금보다 우선한다");
                Assert.That(Vector3.Distance(before, camera.transform.position), Is.LessThan(.3f), "클릭 시 순간 이동");
                Assert.That(renderer.color, Is.EqualTo(originalColor));
                yield return new WaitForSecondsRealtime(target.TransitionDuration + .1f);
                Assert.That(Vector3.Distance(camera.transform.position, target.FocusPoint.position), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, target.FocusPoint.rotation), Is.LessThan(.02f));
                Assert.That(camera.fieldOfView, Is.EqualTo(target.FieldOfView).Within(.01f));
                PointAt(camera.pixelRect.max - Vector2.one);
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That(Vector3.Distance(camera.transform.position, target.FocusPoint.position), Is.LessThan(.001f), "집중 중 추적 중지");
                PointAt(camera.pixelRect.max + Vector2.one * 10);
                if (i % 2 == 0) input.Press(mouse.rightButton);
                else input.Press(keyboard.escapeKey);
                yield return null;
                input.Release(mouse.rightButton);
                input.Release(keyboard.escapeKey);
                yield return new WaitForSecondsRealtime(.85f);
                AssertHome();
            }

            // 시차 자체의 중앙 복원을 검사할 때는 중앙 시계의 정상적인 호버 잠금을 분리한다.
            var inputEnabled = targets.Select(target => target.InputEnabled).ToArray();
            foreach (var target in targets) target.SetInputEnabled(false);
            try
            {
                PointAt(camera.pixelRect.max - Vector2.one);
                yield return new WaitForSecondsRealtime(1);
                Assert.That(Quaternion.Angle(homeRotation, camera.transform.rotation), Is.InRange(7.9f, 8.4f));
                Assert.That(Vector3.Distance(homePosition, camera.transform.position), Is.GreaterThan(.23f));
                PointAt(camera.pixelRect.center);
                yield return new WaitForSecondsRealtime(1);
                Assert.That(look.IsHoverLocked, Is.False);
                AssertHome();
            }
            finally
            {
                for (int i = 0; i < targets.Length; i++) targets[i].SetInputEnabled(inputEnabled[i]);
            }
        }

        [UnityTest]
        public IEnumerator InterruptedTransitionsInvalidTargetsAndDisableReleaseTheCamera()
        {
            PointAt(camera.pixelRect.max + Vector2.one * 10);
            InputSystem.Update();
            var target = GameObject.Find("ClockSlots/ClockSlot1/ClockPlaceholder").GetComponent<TabletopFocusTarget>();
            Assert.That(focus.Focus(target), Is.True);
            Assert.That(look.IsHoverLocked, Is.False);
            yield return new WaitForSecondsRealtime(.2f);
            var before = camera.transform.position;
            focus.ReturnToOverview();
            Assert.That(look.IsHoverLocked, Is.False);
            Assert.That(camera.transform.position, Is.EqualTo(before), "복귀 명령은 pose를 즉시 바꾸지 않는다");
            PointAt(camera.pixelRect.max + Vector2.one * 10);
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

        [UnityTest]
        public IEnumerator MagneticHoverMovesSettlesAndPaddingNeverCreatesClickTargets()
        {
            PointAt(camera.pixelRect.max - Vector2.one);
            yield return new WaitForSecondsRealtime(.2f);
            var target = CreateHoverTarget("HoverLockA", new Vector2(.37f, .46f));
            Vector3 startPosition = camera.transform.position;
            Quaternion startRotation = camera.transform.rotation;
            Vector2 entryPointer = camera.WorldToScreenPoint(target.transform.position);
            ProcessPointer(entryPointer);
            Assert.That(focus.HoveredTarget, Is.SameAs(target), "첫 Raycast 즉시 검출");
            Assert.That(focus.MagneticTarget, Is.SameAs(target));
            Assert.That(look.IsHoverLocked, Is.True);
            Assert.That(focus.HasCameraControl, Is.False, "호버는 클릭 집중 상태와 구분한다");
            Assert.That(focus.CurrentTarget, Is.Null);
            AssertPose(startPosition, startRotation);
            yield return HoldScreenPointer(entryPointer, .05f);
            Assert.That(Vector3.Distance(camera.transform.position, startPosition), Is.GreaterThan(.0001f), "기존 즉시 고정과 달리 얕은 구도로 이동한다");
            Assert.That(Vector3.Distance(camera.transform.position, target.HoverPoint.position), Is.GreaterThan(.0001f), "호버 진입은 순간 이동하지 않는다");
            yield return HoldScreenPointer(entryPointer, .25f);
            Vector3 heldPosition = target.HoverPoint.position;
            Quaternion heldRotation = target.HoverPoint.rotation;
            AssertPose(heldPosition, heldRotation);
            Assert.That(camera.fieldOfView, Is.EqualTo(homeFov).Within(.01f), "호버는 클릭 집중 FOV를 적용하지 않는다");

            var collider = target.GetComponent<BoxCollider>();
            Vector2 paddedPoint = RightOfScreenBounds(collider, 3);
            Assert.That(camera.pixelRect.Contains(paddedPoint), Is.True);
            Assert.That(Physics.Raycast(camera.ScreenPointToRay(paddedPoint), 30, 1 << 31, QueryTriggerInteraction.Ignore), Is.False);
            int clicks = 0;
            target.Clicked += () => clicks++;
            for (int crossing = 0; crossing < 6; crossing++)
            {
                ProcessPointer(paddedPoint);
                Assert.That(focus.MagneticTarget, Is.SameAs(target), "경계나 보조 ray는 자석 대상만 유지한다");
                Assert.That(look.IsHoverLocked, Is.True);
                AssertPose(heldPosition, heldRotation);
                if (crossing == 0)
                {
                    input.Press(mouse.leftButton);
                    InputSystem.Update();
                    focus.SendMessage("Update");
                    input.Release(mouse.leftButton);
                    InputSystem.Update();
                    Assert.That(clicks, Is.Zero, "잠금 padding에서 대상 클릭을 발생시키지 않는다");
                }
                ProcessPointer(camera.WorldToScreenPoint(target.transform.position));
                yield return null;
                Assert.That(focus.HoveredTarget, Is.SameAs(target), "경계에서 재진입 " + crossing);
                Assert.That(look.IsHoverLocked, Is.True);
                AssertPose(heldPosition, heldRotation);
            }
            input.Press(mouse.leftButton);
            InputSystem.Update();
            focus.SendMessage("Update");
            input.Release(mouse.leftButton);
            InputSystem.Update();
            Assert.That(clicks, Is.EqualTo(1), "실제 Collider 클릭만 전달한다");

            yield return HoldScreenPointer(camera.pixelRect.min + Vector2.one * 10, .4f);
            Assert.That(focus.HoveredTarget, Is.Null);
            Assert.That(focus.MagneticTarget, Is.Null);
            Assert.That(look.IsHoverLocked, Is.False, "대상 이탈 후 자유 시차로 복귀한다");
            Assert.That(Vector3.Distance(camera.transform.position, heldPosition), Is.GreaterThan(.001f));
        }

        [UnityTest]
        public IEnumerator SwitchingMagneticTargetsAndInvalidationDoNotBlockFocus()
        {
            var first = CreateHoverTarget("HoverLockA", new Vector2(.35f, .45f));
            var second = CreateHoverTarget("HoverLockB", new Vector2(.65f, .55f));
            Vector2 firstPointer = camera.WorldToScreenPoint(first.transform.position);
            ProcessPointer(firstPointer);
            yield return HoldScreenPointer(firstPointer, .25f);
            Assert.That(focus.HoveredTarget, Is.SameAs(first));
            Assert.That(look.IsHoverLocked, Is.True);
            Vector3 heldPosition = camera.transform.position;
            Quaternion heldRotation = camera.transform.rotation;
            Vector2 secondPointer = camera.WorldToScreenPoint(second.transform.position);
            ProcessPointer(secondPointer);
            Assert.That(focus.HoveredTarget, Is.SameAs(second), "새 대상은 해제 지연 없이 전환한다");
            Assert.That(focus.MagneticTarget, Is.SameAs(second));
            Assert.That(first.IsHighlighted, Is.False);
            Assert.That(look.IsHoverLocked, Is.True);
            AssertPose(heldPosition, heldRotation);
            yield return HoldScreenPointer(secondPointer, .04f);
            Vector3 interruptedPosition = camera.transform.position;
            Quaternion interruptedRotation = camera.transform.rotation;
            Assert.That(Vector3.Distance(interruptedPosition, heldPosition), Is.GreaterThan(.0001f));
            firstPointer = camera.WorldToScreenPoint(first.transform.position);
            ProcessPointer(firstPointer);
            Assert.That(focus.MagneticTarget, Is.SameAs(first), "두 번째 대상에 도착하기 전 즉시 재지정한다");
            AssertPose(interruptedPosition, interruptedRotation);
            yield return HoldScreenPointer(firstPointer, .04f);
            interruptedPosition = camera.transform.position;
            interruptedRotation = camera.transform.rotation;
            secondPointer = camera.WorldToScreenPoint(second.transform.position);
            ProcessPointer(secondPointer);
            Assert.That(focus.MagneticTarget, Is.SameAs(second), "빠른 A-B-A-B 이동도 마지막 대상이 우선한다");
            AssertPose(interruptedPosition, interruptedRotation);
            yield return HoldScreenPointer(secondPointer, .25f);
            AssertPose(second.HoverPoint.position, second.HoverPoint.rotation);

            second.SetInputEnabled(false);
            yield return null;
            Assert.That(focus.HoveredTarget, Is.Null);
            Assert.That(look.IsHoverLocked, Is.False, "입력 차단은 padding이나 해제 지연을 기다리지 않는다");
            second.SetInputEnabled(true);
            yield return Hover(second);
            second.enabled = false;
            yield return null;
            Assert.That(look.IsHoverLocked, Is.False, "비활성 대상 즉시 해제");
            second.enabled = true;
            yield return Hover(second);
            Object.Destroy(second.gameObject);
            yield return null;
            yield return null;
            Assert.That(look.IsHoverLocked, Is.False, "파괴된 대상 즉시 해제");

            yield return Hover(first);
            focus.SendMessage("OnApplicationFocus", false);
            Assert.That(look.IsHoverLocked, Is.False, "창 포커스 상실 즉시 해제");
            focus.SendMessage("OnApplicationFocus", true);
            yield return Hover(first);
            PointAt(camera.pixelRect.max + Vector2.one * 20);
            yield return null;
            Assert.That(look.IsHoverLocked, Is.False, "뷰포트 밖에서는 해제 지연을 적용하지 않는다");

            yield return Hover(first);
            Assert.That(focus.Focus(first), Is.True);
            Assert.That(look.IsHoverLocked, Is.False);
            Assert.That(focus.MagneticTarget, Is.Null);
            yield return new WaitForSecondsRealtime(first.TransitionDuration + .1f);
            Assert.That(Vector3.Distance(camera.transform.position, first.FocusPoint.position), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, first.FocusPoint.rotation), Is.LessThan(.02f));
            PointAt(camera.pixelRect.max + Vector2.one * 20);
            focus.ReturnToOverview();
            Assert.That(look.IsHoverLocked, Is.False);
            yield return new WaitForSecondsRealtime(.85f);
            AssertHome();

            yield return Hover(first);
            focus.enabled = false;
            Assert.That(look.IsHoverLocked, Is.False, "카메라 컴포넌트 비활성화 즉시 해제");
        }

        [UnityTest]
        public IEnumerator EntryRayMaintainsIncumbentDuringCameraMotionButClicksUseOnlyTheCurrentRay()
        {
            var first = CreateHoverTarget("ReferenceHoverA", new Vector2(.4f, .5f));
            first.GetComponent<BoxCollider>().size = new Vector3(.025f, .025f, .04f);
            first.HoverPoint.SetPositionAndRotation(camera.transform.position + camera.transform.right * .12f, camera.transform.rotation);
            Physics.SyncTransforms();
            Vector2 pointer = camera.WorldToScreenPoint(first.transform.position);
            int firstClicks = 0, secondClicks = 0;
            first.Clicked += () => firstClicks++;
            ProcessPointer(pointer);
            yield return HoldScreenPointer(pointer, .3f);
            AssertPose(first.HoverPoint.position, first.HoverPoint.rotation);
            Assert.That(focus.MagneticTarget, Is.SameAs(first), "포인터가 정지한 채 카메라만 움직여도 진입 ray의 대상을 유지한다");
            Assert.That(focus.HoveredTarget, Is.SameAs(first));
            Assert.That(Physics.Raycast(camera.ScreenPointToRay(pointer), 30, 1 << 31, QueryTriggerInteraction.Ignore), Is.False,
                "현재 카메라의 실제 ray는 좁은 원래 Collider를 벗어났다");
            input.Press(mouse.leftButton);
            InputSystem.Update();
            focus.SendMessage("Update");
            Assert.That(firstClicks, Is.Zero, "보조 ray만 유지하는 대상에는 클릭을 전달하지 않는다");
            input.Release(mouse.leftButton);
            InputSystem.Update();

            Vector3 viewport = camera.ScreenToViewportPoint(pointer);
            var second = CreateHoverTarget("ReferenceHoverB", new Vector2(viewport.x, viewport.y));
            second.GetComponent<BoxCollider>().size = new Vector3(.025f, .025f, .04f);
            second.Clicked += () => secondClicks++;
            Physics.SyncTransforms();
            ProcessPointer(pointer);
            Assert.That(focus.MagneticTarget, Is.SameAs(first), "카메라 이동으로 생긴 이웃 hit는 정지 포인터의 대상을 바꾸지 않는다");
            Assert.That(Physics.Raycast(camera.ScreenPointToRay(pointer), out RaycastHit directHit, 30, 1 << 31,
                QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(directHit.collider.GetComponent<TabletopFocusTarget>(), Is.SameAs(second));
            input.Press(mouse.leftButton);
            InputSystem.Update();
            focus.SendMessage("Update");
            input.Release(mouse.leftButton);
            InputSystem.Update();
            Assert.That(secondClicks, Is.EqualTo(1), "클릭은 자석 대상이 아닌 현재 화면의 exact hit에만 전달한다");
            Assert.That(firstClicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AutomaticHoverUsesCameraAxisOffsetsAndKeepsFocusPointAndFovIndependent()
        {
            ProcessPointer(camera.pixelRect.max + Vector2.one * 10);
            yield return HoldScreenPointer(camera.pixelRect.max + Vector2.one * 10, .3f);
            var target = CreateHoverTarget("AutomaticHover", new Vector2(.65f, .55f));
            var serialized = new SerializedObject(target);
            serialized.FindProperty("hoverPoint").objectReferenceValue = null;
            serialized.FindProperty("hoverStrength").floatValue = 0;
            serialized.FindProperty("hoverPositionOffset").vector3Value = new Vector3(.03f, .01f, .02f);
            serialized.FindProperty("hoverRotationOffset").vector2Value = new Vector2(-.6f, .8f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Vector3 focusPosition = target.FocusPoint.position;
            Quaternion focusRotation = target.FocusPoint.rotation;
            Vector2 pointer = camera.WorldToScreenPoint(target.transform.position);
            ProcessPointer(pointer);
            yield return HoldScreenPointer(pointer, .25f);
            AssertPose(homePosition + homeRotation * new Vector3(.03f, .01f, .02f),
                Quaternion.AngleAxis(.8f, Vector3.up) * homeRotation * Quaternion.AngleAxis(-.6f, Vector3.right));
            Assert.That(target.FocusPoint.position, Is.EqualTo(focusPosition));
            Assert.That(Quaternion.Angle(target.FocusPoint.rotation, focusRotation), Is.LessThan(.01f));
            Assert.That(camera.fieldOfView, Is.EqualTo(homeFov).Within(.01f));
            Assert.That(focus.HasCameraControl, Is.False);
        }

        [UnityTest]
        public IEnumerator FreeParallaxKeepsVisualHoverAndClicksOnlyTheCurrentVisibleCollider()
        {
            ConfigureFreeParallax();
            yield return HoldScreenPointer(camera.pixelRect.max + Vector2.one * 10, .4f);
            var target = CreateHoverTarget("FreeParallaxTarget", new Vector2(.72f, .65f));
            var visual = target.gameObject.AddComponent<SpriteRenderer>();
            Color originalColor = new Color(.4f, .5f, .6f, 1);
            visual.color = originalColor;
            var serializedTarget = new SerializedObject(target);
            serializedTarget.FindProperty("highlightRenderers").arraySize = 1;
            serializedTarget.FindProperty("highlightRenderers").GetArrayElementAtIndex(0).objectReferenceValue = visual;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
            int clicks = 0;
            target.Clicked += () => clicks++;
            Vector3 startPosition = camera.transform.position;
            Quaternion startRotation = camera.transform.rotation;
            Vector3 firstMovingPosition = startPosition;
            for (int frame = 0; frame < 6; frame++)
            {
                ProcessPointer(camera.WorldToScreenPoint(target.transform.position));
                Assert.That(focus.HoveredTarget, Is.SameAs(target));
                Assert.That(target.IsHighlighted, Is.True);
                Assert.That(visual.color.maxColorComponent, Is.GreaterThan(originalColor.maxColorComponent));
                Assert.That(focus.MagneticTarget, Is.Null);
                Assert.That(look.IsHoverLocked, Is.False);
                Assert.That(focus.HasCameraControl, Is.False);
                look.SendMessage("LateUpdate");
                if (frame == 0) firstMovingPosition = camera.transform.position;
                yield return null;
            }
            Assert.That(Vector3.Distance(startPosition, firstMovingPosition), Is.GreaterThan(.001f));
            Assert.That(Vector3.Distance(camera.transform.position, firstMovingPosition), Is.GreaterThan(.0001f),
                "대상을 가리키는 동안에도 자유 시차가 여러 프레임에 걸쳐 계속 움직인다");
            Assert.That(Quaternion.Angle(startRotation, camera.transform.rotation), Is.GreaterThan(.05f));

            ProcessPointer(camera.WorldToScreenPoint(target.transform.position));
            Assert.That(Physics.Raycast(camera.ScreenPointToRay(mouse.position.ReadValue()), out RaycastHit hit, 30,
                1 << 31, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(hit.collider.GetComponent<TabletopFocusTarget>(), Is.SameAs(target));
            input.Press(mouse.leftButton);
            InputSystem.Update();
            focus.SendMessage("Update");
            input.Release(mouse.leftButton);
            InputSystem.Update();
            Assert.That(clicks, Is.EqualTo(1), "움직이는 현재 카메라에서 실제 보이는 Collider 클릭을 전달한다");

            Vector2 outsideEdge = RightOfScreenBounds(target.GetComponent<BoxCollider>(), 3);
            Assert.That(camera.pixelRect.Contains(outsideEdge), Is.True);
            ProcessPointer(outsideEdge);
            Assert.That(Physics.Raycast(camera.ScreenPointToRay(outsideEdge), 30, 1 << 31,
                QueryTriggerInteraction.Ignore), Is.False);
            Assert.That(focus.HoveredTarget, Is.Null);
            Assert.That(target.IsHighlighted, Is.False);
            Assert.That(visual.color, Is.EqualTo(originalColor));
            input.Press(mouse.leftButton);
            InputSystem.Update();
            focus.SendMessage("Update");
            input.Release(mouse.leftButton);
            InputSystem.Update();
            Assert.That(clicks, Is.EqualTo(1), "경계 옆 빈 곳을 클릭해도 직전 대상을 다시 누르지 않는다");
            Assert.That(focus.MagneticTarget, Is.Null);
            Assert.That(look.IsHoverLocked, Is.False);

            serializedTarget.Update();
            serializedTarget.FindProperty("focusOnClick").boolValue = true;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
            ProcessPointer(camera.WorldToScreenPoint(target.transform.position));
            input.Press(mouse.leftButton);
            InputSystem.Update();
            focus.SendMessage("Update");
            input.Release(mouse.leftButton);
            InputSystem.Update();
            Assert.That(clicks, Is.EqualTo(2));
            Assert.That(focus.CurrentTarget, Is.SameAs(target));
            Assert.That(focus.HasCameraControl, Is.True);
            Assert.That(look.IsHoverLocked, Is.False);
            PointAt(camera.pixelRect.max + Vector2.one * 10);
            yield return new WaitForSecondsRealtime(target.TransitionDuration + .1f);
            AssertPose(target.FocusPoint.position, target.FocusPoint.rotation);
            Assert.That(camera.fieldOfView, Is.EqualTo(target.FieldOfView).Within(.01f));
            input.Press(mouse.rightButton);
            InputSystem.Update();
            focus.SendMessage("Update");
            input.Release(mouse.rightButton);
            InputSystem.Update();
            yield return new WaitForSecondsRealtime(.85f);
            AssertHome();
            Assert.That(focus.MagneticTarget, Is.Null);
            Assert.That(look.IsHoverLocked, Is.False);
        }

        [UnityTest]
        public IEnumerator MagneticCanBeComparedOnAndOffWithoutDisablingHoverOrFocus()
        {
            ConfigureFreeParallax();
            yield return HoldScreenPointer(camera.pixelRect.max + Vector2.one * 10, .4f);
            var target = CreateHoverTarget("MagneticComparison", new Vector2(.7f, .6f));
            var serializedFocus = new SerializedObject(focus);
            serializedFocus.FindProperty("magneticHover").boolValue = true;
            serializedFocus.ApplyModifiedPropertiesWithoutUndo();
            Vector2 pointer = camera.WorldToScreenPoint(target.transform.position);
            ProcessPointer(pointer);
            yield return HoldScreenPointer(pointer, .25f);
            Assert.That(focus.MagneticTarget, Is.SameAs(target));
            Assert.That(look.IsHoverLocked, Is.True);
            AssertPose(target.HoverPoint.position, target.HoverPoint.rotation);
            Vector3 magneticPosition = camera.transform.position;
            Quaternion magneticRotation = camera.transform.rotation;

            serializedFocus.Update();
            serializedFocus.FindProperty("magneticHover").boolValue = false;
            serializedFocus.ApplyModifiedPropertiesWithoutUndo();
            ProcessPointer(camera.WorldToScreenPoint(target.transform.position));
            Assert.That(focus.MagneticTarget, Is.Null);
            Assert.That(look.IsHoverLocked, Is.False);
            AssertPose(magneticPosition, magneticRotation);
            float elapsed = 0;
            while (elapsed < .4f)
            {
                ProcessPointer(camera.WorldToScreenPoint(target.transform.position));
                Assert.That(focus.HoveredTarget, Is.SameAs(target), "자석을 꺼도 실제 호버 강조는 유지한다");
                Assert.That(target.IsHighlighted, Is.True);
                Assert.That(focus.MagneticTarget, Is.Null);
                Assert.That(look.IsHoverLocked, Is.False);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            Assert.That(Vector3.Distance(camera.transform.position, magneticPosition), Is.GreaterThan(.001f));
            Assert.That(focus.HasCameraControl, Is.False);

            serializedFocus.Update();
            serializedFocus.FindProperty("magneticHover").boolValue = true;
            serializedFocus.ApplyModifiedPropertiesWithoutUndo();
            pointer = camera.WorldToScreenPoint(target.transform.position);
            ProcessPointer(pointer);
            yield return HoldScreenPointer(pointer, .25f);
            Assert.That(focus.MagneticTarget, Is.SameAs(target));
            Assert.That(look.IsHoverLocked, Is.True);
            AssertPose(target.HoverPoint.position, target.HoverPoint.rotation);
        }

        private void ConfigureFreeParallax()
        {
            Time.timeScale = 1;
            var serializedLook = new SerializedObject(look);
            serializedLook.FindProperty("horizontalDegrees").floatValue = 4;
            serializedLook.FindProperty("verticalDegrees").floatValue = 2.5f;
            serializedLook.FindProperty("horizontalTravel").floatValue = .25f;
            serializedLook.FindProperty("verticalTravel").floatValue = .12f;
            serializedLook.FindProperty("response").floatValue = 18;
            serializedLook.FindProperty("deadZone").floatValue = .02f;
            serializedLook.ApplyModifiedPropertiesWithoutUndo();
            var serializedFocus = new SerializedObject(focus);
            serializedFocus.FindProperty("magneticHover").boolValue = false;
            serializedFocus.ApplyModifiedPropertiesWithoutUndo();
        }

        private TabletopFocusTarget CreateHoverTarget(string name, Vector2 viewport)
        {
            if (!hoverFixtures)
            {
                hoverFixtures = new GameObject("HoverLockTestFixtures");
                var cameraSettings = new SerializedObject(focus);
                cameraSettings.FindProperty("interactionLayers").intValue = 1 << 31;
                cameraSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            var targetObject = new GameObject(name);
            targetObject.layer = 31;
            targetObject.transform.SetParent(hoverFixtures.transform, false);
            targetObject.transform.SetPositionAndRotation(camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, 2)),
                camera.transform.rotation);
            targetObject.AddComponent<BoxCollider>().size = new Vector3(.24f, .24f, .08f);
            var point = new GameObject("FocusPoint").transform;
            point.SetParent(targetObject.transform, false);
            point.SetPositionAndRotation(camera.transform.position + camera.transform.right * .1f, camera.transform.rotation);
            var hoverPoint = new GameObject("HoverPoint").transform;
            hoverPoint.SetParent(targetObject.transform, false);
            hoverPoint.SetPositionAndRotation(camera.transform.position + camera.transform.right * ((viewport.x - .5f) * .16f)
                + camera.transform.up * ((viewport.y - .5f) * .08f) + camera.transform.forward * .025f,
                Quaternion.AngleAxis((viewport.x - .5f) * 2, Vector3.up) * camera.transform.rotation
                * Quaternion.AngleAxis(-(viewport.y - .5f) * 2, Vector3.right));
            var target = targetObject.AddComponent<TabletopFocusTarget>();
            var serialized = new SerializedObject(target);
            serialized.FindProperty("focusPoint").objectReferenceValue = point;
            serialized.FindProperty("hoverPoint").objectReferenceValue = hoverPoint;
            serialized.FindProperty("focusOnClick").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();
            return target;
        }

        private IEnumerator Hover(TabletopFocusTarget target)
        {
            focus.SendMessage("OnApplicationFocus", true);
            look.SendMessage("OnApplicationFocus", true);
            for (int frame = 0; frame < 3; frame++)
            {
                ProcessPointer(camera.WorldToScreenPoint(target.transform.position));
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(target));
            Assert.That(focus.MagneticTarget, Is.SameAs(target));
            Assert.That(look.IsHoverLocked, Is.True);
        }

        private void ProcessPointer(Vector2 pointer)
        {
            focus.SendMessage("OnApplicationFocus", true);
            look.SendMessage("OnApplicationFocus", true);
            PointAt(pointer);
            // UnityTest의 Set는 큐에만 넣는다. 수동 Update 검사 전에 실제 장치 상태를 갱신한다.
            InputSystem.Update();
            focus.SendMessage("Update");
        }

        private IEnumerator HoldScreenPointer(Vector2 pointer, float seconds)
        {
            float elapsed = 0;
            float deadline = Time.realtimeSinceStartup + 3;
            while (elapsed < seconds && Time.realtimeSinceStartup < deadline)
            {
                ProcessPointer(pointer);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(seconds), "카메라 보간 업데이트 시간 확보");
        }

        private Vector2 RightOfScreenBounds(Collider collider, float pixels)
        {
            Bounds bounds = collider.bounds;
            float right = float.NegativeInfinity;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                right = Mathf.Max(right, camera.WorldToScreenPoint(corner).x);
            }
            return new Vector2(right + pixels, camera.WorldToScreenPoint(bounds.center).y);
        }

        private void AssertPose(Vector3 position, Quaternion rotation)
        {
            Assert.That(Vector3.Distance(camera.transform.position, position), Is.LessThan(.00001f), "명령 직후 연속성 또는 보간 완료 후 정확한 위치");
            Assert.That(Quaternion.Angle(camera.transform.rotation, rotation), Is.LessThan(.01f), "명령 직후 연속성 또는 보간 완료 후 정확한 회전");
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
