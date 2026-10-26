using System;
using System.Collections.Generic;

namespace TimeTheft
{
    public static partial class TimeTheftAi
    {
        // 실제 위치 없이 공개 다중집합의 값과 자기 확인 여부만 가진 가상 상태.
        private readonly struct FutureClock
        {
            public readonly ClockFace Face;
            public readonly bool Known;
            public FutureClock(ClockFace face, bool known) { Face = face; Known = known; }
        }

        private readonly struct FutureState
        {
            public readonly int Own, Other, Bank;
            public readonly FutureClock[] Clocks;
            public FutureState(int own, int other, int bank, FutureClock[] clocks)
            { Own = own; Other = other; Bank = bank; Clocks = clocks; }
            public bool Terminal => Own == 0 || Other == 0;
        }

        private readonly struct FutureBranch
        {
            public readonly FutureState State;
            public readonly double Reward, Probability;
            public FutureBranch(FutureState state, double reward, double probability)
            { State = state; Reward = reward; Probability = probability; }
        }

        private struct FutureForecast
        {
            public double Normal, Tempo, OpponentReach, OpponentRefill, CuffedOpponentReach, CuffedOpponentRefill;
        }

        private static FutureForecast Forecast(AiItemObservation info, int effects, bool cuffs,
            Dictionary<int, FutureForecast> cache)
        {
            int key = (effects & (Bit(ItemKind.SpareKey) | Bit(ItemKind.Screwdriver) | Bit(ItemKind.TimeBypass))) | (cuffs ? 64 : 0);
            FutureForecast result;
            if (cache != null && cache.TryGetValue(key, out result)) return result;
            result = new FutureForecast();
            var state = FutureStart(info);
            foreach (var branch in FutureChoices(info, state, true, effects, Has(effects, ItemKind.SpareKey)))
            {
                if (branch.State.Terminal) continue;
                double p = branch.Probability;
                result.OpponentReach += p;
                if (branch.State.Clocks.Length <= 1) result.OpponentRefill += p;
                double normal = .55 * FutureLine(info, branch.State, false, 2);
                result.Normal += p * normal;
                if (!cuffs) continue;
                // 현재 D 뒤 상태에서 추가 자기 행동과, 생략하지 않았을 상대 행동의 경로를 비교한다.
                result.Tempo += p * (.55 * FutureLine(info, branch.State, true, 2) - normal);
                if (branch.State.Clocks.Length <= 1)
                {
                    double reach = FreshContinuation(info, branch.State);
                    result.CuffedOpponentReach += p * reach;
                    result.CuffedOpponentRefill += p * reach;
                }
                else
                    foreach (var extra in FutureChoices(info, branch.State, true, 0, false))
                    {
                        if (extra.State.Terminal) continue;
                        double reaches = p * extra.Probability;
                        result.CuffedOpponentReach += reaches;
                        if (extra.State.Clocks.Length <= 1) result.CuffedOpponentRefill += reaches;
                    }
            }
            if (cache != null) cache.Add(key, result);
            return result;
        }

        private static FutureState FutureStart(AiItemObservation info)
        {
            var clocks = new FutureClock[info.RemainingFaces.Count];
            for (int i = 0; i < clocks.Length; i++) clocks[i] = new FutureClock(info.RemainingFaces[i], false);
            foreach (var known in info.KnownClocks)
            {
                if (!Contains(info.AvailableClockIndices, known.Index)) continue;
                for (int i = 0; i < clocks.Length; i++)
                    if (!clocks[i].Known && SameFace(clocks[i].Face, known.Face))
                    { clocks[i] = new FutureClock(clocks[i].Face, true); break; }
            }
            return new FutureState(info.PublicGame.OwnLifeMinutes, info.PublicGame.OpponentLifeMinutes, info.PublicGame.BankMinutes, clocks);
        }

        private static bool SameFace(ClockFace a, ClockFace b) => a.Hours == b.Hours && a.Kind == b.Kind;

        private static List<FutureBranch> FutureChoices(AiItemObservation info, FutureState state, bool self, int effects, bool peek)
        {
            var result = new List<FutureBranch>();
            var known = new List<int>();
            var unknown = new List<int>();
            for (int i = 0; i < state.Clocks.Length; i++)
                if (self && state.Clocks[i].Known) known.Add(i); else unknown.Add(i);
            double knownValue = NoChoice;
            foreach (int i in known) knownValue = Math.Max(knownValue, FutureBest(info, state, OneIndex(i), self, effects));
            double unknownValue = NoChoice;
            if (unknown.Count > 0)
            {
                if (!peek) unknownValue = FutureBest(info, state, unknown, self, effects);
                else
                {
                    unknownValue = 0;
                    foreach (int i in unknown)
                    {
                        var others = WithoutIndex(unknown, i);
                        double keep = FutureBest(info, state, OneIndex(i), self, effects);
                        unknownValue += Math.Max(keep, Math.Max(knownValue, FutureBest(info, state, others, self, effects)));
                    }
                    unknownValue /= unknown.Count;
                }
            }
            double best = Math.Max(knownValue, unknownValue);
            int ties = 0;
            foreach (int i in known)
                if (Math.Abs(FutureBest(info, state, OneIndex(i), self, effects) - best) <= .0001) ties++;
            bool chooseUnknown = unknown.Count > 0 && Math.Abs(unknownValue - best) <= .0001;
            if (chooseUnknown) ties += unknown.Count;
            foreach (int i in known)
                if (Math.Abs(FutureBest(info, state, OneIndex(i), self, effects) - best) <= .0001)
                    AddFutureAction(result, info, state, OneIndex(i), self, effects, 1d / ties, -1);
            if (!chooseUnknown) return result;
            if (!peek)
                AddFutureAction(result, info, state, unknown, self, effects, (double)unknown.Count / ties, -1);
            else
                foreach (int i in unknown)
                {
                    var others = WithoutIndex(unknown, i);
                    double keep = FutureBest(info, state, OneIndex(i), self, effects);
                    double replace = Math.Max(knownValue, FutureBest(info, state, others, self, effects));
                    // 실제 열쇠 방침처럼 동가이면 유지하고, 재선택한 두 번째 값은 확인하지 않는다.
                    if (keep >= replace - .0001)
                        AddFutureAction(result, info, state, OneIndex(i), self, effects, 1d / ties, -1);
                    else AddFutureReplacement(result, info, state, known, others, self, effects, 1d / ties, i);
                }
            return result;
        }

        private static void AddFutureReplacement(List<FutureBranch> result, AiItemObservation info, FutureState state,
            List<int> known, List<int> unknown, bool self, int effects, double probability, int peeked)
        {
            double unknownValue = FutureBest(info, state, unknown, self, effects), best = unknownValue;
            foreach (int i in known) best = Math.Max(best, FutureBest(info, state, OneIndex(i), self, effects));
            int ties = 0;
            foreach (int i in known)
                if (Math.Abs(FutureBest(info, state, OneIndex(i), self, effects) - best) <= .0001) ties++;
            bool chooseUnknown = unknown.Count > 0 && Math.Abs(unknownValue - best) <= .0001;
            if (chooseUnknown) ties += unknown.Count;
            foreach (int i in known)
                if (Math.Abs(FutureBest(info, state, OneIndex(i), self, effects) - best) <= .0001)
                    AddFutureAction(result, info, state, OneIndex(i), self, effects, probability / ties, peeked);
            if (chooseUnknown)
                AddFutureAction(result, info, state, unknown, self, effects, probability * unknown.Count / ties, peeked);
        }

        private static void AddFutureAction(List<FutureBranch> result, AiItemObservation info, FutureState state,
            List<int> possible, bool self, int effects, double probability, int peeked)
        {
            TimeAction action = FutureAction(info, state, possible, self, effects);
            foreach (int index in possible)
            {
                var settlement = FutureCalculate(info, state, state.Clocks[index].Face, action, self, effects);
                var remaining = new FutureClock[state.Clocks.Length - 1];
                int next = 0;
                for (int i = 0; i < state.Clocks.Length; i++)
                    if (i != index) remaining[next++] = new FutureClock(state.Clocks[i].Face, state.Clocks[i].Known || i == peeked);
                var after = self
                    ? new FutureState(settlement.OwnLifeMinutes, settlement.OpponentLifeMinutes, settlement.BankMinutes, remaining)
                    : new FutureState(settlement.OpponentLifeMinutes, settlement.OwnLifeMinutes, settlement.BankMinutes, remaining);
                double reward = FutureUtility(info, state, settlement, self) * (self ? 1 : -1);
                result.Add(new FutureBranch(after, reward, probability / possible.Count));
            }
        }

        private static double FutureLine(AiItemObservation info, FutureState state, bool self, int depth)
        {
            if (state.Terminal) return 0;
            // 한 개가 남으면 공개하지 않고 폐기한다. 다음 묶음은 실제 값 대신 공개 생성 분포의 한 행동으로 근사한다.
            if (state.Clocks.Length <= 1) return .65 * FreshValue(info, state, self);
            double value = 0;
            foreach (var branch in FutureChoices(info, state, self, 0, false))
                value += branch.Probability * (branch.Reward + (depth > 0 && !branch.State.Terminal
                    ? .55 * FutureLine(info, branch.State, !self, depth - 1) : 0));
            return depth == 0 ? .65 * value : value;
        }

        private static double FutureBest(AiItemObservation info, FutureState state, List<int> possible, bool self, int effects)
        {
            if (possible.Count == 0) return NoChoice;
            return Math.Max(FutureActionValue(info, state, possible, TimeAction.Collect, self, effects),
                FutureActionValue(info, state, possible, TimeAction.Ration, self, effects));
        }

        private static TimeAction FutureAction(AiItemObservation info, FutureState state, List<int> possible, bool self, int effects) =>
            FutureActionValue(info, state, possible, TimeAction.Collect, self, effects) >
            FutureActionValue(info, state, possible, TimeAction.Ration, self, effects) ? TimeAction.Collect : TimeAction.Ration;

        private static double FutureActionValue(AiItemObservation info, FutureState state, List<int> possible, TimeAction action, bool self, int effects)
        {
            if (possible.Count == 0) return NoChoice;
            double value = 0;
            foreach (int i in possible)
                value += FutureUtility(info, state, FutureCalculate(info, state, state.Clocks[i].Face, action, self, effects), self);
            return value / possible.Count;
        }

        private static TimeSettlement FutureCalculate(AiItemObservation info, FutureState state, ClockFace face,
            TimeAction action, bool self, int effects) =>
            TimeTheftSettlement.Calculate(face, action, self ? state.Own : state.Other, self ? state.Other : state.Own, state.Bank,
                Has(effects, ItemKind.Screwdriver), Has(effects, ItemKind.TimeBypass),
                info.Strategy.DriverReductionPercent, info.Strategy.BypassPercent);

        private static double FutureUtility(AiItemObservation info, FutureState state, TimeSettlement result, bool self, double priorMinutes = -1)
        {
            int own = self ? state.Own : state.Other, other = self ? state.Other : state.Own;
            double terminal = Math.Max(2400, (double)own + other + state.Bank), risk = info.Strategy.RiskTolerance;
            if (result.OwnLifeMinutes == 0) return -terminal * (1.5 - .5 * risk);
            if (result.OpponentLifeMinutes == 0) return terminal * (1 + .25 * risk);
            double minutes = 0;
            foreach (var clock in state.Clocks) minutes += clock.Face.Hours * (double)GameTime.MinutesPerHour;
            double scale = priorMinutes >= 0 ? priorMinutes : state.Clocks.Length == 0 ? GameTime.MinutesPerHour : minutes / state.Clocks.Length;
            scale = Math.Min(scale, Math.Max(GameTime.MinutesPerHour, Math.Max(state.Bank, Math.Max(own, other))));
            double survival = 1 + (1 - risk) * .6 * Math.Min(1, scale / (own + 1d));
            return (result.OwnLifeMinutes - (double)own) * survival - (result.OpponentLifeMinutes - (double)other);
        }

        private static double FreshActionValue(AiItemObservation info, FutureState state, bool self, TimeAction action)
        {
            double value = 0, minutes = 0;
            foreach (var prior in info.Strategy.NextBatchClocks)
                minutes += prior.Probability * prior.Face.Hours * GameTime.MinutesPerHour;
            foreach (var prior in info.Strategy.NextBatchClocks)
                value += prior.Probability * FutureUtility(info, state,
                    FutureCalculate(info, state, prior.Face, action, self, 0), self, minutes);
            return value;
        }

        private static double FreshValue(AiItemObservation info, FutureState state, bool self) =>
            Math.Max(FreshActionValue(info, state, self, TimeAction.Collect),
                FreshActionValue(info, state, self, TimeAction.Ration)) * (self ? 1 : -1);

        private static double FreshContinuation(AiItemObservation info, FutureState state)
        {
            TimeAction action = FreshActionValue(info, state, true, TimeAction.Collect) >
                FreshActionValue(info, state, true, TimeAction.Ration) ? TimeAction.Collect : TimeAction.Ration;
            double probability = 0;
            foreach (var prior in info.Strategy.NextBatchClocks)
            {
                var result = FutureCalculate(info, state, prior.Face, action, true, 0);
                if (result.OwnLifeMinutes > 0 && result.OpponentLifeMinutes > 0) probability += prior.Probability;
            }
            return probability;
        }

        private static List<int> OneIndex(int index) => new List<int> { index };
        private static List<int> WithoutIndex(List<int> indices, int except)
        {
            var result = new List<int>();
            foreach (int i in indices) if (i != except) result.Add(i);
            return result;
        }
    }
}

