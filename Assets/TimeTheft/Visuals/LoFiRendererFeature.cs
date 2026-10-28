using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace TimeTheft.Visuals
{
    // URP 17 RenderGraph. Overlay UI is drawn after this pass at native resolution.
    public sealed class LoFiRendererFeature : ScriptableRendererFeature
    {
        public Material material;
        [Range(240, 720)] public int verticalResolution = 480;
        [Range(8, 64)] public int colorLevels = 48;
        [Range(0, 1)] public float dithering = .45f;
        [Range(0, .06f)] public float noise = .008f;
        [Tooltip("암부 세부만 복원합니다. 검정과 밝은 영역은 유지합니다.")]
        [Range(0, .12f)] public float shadowRecovery = .055f;
        [Range(.8f, 1.2f)] public float sceneIntensity = .985f;
        private LoFiPass pass;

        public override void Create() => pass = new LoFiPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!material || renderingData.cameraData.cameraType != CameraType.Game || renderingData.cameraData.renderType != CameraRenderType.Base) return;
            material.SetVector("_LoFi", new Vector4(colorLevels, dithering, noise, shadowRecovery));
            material.SetFloat("_SceneIntensity", sceneIntensity);
            pass.material = material;
            pass.height = verticalResolution;
            renderer.EnqueuePass(pass);
        }

        private sealed class LoFiPass : ScriptableRenderPass
        {
            public Material material;
            public int height;
            public LoFiPass() { requiresIntermediateTexture = true; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var source = resources.activeColorTexture;
                var full = graph.GetTextureDesc(source);
                full.name = "TimeTheft LoFi output";
                full.clearBuffer = false;
                var low = full;
                low.height = Mathf.Min(height, full.height);
                low.width = Mathf.Max(1, Mathf.RoundToInt(full.width * (low.height / (float)full.height)));
                low.name = "TimeTheft LoFi low resolution";
                low.filterMode = FilterMode.Point;
                var reduced = graph.CreateTexture(low);
                var output = graph.CreateTexture(full);
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, reduced, material, 0), passName: "TimeTheft quantize and dither");
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(reduced, output, material, 1), passName: "TimeTheft point upscale");
                resources.cameraColor = output;
            }
        }
    }
}

