#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class H2C2RenderingTests
    {
        private const string Scene = "Assets/TimeTheft/PocketWatch/Validation/H2WatchValidation.unity";

        [UnityTest] public IEnumerator GpuCellLightingIsUniformAndBothGridsKeepNarrowGlints()
        {
            yield return EditorSceneManager.LoadSceneInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            var stage = Object.FindFirstObjectByType<H2C2ComparisonStage>(); stage.EvaluateDemo(4);
            var binding = stage.Binding; var camera = Camera.main;
            var layers = binding.MetalRenderers.Select(r => r.gameObject.layer).ToArray(); int mask = camera.cullingMask; Color background = camera.backgroundColor;
            try
            {
                foreach (var r in binding.MetalRenderers) r.gameObject.layer = 31;
                camera.cullingMask = 1 << 31; camera.backgroundColor = Color.clear;
                var ids = Capture(camera, binding, 6); var broad = Capture(camera, binding, 3); var glints = Capture(camera, binding, 4); var shape = Capture(camera, binding, 8);
                var coarse = new Dictionary<int, float>(); var fine = new Dictionary<int, float>(); int valid = 0, total = 0, highlights = 0;
                for (int p = 0; p < ids.Length; p++)
                {
                    if (shape[p].a > .99f) total++;
                    if (ids[p].a < .99f) continue;
                    valid++; int owner = Mathf.RoundToInt(ids[p].b * 16), a = owner * 65536 + Mathf.RoundToInt(ids[p].r * 65535), b = owner * 65536 + Mathf.RoundToInt(ids[p].g * 65535);
                    if (coarse.TryGetValue(a, out float previous)) Assert.That(broad[p].r, Is.EqualTo(previous).Within(1e-5f)); else coarse.Add(a, broad[p].r);
                    if (fine.TryGetValue(b, out previous)) Assert.That(glints[p].r, Is.EqualTo(previous).Within(1e-5f)); else fine.Add(b, glints[p].r);
                    if (glints[p].r >= 10f / 12f - .001f) highlights++;
                }
                Assert.That(coarse.Count, Is.GreaterThan(300));
                Assert.That(fine.Count, Is.GreaterThan(coarse.Count));
                Assert.That(valid / (float)total, Is.GreaterThan(.97f), "Unexpected UV ownership/fallback seams.");
                Assert.That(highlights, Is.GreaterThan(10), "Metal glints must survive the UV96 pass.");
                TestContext.WriteLine("GPU grid cells visible: " + coarse.Count + "/" + fine.Count + "; valid pixels " + valid + "/" + total + "; glint pixels " + highlights);
            }
            finally
            {
                for (int i = 0; i < layers.Length; i++) { binding.MetalRenderers[i].gameObject.layer = layers[i]; binding.MetalRenderers[i].SetPropertyBlock(null); }
                camera.cullingMask = mask; camera.backgroundColor = background;
            }
        }

        [UnityTest] public IEnumerator MovingLightChangesActualMetalAndAStaticHoldDoesNotFlicker()
        {
            yield return EditorSceneManager.LoadSceneInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single)); yield return null; yield return null;
            var stage = Object.FindFirstObjectByType<H2C2ComparisonStage>(); stage.EvaluateDemo(0);
            var camera = Camera.main;
            int mask = camera.cullingMask; Color background = camera.backgroundColor;
            var layers = stage.Binding.MetalRenderers.Select(r => r.gameObject.layer).ToArray();
            try
            {
                // 문자판/바닥의 Lit 반응으로 잘못 통과하지 않도록 C2 금속만 검사한다.
                foreach (var r in stage.Binding.MetalRenderers) r.gameObject.layer = 31;
                camera.cullingMask = 1 << 31; camera.backgroundColor = Color.clear;
                Color[] first = Capture(camera, stage.Binding, 0);
                stage.Animate = true;
                yield return new WaitForSeconds(1f);
                stage.Animate = false;
                Color[] moved = Capture(camera, stage.Binding, 0);
                int changed = Differences(first, moved);
                Assert.That(changed, Is.GreaterThan(100));
                Color[] held = Capture(camera, stage.Binding, 0);
                Assert.That(Differences(moved, held), Is.EqualTo(0));
                TestContext.WriteLine("Moving-light C2 metal-only changed pixels: " + changed);
            }
            finally
            {
                for (int i = 0; i < layers.Length; i++) { stage.Binding.MetalRenderers[i].gameObject.layer = layers[i]; stage.Binding.MetalRenderers[i].SetPropertyBlock(null); }
                camera.cullingMask = mask; camera.backgroundColor = background;
            }
        }

        [UnityTest] public IEnumerator ClosedNormalAndRedAreIdenticalButOpenInnerMaskDiffers()
        {
            yield return EditorSceneManager.LoadSceneInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single)); yield return null; yield return null;
            var stage = Object.FindFirstObjectByType<H2C2ComparisonStage>(); stage.EvaluateDemo(4);
            var view = stage.Binding.GetComponent<H2PocketWatchView>(); var camera = Camera.main;
            view.SetLidAngle(0); view.SetRed(false); stage.Binding.SetC2(true);
            var normal = Capture(camera, stage.Binding, 0);
            view.SetRed(true); stage.Binding.SetC2(true);
            Assert.That(Differences(normal, Capture(camera, stage.Binding, 0)), Is.EqualTo(0));
            view.SetLidAngle(115); view.SetRed(false); stage.Binding.SetC2(true);
            normal = Capture(camera, stage.Binding, 0);
            view.SetRed(true); stage.Binding.SetC2(true);
            Assert.That(Differences(normal, Capture(camera, stage.Binding, 0)), Is.GreaterThan(100));
        }

        private static Color[] Capture(Camera camera, H2C2MaterialBinding binding, int debug)
        {
            // 직접 Camera.Render는 LateUpdate를 지나지 않는다. View가 방금 바꾼 Lid 재질을 먼저 동기화한다.
            binding.SetC2(true);
            var properties = new MaterialPropertyBlock(); properties.SetFloat("_DebugView", debug);
            foreach (var r in binding.MetalRenderers) r.SetPropertyBlock(properties);
            var prior = camera.targetTexture; var active = RenderTexture.active;
            bool priorMsaa = camera.allowMSAA; camera.allowMSAA = false;
            var rt = RenderTexture.GetTemporary(640, 360, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); camera.targetTexture = rt;
            camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true); texture.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); texture.Apply();
            var pixels = texture.GetPixels(); Object.DestroyImmediate(texture);
            camera.targetTexture = prior; camera.allowMSAA = priorMsaa; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); return pixels;
        }
        private static int Differences(Color[] a, Color[] b)
        {
            int count = 0; for (int i = 0; i < a.Length; i++) if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > .001f) count++;
            return count;
        }
    }
}
#endif
