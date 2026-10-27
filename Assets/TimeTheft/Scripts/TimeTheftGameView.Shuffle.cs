using UnityEngine;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView
    {
        private ClockTokenView[] authoredClocks;
        private Vector3[] authoredClockPositions;
        public H2ShufflePresentation ShufflePresentation => GetComponent<H2ShufflePresentation>();

        private void CaptureClockLayout()
        {
            if (authoredClocks != null) return;
            authoredClocks = (ClockTokenView[])clocks.Clone();
            authoredClockPositions = new Vector3[clocks.Length];
            for (int i = 0; i < clocks.Length; i++) authoredClockPositions[i] = clocks[i].transform.localPosition;
        }
        private void ResetClockLayout()
        {
            if (authoredClocks != null) BindShuffleSlots(authoredClocks);
        }
        internal void BindShuffleSlots(ClockTokenView[] destinations)
        {
            clocks = (ClockTokenView[])destinations.Clone();
            for (int i = 0; i < clocks.Length; i++) clocks[i].BindSlot(i, authoredClockPositions[i]);
            Physics.SyncTransforms();
        }
    }
}
