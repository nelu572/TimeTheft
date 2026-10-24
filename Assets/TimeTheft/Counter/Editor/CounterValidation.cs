using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeTheft.CounterStudy.Editor
{
    // 결과 JSON과 캡처는 호출자가 지정한 외부 검증 폴더에만 저장한다.
    public static class CounterValidation
    {
        public const string Root = "Assets/TimeTheft/Counter";
        public const string Prefab = Root + "/IntegratedTimeCounter.prefab";
        public const string Scene = "Assets/Scenes/CounterStructureStudy.unity";

        private static CounterStructureStudy Study()
        {
            var s = Object.FindFirstObjectByType<CounterStructureStudy>();
            if (!s || s.gameObject.scene.path != Scene) throw new InvalidOperationException("CounterStructureStudy에서 실행합니다.");
            return s;
        }

        public static string Verify(string directory)
        {
            var s = Study();
            var model = s.singleCandidate;
            var failures = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (!prefab) throw new InvalidOperationException("기준 Prefab이 없습니다.");
            var dependencies = AssetDatabase.GetDependencies(Scene, true);
            if (dependencies.Any(x => x.Contains("/Experiments/"))) failures.Add("과거 실험 의존성");
            if (s.GetComponentsInChildren<CounterStudyModel>(true).Length != 1) failures.Add("후보 수");
            foreach (var t in s.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) failures.Add("Missing script: " + t.name);
            }
            var meshes = model.GetComponentsInChildren<MeshFilter>(true).Where(f => !f.GetComponent<TMP_Text>()).ToArray();
            foreach (var f in meshes)
            {
                var r = f.GetComponent<MeshRenderer>();
                if (!f.sharedMesh || f.sharedMesh.uv.Length != f.sharedMesh.vertexCount) failures.Add("Mesh/UV: " + f.name);
                if (!r || r.sharedMaterials.Any(m => !m || !m.shader || !m.mainTexture)) failures.Add("Material: " + f.name);
            }
            var imports = new List<object>();
            foreach (string p in dependencies.Where(p => p.StartsWith(Root) && p.EndsWith(".png")))
            {
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                bool ok = importer && importer.filterMode == FilterMode.Point && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed;
                if (!ok) failures.Add("Import: " + p);
                imports.Add(new { path = p, ok });
            }
            var texts = model.GetComponentsInChildren<TextMeshPro>(true);
            foreach (var t in texts)
            {
                t.ForceMeshUpdate(true, true);
                if (!t.font || !t.fontSharedMaterial || t.fontSharedMaterial.shader.name != "TimeTheft/Counter Pixel Glyph") failures.Add("Font: " + t.name);
                foreach (char c in t.text) if (!t.font.HasCharacter(c)) failures.Add("Missing glyph: " + t.name + " / " + c);
            }
            if (texts.Length != 6 || texts.Count(t => t.text == "30:00") != 2 || !texts.Any(t => t.text == "00:00")) failures.Add("표시 값");
            foreach (string part in new[] { "MainCurvedConduit", "AuxiliaryCurvedConduit", "07_UnterminatedCableRoute" })
                if (!meshes.Any(f => f.name == part)) failures.Add("Cable: " + part);
            s.GetLeftTableEdge(out Vector3 near, out Vector3 far);
            Vector3 body = s.singleBodyShell.transform.TransformPoint(s.singleBodyShell.sharedMesh.bounds.center);
            float centerError = Mathf.Abs(Vector3.Dot(body - (near + far) * .5f, (far - near).normalized));
            if (centerError > .0001f || Mathf.Abs(Vector3.Dot(model.transform.right, (far - near).normalized)) < .9999f) failures.Add("가장자리 정렬");
            var result = new
            {
                playing = Application.isPlaying, candidate = model.name, meshInstances = meshes.Length,
                uniqueMeshes = meshes.Select(f => f.sharedMesh).Distinct().Count(),
                triangles = meshes.Sum(f => f.sharedMesh.triangles.Length / 3), dependencies, failures, imports,
                position = new[] { model.transform.position.x, model.transform.position.y, model.transform.position.z },
                yaw = model.transform.eulerAngles.y, model.size, model.depth, centerError,
                texts = texts.Select(t => new { t.name, t.text, font = t.font.name, t.fontSize, color = new[] { t.color.r, t.color.g, t.color.b, t.color.a } }).ToArray()
            };
            Directory.CreateDirectory(directory);
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented,
                new Newtonsoft.Json.JsonSerializerSettings { ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore });
            File.WriteAllText(Path.Combine(directory, "asset-verification.json"), json);
            return "meshes=" + meshes.Length + ", failures=" + failures.Count + ", playing=" + Application.isPlaying;
        }

        private static void ParallaxPose(CounterStructureStudy s, int x, int y)
        {
            var settings = new SerializedObject(s.cameraLook);
            float strength = Mathf.Clamp01(settings.FindProperty("strength").floatValue);
            float horizontal = settings.FindProperty("horizontalDegrees").floatValue * strength;
            float vertical = settings.FindProperty("verticalDegrees").floatValue * strength;
            float tx = settings.FindProperty("horizontalTravel").floatValue * strength;
            float ty = settings.FindProperty("verticalTravel").floatValue * strength;
            var q = Quaternion.Euler(s.gameEuler);
            s.studyCamera.transform.SetPositionAndRotation(s.gamePosition + q * new Vector3(x * tx, y * ty, 0),
                Quaternion.AngleAxis(x * horizontal, Vector3.up) * q * Quaternion.AngleAxis(-y * vertical, Vector3.right));
        }

        private static void Capture(Camera camera, string path)
        {
            var old = camera.targetTexture;
            var active = RenderTexture.active;
            var rt = new RenderTexture(1600, 900, 24);
            Texture2D image = null;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = old;
                RenderTexture.active = active;
                if (image) Object.DestroyImmediate(image);
                Object.DestroyImmediate(rt);
            }
        }

        public static string CaptureViews(string directory)
        {
            var s = Study();
            var oldView = s.currentView;
            bool oldParallax = s.parallax;
            float aspect = s.studyCamera.aspect;
            var rows = new List<object>();
            Action<string> check = name =>
            {
                var texts = s.singleCandidate.GetComponentsInChildren<TextMeshPro>(true).Select(t =>
                {
                    t.ForceMeshUpdate(true, true);
                    int outside = 0;
                    foreach (var c in t.textInfo.characterInfo.Where(c => c.isVisible))
                    {
                        Vector3 center = (c.bottomLeft + c.topRight) * .5f;
                        foreach (var p in new[] { c.bottomLeft, c.bottomRight, c.topLeft, c.topRight })
                        {
                            var q = s.studyCamera.WorldToViewportPoint(t.transform.TransformPoint(Vector3.Lerp(p, center, .15f)));
                            if (q.z <= 0 || q.x < 0 || q.x > 1 || q.y < 0 || q.y > 1) outside++;
                        }
                    }
                    return new { t.name, outside };
                }).ToArray();
                rows.Add(new { view = name, texts });
                Capture(s.studyCamera, Path.Combine(directory, name + ".png"));
            };
            Directory.CreateDirectory(directory);
            try
            {
                s.SetParallax(false);
                s.studyCamera.aspect = 16f / 9;
                s.ShowSingle(CounterStudyView.Game); check("Game");
                foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
                {
                    s.ShowSingle(CounterStudyView.Game);
                    ParallaxPose(s, x, y); check("Parallax_" + x + "_" + y);
                }
                foreach (var v in new[] { CounterStudyView.Front, CounterStudyView.Left, CounterStudyView.Right, CounterStudyView.TablePlan })
                {
                    s.ShowSingle(v); check(v.ToString());
                }
                File.WriteAllText(Path.Combine(directory, "view-verification.json"), Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented));
            }
            finally
            {
                s.studyCamera.aspect = aspect;
                s.SetParallax(oldParallax);
                s.ShowSingle(oldView);
            }
            return "9 views captured; camera and parallax restored";
        }
    }
}
