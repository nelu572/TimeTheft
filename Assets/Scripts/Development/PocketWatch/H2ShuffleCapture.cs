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
    // 실제 Controller를 정상 속도로 실행하는 증거 수집기. 씬/Prefab에 저장하지 않는다.
    public sealed class H2ShuffleCapture : MonoBehaviour
    {
        [Serializable] private sealed class Face { public int source, instance, hours; public string kind; }
        [Serializable] private sealed class Body { public int source, instance, slot; public string root, meshCenter; public float lid; public bool input, red; public int hours; }
        [Serializable] private sealed class Sample { public float time, progress; public int frame, currentClock, resultHours; public string stage, phase, batchPhase, camera, resultKind; public Body[] bodies; }
        [Serializable] private sealed class Report
        {
            public string method = "Actual TimeTheftGame Play Mode, timeScale1, original durations and runtime settings copy changing seed only. Public preview Face captured once before hiding; source identity is actual token instance, never inferred from Face. Public Controller selection + existing action button drive demo. Held frames preserve wall-clock time; no captureDeltaTime or pose playback.";
            public int seed, fps, frames, uniqueFrames, heldFrames, passes; public float actualSeconds, shuffleSeconds;
            public int[] destinationToSource, sourceToDestination, reachedSourceBySlot; public Face[] preview; public Sample[] samples;
        }
        public string Status { get; private set; } = "Idle";
        private bool recording; private MediaEncoder encoder;
        public void Record(string path, int seed, int seconds = 28)
        {
            if(recording || FindObjectsByType<H2ShuffleCapture>(FindObjectsSortMode.None).Any(x=>x!=this&&x.recording))return;
            if(!Application.isPlaying){Status="Play Mode required";return;}recording=true;StartCoroutine(Movie(path,seed,seconds));
        }
        private IEnumerator Movie(string path,int seed,int seconds)
        {
            const int fps=24;Directory.CreateDirectory(Path.GetDirectoryName(path));string images=Path.Combine(Path.GetDirectoryName(path),"Seed"+seed);Directory.CreateDirectory(images);
            var c=FindFirstObjectByType<TimeTheftGameController>();var original=c.Settings;var settings=Instantiate(original);settings.RandomSeed=seed;
            var so=new SerializedObject(c);so.FindProperty("settings").objectReferenceValue=settings;so.ApplyModifiedPropertiesWithoutUndo();
            var target=new Texture2D(1280,720,TextureFormat.RGBA32,false);var scaled=new RenderTexture(1280,720,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);scaled.Create();
            var attributes=new VideoTrackAttributes{width=1280,height=720,frameRate=new MediaRational(fps),includeAlpha=false,bitRateMode=VideoBitrateMode.High};
            var samples=new List<Sample>();var saved=new HashSet<string>();int frames=0;Coroutine drive=null;
            try
            {
                encoder=new MediaEncoder(path,attributes);Status="Warming";yield return null;yield return new WaitForEndOfFrame();yield return null;
                c.RestartGame();var sources=c.View.Clocks;var preview=sources.Select((x,i)=>new Face{source=i,instance=x.GetInstanceID(),hours=c.Match.GetClock(i).Face.Value.Hours,kind=c.Match.GetClock(i).Face.Value.Kind.ToString()}).ToArray();
                float start=Time.realtimeSinceStartup;drive=StartCoroutine(Drive(c,start+seconds));Status="Recording";
                int[] d2s=null,s2d=null,reached=null;int passes=0;float duration=0;
                while(frames<seconds*fps)
                {
                    yield return new WaitForEndOfFrame();float time=Time.realtimeSinceStartup-start;int due=Mathf.Min(seconds*fps,Mathf.FloorToInt(time*fps)+1);if(due<=frames)continue;
                    var active=RenderTexture.active;var screen=ScreenCapture.CaptureScreenshotAsTexture();Graphics.Blit(screen,scaled);RenderTexture.active=scaled;
                    target.ReadPixels(new Rect(0,0,1280,720),0,0);target.Apply();RenderTexture.active=active;Destroy(screen);
                    var match=c.Match;var sh=c.View.ShufflePresentation;var batch=c.View.BatchReveal;var turn=c.View.TurnPresentation;
                    if(d2s==null&&match.LastShuffle!=null){d2s=match.LastShuffle.DestinationToSource.ToArray();s2d=match.LastShuffle.SourceToDestination.ToArray();passes=sh.PassCount;duration=sh.Duration(settings.ShuffleDuration);}
                    if(reached==null&&d2s!=null&&!sh.IsRunning)reached=c.View.Clocks.Select(x=>Array.IndexOf(sources,x)).ToArray();
                    string label=null;
                    if(time<.12f)label="01-Prepare";
                    if(batch.Phase==H2BatchRevealPresentation.RevealPhase.Observing)label="02-Open";
                    if(batch.Phase==H2BatchRevealPresentation.RevealPhase.Returning)label="03-Closed";
                    if(sh.IsRunning){int step=Mathf.Clamp(Mathf.FloorToInt(sh.Progress*5),0,4);label="04-Shuffle-"+step;}
                    if(d2s!=null&&!sh.IsRunning&&match.CurrentClock<0)label="05-Reached";
                    if(turn.Phase==H2TurnPresentation.TurnPhase.Selecting)label="06-Select";
                    if(turn.Phase==H2TurnPresentation.TurnPhase.DecisionHold)label="07-Decision";
                    if(turn.Phase==H2TurnPresentation.TurnPhase.ResultPose)label="08-Reveal";
                    if(turn.Phase==H2TurnPresentation.TurnPhase.CountingTime)label="09-Settlement";
                    if(turn.Phase==H2TurnPresentation.TurnPhase.Returning)label="10-TurnClosed";
                    if(label!=null&&saved.Add(label))File.WriteAllBytes(Path.Combine(images,label+".png"),target.EncodeToPNG());
                    int index=match.CurrentClock;ClockFace? face=index>=0?match.GetClock(index).Face:null;
                    samples.Add(new Sample{time=time,frame=frames,progress=sh.Progress,stage=match.Stage.ToString(),phase=turn.Phase.ToString(),batchPhase=batch.Phase.ToString(),currentClock=index,
                        camera=c.View.FocusCamera.transform.position.ToString("F5"),resultHours=face.HasValue?face.Value.Hours:0,resultKind=face.HasValue?face.Value.Kind.ToString():"Hidden",
                        bodies=sources.Select((x,i)=>new Body{source=i,instance=x.GetInstanceID(),slot=x.SlotIndex,root=x.transform.position.ToString("F5"),meshCenter=x.H2Presentation.Watch.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Case").bounds.center.ToString("F5"),
                            lid=Quaternion.Angle(Quaternion.identity,x.H2Presentation.Watch.LidPivot.localRotation),input=x.SelectionTarget.InputEnabled,red=x.H2Presentation.IsRed,hours=x.H2Presentation.DisplayedHours??0}).ToArray()});
                    while(frames<due){if(!encoder.AddFrame(target))throw new InvalidOperationException("Encoder frame "+frames);frames++;}
                }
                File.WriteAllText(path+".capture.json",JsonUtility.ToJson(new Report{seed=seed,fps=fps,frames=frames,uniqueFrames=samples.Count,heldFrames=frames-samples.Count,actualSeconds=Time.realtimeSinceStartup-start,passes=passes,shuffleSeconds=duration,
                    destinationToSource=d2s,sourceToDestination=s2d,reachedSourceBySlot=reached,preview=preview,samples=samples.ToArray()},true));Status="Recorded";
            }
            finally
            {
                if(drive!=null)StopCoroutine(drive);if(encoder!=null){encoder.Dispose();encoder=null;}so.FindProperty("settings").objectReferenceValue=original;so.ApplyModifiedPropertiesWithoutUndo();Destroy(settings);Destroy(target);
                if(RenderTexture.active==scaled)RenderTexture.active=null;scaled.Release();Destroy(scaled);recording=false;
            }
        }
        private IEnumerator Drive(TimeTheftGameController c,float deadline)
        {
            TurnStage last=c.Match.Stage;float since=Time.realtimeSinceStartup;bool selecting=false;int turns=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                var m=c.Match;if(m.Stage!=last||(selecting&&!c.IsPresentingClock)){last=m.Stage;since=Time.realtimeSinceStartup;}selecting=c.IsPresentingClock;
                if(m.ActiveParticipant==Participant.Player&&!c.IsPresentingItem)
                {
                    float elapsed=Time.realtimeSinceStartup-since;
                    if(m.Stage==TurnStage.AwaitingItems&&elapsed>.5f)c.EndItemPhase();
                    if(m.Stage==TurnStage.AwaitingClock&&elapsed>.6f)c.SelectClock(m.GetAvailableClockIndices().First());
                    if(m.Stage==TurnStage.AwaitingKeyDecision&&!c.IsPresentingClock&&elapsed>2)c.KeepPeekedClock();
                    if(m.Stage==TurnStage.AwaitingAction&&!c.IsPresentingClock&&elapsed>2){(turns++%2==0?c.View.RationButton:c.View.CollectButton).onClick.Invoke();}
                }
                yield return null;
            }
        }
        private void OnDisable(){if(encoder!=null){encoder.Dispose();encoder=null;}recording=false;}
    }
}
#endif
