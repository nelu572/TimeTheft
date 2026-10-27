using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TimeTheft.Editor
{
    public static class H2SettlementClipBuilder
    {
        public static void Build()
        {
            foreach (string name in new[] { "Settlement_Transition", "Close" })
            {
                const string root = "Assets/TimeTheft/PocketWatch/Turn/";
                var source = AssetDatabase.LoadAllAssetsAtPath(root + "Source/" + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                var path = root + "Clips/" + name + "_MotionRoot_Lid.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
                clip.ClearCurves(); clip.frameRate = source.frameRate; clip.wrapMode = WrapMode.ClampForever;
                foreach (var b in AnimationUtility.GetCurveBindings(source))
                {
                    bool body = b.path == "H2_Watch/MotionRoot" && (b.propertyName.StartsWith("m_LocalPosition.") || b.propertyName.StartsWith("m_LocalRotation."));
                    bool lid = b.path == "H2_Watch/MotionRoot/VisualScale/HingeAssembly/LidPivot" && b.propertyName.StartsWith("m_LocalRotation.");
                    if (body || lid) AnimationUtility.SetEditorCurve(clip, b, AnimationUtility.GetEditorCurve(source, b));
                }
                AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip);
            }
        }
    }
}
