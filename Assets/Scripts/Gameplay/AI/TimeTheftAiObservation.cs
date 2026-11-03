using System;
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
        // 다음 묶음의 공개 생성 규칙에서 만든 주변 분포. 실제 추첨/배치/게임 RNG는 포함하지 않는다.
        public IReadOnlyList<AiClockPrior> NextBatchClocks { get; }
        internal AiStrategySettings(GameRules rules)
        {
            Enabled = rules.EnableAiItemStrategy; RiskTolerance = rules.AiRiskTolerance;
            DecisionNoise = rules.AiDecisionNoise; ItemConservation = rules.AiItemConservation;
            DriverReductionPercent = rules.ScrewdriverReductionPercent; BypassPercent = rules.TimeBypassPercent;
            MaxHeldItems = rules.MaxHeldItems; ItemsPerBatch = rules.ItemsPerBatch;
            NextBatchClocks = BuildClockPrior(rules);
        }

        private static IReadOnlyList<AiClockPrior> BuildClockPrior(GameRules rules)
        {
            var prior = new List<AiClockPrior>();
            int range = rules.MaximumHours - rules.MinimumHours + 1;
            bool weighted = rules.NumberWeights != null && rules.NumberWeights.Length > 0;
            double total = 0;
            if (weighted) foreach (int weight in rules.NumberWeights) total += weight;
            else total = range;
            double red = (1 + 2 * (double)rules.ExtraRedProbability) / 5;
            // 기본 1~12와 작은 커스텀 분포는 정확히 열거한다. 큰 범위는 16구간의 질량·평균을 보존한다.
            int buckets = Math.Min(16, range);
            for (int bucket = 0; bucket < buckets; bucket++)
            {
                int start = (int)((long)range * bucket / buckets);
                int end = (int)((long)range * (bucket + 1) / buckets);
                double mass = 0, moment = 0;
                if (weighted)
                    for (int i = start; i < end; i++)
                    { mass += rules.NumberWeights[i]; moment += (rules.MinimumHours + (double)i) * rules.NumberWeights[i]; }
                else { mass = end - start; moment = mass * (rules.MinimumHours + (start + (double)end - 1) / 2); }
                if (mass == 0) continue;
                double mean = Math.Max(rules.MinimumHours + start,
                    Math.Min(rules.MinimumHours + end - 1d, moment / mass));
                int low = (int)Math.Floor(mean);
                double fraction = mean - low;
                AddPrior(prior, low, mass / total * (1 - fraction), red);
                if (fraction > 0) AddPrior(prior, low + 1, mass / total * fraction, red);
            }
            return prior.AsReadOnly();
        }

        private static void AddPrior(List<AiClockPrior> prior, int hours, double probability, double red)
        {
            prior.Add(new AiClockPrior(new ClockFace(hours, ClockKind.Normal), probability * (1 - red)));
            prior.Add(new AiClockPrior(new ClockFace(hours, ClockKind.Red), probability * red));
        }
    }

    public readonly struct AiClockPrior
    {
        public ClockFace Face { get; }
        public double Probability { get; }
        internal AiClockPrior(ClockFace face, double probability) { Face = face; Probability = probability; }
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
