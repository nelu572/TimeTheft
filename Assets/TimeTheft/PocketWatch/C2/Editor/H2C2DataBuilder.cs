using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TimeTheft.Editor
{
    // 실제 임포트 메시에서 정적 GPU 입력만 한 번 생성한다. 실행 중 재계산하지 않는다.
    public static class H2C2DataBuilder
    {
        public const string Root = "Assets/TimeTheft/PocketWatch/";
        public const string Data = Root + "C2/Data/";
        private sealed class Sample
        {
            public Vector3 position, normal;
            public float inner, detail;
            public Vector2 uv;
        }

        public static string Build()
        {
            foreach (string path in Directory.GetFiles(Data, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = false; importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false; importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 512; importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
            }
            var map48 = AssetDatabase.LoadAssetAtPath<Texture2D>(Data + "H2_UVCellIDs48_512.png").GetPixels32();
            var map96 = AssetDatabase.LoadAssetAtPath<Texture2D>(Data + "H2_UVCellIDs96_512.png").GetPixels32();
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(Data + "H2_DetailIndex_512.png").GetPixels();
            var flags = AssetDatabase.LoadAssetAtPath<Texture2D>(Data + "H2_UV_SurfaceFlags_512.png").GetPixels32();
            var samples = new Sample[512 * 512];
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/H2_Watch.fbx");
            var visual = model.transform.Find("H2_Watch/MotionRoot/VisualScale");
            var bindings = JObject.Parse(File.ReadAllText(Root + "Materials/MaterialBindings.json"));
            foreach (var part in bindings["default_parts"])
            {
                if (part["sp_owner"].Type == JTokenType.Null || (string)part["profile"] == "H2_Hands_DarkMetal") continue;
                int owner = (int)part["sp_owner"];
                var mesh = visual.Find((string)part["path"]).GetComponent<MeshFilter>().sharedMesh;
                var uv = mesh.uv; var vertices = mesh.vertices; var normals = mesh.normals; var indices = mesh.triangles;
                for (int tri = 0; tri < indices.Length; tri += 3)
                {
                    int a = indices[tri], b = indices[tri + 1], c = indices[tri + 2];
                    Vector2 ab = uv[b] - uv[a], ac = uv[c] - uv[a];
                    float det = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(det) < 1e-12f) continue;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uv[a].x, uv[b].x, uv[c].x) * 512), 0, 511);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uv[a].x, uv[b].x, uv[c].x) * 512), 0, 511);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uv[a].y, uv[b].y, uv[c].y) * 512), 0, 511);
                    int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uv[a].y, uv[b].y, uv[c].y) * 512), 0, 511);
                    Vector3 dpdu = ((vertices[b] - vertices[a]) * ac.y - (vertices[c] - vertices[a]) * ab.y) / det;
                    Vector3 dpdv = ((vertices[c] - vertices[a]) * ab.x - (vertices[b] - vertices[a]) * ac.x) / det;
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        int pixel = y * 512 + x;
                        if (map48[pixel].a != 255 || map48[pixel].b != owner + 1) continue;
                        Vector2 point = new Vector2((x + .5f) / 512, (y + .5f) / 512), d = point - uv[a];
                        float wb = (d.x * ac.y - d.y * ac.x) / det, wc = (ab.x * d.y - ab.y * d.x) / det, wa = 1 - wb - wc;
                        if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) continue;
                        Vector3 n = (normals[a] * wa + normals[b] * wb + normals[c] * wc).normalized;
                        float du = (detail[y * 512 + Mathf.Min(x + 1, 511)].r - detail[y * 512 + Mathf.Max(x - 1, 0)].r) * .5f * 512;
                        float dv = (detail[Mathf.Min(y + 1, 511) * 512 + x].r - detail[Mathf.Max(y - 1, 0) * 512 + x].r) * .5f * 512;
                        // Original .12*.0007 height; mesh-derived tangent derivatives, no artistic cap normals.
                        n = (n - dpdu.normalized * (du * .000084f / Mathf.Max(dpdu.magnitude, 1e-5f)) - dpdv.normalized * (dv * .000084f / Mathf.Max(dpdv.magnitude, 1e-5f))).normalized;
                        samples[pixel] = new Sample { position = vertices[a] * wa + vertices[b] * wb + vertices[c] * wc, normal = n, uv = point, inner = flags[pixel].r / 255f, detail = detail[pixel].r };
                    }
                }
            }
            int missing48 = BuildGrid(map48, samples, 9225, false);
            int missing96 = BuildGrid(map96, samples, 35145, true);
            if (missing48 != 0 || missing96 != 0) throw new InvalidOperationException("Uncovered UV cells: " + missing48 + "/" + missing96);
            CreateMaterials(bindings);
            AssetDatabase.SaveAssets();
            return "UV48=9225, UV96=35145; missing cells=0; imported mesh P/N; UV96 deterministic 4 samples; runtime CPU rebake=none";
        }

        private static int BuildGrid(Color32[] map, Sample[] raster, int count, bool fine)
        {
            var groups = Enumerable.Range(0, count).Select(_ => new List<Sample>()).ToArray();
            for (int p = 0; p < map.Length; p++)
            {
                int id = map[p].r + map[p].g * 256;
                if (map[p].a == 255 && id < count && raster[p] != null) groups[id].Add(raster[p]);
            }
            int missing = groups.Count(g => g.Count == 0);
            if (missing > 0) return missing;
            int height = (count + 255) / 256, layers = fine ? 4 : 1;
            var positions = new Color[layers][]; var normals = new Color[layers][];
            for (int layer = 0; layer < layers; layer++) { positions[layer] = new Color[256 * height]; normals[layer] = new Color[256 * height]; }
            for (int id = 0; id < count; id++)
            {
                var candidates = groups[id];
                Vector2 min = candidates[0].uv, max = min;
                foreach (var s in candidates) { min = Vector2.Min(min, s.uv); max = Vector2.Max(max, s.uv); }
                for (int layer = 0; layer < layers; layer++)
                {
                    Vector2 target = fine ? new Vector2(Mathf.Lerp(min.x, max.x, (layer % 2) == 0 ? .2f : .8f), Mathf.Lerp(min.y, max.y, layer < 2 ? .2f : .8f)) : (min + max) * .5f;
                    Sample nearest = candidates[0]; float distance = float.MaxValue;
                    foreach (var s in candidates) { float d = (s.uv - target).sqrMagnitude; if (d < distance) { distance = d; nearest = s; } }
                    Vector3 p = nearest.position, n = nearest.normal;
                    positions[layer][id] = new Color(p.x, p.y, p.z, nearest.inner);
                    normals[layer][id] = new Color(n.x, n.y, n.z, nearest.detail);
                }
            }
            if (fine)
            {
                var p = new Texture2DArray(256, height, 4, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var n = new Texture2DArray(256, height, 4, TextureFormat.RGBAHalf, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (int layer = 0; layer < 4; layer++) { p.SetPixels(positions[layer], layer); n.SetPixels(normals[layer], layer); }
                p.Apply(false, true); n.Apply(false, true);
                AssetDatabase.CreateAsset(p, Data + "Cell96_Position.asset"); AssetDatabase.CreateAsset(n, Data + "Cell96_Normal.asset");
            }
            else
            {
                var p = new Texture2D(256, height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var n = new Texture2D(256, height, TextureFormat.RGBAHalf, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                p.SetPixels(positions[0]); n.SetPixels(normals[0]); p.Apply(false, true); n.Apply(false, true);
                AssetDatabase.CreateAsset(p, Data + "Cell48_Position.asset"); AssetDatabase.CreateAsset(n, Data + "Cell48_Normal.asset");
            }
            return 0;
        }

        private static void CreateMaterials(JObject bindings)
        {
            var shader = Shader.Find("TimeTheft/H2 C2 Pixel Metal");
            foreach (var part in bindings["default_parts"])
            {
                if (part["sp_owner"].Type == JTokenType.Null || (string)part["profile"] == "H2_Hands_DarkMetal") continue;
                int owner = (int)part["sp_owner"];
                foreach (bool red in owner == 9 ? new[] { false, true } : new[] { false })
                {
                    var mat = new Material(shader) { name = "H2_C2_" + owner.ToString("00") + (red ? "_RedInner" : "") };
                    mat.SetFloat("_OwnerId", owner); mat.SetFloat("_PartTrim", (string)part["profile"] == "H2_GoldTrim_PBRRef" ? 1 : 0); mat.SetFloat("_RedInner", red ? 1 : 0);
                    string[] fields = { "_CellIds48", "_CellIds96", "_Position48", "_Normal48", "_Position96", "_Normal96", "_DetailMap", "_InnerMask", "_PigmentMap" };
                    string[] paths = { Data + "H2_UVCellIDs48_512.png", Data + "H2_UVCellIDs96_512.png", Data + "Cell48_Position.asset", Data + "Cell48_Normal.asset", Data + "Cell96_Position.asset", Data + "Cell96_Normal.asset", Data + "H2_DetailIndex_512.png", Root + "Textures/Red_C_LidInner_Mask_512.png", Root + "Textures/Red_C_LidInner_Pigment_512.png" };
                    for (int i = 0; i < fields.Length; i++) mat.SetTexture(fields[i], AssetDatabase.LoadAssetAtPath<Texture>(paths[i]));
                    AssetDatabase.CreateAsset(mat, Root + "C2/Materials/" + mat.name + ".mat");
                }
            }
        }
    }
}
