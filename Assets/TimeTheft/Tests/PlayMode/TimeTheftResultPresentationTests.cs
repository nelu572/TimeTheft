#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class TimeTheftResultPresentationTests
    {
        private TimeTheftGameController controller;
        private TimeTheftResultPresentation presentation;
        private TimeTheftSettings settings;
        private InputTestFixture input;
        private Keyboard keyboard;
        private Mouse mouse;
        private float timeScale;
        private bool runInBackground;
        private Light[] lights;
        private float[] intensities;
        private Color[] colors;

        [UnitySetUp]
        public IEnumerator Load()
        {
            timeScale = Time.timeScale; runInBackground = Application.runInBackground;
            Application.runInBackground = true;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("ResultTestKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("ResultTestMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            presentation = controller.View.ResultPresentation;
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.EnableAiItemStrategy = false;
            settings.Rules.NumberWeights = Array.Empty<int>(); settings.Rules.ExtraRedProbability = 0;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.RandomSeed = 635;
            settings.PreviewDuration = settings.ShuffleDuration = settings.AiThinkDuration =
                settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = .06f;
            var data = new SerializedObject(controller);
            data.FindProperty("settings").objectReferenceValue = settings; data.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller);
            var lightData = new SerializedObject(presentation).FindProperty("sceneLights");
            lights = new Light[lightData.arraySize]; intensities = new float[lights.Length]; colors = new Color[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            { lights[i] = (Light)lightData.GetArrayElementAtIndex(i).objectReferenceValue; intensities[i] = lights[i].intensity; colors[i] = lights[i].color; }
            Time.timeScale = 0;
            controller.RestartGame();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false;
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = timeScale; Application.runInBackground = runInBackground;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator PressureReadsOnlyPlayerLifeAndRecoversWithoutChangingBaseVolume()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 18;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock &&
                !controller.View.Lifecycle.IsRunning && !controller.View.WorldItems.ModelUses.IsAcquiring);
            controller.StopAllCoroutines();
            var match = controller.Match;
            var original = (Volume)new SerializedObject(presentation).FindProperty("sourceVolume").objectReferenceValue;
            string baseline = EditorJsonUtility.ToJson(original.sharedProfile);
            original.sharedProfile.TryGet<Bloom>(out var bloom);
            float bloomIntensity = bloom.intensity.value;
            Assert.That(presentation.PressureVolume.weight, Is.Zero, "100%에는 기존 비주얼을 그대로 사용한다");
            Assert.That(presentation.Pressure, Is.Zero);

            int normal1 = ClockOfKind(ClockKind.Normal), normal2 = ClockOfKind(ClockKind.Normal, normal1);
            Settle(match, normal1, TimeAction.Collect); // 상대 HP만 감소. 공용 Bank는 증가.
            controller.View.Numbers(match);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(120));
            Assert.That(presentation.PressureVolume.weight, Is.Zero);

            match.CompleteTurn();
            Settle(match, normal2, TimeAction.Collect); // AI의 일반 시계가 플레이어 HP를 감소시킨다.
            controller.View.Numbers(match);
            yield return WaitFor(() => presentation.Pressure > .7f);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(120));
            Assert.That(presentation.Pressure, Is.GreaterThan(.7f));
            presentation.PressureVolume.sharedProfile.TryGet<ChromaticAberration>(out var ca);
            Assert.That(ca.intensity.value, Is.LessThan(.21f), "숫자 가독성을 위해 상시 색수차는 절제한다");
            Capture("low-hp");
            yield return new WaitForSecondsRealtime(.15f);

            match.CompleteTurn();
            Settle(match, ClockOfKind(ClockKind.Normal, normal1, normal2), TimeAction.Ration);
            controller.View.Numbers(match);
            float previousPressure = presentation.Pressure;
            yield return null;
            Assert.That(presentation.Pressure, Is.GreaterThan(0).And.LessThan(previousPressure));
            yield return WaitFor(() => presentation.Pressure < .35f);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(120 + 18 * GameTime.RationMinutesPerHour));
            Assert.That(presentation.Pressure, Is.LessThan(.35f));
            Assert.That(presentation.Heartbeat, Is.Zero, "30% 이상 회복하면 박동도 멈춘다");
            Assert.That(EditorJsonUtility.ToJson(original.sharedProfile), Is.EqualTo(baseline));
            Assert.That(bloom.intensity.value, Is.EqualTo(bloomIntensity));
            Assert.That(presentation.PressureVolume.sharedProfile.Has<Bloom>(), Is.False);

            controller.RestartGame();
            AssertRestored();
        }

        [UnityTest]
        public IEnumerator LifeLossReactionWaitsForCounterThenHitsOnceAndClearsOnRecoveryOrRestart()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 6;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock &&
                !controller.View.Lifecycle.IsRunning && !controller.View.WorldItems.ModelUses.IsAcquiring);
            controller.StopAllCoroutines();
            var match = controller.Match;
            // 테스트 마우스의 자유 시차를 고정해 후처리 자체가 카메라를 움직이는지 확인한다.
            controller.View.FocusCamera.BeginPresentationCamera();
            var originalCamera = controller.View.FocusCamera.transform.localPosition;
            int first = ClockOfKind(ClockKind.Normal);
            Settle(match, first, TimeAction.Collect); // 상대 손실에는 반응하지 않는다.
            controller.View.BeginTransfer(match); controller.View.NumbersFrame(1);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(presentation.DamageReaction, Is.Zero);
            Assert.That(presentation.Heartbeat, Is.Zero);
            Assert.That(presentation.CameraSwayDegrees, Is.Zero);
            Assert.That(presentation.PressureVolume.weight, Is.Zero);

            match.CompleteTurn();
            int second = ClockOfKind(ClockKind.Normal, first);
            Settle(match, second, TimeAction.Collect);
            controller.View.BeginTransfer(match); controller.View.NumbersFrame(.5f);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(presentation.DamageReaction, Is.Zero, "계수기 감소 도중에는 먼저 반응하지 않는다");
            controller.View.NumbersFrame(1);
            float finishedAt = Time.unscaledTime;
            Assert.That(presentation.DamageReaction, Is.Zero, "감소 완료 뒤 짧게 늦춰서 시작한다");
            yield return WaitFor(() => presentation.DamageReaction > .5f);
            Assert.That(presentation.Pressure, Is.Zero, "HP 70%에서도 손실 순간은 분명히 전달한다");
            presentation.PressureVolume.sharedProfile.TryGet<LensDistortion>(out var lens);
            presentation.PressureVolume.sharedProfile.TryGet<ChromaticAberration>(out var aberration);
            Assert.That(lens.intensity.value, Is.LessThan(-.007f).And.GreaterThanOrEqualTo(-.015f));
            Assert.That(lens.scale.value, Is.GreaterThan(1.005f).And.LessThanOrEqualTo(1.012f));
            Assert.That(aberration.intensity.value, Is.GreaterThan(.15f));
            Assert.That(controller.View.FocusCamera.transform.localPosition, Is.EqualTo(originalCamera));
            var counterBlock = new MaterialPropertyBlock();
            controller.View.PlayerLife.GetComponent<Renderer>().GetPropertyBlock(counterBlock);
            Assert.That(counterBlock.GetFloat(Shader.PropertyToID("_GlowIntensity")), Is.Zero, "후처리 전 발광 잔상이 선명한 숫자와 어긋나지 않게 한다");
            Capture("damage-reaction-peak");

            // Animate의 완료 프레임 중복 호출은 같은 손실을 다시 재생하지 않는다.
            controller.View.NumbersFrame(1); controller.View.NumbersFrame(1);
            yield return WaitFor(() => presentation.DamageReaction == 0);
            Assert.That(Time.unscaledTime - finishedAt, Is.LessThan(.9f));
            Assert.That(presentation.PressureVolume.weight, Is.Zero);
            Assert.That(lens.intensity.overrideState || lens.scale.overrideState, Is.False);
            controller.View.FocusCamera.EndPresentationCamera();
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(presentation.DamageReaction, Is.Zero);

            match.CompleteTurn();
            int third = ClockOfKind(ClockKind.Normal, first, second);
            Settle(match, third, TimeAction.Ration);
            controller.View.BeginTransfer(match); controller.View.NumbersFrame(1);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(presentation.DamageReaction, Is.Zero, "수명 회복에는 손실 반응을 재생하지 않는다");

            match.CompleteTurn();
            Settle(match, ClockOfKind(ClockKind.Normal, first, second, third), TimeAction.Collect);
            controller.View.BeginTransfer(match); controller.View.NumbersFrame(1);
            controller.RestartGame();
            yield return new WaitForSecondsRealtime(.25f);
            AssertRestored();
        }

        [UnityTest]
        public IEnumerator LowLifeHeartbeatPairsAtEdgesAndYieldsToDamageOrRestart()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 18;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock &&
                !controller.View.Lifecycle.IsRunning && !controller.View.WorldItems.ModelUses.IsAcquiring);
            controller.StopAllCoroutines();
            var match = controller.Match;
            int first = ClockOfKind(ClockKind.Normal);
            Settle(match, first, TimeAction.Collect);
            controller.View.Numbers(match);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(presentation.Heartbeat, Is.Zero, "상대만 저HP일 때는 박동하지 않는다");

            match.CompleteTurn();
            Settle(match, ClockOfKind(ClockKind.Normal, first), TimeAction.Collect);
            controller.View.Numbers(match);
            yield return new WaitForSecondsRealtime(.5f);
            presentation.PressureVolume.sharedProfile.TryGet<Vignette>(out var vignette);
            presentation.PressureVolume.sharedProfile.TryGet<LensDistortion>(out var lens);
            var beats = new System.Collections.Generic.List<float>();
            bool inBeat = false;
            float darkest = 0, lightest = 1, until = Time.unscaledTime + 2.3f;
            while (Time.unscaledTime < until)
            {
                float beat = presentation.Heartbeat;
                if (beat > .2f && !inBeat) { beats.Add(Time.unscaledTime); inBeat = true; }
                if (beat < .08f) inBeat = false;
                darkest = Mathf.Max(darkest, vignette.intensity.value);
                lightest = Mathf.Min(lightest, vignette.intensity.value);
                Assert.That(lens.intensity.overrideState || lens.scale.overrideState, Is.False);
                Assert.That(presentation.DamageReaction, Is.Zero);
                yield return null;
            }
            Assert.That(beats.Count, Is.GreaterThanOrEqualTo(3), "저HP에서 박동이 반복된다");
            bool pair = false, rest = false;
            for (int i = 1; i < beats.Count; i++)
            {
                float gap = beats[i] - beats[i - 1];
                pair |= gap < .4f;
                rest |= gap > .45f;
            }
            Assert.That(pair && rest, Is.True, "두 번 두근거린 뒤 쉬는 간격을 둔다");
            Assert.That(darkest - lightest, Is.GreaterThan(.018f));
            Assert.That(darkest, Is.LessThan(.41f), "기본 비네트 0.18 위의 맥동을 절제한다");

            presentation.ReactToLifeLoss(60);
            yield return null;
            Assert.That(presentation.Heartbeat, Is.Zero, "피격 반응 예약부터 우선한다");
            yield return WaitFor(() => presentation.DamageReaction > .2f);
            Assert.That(presentation.Heartbeat, Is.Zero);
            yield return WaitFor(() => presentation.DamageReaction == 0);
            yield return WaitFor(() => presentation.Heartbeat > .2f);
            controller.RestartGame();
            yield return new WaitForSecondsRealtime(.25f);
            AssertRestored();
        }

        [UnityTest]
        public IEnumerator CameraSwayRestoresEveryRenderAndDefeatFallsIntoBlack()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 18;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock &&
                !controller.View.Lifecycle.IsRunning && !controller.View.WorldItems.ModelUses.IsAcquiring);
            controller.StopAllCoroutines();
            var focus = controller.View.FocusCamera;
            var camera = focus.GetComponent<Camera>();
            focus.BeginPresentationCamera();
            Vector3 position = camera.transform.position, up = camera.transform.up, forward = camera.transform.forward;
            var match = controller.Match;
            int first = ClockOfKind(ClockKind.Normal);
            Settle(match, first, TimeAction.Collect); match.CompleteTurn();
            Settle(match, ClockOfKind(ClockKind.Normal, first), TimeAction.Collect);
            controller.View.Numbers(match);
            bool applied = false, restored = false;
            Action<ScriptableRenderContext, Camera> begin = (context, current) =>
            {
                if (current != camera || presentation.CameraSwayDegrees <= 0) return;
                float difference = Vector3.Distance(current.transform.up, up) + Vector3.Distance(current.transform.forward, forward);
                applied |= difference > .0001f && difference < .012f;
            };
            Action<ScriptableRenderContext, Camera> end = (context, current) =>
            {
                if (current != camera) return;
                restored |= Vector3.Distance(current.transform.up, up) + Vector3.Distance(current.transform.forward, forward) < .00001f;
            };
            RenderPipelineManager.beginCameraRendering += begin;
            RenderPipelineManager.endCameraRendering += end;
            try
            {
                yield return WaitFor(() => applied && restored);
                Assert.That(presentation.CameraSwayDegrees, Is.GreaterThan(0).And.LessThanOrEqualTo(.3f));
                Assert.That(camera.transform.position, Is.EqualTo(position));
                Assert.That(Vector3.Distance(camera.transform.up, up), Is.LessThan(.00001f));
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= begin;
                RenderPipelineManager.endCameraRendering -= end;
            }
            focus.EndPresentationCamera();

            yield return Finish(ClockKind.Red);
            Vector3 startPosition = camera.transform.position;
            Quaternion startRotation = camera.transform.rotation;
            yield return new WaitForSecondsRealtime(.9f);
            Assert.That(Quaternion.Angle(startRotation, camera.transform.rotation), Is.GreaterThan(10));
            Assert.That(Vector3.Distance(startPosition, camera.transform.position), Is.GreaterThan(.05f));
            Capture("defeat-fall-mid");
            yield return WaitFor(() => presentation.IsVisible);
            yield return new WaitForSecondsRealtime(.3f);
            AssertBlackResult();
            Assert.That(presentation.CameraSwayDegrees, Is.Zero);
            Capture("defeat-black-result");
            yield return new WaitForSecondsRealtime(.1f);
            controller.RestartGame();
            controller.enabled = false;
            AssertRestored();
            Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
            Assert.That(Quaternion.Angle(camera.transform.rotation, focus.OverviewRotation), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(focus.OverviewFieldOfView));
        }

        [UnityTest]
        public IEnumerator VictoryFollowsFinalSettlementAndShowsClearResultWithinThreeSeconds()
        {
            yield return Finish(ClockKind.Normal);
            float started = Time.realtimeSinceStartup;
            var match = controller.Match;
            Assert.That(match.Winner, Is.EqualTo(GameWinner.Player));
            Assert.That(controller.View.IsCountingTime, Is.False);
            Assert.That(controller.View.IsResultVisible, Is.False);
            // 중복 종료 요청을 무시한다.
            yield return presentation.PlayEnding(GameWinner.Player);
            Assert.That(presentation.IsEnding, Is.True);
            yield return WaitFor(() => presentation.IsVisible);
            Assert.That(Time.realtimeSinceStartup - started, Is.LessThan(3));
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(presentation.Heading.text, Is.EqualTo("승리"));
            AssertBlackResult();
            Assert.That(presentation.PressureVolume.weight, Is.Zero);
            Assert.That(controller.Match, Is.SameAs(match));
            Assert.That(presentation.CanLeaveResult, Is.True);
            Capture("victory");
            yield return new WaitForSecondsRealtime(.15f);
            controller.RestartGame();
            AssertRestored();
        }

        [UnityTest]
        public IEnumerator DefeatSkipCannotLeaveUntilInputReleasedAndMainMenuButtonLoadsTitle()
        {
            yield return Finish(ClockKind.Red);
            var finalMatch = controller.Match;
            Assert.That(finalMatch.Winner, Is.EqualTo(GameWinner.Ai));
            input.Press(keyboard.spaceKey); input.Press(keyboard.rKey);
            InputSystem.Update(); controller.SendMessage("Update");
            yield return WaitFor(() => presentation.IsVisible);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(presentation.Heading.text, Is.EqualTo("패배"));
            AssertBlackResult();
            Assert.That(presentation.CanLeaveResult, Is.False);
            controller.RequestMainMenu();
            Assert.That(controller.Match, Is.SameAs(finalMatch), "스킵/재시작 키를 유지해도 다음 게임으로 넘어가지 않는다");
            Assert.That(presentation.PressureVolume.weight, Is.Zero);
            Capture("defeat");
            yield return new WaitForSecondsRealtime(.15f);

            input.Release(keyboard.spaceKey); input.Release(keyboard.rKey); InputSystem.Update();
            yield return WaitFor(() => presentation.CanLeaveResult);
            // 타이틀 버튼의 기존 0.1초 눌림 연출을 포함해 실제 포인터를 제출한다.
            Assert.That(presentation.MainMenuButton.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo("메인으로"));
            Assert.That(presentation.MainMenuButton.transform.parent.Find("Restart Hint"), Is.Null);
            yield return GameUiTestInput.Ready(presentation.MainMenuButton);
            GameUiTestInput.Point(input, mouse, presentation.MainMenuButton);
            int submitted = 0;
            presentation.MainMenuButton.onClick.AddListener(() => submitted++);
            input.Press(mouse.leftButton); GameUiTestInput.Process();
            yield return null;
            Assert.That(controller.Match, Is.SameAs(finalMatch));
            input.Release(mouse.leftButton); GameUiTestInput.Process();
            yield return WaitFor(() => SceneManager.GetActiveScene().path == "Assets/TimeTheft/TitleScreen/Scenes/TimeTheftTitle.unity");
            Assert.That(submitted, Is.EqualTo(1));
            Assert.That(UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>(), Is.Null);
        }

        private IEnumerator Finish(ClockKind kind)
        {
            settings.Rules.InitialLifeHours = 11;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            controller.RestartGame();
            yield return WaitFor(() => (controller.Match.Stage == TurnStage.AwaitingItems || controller.Match.Stage == TurnStage.AwaitingClock) && !controller.IsPresentingClock);
            if (controller.Match.Stage == TurnStage.AwaitingItems) controller.EndItemPhase();
            controller.SelectClock(ClockOfKind(kind));
            yield return WaitFor(() => !controller.IsPresentingClock && controller.Match.Stage == TurnStage.AwaitingAction);
            controller.ChooseAction(TimeAction.Collect);
            yield return WaitFor(() => presentation.IsEnding);
        }

        private int ClockOfKind(ClockKind kind, params int[] excluded)
        {
            // 독립 모델은 테스트 오라클이다. 런타임의 비공개 시계 API를 넓히지 않는다.
            for (int index = 0; index < 5; index++)
            {
                if (Array.IndexOf(excluded, index) >= 0) continue;
                var reference = new TimeTheftMatch(settings.Rules, new System.Random(settings.RandomSeed));
                reference.BeginBatch(); reference.CloseAndShuffle(); reference.CompleteShuffle();
                if (reference.Stage == TurnStage.AwaitingItems) reference.EndItemPhase(Participant.Player); reference.SelectClock(Participant.Player, index);
                reference.CommitAction(Participant.Player, TimeAction.Collect);
                if (reference.GetClock(index).Face.Value.Kind == kind) return index;
            }
            throw new InvalidOperationException("테스트에 필요한 종류의 시계가 없습니다.");
        }

        private static void Settle(TimeTheftMatch match, int index, TimeAction action)
        {
            var actor = match.ActiveParticipant;
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(actor), Is.True);
            Assert.That(match.SelectClock(actor, index), Is.True);
            Assert.That(match.CommitAction(actor, action), Is.True);
            Assert.That(match.Settle(), Is.True);
        }

        private void AssertBlackResult()
        {
            var canvas = presentation.MainMenuButton.GetComponentInParent<Canvas>();
            var background = canvas.transform.Find("World Shade").GetComponent<UnityEngine.UI.Image>();
            Assert.That(background.color, Is.EqualTo(Color.black));
        }

        private void AssertRestored()
        {
            Assert.That(presentation.IsEnding || presentation.IsVisible, Is.False);
            Assert.That(presentation.Pressure, Is.Zero);
            Assert.That(presentation.DamageReaction, Is.Zero);
            Assert.That(presentation.Heartbeat, Is.Zero);
            Assert.That(presentation.CameraSwayDegrees, Is.Zero);
            Assert.That(presentation.PressureVolume.weight, Is.Zero);
            if (!controller.enabled) Assert.That(controller.View.FocusCamera.IsPresentationCameraOwned, Is.False);
            for (int i = 0; i < lights.Length; i++)
            { Assert.That(lights[i].intensity, Is.EqualTo(intensities[i])); Assert.That(lights[i].color, Is.EqualTo(colors[i])); }
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "결과 연출 상태 전환 제한 시간");
        }

        private static void Capture(string name)
        {
            string directory = System.IO.Path.GetFullPath("Logs/ResultPresentation");
            System.IO.Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory, name + ".png"));
        }
    }
}
#endif
