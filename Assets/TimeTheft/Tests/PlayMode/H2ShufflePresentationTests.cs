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
    public sealed class H2ShufflePresentationTests
    {
        private TimeTheftGameController controller;private TimeTheftSettings settings;
        private H2ShufflePresentation shuffle;private Camera camera;private TabletopFocusCamera focus;
        private InputTestFixture input;private Mouse mouse;private Keyboard keyboard;private float oldTime;
        [Serializable] private sealed class Trace { public int[] destinationToSource,startInstanceIds,endInstanceIds;public string[] endMeshCenters;public float minimumCaseGap;public int passes; }
        [Serializable] private sealed class Traces { public Trace[] permutations; }
        private sealed class ForcedRandom: System.Random
        {
            private readonly int[] draws;public bool Closing;private int used;
            public ForcedRandom(int[] desired):base(17)
            {int[] ids={0,1,2,3,4};var q=new List<int>();for(int i=4;i>0;i--){int j=Array.IndexOf(ids,desired[i],0,i+1);q.Add(j);int s=ids[i];ids[i]=ids[j];ids[j]=s;}draws=q.ToArray();}
            public override int Next(int max)=>Closing?draws[used++]:base.Next(max);
        }
        private static IEnumerable<int[]> Permutations(int[] order=null,int start=0)
        {
            order=order??new[]{0,1,2,3,4};if(start==5){yield return (int[])order.Clone();yield break;}
            for(int i=start;i<5;i++){int s=order[start];order[start]=order[i];order[i]=s;foreach(var p in Permutations(order,start+1))yield return p;s=order[start];order[start]=order[i];order[i]=s;}
        }
        [UnitySetUp] public IEnumerator Load()
        {
            oldTime=Time.timeScale;GameUiTestInput.DisableSceneModules();input=new InputTestFixture();input.Setup();
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse=InputSystem.AddDevice<Mouse>("H2ShuffleMouse");keyboard=InputSystem.AddDevice<Keyboard>("H2ShuffleKeyboard");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity",new LoadSceneParameters(LoadSceneMode.Single));yield return null;
            controller=UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();settings=UnityEngine.Object.Instantiate(controller.Settings);
            settings.RandomSeed=635;settings.Rules.InitialLifeHours=100;settings.Rules.InitialItemCount=settings.Rules.ItemsPerBatch=0;settings.Rules.EnableAiItemStrategy=false;
            settings.PreviewDuration=settings.ShuffleDuration=settings.RevealDuration=settings.TransferDuration=settings.ResultHoldDuration=settings.DiscardDuration=.1f;settings.AiThinkDuration=.3f;
            var so=new SerializedObject(controller);so.FindProperty("settings").objectReferenceValue=settings;so.ApplyModifiedPropertiesWithoutUndo();GameUiTestInput.ShortenBatchReveal(controller);
            shuffle=controller.View.ShufflePresentation;shuffle.SecondsPerPass=.08f;
            focus=controller.View.FocusCamera;camera=focus.GetComponent<Camera>();input.Set(mouse.position,camera.pixelRect.max+Vector2.one*20);Time.timeScale=0;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(controller)controller.enabled=false;if(focus)focus.enabled=false;GameUiTestInput.DisableSceneModules();
            if(settings)UnityEngine.Object.Destroy(settings);Time.timeScale=oldTime;yield return null;input.TearDown();
        }
        private static Renderer Case(ClockTokenView token)=>token.H2Presentation.Watch.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Case");
        private void Hidden()
        {
            foreach(var token in controller.View.Clocks)
            {var h=token.H2Presentation;Assert.That(h.IsOpen||h.IsRed,Is.False);Assert.That(h.DisplayedHours,Is.Null);Assert.That(token.SelectionTarget.InputEnabled,Is.False);Assert.That(Quaternion.Angle(h.Watch.LidPivot.localRotation,Quaternion.identity),Is.LessThan(.001));}
        }
        [UnityTest] public IEnumerator All120PermutationsTrackRealMeshObjectsThroughClosedCollisionFreePathsAndRebase()
        {
            var traces=new List<Trace>();int count=0;
            foreach(var desired in Permutations())
            {
                controller.RestartGame();controller.StopAllCoroutines();controller.View.CancelPresentation();
                var rng=new ForcedRandom(desired);var rules=new GameRules{InitialItemCount=0,ItemsPerBatch=0,MinimumHours=3,MaximumHours=3};
                var match=new TimeTheftMatch(rules,rng,new System.Random(3));match.BeginBatch();rng.Closing=true;match.CloseAndShuffle();
                var sources=controller.View.Clocks;var ids=sources.Select(c=>c.GetInstanceID()).ToArray();
                var slots=sources.Select(c=>c.transform.position).ToArray();var offsets=sources.Select(c=>Case(c).bounds.center-c.transform.position).ToArray();
                shuffle.Prepare(controller.View,match.LastShuffle);shuffle.Prepare(controller.View,match.LastShuffle);
                Assert.That(shuffle.PassCount,Is.InRange(0,5));var prior=sources.Select(c=>Case(c).bounds.center).ToArray();float gap=float.MaxValue;
                for(int f=0;f<=60;f++)
                {
                    shuffle.Frame(f/60f);Hidden();
                    for(int s=0;s<5;s++)
                    {
                        var b=Case(sources[s]).bounds;Assert.That(Vector3.Distance(prior[s],b.center),Is.LessThan(.65f),"연속된 실제 메시 위치 / "+string.Join(",",desired));prior[s]=b.center;
                        Assert.That(sources[s].H2Presentation.MotionRoot.localPosition,Is.EqualTo(Vector3.zero));
                        for(int other=s+1;other<5;other++)
                        {
                            var ob=Case(sources[other]).bounds;var delta=b.center-ob.center;delta.y=0;
                            float clearance=delta.magnitude-.5f*Mathf.Max(b.size.x,b.size.z)-.5f*Mathf.Max(ob.size.x,ob.size.z);
                            gap=Mathf.Min(gap,clearance);Assert.That(clearance,Is.GreaterThan(.02f),"Case clearance / "+string.Join(",",desired));
                        }
                        var p=camera.WorldToViewportPoint(b.center);Assert.That(p.x,Is.InRange(.02f,.98f));Assert.That(p.y,Is.InRange(.02f,.98f));
                    }
                    if(f%15==0)yield return null;
                }
                shuffle.Complete();shuffle.Complete();var final=controller.View.Clocks;
                for(int dest=0;dest<5;dest++)
                {
                    int source=desired[dest];Assert.That(final[dest],Is.SameAs(sources[source]));Assert.That(final[dest].SlotIndex,Is.EqualTo(dest));
                    Assert.That(Vector3.Distance(final[dest].transform.position,slots[dest]),Is.LessThan(.00001f));
                    Assert.That(Vector3.Distance(Case(final[dest]).bounds.center,slots[dest]+offsets[source]),Is.LessThan(.00001f));
                }
                Assert.That(focus.IsPresentationCameraOwned,Is.False);
                traces.Add(new Trace{destinationToSource=desired,startInstanceIds=ids,endInstanceIds=final.Select(c=>c.GetInstanceID()).ToArray(),endMeshCenters=final.Select(c=>Case(c).bounds.center.ToString("F5")).ToArray(),minimumCaseGap=gap,passes=shuffle.PassCount});count++;
            }
            Assert.That(count,Is.EqualTo(120));Directory.CreateDirectory("Temp/H2Shuffle");File.WriteAllText("Temp/H2Shuffle/all120-physical-traces.json",JsonUtility.ToJson(new Traces{permutations=traces.ToArray()},true));
        }

        [UnityTest] public IEnumerator ActualShuffleColliderClickAndRevealMatchThePublicSourceForEveryDestination()
        {
            var seen=new HashSet<string>();
            foreach(int seed in new[]{635,142})for(int destination=0;destination<5;destination++)
            {
                settings.RandomSeed=seed;controller.RestartGame();
                var before=Enumerable.Range(0,5).Select(i=>controller.Match.GetClock(i).Face.Value).ToArray();var physical=controller.View.Clocks;
                yield return Wait(()=>shuffle.IsRunning);var plan=controller.Match.LastShuffle;seen.Add(string.Join(",",plan.DestinationToSource));
                while(shuffle.IsRunning)
                {Hidden();controller.SelectClock(destination);controller.ChooseAction(TimeAction.Collect);Assert.That(controller.Match.CurrentClock,Is.EqualTo(-1));Assert.That(Enumerable.Range(0,5).All(i=>!controller.Match.GetClock(i).Face.HasValue),Is.True);yield return null;}
                yield return Wait(()=>controller.Match.Stage==TurnStage.AwaitingClock);
                var token=controller.View.Clocks[destination];int source=plan.DestinationToSource[destination];Assert.That(token,Is.SameAs(physical[source]));
                focus.SendMessage("OnApplicationFocus",true);camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus",true);
                for(int f=0;f<10;f++){input.Set(mouse.position,(Vector2)camera.WorldToScreenPoint(token.GetComponent<Collider>().bounds.center));yield return null;}
                Assert.That(focus.HoveredTarget,Is.SameAs(token.SelectionTarget));input.Press(mouse.leftButton);yield return null;input.Release(mouse.leftButton);
                yield return GameUiTestInput.ClockMotionReady(controller);Assert.That(controller.Match.CurrentClock,Is.EqualTo(destination));Assert.That(controller.Match.GetClock(destination).Face,Is.Null);
                controller.ChooseAction(TimeAction.Ration);var face=controller.Match.GetClock(destination).Face.Value;Assert.That(face,Is.EqualTo(before[source]));
                Assert.That(token.H2Presentation.DisplayedHours,Is.EqualTo(face.Hours));Assert.That(token.H2Presentation.IsRed,Is.EqualTo(face.Kind==ClockKind.Red));
            }
            Assert.That(seen.Count,Is.GreaterThanOrEqualTo(2));
        }

        [UnityTest] public IEnumerator RestartCancelDisableAndSpaceNeverApplyTheChosenPermutationTwice()
        {
            foreach(float cut in new[]{0f,.45f,.92f})
            {
                controller.RestartGame();yield return Wait(()=>shuffle.IsRunning&&shuffle.Progress>=cut);controller.RestartGame();
                Assert.That(controller.Match.LastShuffle,Is.Null);Assert.That(shuffle.IsRunning,Is.False);
                var clocks=controller.View.Clocks;for(int i=0;i<5;i++){Assert.That(clocks[i].SlotIndex,Is.EqualTo(i));Assert.That(clocks[i].transform.position.x,Is.EqualTo((i-2)*.98f).Within(.00001f));}
            }
            yield return Wait(()=>shuffle.IsRunning);var plan=controller.Match.LastShuffle;var sources=shuffle.PhysicalSources;
            shuffle.enabled=false;yield return Wait(()=>controller.Match.Stage==TurnStage.AwaitingClock);
            for(int d=0;d<5;d++)Assert.That(controller.View.Clocks[d],Is.SameAs(sources[plan.DestinationToSource[d]]));
            Assert.That(focus.IsPresentationCameraOwned,Is.False);shuffle.enabled=true;shuffle.SecondsPerPass=5;
            controller.RestartGame();yield return Wait(()=>shuffle.IsRunning);plan=controller.Match.LastShuffle;sources=shuffle.PhysicalSources;
            input.Press(keyboard.spaceKey);InputSystem.Update();controller.SendMessage("Update");input.Release(keyboard.spaceKey);InputSystem.Update();
            yield return Wait(()=>controller.Match.Stage==TurnStage.AwaitingClock);
            Assert.That(controller.Match.LastShuffle,Is.SameAs(plan));for(int d=0;d<5;d++)Assert.That(controller.View.Clocks[d],Is.SameAs(sources[plan.DestinationToSource[d]]));
            shuffle.SecondsPerPass=.08f;controller.RestartGame();yield return Wait(()=>shuffle.IsRunning&&shuffle.Progress>.1f);controller.enabled=false;
            Assert.That(shuffle.IsRunning,Is.False);Assert.That(focus.IsPresentationCameraOwned,Is.False);controller.enabled=true;
            yield return Wait(()=>controller.Match.Stage==TurnStage.AwaitingClock);
        }

        [UnityTest] public IEnumerator AiFirstUsesTheSameReachedObjectAndActualSourceFace()
        {
            settings.Rules.FirstParticipant=Participant.Ai;controller.RestartGame();
            var before=Enumerable.Range(0,5).Select(i=>controller.Match.GetClock(i).Face.Value).ToArray();var physical=controller.View.Clocks;
            yield return Wait(()=>shuffle.IsRunning);var plan=controller.Match.LastShuffle;
            yield return Wait(()=>controller.View.TurnPresentation.Phase==H2TurnPresentation.TurnPhase.DecisionHold);
            int d=controller.Match.CurrentClock;Assert.That(controller.View.Clocks[d],Is.SameAs(physical[plan.DestinationToSource[d]]));Assert.That(controller.Match.GetClock(d).Face,Is.Null);
            yield return Wait(()=>controller.Match.Stage==TurnStage.Revealing);Assert.That(controller.Match.GetClock(d).Face.Value,Is.EqualTo(before[plan.DestinationToSource[d]]));
            yield return Wait(()=>controller.Match.ActiveParticipant==Participant.Player&&controller.Match.Stage==TurnStage.AwaitingClock);
            Assert.That(controller.Match.UsedCount,Is.EqualTo(1));Assert.That(focus.IsPresentationCameraOwned,Is.False);
        }
        [UnityTest] public IEnumerator OuterPairKeepsTheOriginalMirroredLaneAndYaw()
        {
            controller.RestartGame();controller.StopAllCoroutines();controller.View.CancelPresentation();
            var rng=new ForcedRandom(new[]{0,1,2,4,3});var match=new TimeTheftMatch(new GameRules{InitialItemCount=0,ItemsPerBatch=0},rng,new System.Random(1));
            match.BeginBatch();rng.Closing=true;match.CloseAndShuffle();shuffle.Prepare(controller.View,match.LastShuffle);shuffle.Frame(.45f);
            var model=shuffle.PhysicalSources[3].H2Presentation;
            var source=AssetDatabase.LoadAllAssetsAtPath("Assets/TimeTheft/PocketWatch/Shuffle/Source/Shuffle_Clock03.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
            var bindings=AnimationUtility.GetCurveBindings(source);var values=new float[4];int i=0;
            foreach(string component in new[]{"x","y","z","w"})
            {var b=bindings.First(k=>k.path=="H2_Watch/MotionRoot"&&k.propertyName=="m_LocalRotation."+component);values[i++]=AnimationUtility.GetEditorCurve(source,b).Evaluate(4.5f/24);}
            Assert.That(Quaternion.Angle(model.MotionRoot.localRotation,new Quaternion(values[0],values[1],values[2],values[3])),Is.LessThan(.001f));
            shuffle.Cancel();yield return null;
        }
        [UnityTest] public IEnumerator ControllerFeedsRawTimeToAlreadyEasedSourcePasses()
        {
            settings.ShuffleDuration=3;controller.RestartGame();double started=0;float initialDelta=0;
            Action<ClockShuffle> handler=p=>{started=Time.realtimeSinceStartupAsDouble;initialDelta=Time.unscaledDeltaTime;};shuffle.Started+=handler;
            yield return Wait(()=>shuffle.IsRunning);float duration=shuffle.Duration(settings.ShuffleDuration);int checks=0;
            while(shuffle.IsRunning)
            {
                float raw=(float)(Time.realtimeSinceStartupAsDouble-started+initialDelta)/duration;
                if(raw>.1f&&raw<.9f)
                {Assert.That(shuffle.Progress,Is.EqualTo(raw).Within(Mathf.Max(.035f,2*Time.unscaledDeltaTime/duration)),"원본 가감속에 전역 easing을 중복 적용하지 않는다");checks++;}
                yield return null;
            }
            shuffle.Started-=handler;Assert.That(checks,Is.GreaterThan(5));
        }
        private IEnumerator Wait(Func<bool> predicate)
        {float end=Time.realtimeSinceStartup+8;while(!predicate()&&Time.realtimeSinceStartup<end)yield return null;Assert.That(predicate(),Is.True,controller.Match.Stage+" / shuffle "+shuffle.Progress);}
    }
}
#endif
