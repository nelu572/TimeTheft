using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TimeTheft.Editor
{
    public static class H2ShuffleClipBuilder
    {
        public static void Build()
        {
            const string root="Assets/TimeTheft/PocketWatch/Shuffle/";
            for(int side=0;side<2;side++)
            {
                var source=AssetDatabase.LoadAllAssetsAtPath(root+"Source/Shuffle_Clock0"+side+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
                string path=root+"Clips/"+(side==0?"Left":"Right")+"_AdjacentSwap.anim";
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if(!clip){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}clip.ClearCurves();clip.frameRate=24;clip.wrapMode=WrapMode.ClampForever;
                foreach(var b in AnimationUtility.GetCurveBindings(source))
                {
                    if(b.path!="H2_Watch/MotionRoot"||(!b.propertyName.StartsWith("m_LocalPosition.")&&!b.propertyName.StartsWith("m_LocalRotation.")))continue;
                    var curve=AnimationUtility.GetEditorCurve(source,b);float end=9f/24;
                    var keys=curve.keys.Where(k=>k.time<=end+.000001f).ToList();
                    if(!keys.Any(k=>Mathf.Abs(k.time-end)<.000001f))keys.Add(new Keyframe(end,curve.Evaluate(end)));
                    AnimationUtility.SetEditorCurve(clip,b,new AnimationCurve(keys.ToArray()));
                }
                AnimationUtility.SetAnimationEvents(clip,new AnimationEvent[0]);var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;
                AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);
            }
        }
    }
}
