#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TimeTheft.Tests
{
    public sealed class EscNavigationValidationTests
    {
        const string Game = "Assets/Scenes/TimeTheftGame.unity";
        const string Title = "Assets/Scenes/TimeTheftTitle.unity";
        static readonly FieldInfo HeldField = typeof(TimeTheftGameController).GetField("escapeHeldSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
        InputTestFixture input;
        Keyboard keyboard;
        Mouse mouse;
        TimeTheftGameController controller;
        TimeTheftSettings settings;
        float oldScale, triggerHeld;
        bool oldBackground;
        InputSettings.BackgroundBehavior oldInputBackground;
        CursorLockMode oldCursor;
        bool oldCursorVisible;
        int titleLoads;
        float Held => (float)HeldField.GetValue(controller);

        [UnitySetUp] public IEnumerator Setup()
        {
            titleLoads = 0; triggerHeld = 0; controller = null; settings = null;
            oldScale = Time.timeScale; oldBackground = Application.runInBackground;
            oldInputBackground = InputSystem.settings.backgroundBehavior;
            oldCursor = Cursor.lockState; oldCursorVisible = Cursor.visible;
            GameUiTestInput.DisableSceneModules(); input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("EscapeValidationKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("EscapeValidationMouse");
            Application.runInBackground = true; Time.timeScale = 0;
            SceneManager.sceneLoaded += SceneLoaded;
            yield return EditorSceneManager.LoadSceneInPlayMode(Game, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(HeldField, Is.Not.Null);
            controller.View.FocusCamera.SendMessage("OnApplicationFocus", true);
            Camera.main.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, Camera.main.pixelRect.max + Vector2.one * 20);
            for (int i=0; i<3; i++) yield return null;
        }
        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != Title) return;
            titleLoads++;
            if (!ReferenceEquals(controller, null)) triggerHeld = (float)HeldField.GetValue(controller);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (controller) controller.enabled = false;
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldScale; Application.runInBackground = oldBackground;
            Cursor.lockState = oldCursor; Cursor.visible = oldCursorVisible;
            yield return null;
            input?.TearDown(); InputSystem.settings.backgroundBehavior = oldInputBackground;
        }
        static IEnumerator Wait(Func<bool> predicate, float seconds = 12)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (!predicate() && Time.realtimeSinceStartupAsDouble < end) yield return null;
            Assert.That(predicate(), Is.True, "Expected legal progress before deadline");
        }
        IEnumerator HoldTo(float seconds)
        {
            input.Press(keyboard.escapeKey);
            yield return Wait(() => !controller || Held >= seconds, 4);
            Assert.That(controller, Is.Not.Null, "Sub-threshold hold must remain in game");
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Game));
            Assert.That(Held, Is.LessThan(1.5f));
        }
        IEnumerator Release()
        {
            input.Release(keyboard.escapeKey); yield return null; yield return null;
            Assert.That(Held, Is.Zero, "Release clears accumulated unscaled hold");
        }
        IEnumerator ClickDelayedMenu(Button button)
        {
            yield return GameUiTestInput.Ready(button);
            mouse.MakeCurrent();
            input.Set(mouse.position, GameUiTestInput.ScreenRect(button).center);
            GameUiTestInput.Process(); yield return null;
            // TitleMenuButton shrinks around its pivot and confirms after 0.1 unscaled seconds.
            input.Set(mouse.position, GameUiTestInput.ScreenRect(button).center);
            GameUiTestInput.Process();
            int clicks = 0;
            UnityEngine.Events.UnityAction observed = () => clicks++;
            button.onClick.AddListener(observed);
            try
            {
                input.Press(mouse.leftButton); GameUiTestInput.Process(); yield return null;
                input.Release(mouse.leftButton); GameUiTestInput.Process();
                yield return Wait(() => clicks > 0, 2);
                Assert.That(clicks, Is.EqualTo(1));
            }
            finally { if (button) button.onClick.RemoveListener(observed); }
        }
        [UnityTest] public IEnumerator ShortInterruptedAndContinuousHoldsRespectThresholdAndNavigateOnce()
        {
            yield return HoldTo(.15f); yield return Release();
            yield return HoldTo(1.2f); yield return Release();
            yield return HoldTo(.55f); yield return Release();
            Assert.That(titleLoads, Is.Zero, "Separate holds do not accumulate");
            input.Press(keyboard.escapeKey);
            yield return Wait(() => SceneManager.GetActiveScene().path == Title, 5);
            Assert.That(triggerHeld, Is.GreaterThanOrEqualTo(1.5f));
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(titleLoads, Is.EqualTo(1), "Held input does not load title repeatedly");
            Assert.That(UnityEngine.Object.FindObjectsByType<TimeTheftGameController>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            TestContext.WriteLine("Unscaled threshold at transition: " + triggerHeld.ToString("F4") + "; title loads=" + titleLoads);
            input.Release(keyboard.escapeKey);
        }
        [UnityTest] public IEnumerator RestartReleaseAndMenuReentryRestoreInputState()
        {
            yield return HoldTo(.45f);
            var previous = controller.Match;
            controller.RestartGame();
            Assert.That(controller.Match, Is.Not.SameAs(previous));
            yield return Release();
            yield return HoldTo(.2f); yield return Release();
            Assert.That(titleLoads, Is.Zero);
            input.Press(keyboard.escapeKey);
            yield return Wait(() => SceneManager.GetActiveScene().path == Title, 5);
            input.Release(keyboard.escapeKey); yield return null;
            var menu = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(c => c.GetType().Name == "TitleScreenController");
            var menuData = new SerializedObject(menu);
            var start = menuData.FindProperty("startButton").objectReferenceValue as Button;
            Assert.That(start, Is.Not.Null);
            yield return Wait(() => !(bool)menu.GetType().GetProperty("IsTransitioning").GetValue(menu));
            yield return ClickDelayedMenu(start);
            TestContext.WriteLine("Title start accepted actual pointer click");
            yield return Wait(() => SceneManager.GetActiveScene().path == Game);
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(Held, Is.Zero, "New scene owns a fresh hold timer");
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            TestContext.WriteLine("Fresh game controller and cursor restored");
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.PreviewDuration = settings.ShuffleDuration = .05f;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings; serialized.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller); controller.RestartGame();
            yield return Wait(() => !controller.IsFlowBlocked &&
                (controller.Match.Stage == TurnStage.AwaitingItems || controller.Match.Stage == TurnStage.AwaitingClock));
            if (controller.Match.Stage == TurnStage.AwaitingItems) controller.EndItemPhase();
            yield return Wait(() => !controller.IsFlowBlocked && controller.Match.Stage == TurnStage.AwaitingClock);
            controller.SelectClock(0); yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            yield return GameUiTestInput.Click(input, mouse, controller.View.CollectButton);
            Assert.That(controller.Match.Stage, Is.Not.EqualTo(TurnStage.AwaitingAction), "Game action button accepts input after menu reentry");
            Assert.That(titleLoads, Is.EqualTo(1));
        }
    }
}
#endif
