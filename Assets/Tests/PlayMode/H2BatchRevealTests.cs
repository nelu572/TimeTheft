#if UNITY_EDITOR
using System;
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
    public sealed class H2BatchRevealTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private H2BatchRevealPresentation reveal;
        private TabletopFocusCamera focus;
        private Camera camera;
        private float oldTime;

        [UnitySetUp] public IEnumerator Load()
        {
            oldTime = Time.timeScale; GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup(); InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("H2BatchMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single)); yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(controller.Settings); settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0; settings.Rules.InitialLifeHours = 100;
            settings.PreviewDuration = .4f;
            settings.ShuffleDuration = settings.AiThinkDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = settings.DiscardDuration = .06f;
            var so = new SerializedObject(controller); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            reveal = controller.View.BatchReveal; focus = controller.View.FocusCamera; camera = Camera.main;
            reveal.PreparationDuration = .05f; reveal.RevealDuration = .45f; reveal.CameraApproachDuration = .22f;
            reveal.CloseDuration = .3f; reveal.CameraReturnDuration = .3f;
            if (controller.View.Lifecycle) controller.View.Lifecycle.SupplyDuration = .04f;
            focus.SendMessage("OnApplicationFocus", true); camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20); Time.timeScale = 0; controller.RestartGame();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false; if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules(); if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTime; yield return null; input?.TearDown();
        }

        [UnityTest] public IEnumerator AllFiveActualValuesSettleBeforeTheSingleObservationTimerAndReturnClosed()
        {
            var faces = Enumerable.Range(0, 5).Select(i => controller.Match.GetClock(i).Face.Value).ToArray();
            Assert.That(controller.View.Clocks.All(c => !c.H2Presentation.IsOpen), Is.True);
            yield return Wait(() => reveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing);
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            for (int i = 0; i < 5; i++)
            {
                var model = controller.View.Clocks[i].H2Presentation;
                Assert.That(model.IsOpen, Is.True); Assert.That(model.DisplayedHours, Is.EqualTo(faces[i].Hours));
                Assert.That(model.IsRed, Is.EqualTo(faces[i].Kind == ClockKind.Red));
                Assert.That(Quaternion.Angle(model.Watch.HourHand.localRotation, Quaternion.Euler(0, 0, (faces[i].Hours % 12) * 30)), Is.LessThan(.01));
                Assert.That(Quaternion.Angle(model.Watch.MinuteHand.localRotation, Quaternion.identity), Is.LessThan(.01));
                Assert.That(Quaternion.Angle(model.Watch.LidPivot.localRotation, Quaternion.identity), Is.EqualTo(109).Within(.1));
            }
            yield return new WaitForSecondsRealtime(.16f);
            Assert.That(reveal.Phase, Is.EqualTo(H2BatchRevealPresentation.RevealPhase.Observing));
            Assert.That(camera.transform.position, Is.EqualTo(position)); Assert.That(camera.transform.rotation, Is.EqualTo(rotation));
            Assert.That(camera.fieldOfView, Is.EqualTo(58));
            yield return Wait(() => reveal.Phase == H2BatchRevealPresentation.RevealPhase.Closing);
            Assert.That(reveal.ClosingStarted - reveal.ReadableSince, Is.InRange(settings.PreviewDuration - .01f, settings.PreviewDuration + .18f));
            yield return Wait(() => reveal.Phase == H2BatchRevealPresentation.RevealPhase.Returning);
            AssertClosed();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(focus.IsPresentationCameraOwned, Is.False); Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
            Assert.That(Quaternion.Angle(camera.transform.rotation, focus.OverviewRotation), Is.LessThan(.001));
            Assert.That(Enumerable.Range(0, 5).All(i => !controller.Match.GetClock(i).Face.HasValue), Is.True); AssertClosed();
        }

        [UnityTest] public IEnumerator RevealRejectsClockActionAndFocusInputUntilCameraOwnershipEnds()
        {
            foreach (var phase in new[] { H2BatchRevealPresentation.RevealPhase.Preparing, H2BatchRevealPresentation.RevealPhase.Revealing,
                H2BatchRevealPresentation.RevealPhase.Observing, H2BatchRevealPresentation.RevealPhase.Closing, H2BatchRevealPresentation.RevealPhase.Returning })
            {
                controller.RestartGame();
                yield return Wait(() => reveal.Phase == phase);
                focus.SendMessage("OnApplicationFocus", true);
                input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(controller.View.Clocks[2].GetComponent<Collider>().bounds.center));
                input.Press(mouse.leftButton); InputSystem.Update(); focus.SendMessage("Update");
                controller.SelectClock(2); controller.ChooseAction(TimeAction.Collect); controller.EndItemPhase();
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview)); Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
                Assert.That(focus.Focus(controller.View.PlayerLife.GetComponentInParent<TabletopFocusTarget>()), Is.False);
                Assert.That(controller.View.Clocks.All(c => !c.SelectionTarget.InputEnabled), Is.True);
                Assert.That(controller.View.CollectButton.gameObject.activeSelf || controller.View.RationButton.gameObject.activeSelf, Is.False);
                input.Release(mouse.leftButton); InputSystem.Update(); input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            }
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
            controller.SelectClock(2); Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction)); AssertClosed();
        }

        [UnityTest] public IEnumerator RestartAndDisableCancelEveryPhaseWithoutStaleCameraOrMotionRoot()
        {
            foreach (var phase in new[] { H2BatchRevealPresentation.RevealPhase.Preparing, H2BatchRevealPresentation.RevealPhase.Revealing,
                H2BatchRevealPresentation.RevealPhase.Observing, H2BatchRevealPresentation.RevealPhase.Closing, H2BatchRevealPresentation.RevealPhase.Returning })
            {
                controller.RestartGame(); yield return Wait(() => reveal.Phase == phase);
                controller.enabled = false;
                Assert.That(reveal.IsRunning || focus.IsPresentationCameraOwned, Is.False);
                Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
                Assert.That(camera.fieldOfView, Is.EqualTo(58)); AssertClosed(); AssertSlotPoses();
                yield return new WaitForSecondsRealtime(.12f); AssertSlotPoses();
                controller.enabled = true;
                Assert.That(controller.Match.Batch, Is.EqualTo(1)); Assert.That(controller.Match.UsedCount, Is.Zero);
                // 새 묶음은 공개 Face를 미리 연결하되 덮개가 가린 준비 자세로 시작한다.
                Assert.That(controller.View.Clocks.All(c => !c.H2Presentation.IsOpen), Is.True);
                Assert.That(controller.View.Clocks.All(c => Quaternion.Angle(c.H2Presentation.Watch.LidPivot.localRotation, Quaternion.identity) < .001f), Is.True);
            }
        }

        [UnityTest] public IEnumerator ConsecutiveBatchesRestoreOriginalSlotsBeforeSelection()
        {
            GameUiTestInput.ShortenBatchReveal(controller); settings.PreviewDuration = .12f;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1; controller.RestartGame();
            var ids = controller.View.Clocks.Select(c => c.GetInstanceID()).ToArray(); int turns = 0;
            float deadline = Time.realtimeSinceStartup + 15;
            while (controller.Match.Batch < 3 && Time.realtimeSinceStartup < deadline)
            {
                if (controller.Match.Stage == TurnStage.AwaitingClock && !controller.View.Lifecycle.IsRunning &&
                    !controller.IsPresentingClock && !controller.View.WorldItems.ModelUses.IsAcquiring)
                {
                    AssertSlotPoses(); AssertClosed();
                    if (controller.Match.ActiveParticipant == Participant.Player) Assert.That(focus.IsPresentationCameraOwned, Is.False);
                    if (controller.Match.ActiveParticipant == Participant.Player)
                    {
                        controller.SelectClock(controller.Match.GetAvailableClockIndices().Last());
                        yield return GameUiTestInput.ClockMotionReady(controller);
                        controller.ChooseAction(TimeAction.Ration); turns++;
                    }
                }
                if (controller.Match.Stage == TurnStage.Shuffling) AssertClosed();
                yield return null;
            }
            Assert.That(controller.Match.Batch, Is.EqualTo(3)); Assert.That(turns, Is.EqualTo(4));
            Assert.That(controller.View.Clocks.Select(c => c.GetInstanceID()), Is.EqualTo(ids));
        }

        [UnityTest] public IEnumerator ObservationDialsRemainLargeAndUnobscuredInTheRealGameRender()
        {
            settings.PreviewDuration = 20; controller.RestartGame();
            yield return Wait(() => reveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing);
            var dials = controller.View.Clocks.Select(c => c.H2Presentation.Watch.GetComponentsInChildren<Renderer>().Single(r => r.name == "Dial")).ToArray();
            var materials = dials.Select(d => d.sharedMaterial).ToArray();
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var disabled = renderers.Select(r => r.forceRenderingOff).ToArray();
            var colors = new[] { Color.red, Color.green, Color.blue, Color.cyan, Color.magenta };
            var masks = new Material[5]; var previous = camera.targetTexture; var active = RenderTexture.active;
            bool msaa = camera.allowMSAA;
            var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(1280, 720, TextureFormat.RGBAFloat, false, true);
            var counts = new int[2][];
            try
            {
                camera.targetTexture = rt; camera.allowMSAA = false;
                for (int i = 0; i < 5; i++)
                {
                    masks[i] = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    masks[i].SetColor("_BaseColor", colors[i]); dials[i].sharedMaterial = masks[i];
                    var center = dials[i].bounds.center;
                    float width = Vector3.Distance(camera.WorldToScreenPoint(center - Vector3.right * .28f), camera.WorldToScreenPoint(center + Vector3.right * .28f));
                    Assert.That(width, Is.GreaterThan(100), "720p dial width: " + i);
                    var point = camera.WorldToViewportPoint(center);
                    Assert.That(point.x, Is.InRange(.08f, .92f)); Assert.That(point.y, Is.InRange(.12f, .88f));
                }
                for (int mode = 0; mode < 2; mode++)
                {
                    // 실제 테이블/덮개/손잡이/바늘을 포함한 영상과 문자판 단독 마스크를 비교한다.
                    for (int i = 0; i < renderers.Length; i++) renderers[i].forceRenderingOff = mode == 1 ? !dials.Contains(renderers[i]) : disabled[i];
                    camera.Render(); RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                    var pixels = texture.GetPixels();
                    // URP의 미세한 화면 공간 음영은 허용하되 가려진 픽셀은 포함하지 않는다.
                    counts[mode] = colors.Select(c => pixels.Count(p => Mathf.Abs(p.r - c.r) + Mathf.Abs(p.g - c.g) + Mathf.Abs(p.b - c.b) < .15f)).ToArray();
                }
                for (int i = 0; i < 5; i++)
                {
                    float fraction = counts[0][i] / (float)counts[1][i];
                    TestContext.WriteLine("Dial " + i + ": visible " + fraction.ToString("P2"));
                    Assert.That(fraction, Is.GreaterThan(.93f), "Lid/table occlusion: " + i);
                }
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].forceRenderingOff = disabled[i];
                for (int i = 0; i < 5; i++) { dials[i].sharedMaterial = materials[i]; if (masks[i]) UnityEngine.Object.Destroy(masks[i]); }
                camera.targetTexture = previous; camera.allowMSAA = msaa; RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.Destroy(texture);
            }
        }

        private void AssertSlotPoses()
        {
            for (int i = 0; i < 5; i++)
            {
                var token = controller.View.Clocks[i];
                Assert.That(token.transform.localPosition, Is.EqualTo(new Vector3((i - 2) * .98f, 1.06f, 0)));
                Assert.That(token.H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(Quaternion.Angle(token.H2Presentation.MotionRoot.localRotation, Quaternion.identity), Is.LessThan(.001));
                Assert.That(System.Array.FindAll(token.GetComponentsInChildren<Collider>(true), c => c.enabled).Length, Is.EqualTo(1));
            }
        }
        private void AssertClosed()
        {
            foreach (var token in controller.View.Clocks)
            {
                Assert.That(token.H2Presentation.IsOpen, Is.False); Assert.That(token.H2Presentation.IsRed, Is.False);
                Assert.That(token.H2Presentation.DisplayedHours, Is.Null);
                Assert.That(Quaternion.Angle(token.H2Presentation.Watch.LidPivot.localRotation, Quaternion.identity), Is.LessThan(.001));
            }
        }
        private IEnumerator Wait(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, reveal.Phase + " / " + controller.Match.Stage);
        }
    }
}
#endif
