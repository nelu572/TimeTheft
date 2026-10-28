using System;
using UnityEngine;

namespace TimeTheft.ItemPresentation
{
    public sealed class BypassCableData : ScriptableObject
    {
        public TextAsset Samples;
        public Mesh[] MeshTemplates;
        public CableTopology[] Topology;
        public int SampleCount = 1521;
        public int RingCount = 127;
        public int RadialCount = 10;
        public int SecondsOffset = 6084;
        public int CentersOffset = 12168;
        public int EndcapsOffset = 4648176;
    }

    [Serializable]
    public sealed class CableTopology
    {
        public int[] LoopVertex;
        public float[] Radius;
    }
}
