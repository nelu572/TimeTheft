using System;
using System.Collections.Generic;

namespace TimeTheft
{
    public static partial class TimeTheftAi
    {
        private const double NoChoice = -1e100;
        private static int Bit(ItemKind kind) => 1 << (int)kind;

        // 기존 호출자는 재현 가능한 기본 RNG를 사용한다. 실제 차례는 Controller의 AI RNG를 받는다.
        public static int? ChooseItem(AiItemObservation info) => ChooseItem(info, new Random(0));

        public static int? ChooseItem(AiItemObservation info, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (!info.Strategy.Enabled || !info.CanUseItems || info.Stage != TurnStage.AwaitingItems ||
                info.RemainingFaces == null || info.RemainingFaces.Count == 0) return null;
            var candidates = new List<ItemState>();
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
                foreach (var item in info.OwnItems)
                    if (item.Kind == kind && !item.IsConsumed && !item.IsFoundationTest && Contains(info.UsableItemIds, item.Id))
                    { candidates.Add(item); break; }
            if (candidates.Count == 0) return null;
            int active = ActiveEffects(info);
            double baseline = PlanValue(info, active, 0);
            double best = baseline + Math.Max(1, Scale(info) * .01);
            int? selected = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                ConsiderPlan(info, random, active, Bit(candidates[i].Kind), candidates[i].Id, baseline, ref best, ref selected);
                for (int j = i + 1; j < candidates.Count; j++)
                    ConsiderPlan(info, random, active, Bit(candidates[i].Kind) | Bit(candidates[j].Kind), candidates[i].Id,
                        baseline, ref best, ref selected);
            }
            // 세 아이템의 정산/정보 시너지만 추가한다. 전체 조합이나 여러 차례를 완전 탐색하지 않는다.
            int triple = Bit(ItemKind.SpareKey) | Bit(ItemKind.Screwdriver) | Bit(ItemKind.TimeBypass);
            int available = 0; int firstTripleId = -1;
            foreach (var candidate in candidates)
            {
                available |= Bit(candidate.Kind);
                if (firstTripleId < 0 && (triple & Bit(candidate.Kind)) != 0) firstTripleId = candidate.Id;
            }
            if ((available & triple) == triple)
                ConsiderPlan(info, random, active, triple, firstTripleId, baseline, ref best, ref selected);
            return selected;
        }

        private static void ConsiderPlan(AiItemObservation info, Random random, int active, int added, int firstId,
            double baseline, ref double best, ref int? selected)
        {
            double score = PlanValue(info, active | added, added);
            // 잡음은 실제 이득이 있는 계획 사이의 선호만 바꾼다. 무효 사용을 만들지 않는다.
            if (score <= baseline + Math.Max(1, Scale(info) * .01)) return;
            score += Noise(info, random);
            if (score > best) { best = score; selected = firstId; }
        }

        public static int SelectClock(AiItemObservation info, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (info.AvailableClockIndices == null || info.AvailableClockIndices.Count == 0)
                throw new ArgumentException("선택할 시계가 없습니다.");
            if (!info.Strategy.Enabled) return SelectClock(info.AvailableClockIndices, random);
            int effects = ActiveEffects(info);
            var unknown = UnknownFaces(info);
            double knownAlternative = BestKnownValue(info, effects, -1);
            double unknownValue = Has(effects, ItemKind.SpareKey)
                ? PeekValue(info, unknown, effects, knownAlternative, false) : BestActionValue(info, unknown, effects, false);
            var choices = new List<int>();
            double best = NoChoice;
            foreach (int index in info.AvailableClockIndices)
            {
                var face = KnownFace(info, index);
                double value = face.HasValue ? BestActionValue(info, One(face.Value), effects, false) : unknownValue;
                value += Noise(info, random);
                if (value > best + .0001) { best = value; choices.Clear(); choices.Add(index); }
                else if (Math.Abs(value - best) <= .0001) choices.Add(index);
            }
            return choices[random.Next(choices.Count)];
        }

        public static int ChooseKeyClock(AiItemObservation info, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            int current = info.CurrentClock;
            var currentFace = KnownFace(info, current);
            if (!info.Strategy.Enabled || !currentFace.HasValue || info.Stage != TurnStage.AwaitingKeyDecision) return current;
            int effects = ActiveEffects(info);
            double keep = BestActionValue(info, One(currentFace.Value), effects, false) + Noise(info, random);
            double unknownValue = BestActionValue(info, UnknownFaces(info), effects, false);
            var alternatives = new List<int>();
            double best = keep;
            foreach (int index in info.AvailableClockIndices)
            {
                if (index == current) continue;
                var face = KnownFace(info, index);
                double value = (face.HasValue ? BestActionValue(info, One(face.Value), effects, false) : unknownValue) + Noise(info, random);
                if (value > best + .0001) { best = value; alternatives.Clear(); alternatives.Add(index); }
                else if (alternatives.Count > 0 && Math.Abs(value - best) <= .0001) alternatives.Add(index);
            }
            return alternatives.Count == 0 ? current : alternatives[random.Next(alternatives.Count)];
        }

        public static TimeAction Decide(AiItemObservation info, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (!info.Strategy.Enabled) return Decide(info.PublicGame, info.Strategy.RiskTolerance, random);
            var face = KnownFace(info, info.CurrentClock);
            var faces = face.HasValue ? One(face.Value) : UnknownFaces(info);
            if (faces.Count == 0) faces = new List<ClockFace>(info.RemainingFaces);
            int effects = ActiveEffects(info);
            double collect = ActionValue(info, faces, effects, TimeAction.Collect, false) + Noise(info, random);
            double ration = ActionValue(info, faces, effects, TimeAction.Ration, false) + Noise(info, random);
            return collect > ration ? TimeAction.Collect : TimeAction.Ration;
        }

        public static TimeSettlement Estimate(AiItemObservation info, ClockFace face, TimeAction action) =>
            Calculate(info, face, action, ActiveEffects(info), false);

        private static double PlanValue(AiItemObservation info, int effects, int added)
        {
            double value = SelectionValue(info, effects);
            bool scissors = Has(added, ItemKind.Scissors);
            bool duck = Has(added, ItemKind.RubberDuck) || Pending(info, ItemKind.RubberDuck);
            bool cuffs = Has(added, ItemKind.Handcuffs) || Pending(info, ItemKind.Handcuffs);
            int count = Math.Max(0, info.OpponentInventory.Count - (scissors ? 1 : 0));
            double threat = AverageOpponentThreat(info);
            double futureValue = 0;
            if (scissors && info.OpponentInventory.Count > 0)
                futureValue += threat * (.5 + .15 * Math.Min(3, info.OpponentInventory.Count));
            // 가위가 마지막 보유품을 없애면 오리의 봉쇄 가치는 사라진다.
            if (duck)
            {
                int futureCount = count;
                // 다음 실제 행동이 새 묶음으로 넘어가면 공개 지급 규칙만 반영한다.
                bool crossesBatch = info.RemainingFaces.Count <= 2 || (cuffs && info.RemainingFaces.Count <= 3);
                if (crossesBatch && info.Strategy.ItemsPerBatch > 0)
                    futureCount = (int)Math.Min(info.Strategy.MaxHeldItems, (long)count + info.Strategy.ItemsPerBatch);
                double denied = count * threat + Math.Max(0, futureCount - count) * PriorOpponentThreat(info);
                futureValue += denied * .75 * (cuffs ? .85 : 1);
            }
            if (cuffs) futureValue += TempoValue(info);
            // 이번 D에서 끝나면 미래 견제/추가 행동은 얻지 못한다. 소모 비용은 지금 지불한다.
            if (futureValue != 0)
                value += ContinuationForSelection(info, UnknownFaces(info), effects, Has(effects, ItemKind.SpareKey), out _) * futureValue;
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
                if (Has(added, kind)) value -= PreservationCost(info, kind);
            return value;
        }

        private static double SelectionValue(AiItemObservation info, int effects)
        {
            double known = BestKnownValue(info, effects, -1);
            var unknown = UnknownFaces(info);
            double value = Has(effects, ItemKind.SpareKey)
                ? PeekValue(info, unknown, effects, known, false) : BestActionValue(info, unknown, effects, false);
            return Math.Max(known, value);
        }

        // 평가 중인 B/C 방침을 잡음 없이 따른다. 미래 게임이나 비공개 위치 값은 읽지 않는다.
        private static double ContinuationForSelection(AiItemObservation info, IReadOnlyList<ClockFace> unknown,
            int effects, bool peek, out double bestValue)
        {
            bestValue = peek ? PeekValue(info, unknown, effects, BestKnownValue(info, effects, -1), false)
                : BestActionValue(info, unknown, effects, false);
            double chance = peek ? ContinuationAfterPeek(info, unknown, effects) : ContinuationForAction(info, unknown, effects);
            double weightedChance = chance * unknown.Count;
            int choices = unknown.Count;
            foreach (var known in info.KnownClocks)
            {
                if (!Contains(info.AvailableClockIndices, known.Index)) continue;
                var face = One(known.Face);
                double value = BestActionValue(info, face, effects, false);
                double continues = ContinuationForAction(info, face, effects);
                if (value > bestValue + .0001)
                {
                    bestValue = value;
                    weightedChance = continues;
                    choices = 1;
                }
                else if (Math.Abs(value - bestValue) <= .0001)
                {
                    weightedChance += continues;
                    choices++;
                }
            }
            return choices == 0 ? 0 : weightedChance / choices;
        }

        private static double ContinuationAfterPeek(AiItemObservation info, IReadOnlyList<ClockFace> unknown, int effects)
        {
            if (unknown.Count == 0) return 0;
            double chance = 0;
            for (int i = 0; i < unknown.Count; i++)
            {
                var current = One(unknown[i]);
                var alternatives = new List<ClockFace>();
                for (int j = 0; j < unknown.Count; j++) if (j != i) alternatives.Add(unknown[j]);
                double replaceChance = ContinuationForSelection(info, alternatives, effects, false, out double replaceValue);
                double keepValue = BestActionValue(info, current, effects, false);
                // 실제 열쇠 결정처럼 동가이면 유지한다. 재선택 뒤에는 두 번째 값을 확인하지 않는다.
                chance += keepValue >= replaceValue - .0001 ? ContinuationForAction(info, current, effects) : replaceChance;
            }
            return chance / unknown.Count;
        }

        private static double ContinuationForAction(AiItemObservation info, IReadOnlyList<ClockFace> faces, int effects)
        {
            if (faces.Count == 0) return 0;
            TimeAction action = ActionValue(info, faces, effects, TimeAction.Collect, false) >
                ActionValue(info, faces, effects, TimeAction.Ration, false) ? TimeAction.Collect : TimeAction.Ration;
            int continues = 0;
            foreach (var face in faces)
            {
                var result = Calculate(info, face, action, effects, false);
                if (result.OwnLifeMinutes > 0 && result.OpponentLifeMinutes > 0) continues++;
            }
            return (double)continues / faces.Count;
        }

        private static double PeekValue(AiItemObservation info, IReadOnlyList<ClockFace> faces, int effects,
            double knownAlternative, bool opponent)
        {
            if (faces.Count == 0) return NoChoice;
            double value = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                var other = new List<ClockFace>();
                for (int j = 0; j < faces.Count; j++) if (j != i) other.Add(faces[j]);
                double keep = BestActionValue(info, One(faces[i]), effects, opponent);
                // 재선택은 다른 닫힌 후보들의 조건부 분포만 평가한다. 다음 실제 값은 읽지 않는다.
                double replace = Math.Max(knownAlternative, BestActionValue(info, other, effects, opponent));
                value += Math.Max(keep, replace);
            }
            return value / faces.Count;
        }

        private static double BestKnownValue(AiItemObservation info, int effects, int except)
        {
            double best = NoChoice;
            foreach (var known in info.KnownClocks)
                if (known.Index != except && Contains(info.AvailableClockIndices, known.Index))
                    best = Math.Max(best, BestActionValue(info, One(known.Face), effects, false));
            return best;
        }

        private static double BestActionValue(AiItemObservation info, IReadOnlyList<ClockFace> faces, int effects, bool opponent)
        {
            if (faces.Count == 0) return NoChoice;
            return Math.Max(ActionValue(info, faces, effects, TimeAction.Collect, opponent),
                ActionValue(info, faces, effects, TimeAction.Ration, opponent));
        }

        private static double ActionValue(AiItemObservation info, IReadOnlyList<ClockFace> faces, int effects,
            TimeAction action, bool opponent)
        {
            if (faces.Count == 0) return NoChoice;
            double result = 0;
            foreach (var face in faces) result += Utility(info, Calculate(info, face, action, effects, opponent), opponent);
            return result / faces.Count;
        }

        private static TimeSettlement Calculate(AiItemObservation info, ClockFace face, TimeAction action, int effects, bool opponent)
        {
            var game = info.PublicGame;
            return TimeTheftSettlement.Calculate(face, action, opponent ? game.OpponentLifeMinutes : game.OwnLifeMinutes,
                opponent ? game.OwnLifeMinutes : game.OpponentLifeMinutes, game.BankMinutes,
                Has(effects, ItemKind.Screwdriver), Has(effects, ItemKind.TimeBypass),
                info.Strategy.DriverReductionPercent, info.Strategy.BypassPercent);
        }

        private static double Utility(AiItemObservation info, TimeSettlement result, bool opponent)
        {
            var game = info.PublicGame;
            int own = opponent ? game.OpponentLifeMinutes : game.OwnLifeMinutes;
            int other = opponent ? game.OwnLifeMinutes : game.OpponentLifeMinutes;
            double terminal = Math.Max(2400, (double)own + other + game.BankMinutes);
            double risk = info.Strategy.RiskTolerance;
            if (result.OwnLifeMinutes == 0) return -terminal * (1.5 - .5 * risk);
            if (result.OpponentLifeMinutes == 0) return terminal * (1 + .25 * risk);
            double survival = 1 + (1 - risk) * .6 * Math.Min(1, Scale(info) / (own + 1d));
            return (result.OwnLifeMinutes - (double)own) * survival - (result.OpponentLifeMinutes - (double)other);
        }

        private static double TempoValue(AiItemObservation info)
        {
            // 현재 D의 드라이버/우회선은 다음 추가 행동으로 이월되지 않는다.
            double own = BestActionValue(info, info.RemainingFaces, 0, false);
            double other = BestActionValue(info, info.RemainingFaces, 0, true);
            double boundary = info.RemainingFaces.Count <= 2 ? .65 : 1;
            return (own + other) * .55 * boundary;
        }

        private static double AverageOpponentThreat(AiItemObservation info)
        {
            if (info.OpponentInventory.Count == 0) return 0;
            double value = 0; int count = 0;
            if (info.OpponentInventory.RevealsKinds)
            {
                foreach (var kind in info.OpponentInventory.Kinds) { value += OpponentThreat(info, kind); count++; }
            }
            else
            {
                // 비공개 종류 대신 동일한 공개 사전값을 사용한다.
                return PriorOpponentThreat(info);
            }
            return count == 0 ? 0 : value / count;
        }

        private static double PriorOpponentThreat(AiItemObservation info)
        {
            double value = 0; int count = 0;
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind))) { value += OpponentThreat(info, kind); count++; }
            return count == 0 ? 0 : value / count;
        }

        private static double OpponentThreat(AiItemObservation info, ItemKind kind)
        {
            double average = Scale(info);
            double baseline = BestActionValue(info, info.RemainingFaces, 0, true);
            switch (kind)
            {
                case ItemKind.SpareKey:
                    return Math.Max(0, PeekValue(info, info.RemainingFaces, 0, NoChoice, true) - baseline);
                case ItemKind.Screwdriver:
                case ItemKind.TimeBypass:
                    return Math.Max(0, BestActionValue(info, info.RemainingFaces, Bit(kind), true) - baseline);
                case ItemKind.Handcuffs: return Math.Max(0, TempoValue(info));
                case ItemKind.RubberDuck: return average * .2 * Math.Min(3, info.OwnItems.Count);
                case ItemKind.Scissors: return average * .18 * (info.OwnItems.Count > 0 ? 1 : 0);
                default: return 0;
            }
        }

        private static double PreservationCost(AiItemObservation info, ItemKind kind)
        {
            double average = Scale(info);
            double fraction = kind == ItemKind.Handcuffs ? .32 : kind == ItemKind.SpareKey || kind == ItemKind.RubberDuck ? .2
                : kind == ItemKind.Screwdriver ? .18 : kind == ItemKind.TimeBypass ? .16 : .14;
            double horizon = Math.Min(1, Math.Min(info.PublicGame.OwnLifeMinutes, info.PublicGame.OpponentLifeMinutes) / Math.Max(60, average * 2.5));
            double pressure = info.Strategy.MaxHeldItems > 0 && info.OwnItems.Count >= info.Strategy.MaxHeldItems ? .65 : 1;
            if (info.Strategy.ItemsPerBatch > 0 && info.RemainingFaces.Count <= 3) pressure *= .8;
            return average * fraction * (.45 + 1.7 * info.Strategy.ItemConservation) * (.25 + .75 * horizon) * pressure;
        }

        private static List<ClockFace> UnknownFaces(AiItemObservation info)
        {
            var unknown = new List<ClockFace>(info.RemainingFaces);
            foreach (var known in info.KnownClocks)
            {
                if (!Contains(info.AvailableClockIndices, known.Index)) continue;
                int index = unknown.FindIndex(face => face.Kind == known.Face.Kind && face.Hours == known.Face.Hours);
                if (index >= 0) unknown.RemoveAt(index);
            }
            return unknown;
        }

        private static ClockFace? KnownFace(AiItemObservation info, int index)
        {
            foreach (var known in info.KnownClocks) if (known.Index == index) return known.Face;
            return null;
        }

        private static int ActiveEffects(AiItemObservation info)
        {
            int mask = 0;
            foreach (var effect in info.OwnCurrentEffects)
                if (!effect.Source.IsFoundationTest && effect.RegisteredTurn == info.Turn) mask |= Bit(effect.Source.Kind);
            return mask;
        }

        private static bool Pending(AiItemObservation info, ItemKind kind)
        {
            foreach (var effect in info.OwnPendingEffects)
                if (!effect.Source.IsFoundationTest && effect.Source.Kind == kind) return true;
            return false;
        }

        private static bool Has(int mask, ItemKind kind) => (mask & Bit(kind)) != 0;
        private static bool Contains(IReadOnlyList<int> values, int value)
        { foreach (int current in values) if (current == value) return true; return false; }
        private static List<ClockFace> One(ClockFace face) => new List<ClockFace> { face };
        private static double AverageMinutes(IReadOnlyList<ClockFace> faces)
        {
            if (faces.Count == 0) return GameTime.MinutesPerHour;
            double total = 0;
            foreach (var face in faces) total += face.Hours * (double)GameTime.MinutesPerHour;
            return total / faces.Count;
        }
        private static double Noise(AiItemObservation info, Random random) => info.Strategy.DecisionNoise == 0 ? 0
            : (random.NextDouble() * 2 - 1) * info.Strategy.DecisionNoise * Scale(info) * .08;
        private static double Scale(AiItemObservation info) => Math.Min(AverageMinutes(info.RemainingFaces),
            Math.Max(GameTime.MinutesPerHour, Math.Max(info.PublicGame.BankMinutes,
                Math.Max(info.PublicGame.OwnLifeMinutes, info.PublicGame.OpponentLifeMinutes))));
    }
}
