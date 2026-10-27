#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Video;

namespace TimeTheft.Editor
{
    // 실제 게임의 unscaled/realtime 진행을 기록한다. 포즈/게임 시간을 수동으로 재생하지 않는다.
    public sealed class H2BatchRevealCapture : MonoBehaviour
    {
        [Serializable] private sealed class Sample { public float time; public int encodedFrame; public string phase, stage, camera; }
        [Serializable] private sealed class CaptureReport
        {
            public string clock = "Live Controller unscaled time, real elapsed timestamps. No captureDeltaTime/timeScale override. Repeated frames preserve elapsed time if capture drops below output fps.";
            public int width = 1280, height = 720, fps, frames, uniqueSamples, repeatedFrames;
            public float actualDuration;
            public Sample[] samples;
        }
        public string Status { get; private set; } = "Idle";
        public int FramesWritten { get; private set; }
        private MediaEncoder encoder;
        public void Record(string path, int seconds = 12, int fps = 24) => StartCoroutine(Movie(path, seconds, fps));
        private IEnumerator Movie(string path, int seconds, int fps)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string images = Path.Combine(Path.GetDirectoryName(path), "Images"); Directory.CreateDirectory(images);
            var controller = FindFirstObjectByType<TimeTheftGameController>(); var presentation = controller.View.BatchReveal;
            var target = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
            // Screenshot texture는 이미 표시 색으로 인코딩되어 있으므로 추가 sRGB 변환 없이 축소한다.
            var scaled = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            scaled.Create();
            var saved = new HashSet<string>(); var samples = new List<Sample>();
            FramesWritten = 0; int expected = seconds * fps;
            var attributes = new VideoTrackAttributes { width = 1280, height = 720, frameRate = new MediaRational(fps), includeAlpha = false, bitRateMode = VideoBitrateMode.High };
            Status = "Warming capture";
            encoder = new MediaEncoder(path, attributes);
            // 인코더 초기화와 MCP 호출의 긴 프레임을 실제 연출 시작 전에 소진한다.
            yield return null; yield return new WaitForEndOfFrame(); yield return null;
            float start = Time.realtimeSinceStartup; controller.RestartGame(); Status = "Recording live game";
            try
            {
                while (FramesWritten < expected)
                {
                    yield return new WaitForEndOfFrame();
                    float elapsed = Time.realtimeSinceStartup - start;
                    int due = Mathf.Min(expected, Mathf.FloorToInt(elapsed * fps) + 1);
                    if (due <= FramesWritten) continue;
                    // 실제 Game View 캡처 크기/방향을 사용한다. HiDPI에서 Screen 크기 RT로 직접 복사하면 잘릴 수 있다.
                    var screen = ScreenCapture.CaptureScreenshotAsTexture(); Graphics.Blit(screen, scaled);
                    var active = RenderTexture.active; RenderTexture.active = scaled;
                    target.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); target.Apply(); RenderTexture.active = active; Destroy(screen);
                    string label = null;
                    if (presentation.Phase == H2BatchRevealPresentation.RevealPhase.Preparing) label = "01-Default";
                    if (presentation.Phase == H2BatchRevealPresentation.RevealPhase.Revealing && presentation.PhaseElapsed >= presentation.RevealDuration * .48f) label = "02-Approach";
                    if (presentation.Phase == H2BatchRevealPresentation.RevealPhase.Observing)
                        label = Time.realtimeSinceStartup - presentation.ReadableSince >= 2.5f ? "04-Observe" : "03-AllOpen";
                    if (presentation.Phase == H2BatchRevealPresentation.RevealPhase.Closing && presentation.PhaseElapsed >= presentation.CloseDuration * .45f) label = "05-Closing";
                    if (presentation.Phase == H2BatchRevealPresentation.RevealPhase.Returning && presentation.PhaseElapsed >= presentation.CameraReturnDuration * .45f) label = "06-Returning";
                    if (!presentation.IsRunning && controller.Match.Stage == TurnStage.Shuffling) label = "07-Returned-Shuffle";
                    if (label != null && saved.Add(label)) File.WriteAllBytes(Path.Combine(images, label + ".png"), target.EncodeToPNG());
                    samples.Add(new Sample { time = elapsed, encodedFrame = FramesWritten, phase = presentation.Phase.ToString(), stage = controller.Match.Stage.ToString(), camera = Camera.main.transform.position.ToString("F4") });
                    while (FramesWritten < due)
                    {
                        if (!encoder.AddFrame(target)) throw new InvalidOperationException("Encoder rejected frame " + FramesWritten);
                        FramesWritten++;
                    }
                }
                File.WriteAllText(path + ".capture.json", JsonUtility.ToJson(new CaptureReport { fps = fps, frames = FramesWritten, uniqueSamples = samples.Count,
                    repeatedFrames = FramesWritten - samples.Count, actualDuration = Time.realtimeSinceStartup - start, samples = samples.ToArray() }, true));
                Status = "Recorded " + path;
            }
            finally
            {
                if (encoder != null) { encoder.Dispose(); encoder = null; }
                scaled.Release(); Destroy(scaled); Destroy(target);
            }
        }
        public void InspectReference(string path) => StartCoroutine(Reference(path));
        private IEnumerator Reference(string path)
        {
            var player = gameObject.AddComponent<VideoPlayer>(); player.playOnAwake = false; player.source = VideoSource.Url; player.url = path;
            player.audioOutputMode = VideoAudioOutputMode.None; player.renderMode = VideoRenderMode.RenderTexture;
            var rt = new RenderTexture(1280, 720, 0); rt.Create(); player.targetTexture = rt;
            Status = "Preparing source review"; player.Prepare(); float deadline = Time.realtimeSinceStartup + 15;
            while (!player.isPrepared && Time.realtimeSinceStartup < deadline) yield return null;
            if (!player.isPrepared) throw new InvalidOperationException("Source review preparation failed");
            Directory.CreateDirectory("Temp/H2BatchReveal/Reference");
            File.WriteAllText("Temp/H2BatchReveal/Reference/video.json", "{\"fps\":" + player.frameRate + ",\"frames\":" + player.frameCount + ",\"seconds\":" + player.length + "}");
            foreach (float time in new[] { 3f, 4.95f, 6f })
            {
                long frame = (long)(time * player.frameRate); player.frame = frame; player.Play(); deadline = Time.realtimeSinceStartup + 10;
                while (player.frame < frame && Time.realtimeSinceStartup < deadline) yield return null;
                yield return null; player.Pause(); yield return new WaitForEndOfFrame();
                var active = RenderTexture.active; RenderTexture.active = rt;
                var texture = new Texture2D(1280, 720, TextureFormat.RGBA32, false); texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                File.WriteAllBytes("Temp/H2BatchReveal/Reference/frame-" + player.frame + ".png", texture.EncodeToPNG()); Destroy(texture); RenderTexture.active = active;
            }
            player.Stop(); Destroy(player); rt.Release(); Destroy(rt); Status = "Source reviewed";
        }
        private void OnDisable() { if (encoder != null) { encoder.Dispose(); encoder = null; } }
    }
}
#endif
