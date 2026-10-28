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
            Assert.That(EditorJsonUtility.ToJson(original.sharedProfile), Is.EqualTo(baseline));
            Assert.That(bloom.intensity.value, Is.EqualTo(bloomIntensity));
            Assert.That(presentation.PressureVolume.sharedProfile.Has<Bloom>(), Is.False);

            controller.RestartGame();
            AssertRestored();
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
            Assert.That(presentation.PressureVolume.weight, Is.Zero);
            Assert.That(controller.Match, Is.SameAs(match));
            Assert.That(presentation.CanRestart, Is.True);
            Capture("victory");
            yield return new WaitForSecondsRealtime(.15f);
            controller.RestartGame();
            AssertRestored();
        }

        [UnityTest]
        public IEnumerator DefeatSkipCannotRestartUntilInputReleasedAndRestartButtonRestoresScene()
        {
            yield return Finish(ClockKind.Red);
            var finalMatch = controller.Match;
            Assert.That(finalMatch.Winner, Is.EqualTo(GameWinner.Ai));
            input.Press(keyboard.spaceKey); input.Press(keyboard.rKey);
            InputSystem.Update(); controller.SendMessage("Update");
            yield return WaitFor(() => presentation.IsVisible);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(presentation.Heading.text, Is.EqualTo("패배"));
            Assert.That(presentation.CanRestart, Is.False);
            controller.RequestRestart();
            Assert.That(controller.Match, Is.SameAs(finalMatch), "스킵/재시작 키를 유지해도 다음 게임으로 넘어가지 않는다");
            Assert.That(presentation.PressureVolume.weight, Is.Zero);
            Capture("defeat");
            yield return new WaitForSecondsRealtime(.15f);

            input.Release(keyboard.spaceKey); input.Release(keyboard.rKey); InputSystem.Update();
            yield return WaitFor(() => presentation.CanRestart);
            // 타이틀 버튼의 기존 0.1초 눌림 연출을 포함해 실제 포인터를 제출한다.
            yield return GameUiTestInput.Ready(presentation.RestartButton);
            GameUiTestInput.Point(input, mouse, presentation.RestartButton);
            int submitted = 0;
            presentation.RestartButton.onClick.AddListener(() => submitted++);
            input.Press(mouse.leftButton); GameUiTestInput.Process();
            yield return null;
            Assert.That(controller.Match, Is.SameAs(finalMatch));
            input.Release(mouse.leftButton); GameUiTestInput.Process();
            yield return WaitFor(() => controller.Match != finalMatch);
            Assert.That(submitted, Is.EqualTo(1));
            AssertRestored();

            // 중간 취소도 같은 복구 경로를 사용한다.
            controller.StopAllCoroutines();
            controller.StartCoroutine(presentation.PlayEnding(GameWinner.Ai));
            yield return new WaitForSecondsRealtime(.7f);
            controller.enabled = false;
            AssertRestored();
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

        private int ClockOfKind(ClockKind kind, int excluded = -1, int excluded2 = -1)
        {
            // 독립 모델은 테스트 오라클이다. 런타임의 비공개 시계 API를 넓히지 않는다.
            for (int index = 0; index < 5; index++)
            {
                if (index == excluded || index == excluded2) continue;
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

        private void AssertRestored()
        {
            Assert.That(presentation.IsEnding || presentation.IsVisible, Is.False);
            Assert.That(presentation.Pressure, Is.Zero);
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
