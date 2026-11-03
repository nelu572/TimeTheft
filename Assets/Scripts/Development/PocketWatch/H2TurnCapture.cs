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
    // 검증용 public 요청만 사용한다. 비공개 Face 조회/포즈별 시간 강제 진행은 하지 않는다.
    public sealed class H2TurnCapture : MonoBehaviour
    {
        [Serializable] private sealed class Sample
        {
            public float time; public int frame, clock, hours;
            public string stage, actor, phase, kind, camera, root;
        }
        [Serializable] private sealed class Report
        {
            public string method = "Actual TimeTheftGame at timeScale=1; original presentation durations, seed635 runtime settings copy. Public SelectClock/action button requests drive the demo. No private Face lookup, captureDeltaTime, or per-frame pose playback. Existing item phases are ended without using player items; AI remains enabled. Held frames preserve elapsed timing.";
            public int fps, frames, uniqueFrames, heldFrames;
            public float actualSeconds;
            public Sample[] samples;
        }
        public string Status { get; private set; } = "Idle";
        private MediaEncoder encoder;
        private bool ownsRecorder;
        public void Record(string path, int seconds = 45)
        {
            if (ownsRecorder) return;
            if (FindObjectsByType<H2TurnCapture>(FindObjectsSortMode.None).Any(c => c != this && c.ownsRecorder))
            { Status = "Recording already active"; return; }
            if (!Application.isPlaying) { Status = "Play Mode required"; return; }
            ownsRecorder = true; StartCoroutine(Movie(path, seconds));
        }
        private IEnumerator Movie(string path, int seconds)
        {
            const int fps = 24; Directory.CreateDirectory(Path.GetDirectoryName(path));
            string images = Path.Combine(Path.GetDirectoryName(path), "Images"); Directory.CreateDirectory(images);
            var controller = FindFirstObjectByType<TimeTheftGameController>();
            var original = controller.Settings; var settings = Instantiate(original); settings.RandomSeed = 635;
            var so = new SerializedObject(controller); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            var target = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
            var scaled = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); scaled.Create();
            var attributes = new VideoTrackAttributes { width = 1280, height = 720, frameRate = new MediaRational(fps), includeAlpha = false, bitRateMode = VideoBitrateMode.High };
            var saved = new HashSet<string>(); var samples = new List<Sample>(); int frames = 0;
            Coroutine driver = null;
            try
            {
                encoder = new MediaEncoder(path, attributes); Status = "Warming";
                yield return null; yield return new WaitForEndOfFrame(); yield return null;
                float start = Time.realtimeSinceStartup; controller.RestartGame();
                driver = StartCoroutine(Drive(controller, start + seconds)); Status = "Recording actual game";
                while (frames < seconds * fps)
                {
                    yield return new WaitForEndOfFrame(); float time = Time.realtimeSinceStartup - start;
                    int due = Mathf.Min(seconds * fps, Mathf.FloorToInt(time * fps) + 1); if (due <= frames) continue;
                    var active = RenderTexture.active;
                    var screen = ScreenCapture.CaptureScreenshotAsTexture(); Graphics.Blit(screen, scaled);
                    RenderTexture.active = scaled;
                    target.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); target.Apply(); RenderTexture.active = active; Destroy(screen);
                    var match = controller.Match; var motion = controller.View.TurnPresentation; int index = match.CurrentClock;
                    ClockFace? face = index >= 0 ? match.GetClock(index).Face : null;
                    string label = null; bool player = match.ActiveParticipant == Participant.Player;
                    if (match.Stage == TurnStage.AwaitingClock && player) label = "01-Closed-ready";
                    if (motion.Phase == H2TurnPresentation.TurnPhase.Selecting && motion.PhaseElapsed >= motion.SelectDuration * .45f) label = player ? "02-Select" : "08-AI-Select";
                    if (motion.Phase == H2TurnPresentation.TurnPhase.DecisionHold) label = player ? "03-Decision-closed" : "09-AI-Decision";
                    if (motion.Phase == H2TurnPresentation.TurnPhase.RevealEnter && motion.PhaseElapsed >= motion.RevealEnterDuration * .5f) label = player ? "04-Reveal-Enter" : "10-AI-Reveal";
                    if (motion.Phase == H2TurnPresentation.TurnPhase.ResultPose && face.HasValue && match.Stage == TurnStage.Revealing)
                        label = (player ? "05-Player-" : "11-AI-") + face.Value.Kind + "-open";
                    if (motion.Phase == H2TurnPresentation.TurnPhase.LegacyReturn && motion.PhaseElapsed >= motion.LegacyReturnDuration * .45f) label = "06-Legacy-camera-handoff";
                    if (match.Stage == TurnStage.Settling) label = "07-Existing-settlement";
                    if (label != null && saved.Add(label)) File.WriteAllBytes(Path.Combine(images, label + ".png"), target.EncodeToPNG());
                    samples.Add(new Sample { time = time, frame = frames, clock = index, hours = face.HasValue ? face.Value.Hours : 0,
                        kind = face.HasValue ? face.Value.Kind.ToString() : "Hidden", stage = match.Stage.ToString(), actor = match.ActiveParticipant.ToString(),
                        phase = motion.Phase.ToString(), camera = controller.View.FocusCamera.transform.position.ToString("F4"),
                        root = index >= 0 ? controller.View.Clocks[index].H2Presentation.MotionRoot.position.ToString("F4") : "none" });
                    while (frames < due) { if (!encoder.AddFrame(target)) throw new InvalidOperationException("Encoder frame " + frames); frames++; }
                }
                File.WriteAllText(path + ".capture.json", JsonUtility.ToJson(new Report { fps = fps, frames = frames, uniqueFrames = samples.Count,
                    heldFrames = frames - samples.Count, actualSeconds = Time.realtimeSinceStartup - start, samples = samples.ToArray() }, true));
                Status = "Recorded";
            }
            finally
            {
                if (driver != null) StopCoroutine(driver); if (encoder != null) { encoder.Dispose(); encoder = null; }
                so.FindProperty("settings").objectReferenceValue = original; so.ApplyModifiedPropertiesWithoutUndo();
                Destroy(settings); Destroy(target);
                if (RenderTexture.active == scaled) RenderTexture.active = null;
                scaled.Release(); Destroy(scaled); ownsRecorder = false;
            }
        }
        private IEnumerator Drive(TimeTheftGameController c, float deadline)
        {
            float stageSince = Time.realtimeSinceStartup; TurnStage last = c.Match.Stage; int playerTurns = 0;
            bool wasSelecting = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                var m = c.Match;
                if (m.Stage != last || (wasSelecting && !c.IsPresentingClock)) { last = m.Stage; stageSince = Time.realtimeSinceStartup; }
                wasSelecting = c.IsPresentingClock;
                if (m.ActiveParticipant == Participant.Player && !c.IsPresentingItem)
                {
                    if (m.Stage == TurnStage.AwaitingItems && Time.realtimeSinceStartup - stageSince > .5f) c.EndItemPhase();
                    if (m.Stage == TurnStage.AwaitingClock && Time.realtimeSinceStartup - stageSince > .6f)
                    {
                        var ids = m.GetAvailableClockIndices(); c.SelectClock(playerTurns == 0 && ids.Contains(0) ? 0 : ids.Last());
                    }
                    if (m.Stage == TurnStage.AwaitingKeyDecision && !c.IsPresentingClock && Time.realtimeSinceStartup - stageSince > 2) c.KeepPeekedClock();
                    if (m.Stage == TurnStage.AwaitingAction && !c.IsPresentingClock && Time.realtimeSinceStartup - stageSince > 2)
                    { (playerTurns++ % 2 == 0 ? c.View.RationButton : c.View.CollectButton).onClick.Invoke(); }
                }
                yield return null;
            }
        }
        private void OnDisable() { if (encoder != null) { encoder.Dispose(); encoder = null; } ownsRecorder = false; }
    }
}
#endif
