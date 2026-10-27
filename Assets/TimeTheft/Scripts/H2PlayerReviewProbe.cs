#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace TimeTheft
{
    // Opt-in standalone review only. Normal launches create no object and change no settings.
    public sealed class H2PlayerReviewProbe : MonoBehaviour
    {
        [Serializable] private sealed class Row
        {
            public string phase, gpuQuality; public int frames, gpuSamples, over33Ms, over50Ms, over100Ms;
            public double medianFrameMs, p95FrameMs, p99FrameMs, maxFrameMs, medianGpuMs = -1, p95GpuMs = -1;
        }
        [Serializable] private sealed class Report
        {
            public string method = "Actual main scene, seed635 settings copy, original durations/timeScale1; public player requests, AI unchanged. No readback/Profiler recording. CPU=observed end-of-frame wall intervals (includes OS/wait). GPU=whole Player, unique native timestamps mapped only after QPC clock validation; exclude 0.15s at both phase boundaries. GPU -1=unavailable; sparse samples are not representative.";
            public string unity, platform, device, vendor, driverApi, quality, pipeline, settings, output, framesCsv, gpuCsv, failure, clockSource;
            public int width, height, requestedWidth, requestedHeight, vSync, targetFps, unfocusedFrames, warnings, errors;
            public int gpuRetrieved, gpuUnique, gpuDuplicate, gpuZero, gpuBad, gpuOutside, gpuBoundary, gpuClockRejected, gpuAccepted;
            public bool featureEnabled, highResolutionClock, clockValidated, editor;
            public long stopwatchFrequency, nativeFrequency; public ulong cpuFrequency, gpuFrequency; public double seconds, renderScale;
            public Row[] rows; public string[] logErrors;
        }
        private sealed class Frame { public long tick; public string phase; public double ms; }
        private sealed class Span { public string phase; public long start, end; }
        private sealed class Gpu { public FrameTiming timing; public long received; public string phase = "", reason = ""; }
        private readonly List<Frame> frames = new List<Frame>(); private readonly List<Span> spans = new List<Span>();
        private readonly List<Gpu> gpu = new List<Gpu>(); private readonly HashSet<ulong> seen = new HashSet<ulong>();
        private readonly List<string> logErrors = new List<string>(); private readonly FrameTiming[] timings = new FrameTiming[16];
        private TimeTheftGameController controller; private TimeTheftSettings original, copy; private FieldInfo settingsField;
        private bool oldRunInBackground;
        private readonly Report report = new Report(); private float oldTimeScale; private int oldVSync, oldTarget; private bool changed;
        private long started, ended, nativeFrequency; private bool nativeTimerReady; private const double EdgeSeconds = .15;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Environment.GetCommandLineArgs().Contains("-h2-review-probe")) return;
            var go = new GameObject("H2 Player Review Probe") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go); go.AddComponent<H2PlayerReviewProbe>();
        }
        private static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        private void OnEnable() => Application.logMessageReceived += Log;
        private void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Warning) report.warnings++;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            report.errors++; if (logErrors.Count < 20) logErrors.Add(type + ": " + message + "\n" + stack);
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceFrequency(out long value);
        private bool InitializeNativeClock()
        {
            report.clockSource = "Win32 QueryPerformanceCounter (native OS epoch)";
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
            { report.failure = "Native QPC measurement supports Windows only."; return false; }
            try { nativeTimerReady = QueryPerformanceFrequency(out nativeFrequency) && nativeFrequency > 0; }
            catch (Exception e) { report.failure = "Native QPC initialization failed: " + e.Message; }
            report.nativeFrequency = nativeFrequency;
            if (!nativeTimerReady && string.IsNullOrEmpty(report.failure)) report.failure = "QueryPerformanceFrequency failed.";
            return nativeTimerReady && ReadTick() > 0;
        }
        private long ReadTick()
        {
            if (!nativeTimerReady) return 0;
            try { if (QueryPerformanceCounter(out long tick) && tick > 0) return tick; }
            catch (Exception e) { report.failure = "Native QPC read failed: " + e.Message; }
            nativeTimerReady = false;
            if (string.IsNullOrEmpty(report.failure)) report.failure = "QueryPerformanceCounter failed; measurements invalid.";
            return 0;
        }
        private IEnumerator Start()
        {
            report.output = Path.GetFullPath(Arg("-h2-output", Path.Combine(Application.persistentDataPath, "H2PlayerReview.json")));
            UnityEngine.Debug.Log("H2 Player review starting: " + report.output);
            double seconds; if (!double.TryParse(Arg("-h2-seconds", "90"), NumberStyles.Float, Inv, out seconds) || seconds <= 0 || seconds > 600) seconds = 90;
            if (!InitializeNativeClock()) { Finish(); yield break; }
            double timeout = Time.realtimeSinceStartupAsDouble + 30;
            while ((!controller || controller.Match == null || !controller.View) && Time.realtimeSinceStartupAsDouble < timeout)
            { controller = FindFirstObjectByType<TimeTheftGameController>(); yield return null; }
            if (!controller || controller.Match == null || !controller.View) { report.failure = "Controller Start did not become ready in 30s."; Finish(); yield break; }
            original = controller.Settings; copy = Instantiate(original); copy.RandomSeed = 635;
            settingsField = typeof(TimeTheftGameController).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic);
            if (settingsField == null) { report.failure = "Controller settings field unavailable."; Finish(); yield break; }
            oldTimeScale = Time.timeScale; oldVSync = QualitySettings.vSyncCount; oldTarget = Application.targetFrameRate; oldRunInBackground = Application.runInBackground; changed = true;
            Application.runInBackground = true;
            Time.timeScale = 1; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; settingsField.SetValue(controller, copy);
            yield return new WaitForSecondsRealtime(5); controller.RestartGame();
            Metadata(); started = ReadTick(); if (!nativeTimerReady) { Finish(); yield break; } long previous = started;
            string phase = Phase(); spans.Add(new Span { phase = phase, start = started });
            TurnStage lastStage = controller.Match.Stage; bool wasSelecting = false; double stageSince = 0;
            var eof = new WaitForEndOfFrame();
            while (nativeTimerReady)
            {
                long check = ReadTick(); if (!nativeTimerReady || (check - started) / (double)nativeFrequency >= seconds) break;
                FrameTimingManager.CaptureFrameTimings(); yield return eof;
                long now = ReadTick(); if (!nativeTimerReady) break; string current = Phase();
                if (current != phase) { spans[spans.Count - 1].end = now; spans.Add(new Span { phase = current, start = now }); phase = current; }
                frames.Add(new Frame { tick = now, phase = phase, ms = (now - previous) * 1000.0 / nativeFrequency }); previous = now;
                if (!Application.isFocused) report.unfocusedFrames++;
                uint count = FrameTimingManager.GetLatestTimings((uint)timings.Length, timings); report.gpuRetrieved += (int)count;
                for (int i = 0; i < count; i++)
                {
                    if (!seen.Add(timings[i].frameStartTimestamp)) { report.gpuDuplicate++; continue; }
                    gpu.Add(new Gpu { timing = timings[i], received = now });
                }
                double elapsed = (now - started) / (double)nativeFrequency; var match = controller.Match;
                if (lastStage != match.Stage || (wasSelecting && !controller.IsPresentingClock)) { lastStage = match.Stage; stageSince = elapsed; }
                wasSelecting = controller.IsPresentingClock;
                if (match.ActiveParticipant != Participant.Player || controller.IsPresentingItem) continue;
                if (match.Stage == TurnStage.AwaitingItems && elapsed - stageSince > .5) controller.EndItemPhase();
                else if (match.Stage == TurnStage.AwaitingClock && elapsed - stageSince > .6)
                { var ids = match.GetAvailableClockIndices(); if (ids.Length > 0) controller.SelectClock(ids[ids.Length - 1]); }
                else if (!controller.IsPresentingClock && elapsed - stageSince > 2)
                {
                    if (match.Stage == TurnStage.AwaitingKeyDecision) controller.KeepPeekedClock();
                    else if (match.Stage == TurnStage.AwaitingAction && controller.View.RationButton.interactable) controller.View.RationButton.onClick.Invoke();
                }
            }
            ended = ReadTick(); if (!nativeTimerReady) ended = previous;
            spans[spans.Count - 1].end = ended; report.seconds = (ended - started) / (double)nativeFrequency; Finish();
        }
        private string Phase()
        {
            var v = controller.View; var life = v.Lifecycle; var batch = v.BatchReveal; var turn = v.TurnPresentation;
            if (life && life.IsRunning) return (life.IsDiscarding ? "Discard_" : "") + life.Phase;
            if (batch && batch.IsRunning) return batch.Phase == H2BatchRevealPresentation.RevealPhase.Observing ? "BatchObserve" : "Batch_" + batch.Phase;
            if (v.ShufflePresentation && v.ShufflePresentation.IsRunning) return "Shuffle";
            if (turn && turn.Phase != H2TurnPresentation.TurnPhase.Idle && turn.Phase != H2TurnPresentation.TurnPhase.Closed) return turn.Phase.ToString();
            var m = controller.Match;
            return (m.Stage == TurnStage.AwaitingItems || m.Stage == TurnStage.AwaitingClock) && m.GetAvailableClockIndices().Length == 5 ? "FiveClosedOverview" : m.Stage.ToString();
        }
        private void Metadata()
        {
            report.unity = Application.unityVersion; report.editor = Application.isEditor; report.platform = Application.platform.ToString();
            report.device = SystemInfo.graphicsDeviceName; report.vendor = SystemInfo.graphicsDeviceVendor;
            report.driverApi = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceVersion;
            report.width = Screen.width; report.height = Screen.height; int.TryParse(Arg("-screen-width", "0"), out report.requestedWidth); int.TryParse(Arg("-screen-height", "0"), out report.requestedHeight);
            report.vSync = QualitySettings.vSyncCount; report.targetFps = Application.targetFrameRate; report.quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            var pipeline = GraphicsSettings.currentRenderPipeline; report.pipeline = pipeline ? pipeline.name + " / " + pipeline.GetType().Name : "Built-in";
            report.renderScale = pipeline?.GetType().GetProperty("renderScale")?.GetValue(pipeline) is float scale ? scale : -1;
            report.featureEnabled = FrameTimingManager.IsFeatureEnabled(); report.cpuFrequency = FrameTimingManager.GetCpuTimerFrequency(); report.gpuFrequency = FrameTimingManager.GetGpuTimerFrequency();
            report.stopwatchFrequency = Stopwatch.Frequency; report.nativeFrequency = nativeFrequency; report.highResolutionClock = nativeTimerReady; report.settings = JsonUtility.ToJson(copy);
        }
        private void ClassifyGpu()
        {
            report.gpuUnique = gpu.Count;
            int inRange = gpu.Count(g => g.timing.frameStartTimestamp >= (ulong)started && g.timing.frameStartTimestamp <= (ulong)g.received);
            report.clockValidated = report.featureEnabled && nativeTimerReady && nativeFrequency > 0 && report.cpuFrequency == (ulong)nativeFrequency && report.gpuFrequency > 0 && gpu.Count >= 10 && inRange >= gpu.Count * .95;
            foreach (var g in gpu)
            {
                var t = g.timing; double ms = t.gpuFrameTime; ulong stamp = t.frameStartTimestamp;
                if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0 || stamp == 0) { g.reason = "bad-value-or-zero-timestamp"; report.gpuBad++; }
                else if (ms == 0) { g.reason = "zero-unavailable"; report.gpuZero++; }
                else if (!report.clockValidated) { g.reason = "clock-unavailable"; report.gpuClockRejected++; }
                else if (stamp < (ulong)started || stamp > (ulong)g.received) { g.reason = "outside-capture-clock-range"; report.gpuOutside++; }
                else if (ms > (g.received - (long)stamp) * 1000.0 / nativeFrequency + 2) { g.reason = "gpu-duration-exceeds-observed-frame-age"; report.gpuBad++; }
                else
                {
                    var span = spans.FirstOrDefault(s => stamp >= (ulong)s.start && stamp < (ulong)s.end);
                    if (span == null) { g.reason = "outside-phase-range"; report.gpuOutside++; }
                    else if (((long)stamp - span.start) / (double)nativeFrequency < EdgeSeconds || (span.end - (long)stamp) / (double)nativeFrequency < EdgeSeconds) { g.reason = "phase-boundary"; report.gpuBoundary++; }
                    else { g.phase = span.phase; g.reason = "accepted"; report.gpuAccepted++; }
                }
            }
        }
        private static double Percentile(List<double> values, double p) => values.Count == 0 ? -1 : values[Math.Max(0, (int)Math.Ceiling(values.Count * p) - 1)];
        private static string N(double n) => n.ToString("R", Inv);
        private void Finish()
        {
            try
            {
                ClassifyGpu(); var rows = new List<Row>();
                foreach (var group in frames.GroupBy(f => f.phase))
                {
                    var f = group.Select(x => x.ms).OrderBy(x => x).ToList(); var g = gpu.Where(x => x.reason == "accepted" && x.phase == group.Key).Select(x => x.timing.gpuFrameTime).OrderBy(x => x).ToList();
                    rows.Add(new Row { phase = group.Key, frames = f.Count, gpuSamples = g.Count, gpuQuality = g.Count == 0 ? "unavailable" : g.Count < 30 ? "sparse" : "available", medianFrameMs = Percentile(f, .5), p95FrameMs = Percentile(f, .95), p99FrameMs = Percentile(f, .99), maxFrameMs = f.Last(), medianGpuMs = Percentile(g, .5), p95GpuMs = Percentile(g, .95), over33Ms = f.Count(x => x > 33.333333), over50Ms = f.Count(x => x > 50), over100Ms = f.Count(x => x > 100) });
                }
                report.rows = rows.ToArray(); report.logErrors = logErrors.ToArray(); report.framesCsv = report.output + ".frames.csv"; report.gpuCsv = report.output + ".gpu.csv";
                Directory.CreateDirectory(Path.GetDirectoryName(report.output)); var csv = new StringBuilder("elapsedSeconds,phase,wallFrameMs,qpcEnd\n");
                foreach (var f in frames) csv.AppendLine(N((f.tick - started) / (double)nativeFrequency) + "," + f.phase + "," + N(f.ms) + "," + f.tick);
                File.WriteAllText(report.framesCsv, csv.ToString()); csv = new StringBuilder("frameStartTimestamp,receivedQpc,phase,reason,gpuMs,cpuFrameMs,firstSubmitTimestamp,cpuFrameComplete,widthScale,heightScale,syncInterval\n");
                foreach (var g in gpu) { var t = g.timing; csv.AppendLine(t.frameStartTimestamp + "," + g.received + "," + g.phase + "," + g.reason + "," + N(t.gpuFrameTime) + "," + N(t.cpuFrameTime) + "," + t.firstSubmitTimestamp + "," + t.cpuTimeFrameComplete + "," + N(t.widthScale) + "," + N(t.heightScale) + "," + t.syncInterval); }
                File.WriteAllText(report.gpuCsv, csv.ToString()); File.WriteAllText(report.output, JsonUtility.ToJson(report, true));
                UnityEngine.Debug.Log("H2 Player review saved: " + report.output);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("H2 Player review output failed: " + e); }
            Restore(); if (!Application.isEditor) Application.Quit(string.IsNullOrEmpty(report.failure) ? 0 : 1); else Destroy(gameObject);
        }
        private void Restore()
        {
            if (changed) { if (controller) settingsField.SetValue(controller, original); Time.timeScale = oldTimeScale; QualitySettings.vSyncCount = oldVSync; Application.targetFrameRate = oldTarget; Application.runInBackground = oldRunInBackground; changed = false; }
            if (copy) { Destroy(copy); copy = null; }
        }
        private void OnDisable() { Application.logMessageReceived -= Log; Restore(); }
    }
}
#endif
