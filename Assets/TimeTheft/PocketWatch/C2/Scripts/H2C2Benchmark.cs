#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using UnityEditor;
using UnityEditorInternal;

namespace TimeTheft.Editor
{
    public sealed class H2C2Benchmark : MonoBehaviour
    {
        [Serializable] private sealed class Row
        {
            public string mode;
            public int watches, repetition, validGpuSamples;
            public double gpuMeanMs, gpuMedianMs, gpuP95Ms, cpuMainMeanMs;
            public int cameraGpuSamples;
            public double cameraGpuMedianMs, cameraGpuMeanMs, cameraGpuP95Ms;
        }
        [Serializable] private sealed class Report
        {
            public string gpu, api;
            public int width = 1280, height = 720, warmup = 45, samples = 150;
            public string method = "URP begin/end-camera CustomSampler GPU elapsed interval, GPU Profiler enabled; also FrameTimingManager total Editor GPU timestamps. Same fixed 1280x720 camera, shadows/probe; capture off; two opposite-order repetitions. Includes pipeline submission/idle overhead, not isolated shader instruction time or a standalone Player benchmark.";
            public Row[] rows;
        }
        public string Status { get; private set; } = "Idle";
        public void Run(string path) => StartCoroutine(Measure(path));
        private Camera measuredCamera;
        private CustomSampler sampler;
        private Recorder recorder;
        private CommandBuffer samplerCommands;
        private int cameraFrames;
        private bool oldProfiler, oldGpu, oldEditor, profiling;

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != measuredCamera) return;
            samplerCommands.Clear(); samplerCommands.BeginSample(sampler); context.ExecuteCommandBuffer(samplerCommands);
        }
        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != measuredCamera) return;
            samplerCommands.Clear(); samplerCommands.EndSample(sampler); context.ExecuteCommandBuffer(samplerCommands);
            cameraFrames++;
        }
        private void OnDisable()
        {
            if (!profiling) return;
            RenderPipelineManager.beginCameraRendering -= BeginCamera; RenderPipelineManager.endCameraRendering -= EndCamera;
            recorder.enabled = false; ProfilerDriver.profileGPU = oldGpu; ProfilerDriver.profileEditor = oldEditor; ProfilerDriver.enabled = oldProfiler; profiling = false;
            samplerCommands.Release();
        }

        private IEnumerator Measure(string path)
        {
            var stage = FindFirstObjectByType<H2C2ComparisonStage>();
            var controls = FindFirstObjectByType<H2WatchValidationStage>();
            var watch = stage.Binding.gameObject;
            var view = watch.GetComponent<H2PocketWatchView>();
            var camera = Camera.main;
            measuredCamera = camera;
            samplerCommands = new CommandBuffer { name = "H2 camera GPU timer" };
            sampler = CustomSampler.Create("H2 C2 Validation Camera GPU", true); recorder = sampler.GetRecorder(); recorder.enabled = true; recorder.CollectFromAllThreads();
            oldProfiler = ProfilerDriver.enabled; oldGpu = ProfilerDriver.profileGPU; oldEditor = ProfilerDriver.profileEditor;
            ProfilerDriver.profileEditor = false; ProfilerDriver.profileGPU = true; ProfilerDriver.enabled = true; profiling = true;
            RenderPipelineManager.beginCameraRendering += BeginCamera; RenderPipelineManager.endCameraRendering += EndCamera;
            var gameView = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().Name == "GameView");
            Vector3 position = camera.transform.position, wp = watch.transform.position;
            Quaternion rotation = camera.transform.rotation, wr = watch.transform.rotation, lid = view.LidPivot.localRotation;
            var priorTarget = camera.targetTexture;
            bool wasC2 = stage.Binding.C2Enabled;
            stage.Animate = false; stage.HideGui = true; controls.enabled = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); target.Create();
            camera.targetTexture = target;
            camera.transform.position = new Vector3(0, 3.8f, 3.8f); camera.transform.LookAt(Vector3.zero);
            watch.transform.position = Vector3.zero; watch.transform.rotation = Quaternion.identity; view.SetLidAngle(0);
            var instances = new List<GameObject> { watch };
            for (int i = 0; i < 4; i++) instances.Add(Instantiate(watch));
            for (int i = 0; i < 5; i++) instances[i].transform.position = new Vector3((i - 2) * .8f, 0, 0);
            var report = new Report { gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString() };
            var rows = new List<Row>(); var timing = new FrameTiming[16];
            for (int repetition = 0; repetition < 2; repetition++)
            {
                int[] cases = repetition == 0 ? new[] { 0, 1, 2, 3 } : new[] { 3, 2, 1, 0 };
                foreach (int scenario in cases)
                {
                    int count = scenario < 2 ? 1 : 5; bool c2 = scenario % 2 == 1;
                    for (int i = 0; i < 5; i++) { instances[i].SetActive(count == 5 || i == 0); instances[i].GetComponent<H2C2MaterialBinding>().SetC2(c2); }
                    if (count == 1) watch.transform.position = Vector3.zero; else watch.transform.position = new Vector3(-1.6f, 0, 0);
                    var gpu = new List<double>(); var cpu = new List<double>();
                    var cameraGpu = new List<double>();
                    var gpuFrames = new HashSet<ulong>(); var cpuFrames = new HashSet<ulong>();
                    Status = "Profiling " + count + " / " + (c2 ? "C2" : "Lit") + " / repeat " + repetition;
                    int priorFrame = cameraFrames, frame = 0;
                    float deadline = Time.realtimeSinceStartup + 25;
                    while (frame < report.warmup + report.samples && Time.realtimeSinceStartup < deadline)
                    {
                        if (gameView) gameView.Repaint();
                        FrameTimingManager.CaptureFrameTimings();
                        yield return new WaitForEndOfFrame();
                        if (cameraFrames == priorFrame) continue;
                        priorFrame = cameraFrames; frame++;
                        if (frame <= report.warmup) continue;
                        if (recorder.gpuSampleBlockCount > 0 && recorder.gpuElapsedNanoseconds > 0) cameraGpu.Add(recorder.gpuElapsedNanoseconds / 1000000.0);
                        uint returned = FrameTimingManager.GetLatestTimings(16, timing);
                        for (int k = 0; k < returned; k++)
                        {
                            if (timing[k].gpuFrameTime > 0 && gpuFrames.Add(timing[k].frameStartTimestamp)) gpu.Add(timing[k].gpuFrameTime);
                            if (cpuFrames.Add(timing[k].frameStartTimestamp)) cpu.Add(timing[k].cpuMainThreadFrameTime);
                        }
                    }
                    gpu.Sort();
                    cameraGpu.Sort();
                    rows.Add(new Row { mode = c2 ? "C2" : "URP Lit", watches = count, repetition = repetition, validGpuSamples = gpu.Count,
                        gpuMeanMs = gpu.Count > 0 ? gpu.Average() : 0, gpuMedianMs = gpu.Count > 0 ? gpu[gpu.Count / 2] : 0,
                        gpuP95Ms = gpu.Count > 0 ? gpu[Mathf.Min(gpu.Count - 1, (int)(gpu.Count * .95f))] : 0, cpuMainMeanMs = cpu.Count > 0 ? cpu.Average() : 0,
                        cameraGpuSamples = cameraGpu.Count, cameraGpuMeanMs = cameraGpu.Count > 0 ? cameraGpu.Average() : 0,
                        cameraGpuMedianMs = cameraGpu.Count > 0 ? cameraGpu[cameraGpu.Count / 2] : 0, cameraGpuP95Ms = cameraGpu.Count > 0 ? cameraGpu[Mathf.Min(cameraGpu.Count - 1, (int)(cameraGpu.Count * .95f))] : 0 });
                }
            }
            report.rows = rows.ToArray(); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            for (int i = 1; i < instances.Count; i++) Destroy(instances[i]);
            watch.SetActive(true); watch.transform.position = wp; watch.transform.rotation = wr; view.LidPivot.localRotation = lid;
            stage.Binding.SetC2(wasC2); camera.targetTexture = priorTarget; camera.transform.SetPositionAndRotation(position, rotation);
            if (RenderTexture.active == target) RenderTexture.active = null; target.Release(); Destroy(target);
            controls.enabled = true; stage.HideGui = false; Status = "Finished " + path;
            OnDisable();
        }
    }
}
#endif
