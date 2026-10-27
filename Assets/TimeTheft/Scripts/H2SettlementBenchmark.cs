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
    public sealed class H2SettlementBenchmark : MonoBehaviour
    {
        [Serializable] private sealed class Row { public int width,height,repetition,samples; public string phase; public double medianGpuMs,p95GpuMs; }
        [Serializable] private sealed class Report
        {
            public string gpu,api;
            public string method="Live TimeTheftGame five H2 C2, original lights/T3/counter, original durations, timeScale1. Unique FrameTimingManager whole-Editor GPU timestamps; profileGPU queries on, full Profiler frame recording off to avoid frame-buffer memory pressure. No capture/readback. Ignore first .15s of phase. Two reverse resolution orders. Includes Editor/UI/submission/idle; not isolated shader time or Player performance. Sparse or missing phases are unavailable, not zero cost.";
            public Row[] rows;
        }
        public string Status { get; private set; }="Idle";
        private bool running, oldEnabled,oldGpu,oldEditor;
        public void Run(string path) { if(!running) { running=true; StartCoroutine(Measure(path)); } }
        private void RestoreProfiler() { if(!running)return; ProfilerDriver.profileGPU=oldGpu;ProfilerDriver.profileEditor=oldEditor;ProfilerDriver.enabled=oldEnabled;running=false; }
        private void OnDisable() { RestoreProfiler(); }
        private IEnumerator Measure(string path)
        {
            var c=FindFirstObjectByType<TimeTheftGameController>(); var camera=c.View.FocusCamera.GetComponent<Camera>();
            var original=c.Settings; var settings=Instantiate(original);settings.RandomSeed=635;
            var so=new SerializedObject(c);so.FindProperty("settings").objectReferenceValue=settings;so.ApplyModifiedPropertiesWithoutUndo();
            var driver=gameObject.AddComponent<H2SettlementCapture>(); var oldTarget=camera.targetTexture;
            oldEnabled=ProfilerDriver.enabled;oldGpu=ProfilerDriver.profileGPU;oldEditor=ProfilerDriver.profileEditor;
            ProfilerDriver.profileEditor=false;ProfilerDriver.profileGPU=true;ProfilerDriver.enabled=false;
            var rows=new List<Row>(); var timing=new FrameTiming[16];
            var window=Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w=>w.GetType().Name=="GameView");
            try
            {
                for(int rep=0;rep<2;rep++) foreach(int width in rep==0?new[]{1280,2560}:new[]{2560,1280})
                {
                    var rt=new RenderTexture(width,width*9/16,24,RenderTextureFormat.ARGB32);rt.Create();camera.targetTexture=rt;
                    Coroutine drive=null;
                    try
                    {
                        c.RestartGame(); float deadline=Time.realtimeSinceStartup+24;
                        drive=StartCoroutine(driver.Drive(c,deadline));
                        var groups=new Dictionary<string,List<double>>();var stamps=new HashSet<ulong>();
                        string last="";float phaseStart=Time.realtimeSinceStartup;
                        while(Time.realtimeSinceStartup<deadline)
                        {
                            if(window)window.Repaint();FrameTimingManager.CaptureFrameTimings();yield return new WaitForEndOfFrame();
                            var motion=c.View.TurnPresentation;
                            string phase=motion.Phase.ToString();
                            if(phase=="Idle")phase=c.Match.UsedCount>0?"BetweenTurns":c.Match.Stage==TurnStage.AwaitingClock?"Overview":c.Match.Stage.ToString();
                            if(phase!=last){last=phase;phaseStart=Time.realtimeSinceStartup;}
                            Status=width+" / "+phase+" / "+rep;
                            if(!groups.ContainsKey(phase))groups[phase]=new List<double>();
                            uint count=FrameTimingManager.GetLatestTimings(16,timing);
                            for(int i=0;i<count;i++)if(stamps.Add(timing[i].frameStartTimestamp)&&Time.realtimeSinceStartup-phaseStart>=.15f&&timing[i].gpuFrameTime>0)groups[phase].Add(timing[i].gpuFrameTime);
                        }
                        foreach(var pair in groups)
                        {
                            pair.Value.Sort();if(pair.Value.Count==0)continue;
                            rows.Add(new Row{width=width,height=width*9/16,repetition=rep,phase=pair.Key,samples=pair.Value.Count,
                                medianGpuMs=pair.Value[pair.Value.Count/2],p95GpuMs=pair.Value[Mathf.Min(pair.Value.Count-1,(int)(pair.Value.Count*.95f))]});
                        }
                    }
                    finally { if(drive!=null)StopCoroutine(drive);c.StopAllCoroutines();c.View.CancelPresentation();camera.targetTexture=oldTarget;if(RenderTexture.active==rt)RenderTexture.active=null;rt.Release();Destroy(rt); }
                }
                File.WriteAllText(path,JsonUtility.ToJson(new Report{gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),rows=rows.ToArray()},true));
                Status="Finished";
            }
            finally {so.FindProperty("settings").objectReferenceValue=original;so.ApplyModifiedPropertiesWithoutUndo();Destroy(settings);Destroy(driver);RestoreProfiler();}
        }
    }
}
#endif
