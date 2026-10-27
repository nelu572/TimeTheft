using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class H2TurnAssetTests
    {
        [Test] public void RevealClipKeepsSourceCurvesAndExcludesHandsAndScale()
        {
            var source = AssetDatabase.LoadAllAssetsAtPath("Assets/TimeTheft/PocketWatch/Turn/Source/Reveal_Enter.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/TimeTheft/PocketWatch/Turn/Clips/Reveal_Enter_MotionRoot_Lid.anim");
            Assert.That(clip.length, Is.EqualTo(20f / 24).Within(.00001));
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Count(b => b.path.EndsWith("LidPivot")), Is.EqualTo(4));
            Assert.That(bindings.All(b => !b.propertyName.Contains("Scale") &&
                (b.path == "H2_Watch/MotionRoot" || b.path == "H2_Watch/MotionRoot/VisualScale/HingeAssembly/LidPivot")), Is.True);
            foreach (var b in bindings)
                for (int f = 0; f <= 20; f++) Assert.That(AnimationUtility.GetEditorCurve(clip, b).Evaluate(f / 24f), Is.EqualTo(AnimationUtility.GetEditorCurve(source, b).Evaluate(f / 24f)).Within(.000001));
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
        }
        [Test] public void MainUsesVerifiedSelectAndKeepsOriginalWatchAnimatorDisabled()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
            try
            {
                var view = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TimeTheftGameView>(true)).Single();
                var turn = view.TurnPresentation;
                Assert.That(turn.SelectClip, Is.SameAs(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/TimeTheft/PocketWatch/Animations/Select_MotionRoot.anim")));
                for (int i = 0; i < 5; i++)
                {
                    Assert.That(turn.CanPlay(view, i), Is.True);
                    Assert.That(view.Clocks[i].H2Presentation.Watch.GetComponent<Animator>().enabled, Is.False);
                }
                Assert.That(view.BatchReveal.CanPlay(view), Is.True);
                Assert.That(view.FocusCamera.GetComponent<Camera>().fieldOfView, Is.EqualTo(58));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
