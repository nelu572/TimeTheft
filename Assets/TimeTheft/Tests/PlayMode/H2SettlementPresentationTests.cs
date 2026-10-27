#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class H2SettlementPresentationTests
    {
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private H2TurnPresentation motion;
        private TabletopFocusCamera focus;
        private Camera camera;
        private InputTestFixture input;
        private float oldTime;
        [UnitySetUp] public IEnumerator Load()
        {
            oldTime = Time.timeScale; GameUiTestInput.DisableSceneModules(); input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var mouse=InputSystem.AddDevice<Mouse>(); InputSystem.AddDevice<Keyboard>();
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity",new LoadSceneParameters(LoadSceneMode.Single)); yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(controller.Settings); settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0; settings.Rules.EnableAiItemStrategy = false;
            settings.AiThinkDuration = 10;
            settings.PreviewDuration = settings.ShuffleDuration = .05f;
            settings.RevealDuration = .18f; settings.TransferDuration = .2f; settings.ResultHoldDuration = .18f;
            var so = new SerializedObject(controller); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller); motion = controller.View.TurnPresentation;
            motion.SelectDuration = .06f; motion.RevealEnterDuration = .08f; motion.SettlementTransitionDuration = .18f;
            motion.SettlementLeadDuration = .12f; motion.TurnCloseDuration = .18f; motion.TurnCameraReturnDuration = .12f;
            focus = controller.View.FocusCamera; camera = focus.GetComponent<Camera>();
            input.Set(mouse.position,camera.pixelRect.max+Vector2.one*20);
            Time.timeScale = 0;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false; if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules(); if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTime; yield return null; input.TearDown();
        }

        [UnityTest] public IEnumerator FourOutcomesUseRealCappedAndItemAdjustedAmountsExactlyOnceThroughClose()
        {
            var outcomes = new HashSet<string>();
            settings.Rules.InitialLifeHours = 11; settings.Rules.MinimumHours = settings.Rules.MaximumHours = 7;
            foreach (var action in new[] { TimeAction.Collect, TimeAction.Ration })
                for (int index = 0; index < 5; index++)
                {
                    controller.RestartGame();
                    int driver = TimeTheftItemTestSetup.Give(controller.Match,Participant.Player,ItemKind.Screwdriver);
                    int bypass = TimeTheftItemTestSetup.Give(controller.Match,Participant.Player,ItemKind.TimeBypass);
                    yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingItems);
                    controller.UseItem(driver); yield return Wait(() => !controller.IsPresentingItem);
                    controller.UseItem(bypass); yield return Wait(() => !controller.IsPresentingItem);
                    yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                    controller.SelectClock(index); yield return GameUiTestInput.ClockMotionReady(controller);
                    Assert.That(controller.Match.GetClock(index).Face, Is.Null);
                    controller.ChooseAction(action); var match=controller.Match; var face=match.GetClock(index).Face.Value;
                    outcomes.Add(action + "/" + face.Kind);
                    var expected=TimeTheftSettlement.Calculate(face,action,60,60,1200,true,true,50,50);
                    yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.ResultPose);
                    Assert.That(match.UsedCount, Is.Zero); Assert.That(match.PlayerLifeMinutes, Is.EqualTo(60));
                    var root=controller.View.Clocks[index].H2Presentation.MotionRoot; var pose=root.position; var cam=camera.transform.position;
                    yield return new WaitForSecondsRealtime(.07f);
                    Assert.That(root.position, Is.EqualTo(pose)); Assert.That(camera.transform.position, Is.EqualTo(cam));
                    yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.SettlementTransition);
                    Assert.That(match.UsedCount, Is.Zero);
                    int closed=0; Quaternion hand=Quaternion.identity;
                    Action<int> handler = i => { closed++; Assert.That(i,Is.EqualTo(index)); hand=controller.View.Clocks[i].H2Presentation.Watch.HourHand.localRotation; };
                    motion.CloseCompleted += handler;
                    yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.SettlementLead);
                    Assert.That(match.UsedCount, Is.EqualTo(1));
                    Assert.That(match.TransferredMinutes, Is.EqualTo(action == TimeAction.Ration ? 140 : face.Kind == ClockKind.Red ? 30 : 60));
                    Assert.That(match.PlayerLifeMinutes, Is.EqualTo(expected.OwnLifeMinutes));
                    Assert.That(match.AiLifeMinutes, Is.EqualTo(expected.OpponentLifeMinutes)); Assert.That(match.BankMinutes, Is.EqualTo(expected.BankMinutes));
                    Assert.That(controller.View.PlayerLife.text, Is.EqualTo("01:00"));
                    Assert.That(controller.View.PhaseLabel.text, Does.Contain("실제 " + GameTime.Format(expected.TransferredMinutes)));
                    controller.SelectClock((index+1)%5); controller.ChooseAction(TimeAction.Collect); controller.KeepPeekedClock();
                    Assert.That(match.CurrentClock, Is.EqualTo(index)); Assert.That(match.Action, Is.EqualTo(action));
                    yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.SettlementHold);
                    Assert.That(controller.View.PlayerLife.text, Is.EqualTo(GameTime.Format(expected.OwnLifeMinutes)));
                    Assert.That(controller.View.AiLife.text, Is.EqualTo(GameTime.Format(expected.OpponentLifeMinutes)));
                    Assert.That(controller.View.Bank.text, Is.EqualTo(GameTime.Format(expected.BankMinutes)));
                    if (expected.OwnLifeMinutes == 0 || expected.OpponentLifeMinutes == 0)
                    { Assert.That(match.Stage,Is.EqualTo(TurnStage.GameOver)); Assert.That(controller.View.IsResultVisible,Is.False); }
                    foreach (var label in new[] {controller.View.PlayerLife,controller.View.AiLife,controller.View.Bank})
                    {
                        var bounds=label.GetComponent<Renderer>().bounds;
                        for (int corner=0;corner<8;corner++)
                        {
                            var point=camera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                            Assert.That(point.z,Is.GreaterThan(0)); Assert.That(point.x,Is.InRange(.01f,.99f)); Assert.That(point.y,Is.InRange(.01f,.99f));
                        }
                    }
                    pose=root.position; cam=camera.transform.position; yield return new WaitForSecondsRealtime(.07f);
                    Assert.That(root.position,Is.EqualTo(pose)); Assert.That(camera.transform.position,Is.EqualTo(cam));
                    yield return Wait(() => closed==1); Assert.That(match.UsedCount,Is.EqualTo(1));
                    var boundsWatch=new Bounds(); bool first=true;
                    foreach(var renderer in controller.View.Clocks[index].H2Presentation.Watch.GetComponentsInChildren<Renderer>())
                    { if(first){boundsWatch=renderer.bounds;first=false;}else boundsWatch.Encapsulate(renderer.bounds); }
                    for(int corner=0;corner<8;corner++)
                    {
                        var point=camera.WorldToViewportPoint(boundsWatch.center+Vector3.Scale(boundsWatch.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                        Assert.That(point.x,Is.InRange(.01f,.99f)); Assert.That(point.y,Is.InRange(.01f,.99f));
                    }
                    Assert.That(Quaternion.Angle(hand,Quaternion.Euler(0,0,210)),Is.LessThan(.01f));
                    Assert.That(Quaternion.Angle(controller.View.Clocks[index].H2Presentation.Watch.LidPivot.localRotation,Quaternion.identity),Is.LessThan(.01f));
                    yield return Wait(() => !focus.IsPresentationCameraOwned); motion.CloseCompleted -= handler;
                    Assert.That(closed,Is.EqualTo(1)); Assert.That(match.UsedCount,Is.EqualTo(1));
                    Assert.That(camera.transform.position,Is.EqualTo(focus.OverviewPosition));
                }
            Assert.That(outcomes.Count,Is.EqualTo(4));
        }

        [UnityTest] public IEnumerator BankCapKeepsFinalBalancesAndDoesNotRepeatSettlement()
        {
            settings.Rules.InitialLifeHours=1000; settings.Rules.MinimumHours=settings.Rules.MaximumHours=100;
            controller.RestartGame(); yield return Wait(() => controller.Match.Stage==TurnStage.AwaitingClock);
            controller.SelectClock(0); yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration);
            var match=controller.Match;
            yield return Wait(() => motion.Phase==H2TurnPresentation.TurnPhase.SettlementHold);
            Assert.That(match.TransferredMinutes,Is.EqualTo(1200)); Assert.That(match.BankMinutes,Is.Zero);
            Assert.That(controller.View.Bank.text,Is.EqualTo("00:00"));
            int player=match.PlayerLifeMinutes, ai=match.AiLifeMinutes;
            for(int i=0;i<3;i++) { controller.ChooseAction(TimeAction.Ration); controller.SkipAnimation(); yield return null; }
            Assert.That(match.UsedCount,Is.EqualTo(1)); Assert.That(match.PlayerLifeMinutes,Is.EqualTo(player)); Assert.That(match.AiLifeMinutes,Is.EqualTo(ai));
            yield return Wait(() => !focus.IsPresentationCameraOwned);
        }

        [UnityTest] public IEnumerator RestartAndSpaceCleanEveryPostRevealPhase()
        {
            foreach(var phase in new[]{H2TurnPresentation.TurnPhase.SettlementTransition,H2TurnPresentation.TurnPhase.SettlementLead,
                H2TurnPresentation.TurnPhase.CountingTime,H2TurnPresentation.TurnPhase.SettlementHold,H2TurnPresentation.TurnPhase.Closing,H2TurnPresentation.TurnPhase.Closed})
            {
                controller.RestartGame(); yield return Wait(() => controller.Match.Stage==TurnStage.AwaitingClock);
                controller.SelectClock(0); yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration);
                yield return Wait(() => motion.Phase==phase && (phase != H2TurnPresentation.TurnPhase.Closed ||
                    controller.View.Lifecycle.Phase == H2ClockLifecyclePresentation.LifecyclePhase.MovingToHatch)); controller.SkipAnimation(); yield return null;
                controller.RestartGame();
                Assert.That(controller.Match.UsedCount,Is.Zero); Assert.That(focus.IsPresentationCameraOwned,Is.True); // 새 4-A가 소유한다.
                yield return Wait(() => controller.Match.Stage==TurnStage.AwaitingClock);
                Assert.That(focus.IsPresentationCameraOwned,Is.False); Assert.That(camera.transform.position,Is.EqualTo(focus.OverviewPosition));
                foreach(var token in controller.View.Clocks) Assert.That(token.H2Presentation.MotionRoot.localPosition,Is.EqualTo(Vector3.zero));
            }
        }
        private IEnumerator Wait(Func<bool> condition)
        {
            float end=Time.realtimeSinceStartup+10;
            while(!condition()&&Time.realtimeSinceStartup<end) yield return null;
            Assert.That(condition(),Is.True,controller.Match.Stage+" / "+motion.Phase);
        }
    }
}
#endif
