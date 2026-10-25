using System;
using UnityEngine;

namespace TimeTheft
{
    [Serializable]
    public sealed class PrototypeRules
    {
        [Min(1)] public int InitialLife = 30;
        [Range(1, 12)] public int MinimumValue = 1;
        [Range(1, 12)] public int MaximumValue = 12;
        [Range(0f, 1f)] public float AiSwapAtMinimum = 0.18f;
        [Range(0f, 1f)] public float AiSwapAtMaximum = 0.82f;
    }

    [CreateAssetMenu(menuName = "TimeTheft/프로토타입 설정")]
    public sealed class PrototypeSettings : ScriptableObject
    {
        public PrototypeRules Rules = new PrototypeRules();
        [Header("연출 시간 (초)")]
        [Min(0.05f)] public float MoveDuration = 0.4f;
        [Min(0.05f)] public float RevealDuration = 0.8f;
        [Min(0.05f)] public float SettlementDuration = 0.9f;
        [Min(0.05f)] public float RoundPause = 1.1f;
        [Tooltip("0: 매 게임 새 난수, 그 외: 재현 가능한 시드")]
        public int RandomSeed;
    }
}
