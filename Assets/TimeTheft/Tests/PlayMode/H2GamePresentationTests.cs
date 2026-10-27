#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    public sealed class H2GamePresentationTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private Keyboard keyboard;
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private Camera camera;
        private TabletopFocusCamera focus;
        private float oldTime;
        private const string Output = "Temp/H2GameIntegration/Captures/";

        [UnitySetUp] public IEnumerator Load()
        {
            oldTime = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("H2GameMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("H2GameKeyboard");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            GameUiTestInput.ShortenBatchReveal(controller);
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.RandomSeed = 635;
            settings.Rules.InitialLifeHours = 100;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.PreviewDuration = .8f;
            settings.ShuffleDuration = settings.TransferDuration = settings.AiThinkDuration = settings.ResultHoldDuration = .08f;
            settings.RevealDuration = .8f; settings.DiscardDuration = .2f;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            camera = Camera.main; focus = controller.View.FocusCamera;
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
            Directory.CreateDirectory(Output);
            controller.RestartGame();
            yield return null;
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

        [UnityTest] public IEnumerator FiveRealClicksKeepHiddenHandsAndRevealOnlyCommittedValues()
        {
            bool normal = false, red = false;
            var ids = controller.View.Clocks.Select(c => c.GetInstanceID()).ToArray();
            yield return Wait(() => controller.View.BatchReveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing);
            AssertPresentation();
            yield return Capture("five-open.png");
            for (int i = 0; i < 5; i++)
            {
                if (i > 0) controller.RestartGame();
                yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                AssertPresentation();
                if (i == 0) yield return Capture("five-closed.png");
                yield return ClickClock(i);
                AssertPresentation();
                if (i == 0) yield return Capture("selected-closed.png");
                var selected = controller.View.Clocks[i];
                Assert.That(selected.H2Presentation.DisplayedHours, Is.Null);
                Assert.That(selected.H2Presentation.IsRed, Is.False);
                Assert.That(Quaternion.Angle(selected.H2Presentation.Watch.LidPivot.localRotation, Quaternion.identity), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(selected.H2Presentation.Watch.HourHand.localRotation, Quaternion.identity), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(selected.H2Presentation.Watch.MinuteHand.localRotation, Quaternion.identity), Is.LessThan(.01f));
                yield return GameUiTestInput.Click(input, mouse, i % 2 == 0 ? controller.View.CollectButton : controller.View.RationButton,
                    null, () => AssertPresentation());
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.Revealing));
                bool isRed = controller.Match.GetClock(i).Face.Value.Kind == ClockKind.Red;
                if (isRed && !red) { red = true; yield return Capture("red-result.png"); }
                if (!isRed && !normal) { normal = true; yield return Capture("normal-result.png"); }
                Assert.That(controller.View.Clocks.Select(c => c.GetInstanceID()),
                    Is.EqualTo(controller.Match.LastShuffle.DestinationToSource.Select(source => ids[source])));
            }
            Assert.That(normal && red, Is.True, "시드의 실제 셔플 결과에서 일반과 빨강 모두 확인");
        }

        [UnityTest] public IEnumerator TwoBatchesDiscardWithoutOpeningAndRestartResetsAllFiveModels()
        {
            int used = 0, discarded = 0, resets = 0;
            int previousBatch = 0;
            float deadline = Time.realtimeSinceStartup + 40;
            while (controller.Match.Batch < 3 && Time.realtimeSinceStartup < deadline)
            {
                AssertPresentation();
                var match = controller.Match;
                if (match.Batch != previousBatch) { previousBatch = match.Batch; resets++; }
                used = Mathf.Max(used, match.UsedCount);
                if (match.Stage == TurnStage.BatchComplete)
                {
                    foreach (var clock in controller.View.Clocks)
                        if (clock.H2Presentation.Visibility == ClockVisibility.Discarded)
                        { discarded++; Assert.That(clock.H2Presentation.DisplayedHours, Is.Null); Assert.That(clock.H2Presentation.IsOpen, Is.False); }
                }
                if (match.Stage == TurnStage.AwaitingClock && match.ActiveParticipant == Participant.Player)
                {
                    yield return ClickClock(match.GetAvailableClockIndices().Last());
                    yield return GameUiTestInput.Click(input, mouse, controller.View.RationButton);
                }
                yield return null;
            }
            Assert.That(controller.Match.Batch, Is.EqualTo(3));
            Assert.That(used, Is.EqualTo(4)); Assert.That(discarded, Is.GreaterThan(0)); Assert.That(resets, Is.GreaterThanOrEqualTo(2));
            controller.RestartGame(); yield return null;
            yield return Wait(() => controller.View.BatchReveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing);
            Assert.That(controller.Match.Batch, Is.EqualTo(1));
            Assert.That(controller.Match.UsedCount, Is.EqualTo(0));
            AssertPresentation();
            Assert.That(controller.View.Clocks.All(c => c.gameObject.activeSelf && !c.IsSelected && c.H2Presentation.IsOpen), Is.True);
        }

        [UnityTest] public IEnumerator FreeParallaxKeepsBodyRaycastsAndCounterFocusReturn()
        {
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            var home = camera.transform.position;
            input.Set(mouse.position, new Vector2(camera.pixelRect.xMin + camera.pixelWidth * .9f, camera.pixelRect.yMin + camera.pixelHeight * .6f));
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Vector3.Distance(camera.transform.position, home), Is.GreaterThan(.001f));
            Physics.SyncTransforms();
            foreach (var clock in controller.View.Clocks)
            {
                RaycastHit hit;
                Assert.That(Physics.Raycast(camera.ScreenPointToRay(camera.WorldToScreenPoint(clock.GetComponent<Collider>().bounds.center)), out hit, 30), Is.True);
                Assert.That(hit.collider, Is.SameAs(clock.GetComponent<Collider>()));
            }
            yield return Capture("free-parallax.png");
            var counter = controller.View.PlayerLife.GetComponentInParent<TabletopFocusTarget>();
            Assert.That(focus.Focus(counter), Is.True);
            yield return Wait(() => !focus.IsTransitioning);
            Assert.That(focus.CurrentTarget, Is.SameAs(counter));
            input.Press(keyboard.escapeKey); yield return null; input.Release(keyboard.escapeKey);
            yield return Wait(() => !focus.HasCameraControl);
            Assert.That(controller.View.Clocks.All(c => !c.H2Presentation.IsOpen), Is.True);
            var adapter = controller.View.Clocks[0].H2Presentation;
            adapter.enabled = false;
            Assert.That(adapter.Watch.gameObject.activeSelf, Is.False);
            Assert.That(controller.View.Clocks[0].transform.Find("Visual/Case").GetComponent<Renderer>().enabled, Is.True);
            adapter.enabled = true;
            adapter.Apply(controller.Match.GetClock(0));
            Assert.That(controller.View.Clocks[0].transform.Find("Visual/Case").GetComponent<Renderer>().enabled, Is.False);
        }

        [UnityTest] public IEnumerator MainSceneMetalRespondsToFreeParallaxWithVisiblePixelHighlights()
        {
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            controller.StopAllCoroutines();
            // 같은 UV ID의 다른 인스턴스를 섞지 않고 실제 중앙 시계의 같은 셀을 추적한다.
            var renderers = controller.View.Clocks[2].H2Presentation.Materials.MetalRenderers;
            var layers = renderers.Select(r => r.gameObject.layer).ToArray();
            int mask = camera.cullingMask; var background = camera.backgroundColor;
            try
            {
                foreach (var r in renderers) r.gameObject.layer = 31;
                camera.cullingMask = 1 << 31; camera.backgroundColor = Color.clear;
                var ids = ReadMetal(renderers, 6); var pbr = ReadMetal(renderers, 1);
                var broad = ReadMetal(renderers, 3); var glint = ReadMetal(renderers, 4);
                var before = CellColors(ids, pbr);
                input.Set(mouse.position, new Vector2(camera.pixelWidth * .94f, camera.pixelHeight * .63f));
                yield return new WaitForSecondsRealtime(.5f);
                var moved = CellColors(ReadMetal(renderers, 6), ReadMetal(renderers, 1));
                int common = 0, changed = 0;
                foreach (var pair in before)
                    if (moved.TryGetValue(pair.Key, out Color color))
                    { common++; if (Mathf.Abs(pair.Value.r - color.r) + Mathf.Abs(pair.Value.g - color.g) + Mathf.Abs(pair.Value.b - color.b) > .001f) changed++; }
                int visible = ids.Count(c => c.a > .99f), highlights = glint.Count(c => c.a > .99f && c.r >= 10f / 12f - .001f);
                int[] histogram = new int[13];
                for (int i = 0; i < ids.Length; i++) if (ids[i].a > .99f) histogram[Mathf.Clamp(Mathf.RoundToInt(broad[i].r * 12), 0, 12)]++;
                File.WriteAllText("Temp/H2GameIntegration/main-metal-gpu.json", "{\"width\":1280,\"height\":720,\"metalPixels\":" + visible
                    + ",\"visibleCoarseCells\":" + before.Count + ",\"commonCoarseCells\":" + common + ",\"viewDependentPbrChangedCells\":" + changed
                    + ",\"uv96GlintPixels\":" + highlights + ",\"baseIndexHistogram\":[" + string.Join(",", histogram) + "]}");
                Assert.That(common, Is.GreaterThan(300)); Assert.That(changed, Is.GreaterThan(20));
                Assert.That(highlights, Is.GreaterThan(10));
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) { renderers[i].gameObject.layer = layers[i]; renderers[i].SetPropertyBlock(null); }
                camera.cullingMask = mask; camera.backgroundColor = background;
            }
        }

        private static Dictionary<int, Color> CellColors(Color[] ids, Color[] colors)
        {
            var result = new Dictionary<int, Color>();
            for (int p = 0; p < ids.Length; p++) if (ids[p].a > .99f)
            {
                int key = Mathf.RoundToInt(ids[p].b * 16) * 65536 + Mathf.RoundToInt(ids[p].r * 65535);
                if (!result.ContainsKey(key)) result.Add(key, colors[p]);
            }
            return result;
        }

        private Color[] ReadMetal(Renderer[] renderers, int debug)
        {
            var block = new MaterialPropertyBlock(); block.SetFloat("_DebugView", debug);
            foreach (var renderer in renderers) renderer.SetPropertyBlock(block);
            var target = camera.targetTexture; var active = RenderTexture.active; bool msaa = camera.allowMSAA;
            var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var image = new Texture2D(1280, 720, TextureFormat.RGBAFloat, false, true);
            try
            {
                camera.allowMSAA = false; camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); return image.GetPixels();
            }
            finally
            { camera.targetTexture = target; camera.allowMSAA = msaa; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(image); }
        }

        private void AssertPresentation()
        {
            var match = controller.Match;
            if (match.Stage == TurnStage.BatchPreview && controller.View.BatchReveal.Phase != H2BatchRevealPresentation.RevealPhase.Observing) return;
            for (int i = 0; i < 5; i++)
            {
                var clock = controller.View.Clocks[i]; var adapter = clock.H2Presentation; var state = match.GetClock(i);
                var lifecycle = controller.View.Lifecycle;
                if (lifecycle && lifecycle.IsRunning && lifecycle.ClockIndex == i)
                {
                    Assert.That(clock.gameObject.activeSelf, Is.True);
                    Assert.That(adapter.IsOpen || adapter.IsRed, Is.False); Assert.That(adapter.DisplayedHours, Is.Null);
                    Assert.That(Quaternion.Angle(adapter.Watch.LidPivot.localRotation, Quaternion.identity), Is.LessThan(.001f));
                    Assert.That(clock.SelectionTarget.InputEnabled || clock.GetComponent<Collider>().enabled, Is.False);
                    Assert.That(adapter.Materials.C2Enabled, Is.True);
                    continue;
                }
                bool visible = state.Visibility == ClockVisibility.Open || state.Visibility == ClockVisibility.Hidden;
                Assert.That(clock.gameObject.activeSelf, Is.EqualTo(visible), "slot " + i + " / " + match.Stage);
                Assert.That(adapter.DisplayedHours, Is.EqualTo(state.Face?.Hours));
                bool opening = controller.View.TurnPresentation.Phase == H2TurnPresentation.TurnPhase.RevealEnter
                    && controller.View.TurnPresentation.ClockIndex == i;
                var turn = controller.View.TurnPresentation;
                bool closing = turn.ClockIndex == i && (turn.Phase == H2TurnPresentation.TurnPhase.Closing ||
                    turn.Phase == H2TurnPresentation.TurnPhase.Returning || turn.Phase == H2TurnPresentation.TurnPhase.Closed);
                if (closing)
                {
                    float lidAngle = Quaternion.Angle(Quaternion.identity, adapter.Watch.LidPivot.localRotation);
                    Assert.That(lidAngle, Is.InRange(0, 109.01f));
                    Assert.That(adapter.IsOpen, Is.EqualTo(lidAngle >= 85));
                    if (turn.Phase != H2TurnPresentation.TurnPhase.Closing) Assert.That(lidAngle, Is.LessThan(.01f));
                }
                else if (!opening) Assert.That(adapter.IsOpen, Is.EqualTo(state.Face.HasValue));
                Assert.That(adapter.IsRed, Is.EqualTo(state.Face.HasValue && state.Face.Value.Kind == ClockKind.Red));
                float hour = state.Face.HasValue ? 30 * (state.Face.Value.Hours % 12) : 0;
                Assert.That(Quaternion.Angle(adapter.Watch.HourHand.localRotation, Quaternion.Euler(0, 0, hour)), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(adapter.Watch.MinuteHand.localRotation, Quaternion.identity), Is.LessThan(.01f));
                Assert.That(adapter.Materials.C2Enabled, Is.True);
                if (!visible) Assert.That(clock.SelectionTarget.InputEnabled, Is.False);
            }
        }

        private IEnumerator ClickClock(int index)
        {
            yield return Wait(() => !focus.IsTransitioning);
            var clock = controller.View.Clocks[index];
            Assert.That(clock.SelectionTarget.InputEnabled, Is.True);
            // 실제 메인 입력 하네스와 동일하게 각 클릭 전에 가상 입력의 창 포커스를 준비한다.
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            for (int frame = 0; frame < 10; frame++)
            {
                mouse.MakeCurrent();
                input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(clock.GetComponent<Collider>().bounds.center));
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(clock.SelectionTarget), "stage=" + controller.Match.Stage
                + " pointer=" + mouse.position.ReadValue() + " camera=" + camera.transform.position
                + " control=" + focus.HasCameraControl + " targetEnabled=" + clock.SelectionTarget.InputEnabled);
            Assert.That(clock.SelectionTarget.IsHighlighted, Is.True);
            input.Press(mouse.leftButton); yield return null; input.Release(mouse.leftButton); yield return null;
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(index));
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(clock.IsSelected, Is.True);
            yield return GameUiTestInput.ClockMotionReady(controller);
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Output + name, image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }

        private IEnumerator Wait(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, controller.Match.Stage.ToString());
        }
    }
}
#endif

