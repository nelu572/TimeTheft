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
    public sealed class H2TurnPresentationTests
    {
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private H2TurnPresentation turn;
        private Camera camera;
        private TabletopFocusCamera focus;
        private InputTestFixture input;
        private Mouse mouse;
        private float oldTime;
        [UnitySetUp] public IEnumerator Load()
        {
            oldTime = Time.timeScale; GameUiTestInput.DisableSceneModules(); input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("H2TurnMouse"); InputSystem.AddDevice<Keyboard>("H2TurnKeyboard");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single)); yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(controller.Settings); settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0; settings.Rules.InitialLifeHours = 100;
            settings.Rules.EnableAiItemStrategy = false;
            settings.PreviewDuration = settings.ShuffleDuration = settings.TransferDuration = settings.ResultHoldDuration = settings.DiscardDuration = .06f;
            settings.RevealDuration = 1; settings.AiThinkDuration = .2f;
            var so = new SerializedObject(controller); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller); turn = controller.View.TurnPresentation;
            turn.SelectDuration = .25f; turn.RevealEnterDuration = .3f; turn.LegacyReturnDuration = .12f;
            focus = controller.View.FocusCamera; camera = focus.GetComponent<Camera>();
            focus.SendMessage("OnApplicationFocus", true); camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20); Time.timeScale = 0; controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false; if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules(); if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTime; yield return null; input?.TearDown();
        }

        [UnityTest] public IEnumerator EverySlotRetargetsToOneClosedHoldAndRevealsTheActualFaceWithoutChangingOtherWatches()
        {
            var kinds = new System.Collections.Generic.HashSet<ClockKind>();
            for (int index = 0; index < 5; index++)
            {
                if (index > 0) { controller.RestartGame(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock); }
                var token = controller.View.Clocks[index]; var model = token.H2Presentation;
                var scale = model.MotionRoot.Find("VisualScale").localScale;
                var body = model.Watch.GetComponentsInChildren<Renderer>().Single(r => r.name == "Case");
                controller.SelectClock(index); var match = controller.Match;
                Assert.That(match.CurrentClock, Is.EqualTo(index)); Assert.That(controller.IsPresentingClock, Is.True);
                controller.SelectClock((index + 1) % 5); controller.ChooseAction(TimeAction.Collect);
                Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
                while (controller.IsPresentingClock)
                {
                    AssertHidden();
                    for (int j = 0; j < 5; j++) if (j != index)
                    {
                        Assert.That(controller.View.Clocks[j].H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
                        var other = controller.View.Clocks[j].H2Presentation.Watch.GetComponentsInChildren<Renderer>().Single(r => r.name == "Case");
                        Assert.That(body.bounds.Intersects(other.bounds), Is.False, "Select body clearance: " + index + " / " + j);
                    }
                    yield return null;
                }
                Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.DecisionHold));
                Assert.That(model.MotionRoot.position.x, Is.EqualTo(.1f).Within(.002));
                Assert.That(model.MotionRoot.position.z, Is.EqualTo(-.9f).Within(.002));
                var hold = model.MotionRoot.localPosition; var rotation = model.MotionRoot.localRotation;
                yield return new WaitForSecondsRealtime(.4f);
                Assert.That(model.MotionRoot.localPosition, Is.EqualTo(hold)); Assert.That(model.MotionRoot.localRotation, Is.EqualTo(rotation)); AssertHidden();
                controller.ChooseAction(TimeAction.Ration); var face = match.GetClock(index).Face.Value; kinds.Add(face.Kind);
                controller.ChooseAction(TimeAction.Collect); controller.SelectClock((index + 1) % 5);
                Assert.That(match.Action, Is.EqualTo(TimeAction.Ration)); Assert.That(match.CurrentClock, Is.EqualTo(index));
                yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.ResultPose);
                Assert.That(match.Stage, Is.EqualTo(TurnStage.Revealing));
                Assert.That(model.DisplayedHours, Is.EqualTo(face.Hours)); Assert.That(model.IsRed, Is.EqualTo(face.Kind == ClockKind.Red));
                Assert.That(model.IsOpen, Is.True); Assert.That(Quaternion.Angle(model.Watch.LidPivot.localRotation, Quaternion.identity), Is.EqualTo(109).Within(.1));
                Assert.That(Quaternion.Angle(model.Watch.HourHand.localRotation, Quaternion.Euler(0, 0, (face.Hours % 12) * 30)), Is.LessThan(.01));
                Assert.That(Quaternion.Angle(model.Watch.MinuteHand.localRotation, Quaternion.identity), Is.LessThan(.01));
                Assert.That(model.MotionRoot.Find("VisualScale").localScale, Is.EqualTo(scale)); Assert.That(model.Materials.C2Enabled, Is.True);
                var center = model.Watch.HourHand.position;
                float width = Vector3.Distance(camera.WorldToViewportPoint(center - Vector3.right * .28f), camera.WorldToViewportPoint(center + Vector3.right * .28f)) * 1280;
                Assert.That(width, Is.GreaterThan(170));
                Assert.That(focus.IsPresentationCameraOwned, Is.True);
            }
            Assert.That(kinds.Count, Is.EqualTo(2), "같은 공유 모션으로 일반/빨강을 모두 검증한다");
        }

        [UnityTest] public IEnumerator ActualColliderAndActionButtonInputWaitsForSelectAndFocusCanInspectCountersDuringHold()
        {
            var token = controller.View.Clocks[4];
            for (int i = 0; i < 10; i++) { input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(token.GetComponent<Collider>().bounds.center)); yield return null; }
            Assert.That(focus.HoveredTarget, Is.SameAs(token.SelectionTarget));
            input.Press(mouse.leftButton); yield return null; input.Release(mouse.leftButton); yield return null;
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(4)); Assert.That(controller.IsPresentingClock, Is.True);
            Assert.That(controller.View.CollectButton.gameObject.activeSelf, Is.False);
            controller.ChooseAction(TimeAction.Collect); AssertHidden();
            yield return Wait(() => !controller.IsPresentingClock);
            var p = token.H2Presentation.MotionRoot.localPosition;
            yield return new WaitForSecondsRealtime(1.2f); AssertHidden(); Assert.That(token.H2Presentation.MotionRoot.localPosition, Is.EqualTo(p));
            var counter = controller.View.PlayerLife.GetComponentInParent<TabletopFocusTarget>();
            Assert.That(focus.Focus(counter), Is.True); yield return Wait(() => !focus.IsTransitioning);
            Assert.That(camera.fieldOfView, Is.EqualTo(counter.FieldOfView).Within(.001));
            // Focus한 실제 현재 카메라에서 Reveal을 시작하며 먼저 overview로 순간이동하지 않는다.
            var before = camera.transform.position;
            float beforeFov = camera.fieldOfView;
            controller.ChooseAction(TimeAction.Ration);
            Assert.That(camera.transform.position, Is.EqualTo(before)); Assert.That(focus.IsPresentationCameraOwned, Is.True);
            Assert.That(camera.fieldOfView, Is.EqualTo(beforeFov));
            controller.ChooseAction(TimeAction.Collect);
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.ResultPose);
            Assert.That(camera.fieldOfView, Is.EqualTo(focus.OverviewFieldOfView));
            Assert.That(controller.Match.Action, Is.EqualTo(TimeAction.Ration));
            yield return Wait(() => controller.Match.UsedCount > 0);
            yield return Wait(() => !focus.IsPresentationCameraOwned);
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
            yield return Wait(() => controller.Match.ActiveParticipant == Participant.Player && controller.Match.Stage == TurnStage.AwaitingClock);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            controller.SelectClock(controller.Match.GetAvailableClockIndices().First()); yield return GameUiTestInput.ClockMotionReady(controller);
            yield return GameUiTestInput.Click(input, mouse, controller.View.CollectButton);
            Assert.That(controller.Match.Action, Is.EqualTo(TimeAction.Collect));
        }

        [UnityTest] public IEnumerator AiUsesTheSameClosedSelectionAndRevealThenReturnsToTheExistingPlayerTurn()
        {
            settings.Rules.FirstParticipant = Participant.Ai; controller.RestartGame();
            yield return Wait(() => turn.IsSelecting); int index = controller.Match.CurrentClock;
            Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Ai)); AssertHidden();
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.DecisionHold); AssertHidden();
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.ResultPose);
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(index)); Assert.That(controller.View.Clocks[index].H2Presentation.IsOpen, Is.True);
            yield return Wait(() => controller.Match.ActiveParticipant == Participant.Player && controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.UsedCount, Is.EqualTo(1)); Assert.That(controller.View.Clocks[index].gameObject.activeSelf, Is.False);
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
        }

        [UnityTest] public IEnumerator RestartAndDisableReleaseEveryMotionStageWithoutStaleWrites()
        {
            foreach (var phase in new[] { H2TurnPresentation.TurnPhase.Selecting, H2TurnPresentation.TurnPhase.DecisionHold,
                H2TurnPresentation.TurnPhase.RevealEnter, H2TurnPresentation.TurnPhase.ResultPose, H2TurnPresentation.TurnPhase.SettlementTransition })
            {
                controller.RestartGame(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                controller.SelectClock(0);
                if (phase != H2TurnPresentation.TurnPhase.Selecting)
                {
                    yield return Wait(() => !controller.IsPresentingClock);
                    if (phase != H2TurnPresentation.TurnPhase.DecisionHold) controller.ChooseAction(TimeAction.Ration);
                }
                yield return Wait(() => turn.Phase == phase);
                if (phase == H2TurnPresentation.TurnPhase.DecisionHold)
                {
                    Assert.That(focus.Focus(controller.View.PlayerLife.GetComponentInParent<TabletopFocusTarget>()), Is.True);
                    yield return Wait(() => !focus.IsTransitioning);
                }
                controller.enabled = false;
                Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.Idle)); Assert.That(focus.IsPresentationCameraOwned, Is.False);
                Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
                Assert.That(camera.fieldOfView, Is.EqualTo(focus.OverviewFieldOfView));
                foreach (var t in controller.View.Clocks) Assert.That(t.H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
                controller.enabled = true; Assert.That(controller.Match.Batch, Is.EqualTo(1)); Assert.That(controller.Match.UsedCount, Is.Zero);
            }
        }

        [UnityTest] public IEnumerator DisablingTheOptionalPresentationFallsBackWithoutLeavingInputOrCameraLocked()
        {
            foreach (var phase in new[] { H2TurnPresentation.TurnPhase.Selecting, H2TurnPresentation.TurnPhase.RevealEnter,
                H2TurnPresentation.TurnPhase.ResultPose, H2TurnPresentation.TurnPhase.SettlementTransition })
            {
                turn.enabled = true; controller.RestartGame(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                controller.SelectClock(3);
                if (phase != H2TurnPresentation.TurnPhase.Selecting)
                { yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration); }
                yield return Wait(() => turn.Phase == phase); turn.enabled = false;
                if (phase == H2TurnPresentation.TurnPhase.Selecting)
                    yield return Wait(() => !controller.IsPresentingClock);
                Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.Idle));
                if (phase == H2TurnPresentation.TurnPhase.Selecting) controller.ChooseAction(TimeAction.Ration);
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.Revealing));
                var face = controller.Match.GetClock(3).Face.Value; var model = controller.View.Clocks[3].H2Presentation;
                Assert.That(model.IsOpen, Is.True); Assert.That(model.DisplayedHours, Is.EqualTo(face.Hours));
                Assert.That(model.IsRed, Is.EqualTo(face.Kind == ClockKind.Red));
                yield return Wait(() => controller.Match.UsedCount > 0);
                Assert.That(focus.IsPresentationCameraOwned, Is.False);
            }
        }

        [UnityTest] public IEnumerator SpaceCompletesOnlyTheActiveMotionAndNeverCommitsTheClosedDecision()
        {
            turn.SelectDuration = turn.RevealEnterDuration = turn.SettlementTransitionDuration = 10;
            controller.SelectClock(4); PressSpace();
            yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.DecisionHold)); AssertHidden();
            PressSpace(); yield return null;
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction)); AssertHidden();
            controller.ChooseAction(TimeAction.Ration); PressSpace();
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.ResultPose);
            Assert.That(controller.View.Clocks[4].H2Presentation.IsOpen, Is.True);
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.SettlementTransition); PressSpace();
            yield return Wait(() => controller.Match.UsedCount > 0);
            yield return Wait(() => !focus.IsPresentationCameraOwned);
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
            controller.RestartGame();
            int key = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingItems);
            controller.UseItem(key); yield return Wait(() => !controller.IsPresentingItem);
            controller.EndItemPhase(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            controller.SelectClock(0); PressSpace(); yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
            controller.SelectClock(1); PressSpace(); yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(1)); Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            AssertHidden(); Assert.That(controller.View.Clocks[0].H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
        }
        private void PressSpace()
        {
            input.Press(Keyboard.current.spaceKey); InputSystem.Update(); controller.SendMessage("Update");
            input.Release(Keyboard.current.spaceKey); InputSystem.Update();
        }

        private void AssertHidden()
        {
            Assert.That(Enumerable.Range(0, 5).All(i => !controller.Match.GetClock(i).Face.HasValue), Is.True);
            foreach (var c in controller.View.Clocks)
            {
                Assert.That(c.H2Presentation.IsOpen || c.H2Presentation.IsRed, Is.False);
                Assert.That(c.H2Presentation.DisplayedHours, Is.Null);
                Assert.That(Quaternion.Angle(c.H2Presentation.Watch.LidPivot.localRotation, Quaternion.identity), Is.LessThan(.001));
            }
        }
        private IEnumerator Wait(Func<bool> condition)
        {
            float end = Time.realtimeSinceStartup + 7;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(condition(), Is.True, controller.Match.Stage + " / " + turn.Phase);
        }
    }
}
#endif
