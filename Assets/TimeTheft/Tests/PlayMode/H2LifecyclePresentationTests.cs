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
    public sealed class H2LifecyclePresentationTests
    {
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private H2ClockLifecyclePresentation life;
        private TabletopFocusCamera focus;
        private InputTestFixture input;
        private float oldTime;

        [UnitySetUp] public IEnumerator Load()
        {
            oldTime = Time.timeScale; GameUiTestInput.DisableSceneModules(); input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var mouse = InputSystem.AddDevice<Mouse>("H2LifecycleMouse"); InputSystem.AddDevice<Keyboard>("H2LifecycleKeyboard");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single)); yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(controller.Settings); settings.RandomSeed = 635;
            settings.Rules.FirstParticipant = Participant.Player; settings.Rules.InitialLifeHours = 100; settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0; settings.Rules.EnableAiItemStrategy = false;
            settings.PreviewDuration = settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = .08f; settings.AiThinkDuration = .15f;
            var so = new SerializedObject(controller); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller); life = controller.View.Lifecycle; Assert.That(life, Is.Not.Null);
            life.SupplyDuration = .2f; life.MoveDuration = .18f; life.HatchCycleDuration = .4f; life.BinHoldDuration = life.DiscardBinHoldDuration = .08f;
            focus = controller.View.FocusCamera; input.Set(mouse.position, focus.GetComponent<Camera>().pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0; controller.RestartGame();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false; if (focus) focus.enabled = false; GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings); Time.timeScale = oldTime; yield return null; input.TearDown();
        }
        private void DrivePublicTurn()
        {
            var match = controller.Match;
            if (match.ActiveParticipant != Participant.Player || controller.IsPresentingClock || controller.IsPresentingItem || life.IsRunning) return;
            if (match.Stage == TurnStage.AwaitingItems) controller.EndItemPhase();
            else if (match.Stage == TurnStage.AwaitingClock) controller.SelectClock(match.GetAvailableClockIndices().First());
            else if (match.Stage == TurnStage.AwaitingAction) controller.ChooseAction(TimeAction.Ration);
        }
        private static void ClosedAndUnselectable(ClockTokenView token)
        {
            var model = token.H2Presentation;
            Assert.That(model.IsOpen || model.IsRed, Is.False); Assert.That(model.DisplayedHours, Is.Null);
            Assert.That(Quaternion.Angle(model.Watch.LidPivot.localRotation, Quaternion.identity), Is.LessThan(.001f));
            Assert.That(token.SelectionTarget.InputEnabled, Is.False);
        }
        private void ResetState()
        {
            Assert.That(controller.Match.UsedCount, Is.Zero); Assert.That(controller.Match.DiscardedCount, Is.Zero);
            Assert.That(controller.View.Clocks.Select(c => c.GetInstanceID()).Distinct().Count(), Is.EqualTo(5));
            Assert.That(Quaternion.Angle(life.Hatch.FlapPivot.localRotation, Quaternion.identity), Is.LessThan(.001f));
            foreach (var token in controller.View.Clocks) { Assert.That(token.gameObject.activeSelf, Is.True); ClosedAndUnselectable(token); }
        }

        [UnityTest] public IEnumerator FullBatchRemovesTheReachedObjectsExactlyOnceDiscardsPrivatelyAndSuppliesNextBatch()
        {
            var sources = controller.View.Clocks; var initial = Enumerable.Range(0, 5).Select(i => controller.Match.GetClock(i).Face.Value).ToArray();
            ClockShuffle plan = null; int supplies = 0, usedRemovals = 0, discards = 0; var removed = new HashSet<int>();
            var expected = new Dictionary<int, int[]>(); int[] during = null; int duringUsed = 0;
            controller.View.ShufflePresentation.Started += p => { if (controller.Match.Batch == 1) plan = p; };
            life.SupplyCompleted += () => supplies++;
            life.RemoveStarted += (index, discarded) =>
            {
                Assert.That(plan, Is.Not.Null); var match = controller.Match; var token = controller.View.Clocks[index];
                Assert.That(token, Is.SameAs(sources[plan.DestinationToSource[index]])); Assert.That(removed.Add(token.GetInstanceID()), Is.True);
                ClosedAndUnselectable(token); Assert.That(token.GetComponent<Collider>().enabled, Is.False);
                Assert.That(controller.View.Clocks.All(c => !c.SelectionTarget.InputEnabled), Is.True);
                during = new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes }; duringUsed = match.UsedCount;
                if (discarded)
                {
                    Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete)); Assert.That(match.UsedCount, Is.EqualTo(4));
                    Assert.That(match.GetClock(index).Visibility, Is.EqualTo(ClockVisibility.Discarded)); Assert.That(match.GetClock(index).Face, Is.Null); discards++;
                }
                else
                {
                    Assert.That(life.ClosedClockIndex, Is.EqualTo(index)); Assert.That(expected.ContainsKey(match.Turn), Is.True);
                    Assert.That(during, Is.EqualTo(expected[match.Turn])); usedRemovals++;
                }
            };
            life.RemoveCompleted += (index, discarded) =>
            { Assert.That(controller.View.Clocks[index].gameObject.activeSelf, Is.False); during = null; };
            // Setup의 첫 프레임 지연에 관계없이 공급 시작 전부터 이벤트를 관찰한다.
            controller.RestartGame();
            sources = controller.View.Clocks;
            initial = Enumerable.Range(0, 5).Select(i => controller.Match.GetClock(i).Face.Value).ToArray();
            bool nextObserved = false; float deadline = Time.realtimeSinceStartup + 25;
            while (!(controller.Match.Batch == 2 && controller.Match.Stage == TurnStage.AwaitingClock) && Time.realtimeSinceStartup < deadline)
            {
                var match = controller.Match;
                if (match.Batch == 1 && match.Stage == TurnStage.Revealing && !expected.ContainsKey(match.Turn))
                {
                    var face = match.GetClock(match.CurrentClock).Face.Value;
                    Assert.That(face, Is.EqualTo(initial[plan.DestinationToSource[match.CurrentClock]]));
                    bool player = match.ActiveParticipant == Participant.Player;
                    var result = TimeTheftSettlement.Calculate(face, match.Action, player ? match.PlayerLifeMinutes : match.AiLifeMinutes,
                        player ? match.AiLifeMinutes : match.PlayerLifeMinutes, match.BankMinutes, false, false, 50, 50);
                    expected.Add(match.Turn, new[] { player ? result.OwnLifeMinutes : result.OpponentLifeMinutes, player ? result.OpponentLifeMinutes : result.OwnLifeMinutes, result.BankMinutes });
                }
                if (during != null)
                {
                    Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes }, Is.EqualTo(during), "회수는 정산을 다시 실행하지 않는다");
                    Assert.That(match.UsedCount, Is.EqualTo(duringUsed)); ClosedAndUnselectable(controller.View.Clocks[life.ClockIndex]);
                    var parts = controller.View.Clocks[life.ClockIndex].H2Presentation.Watch.GetComponentsInChildren<Renderer>();
                    var bounds = parts[0].bounds; foreach (var part in parts) bounds.Encapsulate(part.bounds);
                    if (life.Phase == H2ClockLifecyclePresentation.LifecyclePhase.HatchCycle)
                    {
                        if (bounds.min.y < 1.0074f && bounds.max.y > 1.0076f)
                        {
                            Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(2.058f), "실제 전체 시계 메시가 개구부를 통과한다");
                            Assert.That(bounds.max.x, Is.LessThanOrEqualTo(3.442f));
                            Assert.That(bounds.min.z, Is.GreaterThanOrEqualTo(-1.502f));
                            Assert.That(bounds.max.z, Is.LessThanOrEqualTo(-.398f));
                        }
                        Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(-.6962f), "수납부 바닥을 관통하지 않는다");
                    }
                }
                if (life.IsSupplying)
                    foreach (var token in controller.View.Clocks) { ClosedAndUnselectable(token); Assert.That(token.GetComponent<Collider>().enabled, Is.False); }
                if (match.Batch == 2 && controller.View.BatchReveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing)
                {
                    nextObserved = true;
                    for (int i = 0; i < 5; i++) Assert.That(controller.View.Clocks[i].H2Presentation.DisplayedHours, Is.EqualTo(match.GetClock(i).Face.Value.Hours));
                }
                DrivePublicTurn(); yield return null;
            }
            Assert.That(controller.Match.Batch, Is.EqualTo(2)); Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(usedRemovals, Is.EqualTo(4)); Assert.That(discards, Is.EqualTo(1)); Assert.That(removed.Count, Is.EqualTo(5));
            Assert.That(supplies, Is.EqualTo(2)); Assert.That(nextObserved, Is.True); Assert.That(controller.Match.LastShuffle, Is.Not.Null);
            for (int d = 0; d < 5; d++) Assert.That(controller.View.Clocks[d], Is.SameAs(sources[controller.Match.LastShuffle.DestinationToSource[d]]));
            Assert.That(UnityEngine.Object.FindObjectsByType<ClockTokenView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(5), "공급은 기존 다섯 물리 객체를 재사용한다");
            Assert.That(controller.View.Clocks.All(c => c.gameObject.activeSelf && c.GetComponent<Collider>().enabled), Is.True);
            Assert.That(controller.View.Clocks.All(c => c.H2Presentation.MotionRoot.localPosition == Vector3.zero), Is.True);
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
        }

        [UnityTest] public IEnumerator RestartDuringSupplyMoveOpenDropBinAndPrivateDiscardRestoresOneClosedSet()
        {
            controller.RestartGame();
            yield return Wait(() => life.IsSupplying && life.PhaseElapsed >= .05f); controller.RestartGame(); ResetState();
            var phases = new[] { H2ClockLifecyclePresentation.LifecyclePhase.MovingToHatch, H2ClockLifecyclePresentation.LifecyclePhase.HatchCycle, H2ClockLifecyclePresentation.LifecyclePhase.HatchCycle, H2ClockLifecyclePresentation.LifecyclePhase.BinHold };
            var fractions = new[] { 0f, .2f, .65f, 0f };
            for (int cut = 0; cut < phases.Length; cut++)
            {
                var phase = phases[cut];
                yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                controller.SelectClock(0); yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration);
                float fraction = fractions[cut];
                yield return Wait(() => life.Phase == phase && (phase != H2ClockLifecyclePresentation.LifecyclePhase.HatchCycle || life.Hatch.CycleProgress >= fraction));
                controller.RestartGame(); ResetState();
            }
            float deadline = Time.realtimeSinceStartup + 20;
            while (!(life.IsDiscarding && life.Phase == H2ClockLifecyclePresentation.LifecyclePhase.HatchCycle && life.Hatch.CycleProgress >= .3f) && Time.realtimeSinceStartup < deadline)
            { DrivePublicTurn(); yield return null; }
            Assert.That(life.IsDiscarding, Is.True); Assert.That(controller.Match.GetClock(life.ClockIndex).Face, Is.Null);
            controller.RestartGame(); ResetState(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(focus.IsPresentationCameraOwned, Is.False); Assert.That(life.Carrier.activeSelf, Is.False);
        }

        [UnityTest] public IEnumerator SpaceAndComponentOrOwnerDisableReleaseHatchCameraAndUsedClockWithoutASecondSettlement()
        {
            controller.RestartGame();
            controller.SkipAnimation(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            controller.SelectClock(0); yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration);
            yield return Wait(() => life.Phase == H2ClockLifecyclePresentation.LifecyclePhase.MovingToHatch);
            var match = controller.Match; var balances = new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes };
            controller.SkipAnimation(); yield return Wait(() => match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.UsedCount, Is.EqualTo(1)); Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes }, Is.EqualTo(balances));
            Assert.That(controller.View.Clocks[0].gameObject.activeSelf, Is.False); Assert.That(focus.IsPresentationCameraOwned, Is.False);
            foreach (bool disableOwner in new[] { false, true })
            {
                controller.RestartGame(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                controller.SelectClock(0); yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration);
                yield return Wait(() => life.Phase == H2ClockLifecyclePresentation.LifecyclePhase.HatchCycle && life.Hatch.CycleProgress >= .2f);
                if (disableOwner) controller.enabled = false; else life.enabled = false;
                Assert.That(life.IsRunning, Is.False); Assert.That(focus.IsPresentationCameraOwned, Is.False); Assert.That(life.Carrier.activeSelf, Is.False);
                Assert.That(Quaternion.Angle(life.Hatch.FlapPivot.localRotation, Quaternion.identity), Is.LessThan(.001f));
                if (disableOwner) controller.enabled = true;
                else { yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock); Assert.That(controller.Match.UsedCount, Is.EqualTo(1)); life.enabled = true; controller.RestartGame(); }
                yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                Assert.That(controller.View.Clocks.All(c => c.gameObject.activeSelf && c.GetComponent<Collider>().enabled), Is.True);
            }
        }

        [UnityTest] public IEnumerator MainTableRayReachesClosedFlapThenActualReceiverThroughTheOpenAperture()
        {
            controller.StopAllCoroutines(); controller.View.CancelPresentation(); var hatch = life.Hatch; hatch.ResetClosed(); Physics.SyncTransforms();
            var anchor = hatch.Anchor.position; RaycastHit hit;
            Assert.That(Physics.Raycast(new Vector3(anchor.x, 2.5f, anchor.z), Vector3.down, out hit, 4), Is.True);
            Assert.That(hit.collider.name, Is.EqualTo("Hatch_FeltFlap")); Assert.That(hit.point.y, Is.EqualTo(1.0075f).Within(.001f));
            hatch.SampleCycle(.5f); Physics.SyncTransforms();
            foreach (float x in new[] { -.24f, 0, .24f }) foreach (float z in new[] { -.24f, 0, .24f })
            {
                Assert.That(Physics.Raycast(new Vector3(anchor.x + x, 2.5f, anchor.z + z), Vector3.down, out hit, 4), Is.True);
                Assert.That(hit.collider.name, Does.Contain("ImpactFloor"), "기존 상판/BoxCollider가 개구부를 막지 않는다");
                Assert.That(hit.point.y, Is.EqualTo(-.69413f).Within(.001f));
            }
            Assert.That(Physics.Raycast(new Vector3(anchor.x + 1, 2.5f, anchor.z), Vector3.down, out hit, 4), Is.True);
            Assert.That(hit.collider.name, Is.EqualTo("T3_Tabletop_WithHatchHole")); Assert.That(hit.point.y, Is.EqualTo(1.0075f).Within(.001f));
            hatch.ResetClosed(); yield return null;
        }
        private IEnumerator Wait(Func<bool> predicate)
        {
            float end = Time.realtimeSinceStartup + 10;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, controller.Match.Stage + " / " + life.Phase + " / " + life.PhaseElapsed);
        }
    }
}
#endif
