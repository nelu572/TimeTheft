#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
namespace TimeTheft.Editor
{
    public sealed class H2LifecycleCapture : MonoBehaviour
    {
        [Serializable] private sealed class Body { public int slot,id,hours;public bool active,input,collider,red;public float lid;public string position; }
        [Serializable] private sealed class Sample { public float time,elapsed,hatchProgress;public int frame,batch,used,discarded,clock,player,ai,bank;public string stage,turn,phase,camera;public bool discard;public Body[] bodies; }
        [Serializable] private sealed class Event { public float time;public string name;public int batch,slot,id,used,discarded,player,ai,bank;public bool isDiscard,active,facePresent; }
        [Serializable] private sealed class Report
        {
            public string method="Actual TimeTheftGame/timeScale1/original durations. Runtime settings copy changes seed only. Public Controller selection/action button drives player; AI remains enabled; player item phase ends without using an item. No pose playback/captureDeltaTime/private Face access. Held frames preserve elapsed time.";
            public int fps,frames,uniqueFrames,heldFrames;public float actualSeconds;public Event[] events;public Sample[] samples;
        }
        public string Status {get;private set;}="Idle";private bool recording;private MediaEncoder encoder;
        public void Record(string path,int seconds=80)
        {
            if(recording||FindObjectsByType<H2LifecycleCapture>(FindObjectsSortMode.None).Any(x=>x!=this&&x.recording)||!Application.isPlaying)return;
            recording=true;StartCoroutine(Movie(path,seconds));
        }
        private IEnumerator Movie(string path,int seconds)
        {
            const int fps=24;Directory.CreateDirectory(Path.GetDirectoryName(path));string images=Path.Combine(Path.GetDirectoryName(path),"Images");Directory.CreateDirectory(images);
            var c=FindFirstObjectByType<TimeTheftGameController>();var original=c.Settings;var settings=Instantiate(original);settings.RandomSeed=635;
            var so=new SerializedObject(c);so.FindProperty("settings").objectReferenceValue=settings;so.ApplyModifiedPropertiesWithoutUndo();
            var target=new Texture2D(1280,720,TextureFormat.RGBA32,false);var scaled=new RenderTexture(1280,720,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);scaled.Create();
            var attributes=new VideoTrackAttributes{width=1280,height=720,frameRate=new MediaRational(fps),includeAlpha=false,bitRateMode=VideoBitrateMode.High};
            var samples=new List<Sample>();var events=new List<Event>();var saved=new HashSet<string>();int frames=0;float start=0;Coroutine drive=null;
            var life=c.View.Lifecycle;
            Action<int,bool> began=(i,d)=>events.Add(EventRow(c,"RemoveStarted",i,d,Time.realtimeSinceStartup-start));
            Action<int,bool> ended=(i,d)=>events.Add(EventRow(c,"RemoveCompleted",i,d,Time.realtimeSinceStartup-start));
            Action supplied=()=>events.Add(EventRow(c,"SupplyCompleted",-1,false,Time.realtimeSinceStartup-start));
            try
            {
                encoder=new MediaEncoder(path,attributes);Status="Warming";yield return null;yield return new WaitForEndOfFrame();yield return null;
                life.RemoveStarted+=began;life.RemoveCompleted+=ended;life.SupplyCompleted+=supplied;
                c.RestartGame();start=Time.realtimeSinceStartup;drive=StartCoroutine(Drive(c,start+seconds));Status="Recording";
                while(frames<seconds*fps)
                {
                    yield return new WaitForEndOfFrame();float time=Time.realtimeSinceStartup-start;int due=Mathf.Min(seconds*fps,Mathf.FloorToInt(time*fps)+1);if(due<=frames)continue;
                    var old=RenderTexture.active;var screen=ScreenCapture.CaptureScreenshotAsTexture();Graphics.Blit(screen,scaled);RenderTexture.active=scaled;target.ReadPixels(new Rect(0,0,1280,720),0,0);target.Apply();RenderTexture.active=old;Destroy(screen);
                    var m=c.Match;string label=null;string prefix=life.IsDiscarding?"LastDiscard":m.Batch==1?"Used"+m.UsedCount:"B"+m.Batch+"Used"+m.UsedCount;
                    if(life.Phase==H2ClockLifecyclePresentation.LifecyclePhase.Supplying&&life.PhaseElapsed>life.SupplyDuration*.4f)label="B"+m.Batch+"-Supply";
                    if(c.View.BatchReveal.Phase==H2BatchRevealPresentation.RevealPhase.Observing)label="B"+m.Batch+"-InitialOpen";
                    var turn=c.View.TurnPresentation;
                    if(turn.Phase==H2TurnPresentation.TurnPhase.Selecting&&turn.PhaseElapsed>turn.SelectDuration*.5f)label=prefix+"-Select";
                    if(turn.Phase==H2TurnPresentation.TurnPhase.DecisionHold)label=prefix+"-Decision";
                    if(turn.Phase==H2TurnPresentation.TurnPhase.ResultPose)label=prefix+"-ResultOpen";
                    if(turn.Phase==H2TurnPresentation.TurnPhase.SettlementHold)label=prefix+"-Settlement";
                    if(life.Phase==H2ClockLifecyclePresentation.LifecyclePhase.MovingToHatch&&life.PhaseElapsed>life.MoveDuration*.45f)label=prefix+"-Move";
                    if(life.Phase==H2ClockLifecyclePresentation.LifecyclePhase.HatchCycle)
                    {
                        float t=life.Hatch.CycleProgress;
                        if(t>.35f&&t<.45f)label=prefix+"-Open";
                        if(t>.45f&&t<.6f)label=prefix+"-Drop";
                        if(t>.78f&&t<.9f)label=prefix+"-Close";
                    }
                    if(life.Phase==H2ClockLifecyclePresentation.LifecyclePhase.BinHold)label=prefix+"-Locked";
                    if(label!=null&&saved.Add(label))File.WriteAllBytes(Path.Combine(images,label+".png"),target.EncodeToPNG());
                    samples.Add(new Sample{time=time,frame=frames,batch=m.Batch,used=m.UsedCount,discarded=m.DiscardedCount,clock=life.ClockIndex,player=m.PlayerLifeMinutes,ai=m.AiLifeMinutes,bank=m.BankMinutes,stage=m.Stage.ToString(),turn=c.View.TurnPresentation.Phase.ToString(),phase=life.Phase.ToString(),elapsed=life.PhaseElapsed,hatchProgress=life.Hatch.CycleProgress,discard=life.IsDiscarding,camera=c.View.FocusCamera.transform.position.ToString("F5"),
                        bodies=c.View.Clocks.Select((x,i)=>new Body{slot=i,id=x.GetInstanceID(),hours=x.H2Presentation.DisplayedHours??0,active=x.gameObject.activeSelf,input=x.SelectionTarget.InputEnabled,collider=x.GetComponent<Collider>().enabled,red=x.H2Presentation.IsRed,lid=Quaternion.Angle(Quaternion.identity,x.H2Presentation.Watch.LidPivot.localRotation),position=x.H2Presentation.MotionRoot.position.ToString("F5")}).ToArray()});
                    while(frames<due){if(!encoder.AddFrame(target))throw new InvalidOperationException("Encoder "+frames);frames++;}
                }
                File.WriteAllText(path+".capture.json",JsonUtility.ToJson(new Report{fps=fps,frames=frames,uniqueFrames=samples.Count,heldFrames=frames-samples.Count,actualSeconds=Time.realtimeSinceStartup-start,events=events.ToArray(),samples=samples.ToArray()},true));Status="Recorded";
            }
            finally
            {
                life.RemoveStarted-=began;life.RemoveCompleted-=ended;life.SupplyCompleted-=supplied;if(drive!=null)StopCoroutine(drive);if(encoder!=null){encoder.Dispose();encoder=null;}
                so.FindProperty("settings").objectReferenceValue=original;so.ApplyModifiedPropertiesWithoutUndo();Destroy(settings);Destroy(target);if(RenderTexture.active==scaled)RenderTexture.active=null;scaled.Release();Destroy(scaled);recording=false;
            }
        }
        private static Event EventRow(TimeTheftGameController c,string name,int i,bool discard,float time)
        {
            var m=c.Match;var token=i>=0?c.View.Clocks[i]:null;
            return new Event{time=time,name=name,batch=m.Batch,slot=i,id=token?token.GetInstanceID():0,used=m.UsedCount,discarded=m.DiscardedCount,player=m.PlayerLifeMinutes,ai=m.AiLifeMinutes,bank=m.BankMinutes,isDiscard=discard,active=token&&token.gameObject.activeSelf,facePresent=i>=0&&m.GetClock(i).Face.HasValue};
        }
        private IEnumerator Drive(TimeTheftGameController c,float deadline)
        {
            var last=c.Match.Stage;float since=Time.realtimeSinceStartup;bool selecting=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                var m=c.Match;if(last!=m.Stage||(selecting&&!c.IsPresentingClock)){last=m.Stage;since=Time.realtimeSinceStartup;}selecting=c.IsPresentingClock;
                if(m.ActiveParticipant==Participant.Player&&!c.IsPresentingItem)
                {
                    float elapsed=Time.realtimeSinceStartup-since;
                    if(m.Stage==TurnStage.AwaitingItems&&elapsed>.5f)c.EndItemPhase();
                    if(m.Stage==TurnStage.AwaitingClock&&elapsed>.6f)c.SelectClock(m.GetAvailableClockIndices().First());
                    if(m.Stage==TurnStage.AwaitingKeyDecision&&!c.IsPresentingClock&&elapsed>2)c.KeepPeekedClock();
                    if(m.Stage==TurnStage.AwaitingAction&&!c.IsPresentingClock&&elapsed>2)c.View.RationButton.onClick.Invoke();
                }
                yield return null;
            }
        }
        private void OnDisable(){if(encoder!=null){encoder.Dispose();encoder=null;}recording=false;}
    }
}
#endif
