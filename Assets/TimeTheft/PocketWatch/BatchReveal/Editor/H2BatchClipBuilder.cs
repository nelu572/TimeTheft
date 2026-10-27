using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TimeTheft.Editor
{
    public static class H2BatchClipBuilder
    {
        public const string Root = "Assets/TimeTheft/PocketWatch/BatchReveal/";
        public static void Build()
        {
            foreach (string group in new[] { "Initial_Reveal", "Close_All" })
            for (int i = 0; i < 5; i++)
            {
                string name = group + "_Clock" + i.ToString("00");
                var source = AssetDatabase.LoadAllAssetsAtPath(Root + "Source/" + name + ".fbx")
                    .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                string path = Root + "Clips/" + name + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
                clip.ClearCurves(); clip.frameRate = source.frameRate; clip.wrapMode = WrapMode.ClampForever;
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    bool root = binding.path == "H2_Watch/MotionRoot" &&
                        (binding.propertyName.StartsWith("m_LocalPosition.") || binding.propertyName.StartsWith("m_LocalRotation."));
                    bool lid = binding.path == "H2_Watch/MotionRoot/VisualScale/HingeAssembly/LidPivot" && binding.propertyName.StartsWith("m_LocalRotation.");
                    if (root || lid) AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, binding));
                }
                AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
            }
        }
    }
}
