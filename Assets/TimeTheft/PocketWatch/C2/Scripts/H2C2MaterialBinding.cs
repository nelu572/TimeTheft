using UnityEngine;

namespace TimeTheft
{
    public sealed class H2C2MaterialBinding : MonoBehaviour
    {
        [SerializeField] private Renderer[] metalRenderers;
        [SerializeField] private Material[] litMaterials, c2Materials;
        [SerializeField] private Renderer dial, lid;
        [SerializeField] private Material redDial, litRedLid, c2RedLid;
        [SerializeField] private bool c2Enabled = true;
        public bool C2Enabled => c2Enabled;
        public Renderer[] MetalRenderers => metalRenderers;

        private void OnEnable() => SetC2(c2Enabled);

        public void SetC2(bool enabled)
        {
            c2Enabled = enabled;
            if (metalRenderers == null || litMaterials == null || c2Materials == null) return;
            for (int i = 0; i < metalRenderers.Length; i++)
                metalRenderers[i].sharedMaterial = c2Enabled ? c2Materials[i] : litMaterials[i];
            ApplyLid();
        }

        private void LateUpdate() => ApplyLid();

        private void ApplyLid()
        {
            if (!dial || !lid || metalRenderers == null) return;
            bool red = dial.sharedMaterial == redDial;
            for (int i = 0; i < metalRenderers.Length; i++)
            {
                if (metalRenderers[i] != lid) continue;
                Material material = red ? (c2Enabled ? c2RedLid : litRedLid) : (c2Enabled ? c2Materials[i] : litMaterials[i]);
                if (lid.sharedMaterial != material) lid.sharedMaterial = material;
                break;
            }
        }
    }
}
