#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace TimeTheft.Editor
{
    // 실제 메인 씬의 5개 시계만 Lit/C2로 비교하는 일회성 Editor 계측기. 씬에는 저장하지 않는다.
    public sealed class H2GameGpuProbe : MonoBehaviour
    {
        [Serializable] private sealed class Row
        {
            public int width, height, repetition, samples, cameraFrames;
            public bool c2, open;
            public double frameGpuMedianMs, frameGpuP95Ms;
        }
        [Serializable] private sealed class Report
        {
            public string gpu, api;
            public string method = "Actual TimeTheftGame five H2, original camera/lights/T3/counter. Unique FrameTimingManager whole-Editor GPU timestamps; GPU profiler enabled, no custom Begin/End sample. Warmup45 + actual camera renders150, reversed order twice. Includes UI/Editor submission/idle and profiler overhead, not isolated camera/shader execution or Player.";
            public Row[] rows;
        }
        public string Status { get; private set; } = "Idle";
        private Camera measured;
        private int renders;
        private bool profiling, oldEnabled, oldGpu, oldEditor;
        public void Run(string path) => StartCoroutine(Measure(path));
        private void EndCamera(ScriptableRenderContext context, Camera camera)
        { if (camera == measured) renders++; }
        private void OnDisable()
        {
            if (!profiling) return;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            ProfilerDriver.profileGPU = oldGpu; ProfilerDriver.profileEditor = oldEditor; ProfilerDriver.enabled = oldEnabled; profiling = false;
        }
        private IEnumerator Measure(string path)
        {
            var controller = FindFirstObjectByType<TimeTheftGameController>(); controller.StopAllCoroutines();
            var clocks = controller.View.Clocks; var camera = Camera.main; measured = camera;
            var look = camera.GetComponent<TabletopCameraLook>(); var focus = camera.GetComponent<TabletopFocusCamera>();
            bool priorLook = look.enabled, priorFocus = focus.enabled;
            look.enabled = false; focus.enabled = false;
            var priorTarget = camera.targetTexture;
            var states = Enumerable.Range(0, 5).Select(i => controller.Match.GetClock(i)).ToArray();
            oldEnabled = ProfilerDriver.enabled; oldGpu = ProfilerDriver.profileGPU; oldEditor = ProfilerDriver.profileEditor;
            ProfilerDriver.profileEditor = false; ProfilerDriver.profileGPU = true; ProfilerDriver.enabled = true; profiling = true;
            RenderPipelineManager.endCameraRendering += EndCamera;
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().Name == "GameView");
            var rows = new List<Row>(); var timing = new FrameTiming[16];
            for (int resolution = 0; resolution < 2; resolution++)
            {
                int width = resolution == 0 ? 1280 : 2560, height = resolution == 0 ? 720 : 1440;
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); rt.Create(); camera.targetTexture = rt;
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    int[] order = repeat == 0 ? new[] { 0, 1, 2, 3 } : new[] { 3, 2, 1, 0 };
                    foreach (int scenario in order)
                    {
                        bool c2 = scenario % 2 == 1, open = scenario >= 2;
                        foreach (var clock in clocks)
                        {
                            clock.gameObject.SetActive(true); clock.H2Presentation.Watch.SetLidAngle(open ? 115 : 0);
                            clock.H2Presentation.Materials.SetC2(c2);
                        }
                        Status = width + " / " + (open ? "open" : "closed") + " / " + (c2 ? "C2" : "Lit") + " / " + repeat;
                        int last = renders, frames = 0; float deadline = Time.realtimeSinceStartup + 35;
                        var total = new List<double>(); var stamps = new HashSet<ulong>();
                        while (frames < 195 && Time.realtimeSinceStartup < deadline)
                        {
                            if (window) window.Repaint(); FrameTimingManager.CaptureFrameTimings(); yield return new WaitForEndOfFrame();
                            if (renders == last) continue; last = renders; frames++;
                            uint count = FrameTimingManager.GetLatestTimings(16, timing);
                            for (int i = 0; i < count; i++)
                                if (stamps.Add(timing[i].frameStartTimestamp) && frames > 45 && timing[i].gpuFrameTime > 0) total.Add(timing[i].gpuFrameTime);
                        }
                        total.Sort();
                        rows.Add(new Row { width = width, height = height, repetition = repeat, c2 = c2, open = open, samples = total.Count, cameraFrames = frames,
                            frameGpuMedianMs = total.Count > 0 ? total[total.Count / 2] : 0,
                            frameGpuP95Ms = total.Count > 0 ? total[Mathf.Min(total.Count - 1, (int)(total.Count * .95f))] : 0 });
                    }
                }
                camera.targetTexture = priorTarget; if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); Destroy(rt);
            }
            File.WriteAllText(path, JsonUtility.ToJson(new Report { gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), rows = rows.ToArray() }, true));
            for (int i = 0; i < 5; i++) { clocks[i].H2Presentation.Materials.SetC2(true); clocks[i].Show(states[i]); }
            look.enabled = priorLook; focus.enabled = priorFocus; OnDisable(); Status = "Finished";
        }
    }
}
#endif
