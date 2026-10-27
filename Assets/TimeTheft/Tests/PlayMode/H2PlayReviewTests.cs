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
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class H2PlayReviewTests
    {
        private InputTestFixture input;
        private TimeTheftGameController controller;
        private TimeTheftSettings settings, originalSettings;
        private H2BatchRevealPresentation reveal;
        private TabletopFocusCamera focus;
        private Camera camera;
        private float oldTime;

        [UnitySetUp] public IEnumerator Load()
        {
            oldTime = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var mouse = InputSystem.AddDevice<Mouse>("H2PlayReviewMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            originalSettings = controller.Settings;
            settings = UnityEngine.Object.Instantiate(originalSettings);
            settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.InitialLifeHours = 100;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.Rules.EnableAiItemStrategy = false;
            settings.AiThinkDuration = 10;
            settings.PreviewDuration = settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = .08f;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller);
            reveal = controller.View.BatchReveal;
            focus = controller.View.FocusCamera; camera = focus.GetComponent<Camera>();
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false;
            if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTime;
            yield return null;
            input?.TearDown();
        }

        [UnityTest] public IEnumerator ActualDialSilhouettesRemainReadableAndSeparateAt720p()
        {
            settings.PreviewDuration = 20;
            controller.RestartGame();
            yield return Wait(() => reveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing);
            var previous = camera.targetTexture; float aspect = camera.aspect;
            var target = RenderTexture.GetTemporary(1280, 720, 24);
            try
            {
                camera.targetTexture = target; camera.aspect = 16f / 9;
                var silhouettes = new List<Vector2[]>();
                for (int i = 0; i < 5; i++)
                {
                    var dial = controller.View.Clocks[i].H2Presentation.Watch.GetComponentsInChildren<MeshFilter>()
                        .Single(filter => filter.name == "Dial");
                    var mesh = dial.sharedMesh;
                    Assert.That(mesh.isReadable, Is.True, "원본 H2 문자판 메시를 직접 검사한다");
                    var world = mesh.vertices.Select(dial.transform.TransformPoint).ToArray();
                    var projected = world.Select(camera.WorldToScreenPoint).ToArray();
                    foreach (var point in projected)
                    {
                        Assert.That(point.z, Is.GreaterThan(0), "문자판이 카메라 앞에 있다: " + i);
                        Assert.That(point.x, Is.InRange(25.6f, 1254.4f), "문자판 좌우 잘림: " + i);
                        Assert.That(point.y, Is.InRange(14.4f, 705.6f), "문자판 상하 잘림: " + i);
                    }
                    var hull = ConvexHull(projected.Select(point => (Vector2)point));
                    float minor = MinimumWidth(hull);
                    var center = world.Aggregate(Vector3.zero, (sum, point) => sum + point) / world.Length;
                    float facing = Mathf.Abs(Vector3.Dot(FaceNormal(world, mesh.triangles),
                        (camera.transform.position - center).normalized));
                    TestContext.WriteLine($"Dial {i}: 실제 실루엣 최소 폭 {minor:F1}px / 정면 내적 {facing:F3}");
                    // 기존 가림 마스크 검사(.93)는 유지하고, 옆으로 선 문자판을 별도로 검출한다.
                    Assert.That(minor, Is.GreaterThanOrEqualTo(90), "720p 문자판 최소 읽기 폭: " + i);
                    Assert.That(facing, Is.GreaterThan(.75f), "문자판이 과도하게 옆으로 기울었다: " + i);
                    silhouettes.Add(hull);
                }
                for (int i = 0; i < silhouettes.Count; i++)
                    for (int j = i + 1; j < silhouettes.Count; j++)
                        Assert.That(Overlap(silhouettes[i], silhouettes[j]), Is.False, $"문자판 실루엣 겹침: {i}/{j}");
            }
            finally
            {
                camera.targetTexture = previous; camera.aspect = aspect;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        [UnityTest] public IEnumerator FullObservationBudgetAndCorrectedPoseReturnWithoutChangingSourceSettings()
        {
            Assert.That(originalSettings.PreviewDuration, Is.EqualTo(5.5f));
            settings.PreviewDuration = originalSettings.PreviewDuration;
            settings.Rules.InitialItemCount = 1;
            controller.RestartGame();
            yield return Wait(() => reveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing);
            Assert.That(controller.View.WorldItems.PlayerItems.Count, Is.EqualTo(1));
            AssertWorldItemsVisible(false);
            foreach (var target in new[] { controller.View.Lifecycle.Carrier, controller.View.Lifecycle.Hatch.gameObject })
            {
                var sampler = target.GetComponent<Animator>();
                Assert.That(sampler, Is.Not.Null, "독립 Player의 non-Legacy 샘플링에 필요한 Animator");
                Assert.That(sampler.enabled, Is.False);
                Assert.That(sampler.runtimeAnimatorController, Is.Null);
            }
            var roots = controller.View.Clocks.Select(clock => clock.H2Presentation.MotionRoot).ToArray();
            var positions = roots.Select(root => root.position).ToArray();
            var rotations = roots.Select(root => root.rotation).ToArray();
            var cameraPosition = camera.transform.position; var cameraRotation = camera.transform.rotation;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(reveal.Phase, Is.EqualTo(H2BatchRevealPresentation.RevealPhase.Observing));
            for (int i = 0; i < roots.Length; i++)
            {
                Assert.That(Vector3.Distance(roots[i].position, positions[i]), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(roots[i].rotation, rotations[i]), Is.LessThan(.01f));
            }
            Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(Quaternion.Angle(camera.transform.rotation, cameraRotation), Is.LessThan(.001f));
            yield return Wait(() => reveal.Phase == H2BatchRevealPresentation.RevealPhase.Closing, 7);
            Assert.That(reveal.ClosingStarted - reveal.ReadableSince, Is.InRange(5.49f, 5.68f));
            // 닫힘 클립에 관찰 보정을 다시 누적하거나, 닫기 직전 보정을 갑자기 제거하지 않는다.
            for (int i = 0; i < roots.Length; i++)
            {
                Assert.That(Vector3.Distance(roots[i].position, positions[i]), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(roots[i].rotation, rotations[i]), Is.LessThan(.01f));
            }
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingItems);
            foreach (var clock in controller.View.Clocks)
            {
                Assert.That(clock.H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(Quaternion.Angle(clock.H2Presentation.MotionRoot.localRotation, Quaternion.identity), Is.LessThan(.001f));
                Assert.That(clock.H2Presentation.IsOpen, Is.False);
                Assert.That(clock.H2Presentation.DisplayedHours, Is.Null);
                Assert.That(clock.H2Presentation.IsRed, Is.False);
            }
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
            Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
            Assert.That(Quaternion.Angle(camera.transform.rotation, focus.OverviewRotation), Is.LessThan(.001f));
            Assert.That(originalSettings.PreviewDuration, Is.EqualTo(5.5f));
            AssertWorldItemsVisible(true);
            controller.EndItemPhase();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            controller.SelectClock(0);
            yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(controller.View.TurnPresentation.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.DecisionHold));
            AssertWorldItemsVisible(true);
        }

        [UnityTest] public IEnumerator SettlementDeltasRemainReadableThenClearAtTheRealRemovalBoundary()
        {
            var motion = controller.View.TurnPresentation; var lifecycle = controller.View.Lifecycle;
            motion.TurnCloseDuration = .18f;
            lifecycle.MoveDuration = .2f;
            Assert.That(settings.ResultHoldDuration, Is.EqualTo(originalSettings.ResultHoldDuration));
            Assert.That(settings.ResultHoldDuration, Is.GreaterThanOrEqualTo(1), "결과 관찰 시간을 줄여 잔류 문구 문제를 감추지 않는다");
            var serialized = new SerializedObject(controller.View);
            var deltas = new[] { "playerDelta", "aiDelta", "bankDelta" }
                .Select(name => (TextMeshPro)serialized.FindProperty(name).objectReferenceValue).ToArray();
            controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            controller.SelectClock(0);
            yield return GameUiTestInput.ClockMotionReady(controller);
            controller.ChooseAction(TimeAction.Ration);
            yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.SettlementHold);
            float readableAt = Time.realtimeSinceStartup;
            var balances = Balances();
            Assert.That(controller.Match.UsedCount, Is.EqualTo(1));
            AssertDeltasVisible(deltas);
            Assert.That(controller.View.PlayerLife.text, Is.EqualTo(GameTime.Format(balances[0])));
            Assert.That(controller.View.AiLife.text, Is.EqualTo(GameTime.Format(balances[1])));
            Assert.That(controller.View.Bank.text, Is.EqualTo(GameTime.Format(balances[2])));
            int closed = 0, removed = 0, removedIndex = -1;
            bool readableAtClose = false, clearedAtRemoval = false, hiddenAtRemoval = false, discarded = true;
            float removedAt = 0;
            Action<int> onClose = index =>
            {
                closed++;
                readableAtClose = index == 0 && deltas.Count(label => label.gameObject.activeSelf && !string.IsNullOrEmpty(label.text)) >= 2;
            };
            Action<int, bool> onRemove = (index, isDiscarded) =>
            {
                removed++; removedIndex = index; discarded = isDiscarded; removedAt = Time.realtimeSinceStartup;
                clearedAtRemoval = deltas.All(label => label.text == "" && !label.gameObject.activeSelf);
                var model = controller.View.Clocks[index].H2Presentation;
                hiddenAtRemoval = !model.IsOpen && !model.IsRed && !model.DisplayedHours.HasValue;
            };
            motion.CloseCompleted += onClose; lifecycle.RemoveStarted += onRemove;
            try
            {
                yield return new WaitForSecondsRealtime(settings.ResultHoldDuration / 2);
                Assert.That(motion.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.SettlementHold));
                AssertDeltasVisible(deltas);
                yield return Wait(() => removed == 1);
                Assert.That(closed, Is.EqualTo(1)); Assert.That(readableAtClose, Is.True);
                Assert.That(removedIndex, Is.Zero); Assert.That(discarded, Is.False);
                Assert.That(removedAt - readableAt, Is.GreaterThanOrEqualTo(settings.ResultHoldDuration));
                Assert.That(clearedAtRemoval, Is.True, "LockLifecycleInput 이후 실제 Remove 경계에서 모든 Delta를 지운다");
                Assert.That(hiddenAtRemoval, Is.True);
                Assert.That(controller.Match.UsedCount, Is.EqualTo(1));
                Assert.That(Balances(), Is.EqualTo(balances));
            }
            finally
            {
                motion.CloseCompleted -= onClose; lifecycle.RemoveStarted -= onRemove;
            }
        }

        private int[] Balances() => new[] { controller.Match.PlayerLifeMinutes, controller.Match.AiLifeMinutes, controller.Match.BankMinutes };
        private static void AssertDeltasVisible(TextMeshPro[] labels) =>
            Assert.That(labels.Count(label => label.gameObject.activeSelf && !string.IsNullOrEmpty(label.text)), Is.GreaterThanOrEqualTo(2));
        private void AssertWorldItemsVisible(bool visible)
        {
            Assert.That(controller.View.WorldItems.PlayerAnchor.gameObject.activeSelf, Is.EqualTo(visible));
            Assert.That(controller.View.WorldItems.OpponentAnchor.gameObject.activeSelf, Is.EqualTo(visible));
        }

        private IEnumerator Wait(Func<bool> condition, float timeout = 5)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, controller.Match.Stage + " / " + reveal.Phase + " / " + controller.View.TurnPresentation.Phase);
        }

        private static Vector3 FaceNormal(Vector3[] vertices, int[] triangles)
        {
            var largest = Vector3.zero;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var normal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]);
                if (normal.sqrMagnitude > largest.sqrMagnitude) largest = normal;
            }
            Assert.That(largest.sqrMagnitude, Is.GreaterThan(0));
            return largest.normalized;
        }

        private static Vector2[] ConvexHull(IEnumerable<Vector2> points)
        {
            var sorted = points.Distinct().OrderBy(point => point.x).ThenBy(point => point.y).ToArray();
            Assert.That(sorted.Length, Is.GreaterThanOrEqualTo(3));
            var hull = new List<Vector2>();
            foreach (var point in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lower = hull.Count;
            for (int i = sorted.Length - 2; i >= 0; i--)
            {
                while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], sorted[i] - hull[hull.Count - 1]) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(sorted[i]);
            }
            hull.RemoveAt(hull.Count - 1);
            Assert.That(hull.Count, Is.GreaterThanOrEqualTo(3));
            return hull.ToArray();
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static IEnumerable<Vector2> Axes(Vector2[] hull)
        {
            for (int i = 0; i < hull.Length; i++)
            {
                var edge = hull[(i + 1) % hull.Length] - hull[i];
                yield return new Vector2(-edge.y, edge.x).normalized;
            }
        }
        private static float MinimumWidth(Vector2[] hull) => Axes(hull)
            .Min(axis => hull.Max(point => Vector2.Dot(point, axis)) - hull.Min(point => Vector2.Dot(point, axis)));
        private static bool Overlap(Vector2[] a, Vector2[] b) => Axes(a).Concat(Axes(b)).All(axis =>
            a.Max(point => Vector2.Dot(point, axis)) > b.Min(point => Vector2.Dot(point, axis)) &&
            b.Max(point => Vector2.Dot(point, axis)) > a.Min(point => Vector2.Dot(point, axis)));
    }
}
#endif
