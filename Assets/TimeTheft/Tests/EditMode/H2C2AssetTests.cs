using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class H2C2AssetTests
    {
        private const string Root = "Assets/TimeTheft/PocketWatch/";

        [Test] public void ShaderCompilesAndUsesSharedMeshInputsWithSeparateOriginalGrids()
        {
            var shader = Shader.Find("TimeTheft/H2 C2 Pixel Metal");
            Assert.That(shader && shader.isSupported, Is.True);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            foreach (var grid in new[] { 48, 96 })
            {
                string path = Root + "C2/Data/H2_UVCellIDs" + grid + "_512.png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.sRGBTexture || importer.mipmapEnabled, Is.False);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
                var pixels = AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetPixels32().Where(p => p.a == 255).ToArray();
                Assert.That(pixels.Length, Is.EqualTo(117576));
                Assert.That(pixels.Select(p => p.r + p.g * 256).Distinct().Count(), Is.EqualTo(grid == 48 ? 9225 : 35145));
                Assert.That(pixels.Any(p => p.b == 9 || p.b == 13), Is.False, "Hands must stay outside C2 pooling.");
            }
            var fine = AssetDatabase.LoadAssetAtPath<Texture2DArray>(Root + "C2/Data/Cell96_Position.asset");
            Assert.That(fine.depth, Is.EqualTo(4));
            Assert.That(fine.format, Is.EqualTo(TextureFormat.RGBAFloat));
            Assert.That(fine.width * fine.height, Is.GreaterThanOrEqualTo(35145));
        }

        [Test] public void C2VariantSharesFourteenMeshesAndKeepsAnExplicitLitFallback()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/H2_PocketWatch.prefab");
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "C2/Prefabs/H2_C2_PocketWatch.prefab");
            Assert.That(variant.GetComponentsInChildren<MeshFilter>().Select(m => m.sharedMesh), Is.EqualTo(source.GetComponentsInChildren<MeshFilter>().Select(m => m.sharedMesh)));
            var instance = Object.Instantiate(variant);
            try
            {
                var binding = instance.GetComponent<H2C2MaterialBinding>();
                Assert.That(binding.MetalRenderers.Length, Is.EqualTo(11));
                binding.SetC2(true);
                Assert.That(binding.MetalRenderers.All(r => r.sharedMaterial.shader.name == "TimeTheft/H2 C2 Pixel Metal"), Is.True);
                binding.SetC2(false);
                Assert.That(binding.MetalRenderers.All(r => r.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"), Is.True);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test] public void RedC2MaterialChangesOnlyLidAndRetainsCommonExternalLightingInputs()
        {
            var normal = AssetDatabase.LoadAssetAtPath<Material>(Root + "C2/Materials/H2_C2_09.mat");
            var red = AssetDatabase.LoadAssetAtPath<Material>(Root + "C2/Materials/H2_C2_09_RedInner.mat");
            Assert.That(normal.GetFloat("_OwnerId"), Is.EqualTo(9));
            Assert.That(red.GetFloat("_RedInner"), Is.EqualTo(1));
            foreach (string property in new[] { "_CellIds48", "_CellIds96", "_Position48", "_Normal48", "_Position96", "_Normal96", "_DetailMap" })
                Assert.That(red.GetTexture(property), Is.SameAs(normal.GetTexture(property)));
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "C2/Prefabs/H2_C2_PocketWatch.prefab"));
            try
            {
                var view = instance.GetComponent<H2PocketWatchView>(); var binding = instance.GetComponent<H2C2MaterialBinding>();
                view.SetRed(true); binding.SetC2(true);
                Assert.That(view.LidPivot.Find("Lid").GetComponent<Renderer>().sharedMaterial, Is.SameAs(red));
                Assert.That(binding.MetalRenderers.Count(r => r.sharedMaterial == red), Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
