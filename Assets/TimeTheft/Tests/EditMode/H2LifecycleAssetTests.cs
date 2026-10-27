using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class H2LifecycleAssetTests
    {
        private const string Root = "Assets/TimeTheft/PocketWatch/Lifecycle/";

        [Test] public void FilteredLifecycleClipsKeepOriginalCurvesWithoutHandsScaleOrImplicitEvents()
        {
            var sources = new[] { "Move_To_Hatch", "Watch_Drop", "Last_Watch_Discard", "SupplyCarrier_Prepare", "SupplyCarrier_NextBatch", "Hatch_Open", "Hatch_Close", "Latch" }
                .Concat(Enumerable.Range(0, 5).Select(i => "Watch_Prepare_Clock0" + i))
                .Concat(Enumerable.Range(0, 5).Select(i => "Next_Batch_Prepare_Clock0" + i)).ToArray();
            foreach (var name in sources)
            {
                bool flap = name.StartsWith("Hatch_"); bool latch = name == "Latch"; bool carrier = name.StartsWith("SupplyCarrier_");
                string filtered = name == "Last_Watch_Discard" ? name + "_Move_MotionRoot" : flap || latch ? name + "_Pivot" : carrier ? name : name + "_MotionRoot";
                var source = AssetDatabase.LoadAllAssetsAtPath(Root + "Source/" + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Clips/" + filtered + ".anim");
                Assert.That(clip, Is.Not.Null, filtered);
                float length = name == "Last_Watch_Discard" ? 16f / 24 : source.length;
                Assert.That(clip.length, Is.EqualTo(length).Within(.000001f), name);
                Assert.That(clip.frameRate, Is.EqualTo(24));
                string path = flap ? "T3_TableHatch_Root/T3_Hatch_Mechanism/Hatch_FlapPivot" : latch ? "T3_TableHatch_Root/T3_Hatch_Mechanism/Hatch_LatchPivot" : carrier ? "SupplyCarrier" : "H2_Watch/MotionRoot";
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.That(bindings, Is.Not.Empty, name);
                Assert.That(bindings.All(b => b.path == path && (b.propertyName.StartsWith("m_LocalPosition.") || b.propertyName.StartsWith("m_LocalRotation."))), Is.True, name);
                foreach (var binding in bindings)
                    for (int f = 0; f <= Mathf.RoundToInt(length * 96); f++)
                        Assert.That(AnimationUtility.GetEditorCurve(clip, binding).Evaluate(f / 96f),
                            Is.EqualTo(AnimationUtility.GetEditorCurve(source, binding).Evaluate(f / 96f)).Within(.00005f), name + " / " + binding.propertyName);
                Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, "규칙/활성 상태는 Controller와 표현부가 소유한다");
            }
        }

        [Test] public void HatchVariantKeepsActualHoleAndExistingFeltMaterialWithIndependentMovingParts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/T3_H2_HatchVariant.prefab");
            Assert.That(prefab, Is.Not.Null);
            var hatch = prefab.GetComponent<H2TableHatchView>();
            Assert.That(hatch, Is.Not.Null);
            Assert.That(hatch.Anchor, Is.Not.Null); Assert.That(hatch.FlapPivot, Is.Not.Null); Assert.That(hatch.LatchPivot, Is.Not.Null);
            Assert.That(hatch.FlapPivot, Is.Not.SameAs(hatch.LatchPivot));
            var top = prefab.GetComponentsInChildren<MeshCollider>(true).Single(m => m.name == "T3_Tabletop_WithHatchHole");
            Assert.That(top.convex, Is.False, "convex hull은 관통구를 메운다");
            Assert.That(top.sharedMesh, Is.Not.Null);
            Assert.That(top.GetComponent<Renderer>().sharedMaterials, Does.Contain(AssetDatabase.LoadAssetAtPath<Material>("Assets/TimeTheft/Table/Materials/TimeTheft_Table_T3.mat")));
            var flap = hatch.FlapPivot.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == "Hatch_FeltFlap");
            Assert.That(flap.size.sqrMagnitude, Is.GreaterThan(1));
            Assert.That(flap.GetComponent<Renderer>().sharedMaterials, Does.Contain(top.GetComponent<Renderer>().sharedMaterial));
            Assert.That(prefab.GetComponentsInChildren<MeshFilter>(true).Length, Is.EqualTo(31));
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Count(r => r.name.StartsWith("T3_") && r.name != top.name && !r.enabled), Is.EqualTo(8), "기존 T3 여덟 부위와 중복 표시하지 않는다");
            var supply = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/SupplyCarrier_H2.prefab");
            Assert.That(supply.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(1));
            Assert.That(supply.GetComponentInChildren<Renderer>(true).sharedMaterials, Does.Contain(top.GetComponent<Renderer>().sharedMaterial));
        }
    }
}
