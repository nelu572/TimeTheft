using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class H2BatchRevealAssetTests
    {
        private const string Root = "Assets/TimeTheft/PocketWatch/BatchReveal/";
        [TestCase("Initial_Reveal")] [TestCase("Close_All")]
        public void RuntimeClipsRetainFbxCurvesWithoutHandsScaleOrRuleEvents(string group)
        {
            for (int i = 0; i < 5; i++)
            {
                string name = group + "_Clock" + i.ToString("00");
                var source = AssetDatabase.LoadAllAssetsAtPath(Root + "Source/" + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Clips/" + name + ".anim");
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.That(bindings.Length, Is.GreaterThanOrEqualTo(4));
                Assert.That(clip.frameRate, Is.EqualTo(24)); Assert.That(clip.length, Is.EqualTo(source.length).Within(.00001));
                Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
                Assert.That(bindings.All(b => (b.path == "H2_Watch/MotionRoot" || b.path == "H2_Watch/MotionRoot/VisualScale/HingeAssembly/LidPivot")
                    && !b.propertyName.Contains("Scale")), Is.True);
                for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * 24); frame++)
                foreach (var binding in bindings)
                    Assert.That(AnimationUtility.GetEditorCurve(clip, binding).Evaluate(frame / 24f),
                        Is.EqualTo(AnimationUtility.GetEditorCurve(source, binding).Evaluate(frame / 24f)).Within(.000001f));
            }
        }

        [Test]
        public void MainSceneHasFiveBoundClipsAndPreservesBaseCameraAndModelSize()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
            try
            {
                var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TimeTheftGameView>(true)).Single();
                Assert.That(view.BatchReveal, Is.Not.Null); Assert.That(view.BatchReveal.CanPlay(view), Is.True);
                Assert.That(view.FocusCamera.GetComponent<Camera>().fieldOfView, Is.EqualTo(58));
                Assert.That(view.FocusCamera.transform.position, Is.EqualTo(new Vector3(0, 4.25f, -4.8f)));
                for (int i = 0; i < 5; i++)
                {
                    Assert.That(view.BatchReveal.RevealClips[i].name, Does.Contain("Clock" + i.ToString("00")));
                    Assert.That(view.Clocks[i].H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(view.Clocks[i].H2Presentation.MotionRoot.Find("VisualScale").localScale, Is.EqualTo(Vector3.one * .7f));
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
