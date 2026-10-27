using UnityEngine;

namespace TimeTheft
{
    [DisallowMultipleComponent]
    public sealed class H2TableHatchView : MonoBehaviour
    {
        [SerializeField] private Transform anchor, flapPivot, latchPivot;
        [SerializeField] private Collider receiverFloor;
        [SerializeField] private AnimationClip openClip, closeClip, latchClip;
        public Transform Anchor => anchor;
        public float FloorHeight => receiverFloor ? receiverFloor.bounds.max.y : float.NegativeInfinity;
        public Transform FlapPivot => flapPivot;
        public Transform LatchPivot => latchPivot;
        public bool IsReady => isActiveAndEnabled && anchor && flapPivot && latchPivot && receiverFloor && openClip && closeClip && latchClip;
        public float CycleProgress { get; private set; }
        public void SampleCycle(float progress)
        {
            if (!IsReady) return;
            float t = Mathf.Clamp01(progress); CycleProgress = t;
            H2AnimationSampling.Sample(latchClip, gameObject, t * latchClip.length);
            if (t < .625f) H2AnimationSampling.Sample(openClip, gameObject, Mathf.Clamp01(t / .375f) * openClip.length);
            else H2AnimationSampling.Sample(closeClip, gameObject, Mathf.Clamp01((t - .625f) / .375f) * closeClip.length);
            Physics.SyncTransforms();
        }
        public void ResetClosed()
        {
            if (closeClip) H2AnimationSampling.Sample(closeClip, gameObject, closeClip.length);
            if (latchClip) H2AnimationSampling.Sample(latchClip, gameObject, latchClip.length);
            CycleProgress = 0;
            Physics.SyncTransforms();
        }
        private void OnDisable() => ResetClosed();
    }
}
