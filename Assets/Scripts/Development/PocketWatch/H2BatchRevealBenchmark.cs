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
    public sealed class H2BatchRevealBenchmark : MonoBehaviour
    {
        [Serializable] private sealed class Row
        {
            public int width, height, repetition, samples;
            public string phase;
            public double medianGpuMs, p95GpuMs, meanGpuMs;
        }
        [Serializable] private sealed class Report
        {
            public string gpu, api;
            public string method = "Live main-scene 5 H2 C2 introduction, original lights/table/counter. Unique FrameTimingManager whole-Editor GPU timestamps, GPU profiler on; no custom sample scopes or capture. Ignore first .15 seconds of each phase. Overview is closed/idle; Approach, Observe, Closing and Return are actual live motion. Two opposite resolution orders. Includes Editor/UI/submission/idle and profiling overhead; not isolated shader cost or Player.";
            public Row[] rows;
        }
        public string Status { get; private set; } = "Idle";
        private bool profiling, oldEnabled, oldGpu, oldEditor;
        public void Run(string path) => StartCoroutine(Measure(path));
        private void OnDisable()
        {
            if (!profiling) return;
            ProfilerDriver.profileGPU = oldGpu; ProfilerDriver.profileEditor = oldEditor; ProfilerDriver.enabled = oldEnabled; profiling = false;
        }
        private IEnumerator Measure(string path)
        {
            var controller = FindFirstObjectByType<TimeTheftGameController>(); var camera = Camera.main;
            var original = controller.Settings; var settings = Instantiate(original); settings.PreviewDuration = 5.5f; settings.RandomSeed = 635;
            var so = new SerializedObject(controller); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            oldEnabled = ProfilerDriver.enabled; oldGpu = ProfilerDriver.profileGPU; oldEditor = ProfilerDriver.profileEditor;
            ProfilerDriver.profileEditor = false; ProfilerDriver.profileGPU = true; ProfilerDriver.enabled = true; profiling = true;
            var oldTarget = camera.targetTexture; var rows = new List<Row>(); var timing = new FrameTiming[16];
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().Name == "GameView");
            for (int repetition = 0; repetition < 2; repetition++)
            foreach (int width in repetition == 0 ? new[] { 1280, 2560 } : new[] { 2560, 1280 })
            {
                int height = width * 9 / 16; var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); rt.Create(); camera.targetTexture = rt;
                var groups = new Dictionary<string, List<double>>(); var stamps = new HashSet<ulong>();
                controller.RestartGame(); var reveal = controller.View.BatchReveal;
                string last = ""; float phaseStart = Time.realtimeSinceStartup, overviewStart = -1;
                float deadline = Time.realtimeSinceStartup + 24;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (window) window.Repaint(); FrameTimingManager.CaptureFrameTimings(); yield return new WaitForEndOfFrame();
                    string phase = reveal.Phase.ToString();
                    if (phase == "Idle") phase = controller.Match.Stage == TurnStage.Shuffling ? "Shuffle" : "Overview";
                    if (phase == "Revealing") phase = "Approach";
                    if (phase == "Observing") phase = "Observe";
                    if (phase == "Returning") phase = "Return";
                    if (phase != last) { last = phase; phaseStart = Time.realtimeSinceStartup; }
                    if (phase == "Overview" && overviewStart < 0) overviewStart = Time.realtimeSinceStartup;
                    if (!groups.ContainsKey(phase)) groups[phase] = new List<double>();
                    Status = width + " / " + phase + " / " + repetition;
                    uint count = FrameTimingManager.GetLatestTimings(16, timing);
                    for (int i = 0; i < count; i++)
                        if (stamps.Add(timing[i].frameStartTimestamp) && Time.realtimeSinceStartup - phaseStart >= .15f && timing[i].gpuFrameTime > 0)
                            groups[phase].Add(timing[i].gpuFrameTime);
                    if (overviewStart >= 0 && Time.realtimeSinceStartup - overviewStart >= 1.2f) break;
                }
                foreach (var pair in groups)
                {
                    pair.Value.Sort(); if (pair.Value.Count == 0) continue;
                    rows.Add(new Row { width = width, height = height, repetition = repetition, phase = pair.Key, samples = pair.Value.Count,
                        medianGpuMs = pair.Value[pair.Value.Count / 2], meanGpuMs = pair.Value.Average(),
                        p95GpuMs = pair.Value[Mathf.Min(pair.Value.Count - 1, (int)(pair.Value.Count * .95f))] });
                }
                camera.targetTexture = oldTarget; if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); Destroy(rt);
            }
            File.WriteAllText(path, JsonUtility.ToJson(new Report { gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), rows = rows.ToArray() }, true));
            controller.StopAllCoroutines(); controller.View.CancelPresentation(); so.FindProperty("settings").objectReferenceValue = original; so.ApplyModifiedPropertiesWithoutUndo();
            Destroy(settings); OnDisable(); Status = "Finished";
        }
    }
}
#endif
