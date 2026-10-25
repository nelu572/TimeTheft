using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace TimeTheft
{
    [Serializable]
    public sealed class GameRules
    {
        [FormerlySerializedAs("InitialLife")]
        [Tooltip("게임 시작 전 총수명 (시간). 실행 모델에서 분 정수로 변환한다.")]
        [Min(11)] public int InitialLifeHours = 30;
        [Min(1)] public int MinimumHours = 1;
        [Min(1)] public int MaximumHours = 12;
        [Tooltip("비우면 범위 내 균등·중복 허용. 지정하면 최소값부터 각 숫자의 상대 가중치.")]
        public int[] NumberWeights = new int[0];
        [Range(0, 1)] public float ExtraRedProbability = .5f;
        [Tooltip("잠정 선공. 묶음마다 선공 변경은 하지 않고 전역 턴 교대를 유지한다.")]
        public Participant FirstParticipant = Participant.Player;
        [Range(0, 1)] public float AiRiskTolerance = .5f;

        public void Validate()
        {
            if (InitialLifeHours <= TimeTheftMatch.InitialContributionMinutes / GameTime.MinutesPerHour ||
                InitialLifeHours > int.MaxValue / (2 * GameTime.MinutesPerHour))
                throw new ArgumentException("시작 총수명은 10h 납부 뒤 수명이 남고 전체 합계가 정수 범위인 값이어야 합니다.");
            if (MinimumHours < 1 || MaximumHours < MinimumHours ||
                MaximumHours > int.MaxValue / (TimeTheftMatch.ClocksPerBatch * GameTime.MinutesPerHour))
                throw new ArgumentException("시계 숫자 범위를 확인하세요.");
            if (FirstParticipant != Participant.Player && FirstParticipant != Participant.Ai)
                throw new ArgumentException("선공 설정을 확인하세요.");
            if (float.IsNaN(ExtraRedProbability) || ExtraRedProbability < 0 || ExtraRedProbability > 1 ||
                float.IsNaN(AiRiskTolerance) || AiRiskTolerance < 0 || AiRiskTolerance > 1)
                throw new ArgumentException("확률과 AI 위험 선호는 0~1이어야 합니다.");
            if (NumberWeights == null || NumberWeights.Length == 0) return;
            if (NumberWeights.Length != MaximumHours - MinimumHours + 1)
                throw new ArgumentException("숫자 가중치의 개수는 숫자 범위와 같아야 합니다.");
            long total = 0;
            foreach (int weight in NumberWeights) { if (weight < 0) throw new ArgumentException("가중치는 음수일 수 없습니다."); total += weight; }
            if (total == 0 || total > int.MaxValue) throw new ArgumentException("가중치 합계를 확인하세요.");
        }

        internal GameRules Copy() => new GameRules
        {
            InitialLifeHours = InitialLifeHours, MinimumHours = MinimumHours, MaximumHours = MaximumHours,
            NumberWeights = NumberWeights == null ? new int[0] : (int[])NumberWeights.Clone(),
            ExtraRedProbability = ExtraRedProbability, FirstParticipant = FirstParticipant, AiRiskTolerance = AiRiskTolerance
        };

        internal int GenerateHours(System.Random random)
        {
            if (NumberWeights == null || NumberWeights.Length == 0) return random.Next(MinimumHours, MaximumHours + 1);
            int total = 0;
            foreach (int weight in NumberWeights) total += weight;
            int choice = random.Next(total);
            for (int i = 0; i < NumberWeights.Length; i++) { choice -= NumberWeights[i]; if (choice < 0) return MinimumHours + i; }
            throw new InvalidOperationException("유효하지 않은 숫자 분포입니다.");
        }
    }

    [CreateAssetMenu(menuName = "TimeTheft/게임 설정 v0.4")]
    public sealed class TimeTheftSettings : ScriptableObject
    {
        public GameRules Rules = new GameRules();
        [Header("연출 시간 (초) · 미정 수치는 플레이 테스트용")]
        [Tooltip("새 묶음의 숫자·종류를 자동으로 보여 주는 관찰 시간")]
        [Min(.05f)] public float PreviewDuration = 5.5f;
        [Min(.05f)] public float ShuffleDuration = 1.1f;
        [Min(.05f)] public float AiThinkDuration = .8f;
        [Min(.05f)] public float RevealDuration = 1.2f;
        [Min(.05f)] public float TransferDuration = 1f;
        [Min(.05f)] public float ResultHoldDuration = 1.4f;
        [Min(.05f)] public float DiscardDuration = .65f;
        [Tooltip("0: 새 난수, 그 외: 재현용 시드")]
        public int RandomSeed;
    }
}
