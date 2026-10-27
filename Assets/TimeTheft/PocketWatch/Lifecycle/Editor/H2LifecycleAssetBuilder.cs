using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeTheft.Editor
{
    public static class H2LifecycleAssetBuilder
    {
        private const string Root = "Assets/TimeTheft/PocketWatch/Lifecycle/";
        private static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Clips/" + name + ".anim");
        public static void BuildClips()
        {
            Copy("Move_To_Hatch", "Move_To_Hatch_MotionRoot", "H2_Watch/MotionRoot");
            Copy("Watch_Drop", "Watch_Drop_MotionRoot", "H2_Watch/MotionRoot");
            Copy("Last_Watch_Discard", "Last_Watch_Discard_Move_MotionRoot", "H2_Watch/MotionRoot", 16f / 24);
            foreach (string group in new[] { "Watch_Prepare", "Next_Batch_Prepare" })
                for (int i = 0; i < 5; i++) Copy(group + "_Clock0" + i, group + "_Clock0" + i + "_MotionRoot", "H2_Watch/MotionRoot");
            Copy("SupplyCarrier_Prepare", "SupplyCarrier_Prepare", "SupplyCarrier");
            Copy("SupplyCarrier_NextBatch", "SupplyCarrier_NextBatch", "SupplyCarrier");
            Copy("Hatch_Open", "Hatch_Open_Pivot", "T3_TableHatch_Root/T3_Hatch_Mechanism/Hatch_FlapPivot");
            Copy("Hatch_Close", "Hatch_Close_Pivot", "T3_TableHatch_Root/T3_Hatch_Mechanism/Hatch_FlapPivot");
            Copy("Latch", "Latch_Pivot", "T3_TableHatch_Root/T3_Hatch_Mechanism/Hatch_LatchPivot");
        }
        private static void Copy(string file, string name, string bindingPath, float stop = float.PositiveInfinity)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(Root + "Source/" + file + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            string path = Root + "Clips/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.frameRate = 24; clip.wrapMode = WrapMode.ClampForever;
            float end = Mathf.Min(source.length, stop);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.path != bindingPath || (!binding.propertyName.StartsWith("m_LocalPosition.") && !binding.propertyName.StartsWith("m_LocalRotation."))) continue;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                var keys = curve.keys.Where(k => k.time <= end + .000001f).ToList();
                if (!keys.Any(k => Mathf.Abs(k.time - end) < .000001f)) keys.Add(new Keyframe(end, curve.Evaluate(end)));
                AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys.ToArray()));
            }
            AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip);
        }
        private static Material Metal(string name, Color linear, float metallic, float roughness)
        {
            string path = Root + "Materials/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", linear.gamma); material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", 1 - roughness);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); return material;
        }
        public static void ConfigureScene()
        {
            if (Application.dataPath != "C:/unity/TimeTheft/Assets" || EditorApplication.isPlaying) throw new InvalidOperationException("TimeTheft Edit Mode required");
            var felt = AssetDatabase.LoadAssetAtPath<Material>("Assets/TimeTheft/Table/Materials/TimeTheft_Table_T3.mat");
            var steel = Metal("Hatch_BlackenedSteel", new Color(.038f, .047f, .040f, 1), .72f, .43f);
            var edge = Metal("Hatch_WornEdge", new Color(.085f, .087f, .064f, 1), .8f, .39f);
            var receiver = Metal("Hatch_DarkReceiver", new Color(.013f, .018f, .016f, 1), .45f, .65f);
            var root = GameObject.Find("T3_H2_Hatch"); var carrier = GameObject.Find("H2_SupplyCarrier");
            if (!root || !carrier) throw new InvalidOperationException("Fresh source instances required");
            root.transform.rotation = Quaternion.Euler(0, 180, 0); carrier.transform.rotation = Quaternion.Euler(0, 180, 0);
            foreach (var item in new[] { root, carrier }) foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m && (m.name.Contains("Felt") || m.name.Contains("T3_")) ? felt : m && m.name.Contains("Worn") ? edge : m && m.name.Contains("Receiver") ? receiver : steel).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name.StartsWith("T3_") && filter.name != "T3_Tabletop_WithHatchHole") filter.GetComponent<Renderer>().enabled = false;
                bool moving = filter.GetComponentsInParent<Transform>().Any(t => t.name == "Hatch_FlapPivot" || t.name == "Hatch_LatchPivot");
                if (moving)
                { var box = filter.gameObject.AddComponent<BoxCollider>(); box.center = filter.sharedMesh.bounds.center; box.size = filter.sharedMesh.bounds.size; }
                else { var mesh = filter.gameObject.AddComponent<MeshCollider>(); mesh.sharedMesh = filter.sharedMesh; mesh.convex = false; }
            }
            var hatch = root.AddComponent<H2TableHatchView>(); var hs = new SerializedObject(hatch);
            hs.FindProperty("anchor").objectReferenceValue = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "T3_Hatch_Mechanism");
            hs.FindProperty("flapPivot").objectReferenceValue = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Hatch_FlapPivot");
            hs.FindProperty("latchPivot").objectReferenceValue = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Hatch_LatchPivot");
            hs.FindProperty("receiverFloor").objectReferenceValue = root.GetComponentsInChildren<Collider>(true).Single(c => c.name == "Hatch_Receiver_ImpactFloor_Static");
            hs.FindProperty("openClip").objectReferenceValue = Clip("Hatch_Open_Pivot"); hs.FindProperty("closeClip").objectReferenceValue = Clip("Hatch_Close_Pivot"); hs.FindProperty("latchClip").objectReferenceValue = Clip("Latch_Pivot");
            hs.ApplyModifiedPropertiesWithoutUndo(); hatch.ResetClosed();
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, Root + "Prefabs/T3_H2_HatchVariant.prefab", InteractionMode.AutomatedAction);
            carrier.SetActive(false);
            PrefabUtility.SaveAsPrefabAssetAndConnect(carrier, Root + "Prefabs/SupplyCarrier_H2.prefab", InteractionMode.AutomatedAction);
            foreach (string n in new[] { "Table/Surface", "Table/Frame" })
                foreach (var proxy in GameObject.Find(n).GetComponents<Collider>()) proxy.enabled = false;
            var oldTop = GameObject.Find("TimeTheft_Table_T3").GetComponentsInChildren<Renderer>().Single(r => r.name == "Tabletop"); oldTop.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldTop);
            foreach (var c in oldTop.GetComponents<Collider>()) { c.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
            var controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>(); var life = controller.gameObject.AddComponent<H2ClockLifecyclePresentation>();
            var so = new SerializedObject(life); so.FindProperty("hatch").objectReferenceValue = hatch; so.FindProperty("carrier").objectReferenceValue = carrier;
            so.FindProperty("moveClip").objectReferenceValue = Clip("Move_To_Hatch_MotionRoot"); so.FindProperty("discardMoveClip").objectReferenceValue = Clip("Last_Watch_Discard_Move_MotionRoot"); so.FindProperty("dropClip").objectReferenceValue = Clip("Watch_Drop_MotionRoot");
            so.FindProperty("carrierPrepare").objectReferenceValue = Clip("SupplyCarrier_Prepare"); so.FindProperty("carrierNext").objectReferenceValue = Clip("SupplyCarrier_NextBatch");
            for (int i = 0; i < 5; i++) { so.FindProperty("prepareClips").GetArrayElementAtIndex(i).objectReferenceValue = Clip("Watch_Prepare_Clock0" + i + "_MotionRoot"); so.FindProperty("nextClips").GetArrayElementAtIndex(i).objectReferenceValue = Clip("Next_Batch_Prepare_Clock0" + i + "_MotionRoot"); }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene); EditorSceneManager.SaveScene(controller.gameObject.scene);
        }
    }
}
