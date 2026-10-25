using System.Collections.Generic;

namespace TimeTheft
{
    public readonly struct AiKnownClock
    {
        public int Index { get; }
        public ClockFace Face { get; }
        internal AiKnownClock(int index, ClockFace face) { Index = index; Face = face; }
    }

    public readonly struct AiStrategySettings
    {
        public bool Enabled { get; }
        public double RiskTolerance { get; }
        public double DecisionNoise { get; }
        public double ItemConservation { get; }
        public int DriverReductionPercent { get; }
        public int BypassPercent { get; }
        public int MaxHeldItems { get; }
        public int ItemsPerBatch { get; }
        internal AiStrategySettings(GameRules rules)
        {
            Enabled = rules.EnableAiItemStrategy; RiskTolerance = rules.AiRiskTolerance;
            DecisionNoise = rules.AiDecisionNoise; ItemConservation = rules.AiItemConservation;
            DriverReductionPercent = rules.ScrewdriverReductionPercent; BypassPercent = rules.TimeBypassPercent;
            MaxHeldItems = rules.MaxHeldItems; ItemsPerBatch = rules.ItemsPerBatch;
        }
    }

    public readonly partial struct AiItemObservation
    {
        public AiStrategySettings Strategy { get; }
        public TurnStage Stage { get; }
        public int Batch { get; }
        public int Turn { get; }
        public int CurrentClock { get; }
        // 공개 구성에서 사용된 값을 뺀 다중집합. 위치 순서와 무관하게 정렬한다.
        public IReadOnlyList<ClockFace> RemainingFaces { get; }
        public IReadOnlyList<int> AvailableClockIndices { get; }
        public IReadOnlyList<AiKnownClock> KnownClocks { get; }
        public IReadOnlyList<int> UsableItemIds { get; }
    }

    public sealed partial class TimeTheftMatch
    {
        public AiItemObservation ObserveItems(Participant participant)
        {
            ValidateParticipant(participant);
            var faces = new List<ClockFace>(publicRemainingFaces);
            faces.Sort((a, b) => a.Kind == b.Kind ? a.Hours.CompareTo(b.Hours) : a.Kind.CompareTo(b.Kind));
            var available = new List<int>();
            var known = new List<AiKnownClock>();
            var usable = new List<int>();
            bool choosing = Stage == TurnStage.AwaitingItems || Stage == TurnStage.AwaitingClock ||
                Stage == TurnStage.AwaitingAction || Stage == TurnStage.AwaitingKeyDecision;
            for (int i = 0; i < ClocksPerBatch; i++)
            {
                if (choosing && !used[i] && DiscardedCount == 0) available.Add(i);
                var face = GetPrivateClock(participant, i);
                if (face.HasValue) known.Add(new AiKnownClock(i, face.Value));
            }
            bool canUse = CanUseItems(participant);
            if (canUse)
                foreach (var item in GetInventory(participant))
                    if (CheckItemUse(participant, item.Id) == ItemUseFailure.None) usable.Add(item.Id);
            return new AiItemObservation(Observe(participant), GetInventory(participant), GetCurrentItemEffects(participant),
                GetPendingItemEffects(participant), canUse, ObserveInventory(participant, Other(participant)),
                new AiStrategySettings(rules), Stage, Batch, Turn, CurrentClock, faces.AsReadOnly(), available.AsReadOnly(),
                known.AsReadOnly(), usable.AsReadOnly());
        }
    }
}
