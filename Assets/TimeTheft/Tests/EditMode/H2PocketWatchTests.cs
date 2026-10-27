using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeTheft.Tests
{
    public sealed class H2PocketWatchTests
    {
        private const string Root = "Assets/TimeTheft/PocketWatch/";
        private GameObject instance;
        private H2PocketWatchView view;

        [Serializable] private class Manifest { public Node[] hierarchy; }
        [Serializable] private class Node
        {
            public string path;
            public float[] loc_B, rot_B_wxyz, scale;
        }

        [SetUp] public void CreateWatch()
        {
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/H2_PocketWatch.prefab"));
            view = instance.GetComponent<H2PocketWatchView>();
        }

        [TearDown] public void Cleanup() => Object.DestroyImmediate(instance);

        [Test] public void ImportedHierarchyMatchesDeliveredManifestAndUnits()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "Models/H2_Watch_Hierarchy.json"));
            Assert.That(manifest.hierarchy.Length, Is.EqualTo(21));
            foreach (var node in manifest.hierarchy)
            {
                var t = instance.transform.Find(node.path);
                Assert.That(t, Is.Not.Null, node.path);
                if (node.path == "H2_Watch") continue; // FBX export root holds the Y-up conversion.
                Assert.That(Vector3.Distance(t.localPosition, new Vector3(-node.loc_B[0], node.loc_B[1], node.loc_B[2])), Is.LessThan(1e-5f), node.path);
                var q = node.rot_B_wxyz;
                Assert.That(Quaternion.Angle(t.localRotation, new Quaternion(q[1], -q[2], -q[3], q[0])), Is.LessThan(.05f), node.path);
            }
            var visual = instance.transform.Find("H2_Watch/MotionRoot/VisualScale");
            Assert.That(visual.localScale.x, Is.EqualTo(.7f).Within(1e-6f));
            Assert.That(visual.Find("Case").GetComponent<Renderer>().bounds.size.x, Is.EqualTo(.672f).Within(1e-5f));
            visual.localScale = Vector3.one;
            Assert.That(visual.Find("Case").GetComponent<Renderer>().bounds.size.x, Is.EqualTo(.96f).Within(1e-5f));
            var importer = (ModelImporter)AssetImporter.GetAtPath(Root + "Models/H2_Watch.fbx");
            Assert.That(importer.globalScale * importer.fileScale, Is.EqualTo(1f));
            Assert.That(importer.preserveHierarchy, Is.True);
        }

        [Test] public void FourteenMeshesKeepUvsNormalsAndTemporaryLitBindings()
        {
            var filters = instance.GetComponentsInChildren<MeshFilter>();
            Assert.That(filters.Length, Is.EqualTo(14));
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertexCount), filter.name);
                Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount), filter.name);
                Assert.That(mesh.uv.All(uv => uv.x >= -1e-5f && uv.x <= 1.00001f && uv.y >= -1e-5f && uv.y <= 1.00001f), Is.True, filter.name);
                Assert.That(filter.GetComponent<Renderer>().sharedMaterials.All(m => m && m.shader.name == "Universal Render Pipeline/Lit"), Is.True);
            }
        }

        [TestCase(10, 10, 305f, 60f)]
        [TestCase(2, 40, 80f, 240f)]
        [TestCase(6, 0, 180f, 0f)]
        public void HandsRotateIndependentlyAroundTheirUnmovedCenters(int h, int m, float hour, float minute)
        {
            Vector3 hp = view.HourHand.localPosition, mp = view.MinuteHand.localPosition;
            Quaternion lid = view.LidPivot.localRotation;
            view.SetTime(h, m);
            Assert.That(Quaternion.Angle(view.HourHand.localRotation, Quaternion.Euler(0, 0, hour)), Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(view.MinuteHand.localRotation, Quaternion.Euler(0, 0, minute)), Is.LessThan(.05f));
            Assert.That(view.HourHand.localPosition, Is.EqualTo(hp));
            Assert.That(view.MinuteHand.localPosition, Is.EqualTo(mp));
            Assert.That(hp.x, Is.EqualTo(0).Within(1e-6f));
            Assert.That(hp.y, Is.EqualTo(0).Within(1e-6f));
            Assert.That(mp.x, Is.EqualTo(0).Within(1e-6f));
            Assert.That(mp.y, Is.EqualTo(0).Within(1e-6f));
            Assert.That(mp.z - hp.z, Is.EqualTo(.0036f).Within(1e-6f));
            Assert.That(view.LidPivot.localRotation, Is.EqualTo(lid));
        }

        [TestCase(0)] [TestCase(60)] [TestCase(115)]
        public void LidKeepsHingeFixedAndLeavesHandsUntouched(float angle)
        {
            view.SetTime(2, 40);
            var pivot = view.LidPivot.position;
            var hour = view.HourHand.localToWorldMatrix;
            var minute = view.MinuteHand.localToWorldMatrix;
            view.SetLidAngle(angle);
            Assert.That(view.LidPivot.position, Is.EqualTo(pivot));
            Assert.That(Quaternion.Angle(view.LidPivot.localRotation, Quaternion.Euler(0, angle, 0)), Is.LessThan(.05f));
            Assert.That(view.HourHand.localToWorldMatrix, Is.EqualTo(hour));
            Assert.That(view.MinuteHand.localToWorldMatrix, Is.EqualTo(minute));
        }

        [Test] public void RedChangesOnlyDialAndInnerLidMaterialsOnTheSameMeshes()
        {
            view.SetRed(false);
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var before = renderers.Select(r => r.sharedMaterial).ToArray();
            var meshes = instance.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
            view.SetRed(true);
            for (int i = 0; i < renderers.Length; i++)
                Assert.That(renderers[i].sharedMaterial != before[i], Is.EqualTo(renderers[i].name == "Dial" || renderers[i].name == "Lid"));
            Assert.That(instance.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh), Is.EqualTo(meshes));
        }

        [Test] public void ColorAndPackedDataTexturesKeepTheirRequiredImportSettings()
        {
            foreach (string path in Directory.GetFiles(Root + "Textures", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool data = path.Contains("MetallicSmoothness") || path.Contains("Mask");
                Assert.That(importer.sRGBTexture, Is.EqualTo(!data), path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
                Assert.That(importer.mipmapEnabled, Is.False, path);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
                Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None), path);
            }
        }

        [Test] public void SelectRetainsSourceMotionAndDoesNotBindLidHandsOrVisualScale()
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(Root + "Animations/Select.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/Select_MotionRoot.anim");
            Assert.That(source.length, Is.EqualTo(.875f).Within(1e-6f));
            Assert.That(source.frameRate, Is.EqualTo(24f));
            Assert.That(AnimationUtility.GetCurveBindings(source).Length, Is.EqualTo(200));
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Length, Is.EqualTo(10));
            Assert.That(bindings.All(b => b.path == "H2_Watch/MotionRoot"), Is.True);
            view.SetTime(2, 40); view.SetLidAngle(60);
            var hour = view.HourHand.localRotation; var minute = view.MinuteHand.localRotation; var lid = view.LidPivot.localRotation;
            for (int frame = 0; frame <= 21; frame++)
            {
                float time = frame / 24f;
                foreach (var binding in bindings)
                    Assert.That(AnimationUtility.GetEditorCurve(clip, binding).Evaluate(time), Is.EqualTo(AnimationUtility.GetEditorCurve(source, binding).Evaluate(time)).Within(1e-7f));
                clip.SampleAnimation(instance, time);
                Assert.That(view.HourHand.localRotation, Is.EqualTo(hour));
                Assert.That(view.MinuteHand.localRotation, Is.EqualTo(minute));
                Assert.That(view.LidPivot.localRotation, Is.EqualTo(lid));
                Assert.That(instance.transform.Find("H2_Watch/MotionRoot/VisualScale").localScale.x, Is.EqualTo(.7f));
            }
            Assert.That(instance.transform.Find("H2_Watch/MotionRoot").localPosition.magnitude, Is.GreaterThan(1f));
        }
    }
}
