using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class H2ShuffleAssetTests
    {
        [TestCase(0,"Left")][TestCase(1,"Right")]
        public void AdjacentPrimitiveKeepsSourceTimingAndExcludesPrivateFixedChannels(int side,string name)
        {
            const string root="Assets/TimeTheft/PocketWatch/Shuffle/";
            var source=AssetDatabase.LoadAllAssetsAtPath(root+"Source/Shuffle_Clock0"+side+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(root+"Clips/"+name+"_AdjacentSwap.anim");
            Assert.That(clip.length,Is.EqualTo(9f/24).Within(.000001));
            var bindings=AnimationUtility.GetCurveBindings(clip);Assert.That(bindings,Is.Not.Empty);
            Assert.That(bindings.All(b=>b.path=="H2_Watch/MotionRoot"&&(b.propertyName.StartsWith("m_LocalPosition.")||b.propertyName.StartsWith("m_LocalRotation."))),Is.True);
            foreach(var b in bindings)for(int f=0;f<=36;f++)
                Assert.That(AnimationUtility.GetEditorCurve(clip,b).Evaluate(f/96f),Is.EqualTo(AnimationUtility.GetEditorCurve(source,b).Evaluate(f/96f)).Within(.00005f));
            Assert.That(AnimationUtility.GetAnimationEvents(clip),Is.Empty);
        }
    }
}
