#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TimeTheft.Visuals.Editor
{
    public static class VisualStyleSetup
    {
        public const string Root = "Assets/TimeTheft/Visuals";
        public const string ScenePath = Root + "/TimeTheftVisualReview.unity";

        [MenuItem("TimeTheft/Visual Style/Create Review Scene")]
        public static void CreateReview()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene: stop without replacing it.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) throw new InvalidOperationException("Review scene already exists.");

            var pipeline = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"));
            pipeline.name = "TimeTheft LoFi Pipeline";
            AssetDatabase.CreateAsset(pipeline, Root + "/LoFiPipeline.asset");
            var renderer = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset"));
            renderer.name = "TimeTheft LoFi Renderer";
            AssetDatabase.CreateAsset(renderer, Root + "/LoFiRenderer.asset");
            var material = new Material(Shader.Find("Hidden/TimeTheft/LoFi"));
            AssetDatabase.CreateAsset(material, Root + "/LoFi.mat");
            var feature = ScriptableObject.CreateInstance<LoFiRendererFeature>();
            feature.name = "LoFi 480p Dither";
            feature.material = material;
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
            // Native-resolution transparent text follows the opaque-world lo-fi pass.
            var readable = ScriptableObject.CreateInstance<RenderObjects>();
            readable.name = "Readable World Text";
            readable.settings.passTag = "TimeTheft native transparents";
            readable.settings.Event = RenderPassEvent.AfterRenderingPostProcessing;
            readable.settings.filterSettings.RenderQueueType = RenderQueueType.Transparent;
            readable.settings.filterSettings.LayerMask = -1;
            readable.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
            readable.settings.overrideDepthState = true;
            readable.settings.enableWrite = false;
            readable.settings.depthCompareFunction = CompareFunction.LessEqual;
            readable.Create();
            AssetDatabase.AddObjectToAsset(readable, renderer); renderer.rendererFeatures.Add(readable);
            var rendererSerialized = new SerializedObject(renderer);
            rendererSerialized.FindProperty("m_TransparentLayerMask").intValue = 0;
            rendererSerialized.ApplyModifiedPropertiesWithoutUndo();
            renderer.SetDirty();
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, Root + "/LoFiVolume.asset");
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0); color.contrast.Override(18); color.saturation.Override(-28);
            color.colorFilter.Override(new Color(.96f, .98f, .9f));
            var white = profile.Add<WhiteBalance>(true); white.temperature.Override(-5); white.tint.Override(-6);
            var tonemap = profile.Add<Tonemapping>(true); tonemap.mode.Override(TonemappingMode.ACES);
            var split = profile.Add<SplitToning>(true);
            split.shadows.Override(new Color(.46f,.5f,.46f)); split.highlights.Override(new Color(.54f,.51f,.46f)); split.balance.Override(-15);
            var vignette = profile.Add<Vignette>(true); vignette.intensity.Override(.25f); vignette.smoothness.Override(.62f);
            var grain = profile.Add<FilmGrain>(true); grain.intensity.Override(.22f); grain.response.Override(.65f);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(.16f); bloom.threshold.Override(1.15f); bloom.scatter.Override(.55f);
            foreach (var effect in profile.components) AssetDatabase.AddObjectToAsset(effect, profile);
            EditorUtility.SetDirty(profile); EditorUtility.SetDirty(renderer); EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            if (!AssetDatabase.CopyAsset("Assets/Scenes/TimeTheftGame.unity", ScenePath)) throw new InvalidOperationException("Scene copy failed.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var originalLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var root = new GameObject("TimeTheft Visual Style");
            var style = root.AddComponent<TimeTheftVisualStyle>();
            style.pipeline = pipeline; style.gameCamera = Camera.main; style.originalLights = originalLights;
            var volume = root.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 100; volume.sharedProfile = profile; volume.enabled = false;
            style.volume = volume;
            var rig = new GameObject("Table Lighting"); rig.transform.SetParent(root.transform); style.lightingRig = rig;
            var clocks = UnityEngine.Object.FindObjectsByType<ClockTokenView>(FindObjectsSortMode.None);
            Vector3 center = clocks.Length > 0 ? clocks.Aggregate(Vector3.zero, (sum, c) => sum + c.transform.position) / clocks.Length : new Vector3(0,.9f,0);
            AddSpot(rig, "Warm overhead key", center + new Vector3(-1.1f,3.2f,-.5f), center, new Color(1f,.83f,.61f), 20, 75);
            AddSpot(rig, "Cold metal edge", center + new Vector3(2.5f,1.9f,.9f), center, new Color(.52f,.66f,.68f), 10, 90);
            AddSpot(rig, "Opponent rim", center + new Vector3(-1.6f,2f,2.8f), center + new Vector3(0,1f,1.6f), new Color(.66f,.76f,.66f), 16, 70);
            AddSpot(rig, "Counter fill", center + new Vector3(-2.5f,2f,-1.8f), center + new Vector3(-3.7f,.4f,.4f), new Color(.83f,.87f,.73f), 12, 70);
            var fill = new GameObject("Dim directional fill"); fill.transform.SetParent(rig.transform);
            fill.transform.rotation = Quaternion.Euler(50,-30,0);
            var fillLight = fill.AddComponent<Light>(); fillLight.type = LightType.Directional;
            fillLight.intensity = .22f; fillLight.color = new Color(.68f,.73f,.67f); fillLight.shadows = LightShadows.Soft;
            rig.SetActive(false);
            ApplyCounterAccent(true);
            ApplyCounterBodyLighting(true);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            Debug.Log("Visual review ready: " + ScenePath + "; table center " + center);
        }
        // Same grade/pixel/dither/noise preset; only local light and dark detail recovery differ.
        public static void ApplyReadability(bool improved)
        {
            var style = UnityEngine.Object.FindFirstObjectByType<TimeTheftVisualStyle>();
            var rig = style.lightingRig;
            var pipeline = new SerializedObject(style.pipeline);
            pipeline.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = improved ? 8 : 4;
            pipeline.ApplyModifiedPropertiesWithoutUndo();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/LoFiRenderer.asset");
            foreach (var f in renderer.rendererFeatures)
                if (f is LoFiRendererFeature loFi) { loFi.shadowRecovery = improved ? .055f : 0; EditorUtility.SetDirty(loFi); }
            var counter = rig.transform.Find("Counter fill").GetComponent<Light>();
            counter.transform.position = improved ? new Vector3(-1.6f,2.7f,-1.1f) : new Vector3(-2.5f,3.06f,-1.8f);
            counter.transform.rotation = Quaternion.LookRotation((improved ? new Vector3(-3.74f,1.5f,.43f) : new Vector3(-3.7f,1.46f,.4f)) - counter.transform.position);
            counter.intensity = improved ? 64 : 12;
            counter.range = improved ? 7 : 12;
            counter.spotAngle = improved ? 65 : 70;
            counter.innerSpotAngle = counter.spotAngle * .65f;
            counter.shadows = improved ? LightShadows.None : LightShadows.Soft;
            rig.transform.Find("Warm overhead key").GetComponent<Light>().intensity = improved ? 16 : 20;
            var rim = rig.transform.Find("Opponent rim").GetComponent<Light>();
            rim.transform.position = improved ? new Vector3(-1.25f,2.9f,4.2f) : new Vector3(-1.6f,3.06f,2.8f);
            rim.transform.rotation = Quaternion.LookRotation((improved ? new Vector3(0,1.8f,3.43f) : new Vector3(0,2.06f,1.6f)) - rim.transform.position);
            rim.intensity = improved ? 20 : 16; rim.range = improved ? 3 : 12;
            rim.spotAngle = improved ? 60 : 70; rim.innerSpotAngle = rim.spotAngle * .65f;
            var bounce = rig.transform.Find("Clock front bounce");
            if (!bounce)
            {
                AddSpot(rig, "Clock front bounce", new Vector3(.3f,2.8f,-2.4f), new Vector3(0,1.06f,0), new Color(.82f,.86f,.8f), 9, 90);
                bounce = rig.transform.Find("Clock front bounce");
                bounce.GetComponent<Light>().shadows = LightShadows.None;
                bounce.GetComponent<Light>().range = 6;
            }
            bounce.gameObject.SetActive(improved);
            style.SetStyle(true);
            EditorUtility.SetDirty(style.pipeline);
        }
        public static void ApplyCounterAccent(bool enhanced)
        {
            ApplyReadability(true);
            var style = UnityEngine.Object.FindFirstObjectByType<TimeTheftVisualStyle>();
            style.lightingRig.transform.Find("Cold metal edge").GetComponent<Light>().intensity = 10;
            style.lightingRig.transform.Find("Dim directional fill").GetComponent<Light>().intensity = .22f;
            style.lightingRig.transform.Find("Clock front bounce").GetComponent<Light>().intensity = 9;
            foreach (var light in style.lightingRig.GetComponentsInChildren<Light>(true))
                light.intensity *= enhanced ? .97f : 1;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/LoFiRenderer.asset");
            foreach (var f in renderer.rendererFeatures)
                if (f is LoFiRendererFeature loFi) { loFi.sceneIntensity = enhanced ? .985f : 1; EditorUtility.SetDirty(loFi); }
            var glow = renderer.rendererFeatures.OfType<RenderObjects>().FirstOrDefault(f => f.name == "Counter Colored Bloom");
            if (!glow)
            {
                glow = ScriptableObject.CreateInstance<RenderObjects>();
                glow.name = "Counter Colored Bloom";
                glow.settings.passTag = "Counter emission only";
                glow.settings.Event = RenderPassEvent.BeforeRenderingPostProcessing;
                glow.settings.filterSettings.RenderQueueType = RenderQueueType.Transparent;
                glow.settings.filterSettings.LayerMask = -1;
                glow.settings.filterSettings.PassNames = new[] { "CounterGlow" };
                glow.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
                glow.Create(); AssetDatabase.AddObjectToAsset(glow, renderer);
                renderer.rendererFeatures.Insert(1, glow); renderer.SetDirty();
            }
            glow.SetActive(enhanced); EditorUtility.SetDirty(glow); EditorUtility.SetDirty(renderer);
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TimeTheft/Counter/Fonts/CounterPixelSegments.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/CounterGlow.mat");
            if (!material)
            {
                material = new Material(font.material) { name = "Counter Colored Emission", shader = Shader.Find("TimeTheft/Visual Counter Glyph") };
                material.SetFloat("_DisplayGain", 1.08f); material.SetFloat("_GlowIntensity", 2);
                AssetDatabase.CreateAsset(material, Root + "/CounterGlow.mat");
            }
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMPro.TextMeshPro>(FindObjectsSortMode.None))
                if (text.name == "BankDigits" || text.name == "PlayerDigits" || text.name == "OpponentDigits")
                    text.fontSharedMaterial = enhanced ? material : font.material;
            Bloom bloom; style.volume.sharedProfile.TryGet(out bloom);
            bloom.intensity.Override(enhanced ? .15f : .16f); EditorUtility.SetDirty(bloom);
        }
        // Counter housing only: a close front key and a weaker grazing side light.
        public static void ApplyCounterBodyLighting(bool improved)
        {
            var style = UnityEngine.Object.FindFirstObjectByType<TimeTheftVisualStyle>();
            var rig = style.lightingRig;
            var front = rig.transform.Find("Counter fill").GetComponent<Light>();
            front.transform.position = improved ? new Vector3(-2.7f,2.6f,-.4f) : new Vector3(-1.6f,2.7f,-1.1f);
            front.transform.rotation = Quaternion.LookRotation((improved ? new Vector3(-3.9f,1.8f,0) : new Vector3(-3.74f,1.5f,.43f)) - front.transform.position);
            front.intensity = improved ? 1.4f : 62.08f;
            front.range = improved ? 2.8f : 7;
            front.spotAngle = improved ? 100 : 65;
            front.innerSpotAngle = front.spotAngle * .65f;
            front.color = improved ? new Color(1,.89f,.7f) : new Color(.83f,.87f,.73f);
            front.shadows = improved ? LightShadows.Soft : LightShadows.None;
            front.shadowBias = improved ? .02f : .025f;
            front.shadowNormalBias = improved ? .03f : .12f;
            var side = rig.transform.Find("Counter side rake");
            if (!side)
            {
                AddSpot(rig, "Counter side rake", new Vector3(-4.2f,2.25f,-2.3f), new Vector3(-4.05f,1.7f,-.9f), new Color(.58f,.68f,.72f), .6f, 65);
                side = rig.transform.Find("Counter side rake");
                side.GetComponent<Light>().range = 2.3f;
                side.GetComponent<Light>().shadows = LightShadows.None;
            }
            side.gameObject.SetActive(improved);
        }
        private static void AddSpot(GameObject rig, string name, Vector3 position, Vector3 target, Color color, float intensity, float angle)
        {
            var go = new GameObject(name); go.transform.SetParent(rig.transform); go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(target - position);
            var light = go.AddComponent<Light>(); light.type = LightType.Spot; light.color = color; light.intensity = intensity;
            light.range = 12; light.spotAngle = angle; light.innerSpotAngle = angle * .65f; light.shadows = LightShadows.Soft;
            light.shadowBias = .025f; light.shadowNormalBias = .12f;
        }
    }
}
#endif

