using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TimeTheft.Visuals
{
    // Scene-local opt-in. No persistent Graphics/Quality asset edits are required.
    [DefaultExecutionOrder(-1000)]
    public sealed class TimeTheftVisualStyle : MonoBehaviour
    {
        public UniversalRenderPipelineAsset pipeline;
        public Camera gameCamera;
        public Volume volume;
        public GameObject lightingRig;
        public Light[] originalLights;
        public bool styleEnabled = true;
        public Color ambientColor = new Color(.2f, .22f, .2f);
        private RenderPipelineAsset previousPipeline;
        private AmbientMode previousAmbientMode;
        private Color previousAmbient;
        private float previousReflection;
        private bool previousPostProcessing;
        private bool previousHdr;
        private bool[] previousLights;
        private bool initialized;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            previousPipeline = QualitySettings.renderPipeline;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbient = RenderSettings.ambientLight;
            previousReflection = RenderSettings.reflectionIntensity;
            previousPostProcessing = gameCamera.GetUniversalAdditionalCameraData().renderPostProcessing;
            previousHdr = gameCamera.allowHDR;
            previousLights = new bool[originalLights.Length];
            for (int i = 0; i < originalLights.Length; i++) previousLights[i] = originalLights[i] && originalLights[i].enabled;
            initialized = true;
            SetStyle(styleEnabled);
        }
        private void OnValidate() { if (Application.isPlaying && initialized) SetStyle(styleEnabled); }
        public void SetStyle(bool enabled)
        {
            styleEnabled = enabled;
            if (!initialized) return;
            QualitySettings.renderPipeline = enabled ? pipeline : previousPipeline;
            if (gameCamera) gameCamera.GetUniversalAdditionalCameraData().renderPostProcessing = enabled || previousPostProcessing;
            if (gameCamera) gameCamera.allowHDR = enabled || previousHdr;
            if (volume) volume.enabled = enabled;
            if (lightingRig) lightingRig.SetActive(enabled);
            for (int i = 0; i < originalLights.Length; i++) if (originalLights[i]) originalLights[i].enabled = !enabled && previousLights[i];
            RenderSettings.ambientMode = enabled ? AmbientMode.Flat : previousAmbientMode;
            RenderSettings.ambientLight = enabled ? ambientColor : previousAmbient;
            RenderSettings.reflectionIntensity = enabled ? .3f : previousReflection;
        }
        private void OnDisable()
        {
            if (!initialized) return;
            SetStyle(false);
            initialized = false;
        }
    }
}

