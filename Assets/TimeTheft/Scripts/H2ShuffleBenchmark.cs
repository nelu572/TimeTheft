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
    public sealed class H2ShuffleBenchmark : MonoBehaviour
    {
        [Serializable] private sealed class Row { public int seed,samples;public string phase;public double medianGpuMs,p95GpuMs; }
        [Serializable] private sealed class Report
        {
            public string method="Live main scene at720p/timeScale1/original durations, two seeds. Unique FrameTimingManager whole-Editor GPU timestamps, profileGPU queries on, full Profiler recording OFF. No video encoding/readback. First.15s phase discarded. Includes Editor/UI/idle, NOT isolated watch/shader GPU cost or Player performance. Zero samples = unavailable.";
            public string gpu,api;public Row[] rows;
        }
        public string Status {get;private set;}="Idle";private bool running,enabledBefore,gpuBefore,editorBefore;
        public void Run(string path){if(!running){running=true;StartCoroutine(Measure(path));}}
        private void Restore(){if(!running)return;ProfilerDriver.enabled=enabledBefore;ProfilerDriver.profileGPU=gpuBefore;ProfilerDriver.profileEditor=editorBefore;running=false;}
        private void OnDisable()=>Restore();
        private IEnumerator Measure(string path)
        {
            var c=FindFirstObjectByType<TimeTheftGameController>();var original=c.Settings;var settings=Instantiate(original);var so=new SerializedObject(c);so.FindProperty("settings").objectReferenceValue=settings;so.ApplyModifiedPropertiesWithoutUndo();
            enabledBefore=ProfilerDriver.enabled;gpuBefore=ProfilerDriver.profileGPU;editorBefore=ProfilerDriver.profileEditor;ProfilerDriver.enabled=false;ProfilerDriver.profileEditor=false;ProfilerDriver.profileGPU=true;
            var window=Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w=>w.GetType().Name=="GameView");var rows=new List<Row>();var timings=new FrameTiming[16];
            try
            {
                foreach(int seed in new[]{635,142})
                {
                    settings.RandomSeed=seed;c.RestartGame();float deadline=Time.realtimeSinceStartup+14;string last="";float since=0;var groups=new Dictionary<string,List<double>>();var stamps=new HashSet<ulong>();
                    while(Time.realtimeSinceStartup<deadline)
                    {
                        if(window)window.Repaint();FrameTimingManager.CaptureFrameTimings();yield return new WaitForEndOfFrame();
                        var b=c.View.BatchReveal;string phase=c.View.ShufflePresentation.IsRunning?"Shuffle5":b.IsRunning?b.Phase.ToString():"ClosedOverview5";
                        if(phase!=last){last=phase;since=Time.realtimeSinceStartup;}if(!groups.ContainsKey(phase))groups[phase]=new List<double>();Status=seed+" / "+phase;
                        uint count=FrameTimingManager.GetLatestTimings(16,timings);for(int i=0;i<count;i++)if(stamps.Add(timings[i].frameStartTimestamp)&&Time.realtimeSinceStartup-since>=.15f&&timings[i].gpuFrameTime>0)groups[phase].Add(timings[i].gpuFrameTime);
                    }
                    foreach(var pair in groups){pair.Value.Sort();rows.Add(new Row{seed=seed,phase=pair.Key,samples=pair.Value.Count,medianGpuMs=pair.Value.Count>0?pair.Value[pair.Value.Count/2]:0,p95GpuMs=pair.Value.Count>0?pair.Value[Mathf.Min(pair.Value.Count-1,(int)(pair.Value.Count*.95f))]:0});}
                }
                File.WriteAllText(path,JsonUtility.ToJson(new Report{gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),rows=rows.ToArray()},true));Status="Finished";
            }
            finally{so.FindProperty("settings").objectReferenceValue=original;so.ApplyModifiedPropertiesWithoutUndo();Destroy(settings);Restore();}
        }
    }
}
#endif
