#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Video;

namespace TimeTheft.Editor
{
    // Editor Play 전용 기록기. 라이브 셰이더 프레임을 인코딩하며 조명 캐시는 만들지 않는다.
    public sealed class H2C2Capture : MonoBehaviour
    {
        public string Status { get; private set; } = "Idle";
        public int FramesWritten { get; private set; }

        public void ExtractReference(string source)
        {
            StartCoroutine(Reference(source));
        }

        private IEnumerator Reference(string source)
        {
            Status = "Preparing delivered Blender C2 video";
            var player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false; player.source = VideoSource.Url; player.url = source;
            player.isLooping = false; player.skipOnDrop = false; player.audioOutputMode = VideoAudioOutputMode.None;
            player.Prepare();
            float deadline = Time.realtimeSinceStartup + 15;
            while (!player.isPrepared && Time.realtimeSinceStartup < deadline) yield return null;
            if (!player.isPrepared) { Status = "Reference preparation timed out"; yield break; }
            var rt = new RenderTexture((int)player.width, (int)player.height, 0, RenderTextureFormat.ARGB32);
            rt.Create(); player.targetTexture = rt; player.renderMode = VideoRenderMode.RenderTexture;
            player.Play();
            deadline = Time.realtimeSinceStartup + 15;
            while (player.frame < 0 && Time.realtimeSinceStartup < deadline) yield return null;
            player.Pause();
            yield return new WaitForEndOfFrame();
            var tex = Read(rt);
            string path = "Assets/TimeTheft/PocketWatch/C2/Reference/Blender_C2_Frame.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            File.WriteAllText("Temp/H2C2/reference-frame.json", "{\"source\":\"" + source.Replace("\\", "/") + "\",\"frame\":" + player.frame + ",\"fps\":" + player.frameRate + ",\"time\":" + player.time + "}");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false; importer.maxTextureSize = 2048; importer.SaveAndReimport();
            Destroy(tex); player.Stop(); Destroy(player); rt.Release(); Destroy(rt);
            Status = "Reference extracted";
        }

        public void Record(string path, int seconds = 24, int fps = 30)
        {
            StartCoroutine(Movie(path, seconds, fps));
        }

        private IEnumerator Movie(string path, int seconds, int fps)
        {
            var stage = FindFirstObjectByType<H2C2ComparisonStage>();
            stage.Animate = false;
            Status = "Warming up capture";
            yield return new WaitForSeconds(.3f);
            var attrs = new VideoTrackAttributes { width = 1280, height = 720, frameRate = new MediaRational(fps), includeAlpha = false, bitRateMode = VideoBitrateMode.High };
            float priorCapture = Time.captureDeltaTime;
            var texture = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
            var resized = new Color32[1280 * 720];
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path), "Images"));
            Status = "Recording"; FramesWritten = 0;
            using (var encoder = new MediaEncoder(path, attrs))
            {
                Time.captureDeltaTime = 1f / fps;
                for (int frame = 0; frame < seconds * fps; frame++)
                {
                    stage.EvaluateDemo(frame / (float)fps);
                    yield return new WaitForEndOfFrame();
                    var screen = ScreenCapture.CaptureScreenshotAsTexture();
                    // ScreenCapture의 표시 바이트를 그대로 축소한다. sRGB RT Blit의 이중 감마 변환을 피한다.
                    var source = screen.GetPixels32();
                    float sx = screen.width / 1280f, sy = screen.height / 720f;
                    for (int y = 0; y < 720; y++)
                    {
                        int row = Mathf.Min(screen.height - 1, (int)((y + .5f) * sy)) * screen.width;
                        for (int x = 0; x < 1280; x++) resized[y * 1280 + x] = source[row + Mathf.Min(screen.width - 1, (int)((x + .5f) * sx))];
                    }
                    texture.SetPixels32(resized); texture.Apply();
                    if (frame == 0)
                    {
                        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path), "Images", Path.GetFileNameWithoutExtension(path) + "-native.png"), screen.EncodeToPNG());
                        File.WriteAllText(path + ".capture.json", "{\"sourceFormat\":\"" + screen.graphicsFormat + "\",\"sourceSrgb\":" + (screen.isDataSRGB ? "true" : "false") + ",\"resize\":\"CPU display-byte nearest; no color conversion\",\"fps\":" + fps + ",\"frames\":" + (seconds * fps) + "}");
                    }
                    bool written = encoder.AddFrame(texture);
                    if (frame == 0 || frame == 240 || frame == 480 || frame == 660 || frame == seconds * fps - 1)
                        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path), "Images", Path.GetFileNameWithoutExtension(path) + "-frame" + frame.ToString("0000") + ".png"), texture.EncodeToPNG());
                    Destroy(screen);
                    if (!written) { Status = "Encoder rejected frame " + frame; break; }
                    FramesWritten++;
                }
            }
            Time.captureDeltaTime = priorCapture;
            Destroy(texture);
            if (FramesWritten == seconds * fps) Status = "Recorded " + path;
        }

        private static Texture2D Read(RenderTexture rt)
        {
            var prior = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prior; return tex;
        }
    }
}
#endif
