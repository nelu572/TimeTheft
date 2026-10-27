#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
namespace TimeTheft.Editor
{
    public sealed class H2LifecycleBenchmark : MonoBehaviour
    {
        [Serializable] private sealed class Row { public string phase;public int gpuSamples,frameSamples;public double medianGpuMs,p95GpuMs,medianFrameMs,p95FrameMs; }
        [Serializable] private sealed class Report { public string gpu,api; public int width,height;
            public string method="One bounded35s actual main-game run, original durations, seed635 runtime copy, timeScale1. Unique FrameTimingManager whole-Editor GPU timestamps, profileGPU queries ON/full Profiler frame recording OFF, no encoder/readback. Drop first.15s of each phase. Zero GPU samples=unavailable, sparse samples not representative. Frame intervals include Editor/OS load, not Player or isolated watch shader cost."; public Row[] rows; }
        public string Status {get;private set;}="Idle";private bool running,oldEnabled,oldGpu,oldEditor;
        public void Run(string path){if(!running){running=true;StartCoroutine(Measure(path));}}
        private void Restore(){if(!running)return;ProfilerDriver.enabled=oldEnabled;ProfilerDriver.profileGPU=oldGpu;ProfilerDriver.profileEditor=oldEditor;running=false;}
        private void OnDisable()=>Restore();
        private IEnumerator Measure(string path)
        {
            var c=FindFirstObjectByType<TimeTheftGameController>();var original=c.Settings;var settings=Instantiate(original);settings.RandomSeed=635;
            var so=new SerializedObject(c);so.FindProperty("settings").objectReferenceValue=settings;so.ApplyModifiedPropertiesWithoutUndo();
            oldEnabled=ProfilerDriver.enabled;oldGpu=ProfilerDriver.profileGPU;oldEditor=ProfilerDriver.profileEditor;ProfilerDriver.enabled=false;ProfilerDriver.profileEditor=false;ProfilerDriver.profileGPU=true;
            var helper=gameObject.AddComponent<H2SettlementCapture>();Coroutine driver=null;
            var window=Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w=>w.GetType().Name=="GameView");var timings=new FrameTiming[16];
            var gpu=new Dictionary<string,List<double>>();var frames=new Dictionary<string,List<double>>();var stamps=new HashSet<ulong>();
            try
            {
                c.RestartGame();float deadline=Time.realtimeSinceStartup+35;driver=StartCoroutine(helper.Drive(c,deadline));string last="";float since=0;double prior=Time.realtimeSinceStartupAsDouble;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(window)window.Repaint();FrameTimingManager.CaptureFrameTimings();yield return new WaitForEndOfFrame();
                    var life=c.View.Lifecycle;var batch=c.View.BatchReveal;var turn=c.View.TurnPresentation;
                    string phase=life.IsRunning?life.Phase.ToString():batch.IsRunning?"Batch_"+batch.Phase:turn.Phase!=H2TurnPresentation.TurnPhase.Idle?turn.Phase.ToString():c.Match.Stage.ToString();
                    if(phase!=last){last=phase;since=Time.realtimeSinceStartup;}if(!gpu.ContainsKey(phase)){gpu[phase]=new List<double>();frames[phase]=new List<double>();}
                    double now=Time.realtimeSinceStartupAsDouble;if(now-since>=.15)frames[phase].Add((now-prior)*1000);prior=now;Status=phase;
                    uint count=FrameTimingManager.GetLatestTimings(16,timings);for(int i=0;i<count;i++)if(stamps.Add(timings[i].frameStartTimestamp)&&now-since>=.15&&timings[i].gpuFrameTime>0)gpu[phase].Add(timings[i].gpuFrameTime);
                }
                var rows=new List<Row>();foreach(var pair in gpu){pair.Value.Sort();frames[pair.Key].Sort();var f=frames[pair.Key];rows.Add(new Row{phase=pair.Key,gpuSamples=pair.Value.Count,frameSamples=f.Count,medianGpuMs=Median(pair.Value),p95GpuMs=P95(pair.Value),medianFrameMs=Median(f),p95FrameMs=P95(f)});}
                File.WriteAllText(path,JsonUtility.ToJson(new Report{gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),width=Screen.width,height=Screen.height,rows=rows.ToArray()},true));Status="Finished";
            }
            finally{if(driver!=null)StopCoroutine(driver);c.StopAllCoroutines();c.View.CancelPresentation();so.FindProperty("settings").objectReferenceValue=original;so.ApplyModifiedPropertiesWithoutUndo();Destroy(settings);Destroy(helper);Restore();}
        }
        private static double Median(List<double> x)=>x.Count>0?x[x.Count/2]:0;
        private static double P95(List<double> x)=>x.Count>0?x[Mathf.Min(x.Count-1,(int)(x.Count*.95))]:0;
    }
}
#endif
