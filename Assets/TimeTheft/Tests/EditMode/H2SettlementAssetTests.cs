using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class H2SettlementAssetTests
    {
        [TestCase("Settlement_Transition", 20)]
        [TestCase("Close", 10)]
        public void FilteredClipRetainsOnlyOriginalBodyAndLidCurves(string name, int frames)
        {
            const string root = "Assets/TimeTheft/PocketWatch/Turn/";
            var source = AssetDatabase.LoadAllAssetsAtPath(root + "Source/" + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(root + "Clips/" + name + "_MotionRoot_Lid.anim");
            Assert.That(clip.length, Is.EqualTo(frames / 24f).Within(.00001));
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Any(b => b.path.EndsWith("LidPivot")), Is.True);
            Assert.That(bindings.All(b => !b.propertyName.Contains("Scale") && (b.path == "H2_Watch/MotionRoot" || b.path == "H2_Watch/MotionRoot/VisualScale/HingeAssembly/LidPivot")), Is.True);
            foreach (var b in bindings)
                for (int f = 0; f <= frames; f++) Assert.That(AnimationUtility.GetEditorCurve(clip,b).Evaluate(f / 24f), Is.EqualTo(AnimationUtility.GetEditorCurve(source,b).Evaluate(f / 24f)).Within(.000001));
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
        }
    }
}
