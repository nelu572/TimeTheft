using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace TimeTheft.Tests
{
    // 렌더링 없이 제품 Match 명령을 실행하는 검증 전용 상대 정책과 관측기다.
    public sealed class AiBroadSimulationTests
    {
        private const int FirstSeed = 20000, SeedCount = 200, MaxActions = 500, MaxCommands = 12000;
        private enum OpponentPolicy { Current, RandomLegal, ImmediateEV, Conservative, Aggressive }
        private static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly MethodInfo Plan = typeof(TimeTheftAi).GetMethod("PlanValue", PrivateStatic);
        private static readonly MethodInfo Active = typeof(TimeTheftAi).GetMethod("ActiveEffects", PrivateStatic);
        private static readonly MethodInfo Cost = typeof(TimeTheftAi).GetMethod("PreservationCost", PrivateStatic);
        private static readonly MethodInfo Tempo = typeof(TimeTheftAi).GetMethod("TempoValue", PrivateStatic);
        private static readonly MethodInfo Scale = typeof(TimeTheftAi).GetMethod("Scale", PrivateStatic);

        [Serializable] private sealed class Side
        {
            public int[] grants = new int[6], uses = new int[6], removed = new int[6], unused = new int[6], noTrigger = new int[6], cancelled = new int[6], endedAWithUsableHeld = new int[6];
            public int driverRation, driverNormal, driverZero, bypassRation, bypassZero;
            public int duckBlocksEmpty, duckBlocksOccupied, bypassNormalCollections, bypassRedCollections;
            public long bypassNormalMinutes, bypassRedMinutes;
        }
        [Serializable] private sealed class Example
        {
            public string category, opponent, actor, item, possiblePlanBeforeNoise, alternativeForBestPlan, faces, ownItems, opponentItems, pending;
            public int seed, first, batch, turn, remaining, ownLife, otherLife, bank;
            public double baseline, standalone, bestPlanScore, alternativeScoreForBestPlan, bestPlanMarginalNet, bestPlanMarginalGross;
            public double maxMarginalNetAcrossStartingPlans, maxMarginalGrossAcrossStartingPlans, preservationCost, eligibilityThreshold, noiseAmplitude;
        }
        [Serializable] private sealed class Counter { public string name; public int count; }
        [Serializable] private sealed class Range { public double mean, median; public int min, max; }
        [Serializable] private sealed class PolicySummary
        {
            public string policy, description;
            public int games, completed, currentWins, currentFirstGames, currentFirstWins, currentSecondGames, currentSecondWins, firstWins;
            public int illegalCommands, stalled, predictionMismatch, settlements, skippedTurns, blockedTurns, emptyDuckBlocks, occupiedDuckBlocks, maxConsecutiveActor;
            public int currentEmptyDuckBlocks, opponentEmptyDuckBlocks, currentOccupiedDuckBlocks, opponentOccupiedDuckBlocks;
            public int gamesWith3PlusConsecutive, gamesWith4PlusConsecutive;
            public Range actions, turns, batches;
            public Side current, opponent;
            public Counter[] combinations, suspicions;
        }
        [Serializable] private sealed class Report
        {
            public string simulation = "AiBroadValidation", note = "Current is Player; opponent policies are validation-only. Outcomes are fixed-seed samples, not balance conclusions. possiblePlanBeforeNoise is the highest scoring feasible explanation for the first item; the RNG-selected internal plan is not exposed. Maximum marginal fields consider all feasible plans starting with that item.";
            public int firstSeed = FirstSeed, seedCount = SeedCount, games;
            public PolicySummary[] policies;
            public Example[] examples;
        }
        private sealed class Game
        {
            public int seed, actions, commands, settlements, mismatch, illegal, skipped, blocked, emptyBlocks, occupiedBlocks, maxConsecutive;
            public Participant first;
            public OpponentPolicy policy;
            public TimeTheftMatch match;
            public bool completed;
            public string fault;
            public readonly Side[] sides = { new Side(), new Side() };
            public readonly Dictionary<string, int> combinations = new Dictionary<string, int>(), suspicions = new Dictionary<string, int>();
            public readonly HashSet<string> events = new HashSet<string>();
            public readonly List<Example> examples = new List<Example>();
            public ulong trace = 14695981039346656037UL;
            public void Hash(int n) { unchecked { trace = (trace ^ (uint)n) * 1099511628211UL; } }
        }

        [Test]
        public void TwoThousandFixedSeedGamesCoverFiveOpponentPoliciesAndAuditPublicDecisions()
        {
            var games = new List<Game>();
            foreach (OpponentPolicy policy in Enum.GetValues(typeof(OpponentPolicy)))
                for (int seed = FirstSeed; seed < FirstSeed + SeedCount; seed++)
                    foreach (Participant first in new[] { Participant.Player, Participant.Ai }) games.Add(Play(seed, first, policy));
            var report = new Report { games = games.Count,
                policies = Enum.GetValues(typeof(OpponentPolicy)).Cast<OpponentPolicy>().Select(p => Summarize(games.Where(g => g.policy == p).ToArray(), p)).ToArray(),
                examples = games.SelectMany(g => g.examples).GroupBy(e => e.category).SelectMany(g => g.Take(g.Key == "preservedPositiveStandalone" ? 5 : 20)).ToArray() };
            string json = JsonUtility.ToJson(report, true);
            string folder = Path.GetFullPath("Temp/AiBroadValidation");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "broad-simulation-2000.json"), json);
            TestContext.WriteLine(json);
            Assert.That(games.Count, Is.EqualTo(2000));
            Assert.That(games.Sum(g => g.illegal), Is.Zero, string.Join("; ", games.Where(g => g.illegal > 0).Take(10).Select(g => g.seed + "/" + g.policy + "/" + g.first + ":" + g.fault)));
            Assert.That(games.Sum(g => g.mismatch), Is.Zero, "실제 정산과 공개 정보 기반 예측 정산 불일치");
            Assert.That(games.All(g => g.completed), Is.True, string.Join("; ", games.Where(g => !g.completed).Take(10).Select(g => g.seed + "/" + g.policy + ":" + g.fault)));
        }

        [Test]
        public void EveryOpponentPolicyReplaysItsCommandsAndResultsFromFreshMatches()
        {
            foreach (OpponentPolicy policy in Enum.GetValues(typeof(OpponentPolicy)))
                foreach (int seed in new[] { FirstSeed, FirstSeed + 31, FirstSeed + 199 })
                    foreach (Participant first in new[] { Participant.Player, Participant.Ai })
                    {
                        var a = Play(seed, first, policy); var b = Play(seed, first, policy);
                        Assert.That(a.completed && b.completed, Is.True);
                        Assert.That(a.illegal + b.illegal + a.mismatch + b.mismatch, Is.Zero);
                        Assert.That(b.trace, Is.EqualTo(a.trace), seed + "/" + first + "/" + policy);
                        Assert.That(b.match.Winner, Is.EqualTo(a.match.Winner));
                    }
        }

        private static Game Play(int seed, Participant first, OpponentPolicy policy)
        {
            var rules = new GameRules { FirstParticipant = first };
            var match = new TimeTheftMatch(rules, new System.Random(seed), new System.Random(seed ^ 0x2471));
            var game = new Game { seed = seed, first = first, policy = policy, match = match };
            var rng = new[] { new System.Random(seed ^ 0x51a7), new System.Random(seed ^ 0x93d1) };
            var usedIds = new HashSet<int>();
            var turnKinds = new List<ItemKind>();
            int trackedTurn = match.Turn, streak = 0;
            Participant? previousActor = null;
            try
            {
                while (match.Stage != TurnStage.GameOver && game.actions < MaxActions && game.commands < MaxCommands)
                {
                    Invariants(match, rules);
                    if (trackedTurn != match.Turn) { turnKinds.Clear(); trackedTurn = match.Turn; }
                    game.commands++; game.Hash((int)match.Stage); game.Hash(match.Turn); game.Hash((int)match.ActiveParticipant);
                    var actor = match.ActiveParticipant; var random = rng[(int)actor];
                    var selectedPolicy = actor == Participant.Player ? OpponentPolicy.Current : policy;
                    switch (match.Stage)
                    {
                        case TurnStage.BatchComplete:
                            Require(match.BeginBatch(), "BeginBatch");
                            foreach (var grant in match.LastItemGrants) foreach (var item in grant.GrantedItems) game.sides[(int)item.Owner].grants[(int)item.Kind]++;
                            break;
                        case TurnStage.BatchPreview: Require(match.CloseAndShuffle(), "CloseAndShuffle"); break;
                        case TurnStage.Shuffling: Require(match.CompleteShuffle(), "CompleteShuffle"); CountEvents(game); break;
                        case TurnStage.AwaitingItems:
                        {
                            var info = match.ObserveItems(actor);
                            int? id = ChooseItem(info, selectedPolicy, random);
                            if (!id.HasValue)
                            {
                                foreach (var held in info.OwnItems) if (info.UsableItemIds.Contains(held.Id)) game.sides[(int)actor].endedAWithUsableHeld[(int)held.Kind]++;
                                if (selectedPolicy == OpponentPolicy.Current) AuditPreservation(game, info);
                                game.Hash(-1); Require(match.EndItemPhase(actor), "EndItemPhase"); break;
                            }
                            Require(info.UsableItemIds.Contains(id.Value) && usedIds.Add(id.Value), "unavailable/repeated ItemId " + id);
                            var item = info.OwnItems.Single(i => i.Id == id.Value);
                            if (selectedPolicy == OpponentPolicy.Current) AuditUse(game, actor, info, item.Kind);
                            game.Hash(id.Value);
                            var use = match.UseItem(actor, id.Value); Require(use.Succeeded, "UseItem " + use.Failure);
                            game.sides[(int)actor].uses[(int)item.Kind]++; turnKinds.Add(item.Kind);
                            Require(!match.GetInventory(actor).Any(i => i.Id == id), "consumed item remains held");
                            if (use.RemovedItem.HasValue)
                            {
                                var removed = use.RemovedItem.Value;
                                game.sides[(int)removed.Owner].removed[(int)removed.Kind]++;
                                Require(!match.GetInventory(removed.Owner).Any(i => i.Id == removed.Id), "cut item remains held");
                            }
                            break;
                        }
                        case TurnStage.AwaitingClock:
                        {
                            var info = match.ObserveItems(actor);
                            int index = selectedPolicy == OpponentPolicy.RandomLegal ? info.AvailableClockIndices[random.Next(info.AvailableClockIndices.Count)]
                                : selectedPolicy == OpponentPolicy.ImmediateEV ? SelectClockEV(info, random) : TimeTheftAi.SelectClock(info, random);
                            game.Hash(index); Require(info.AvailableClockIndices.Contains(index) && match.SelectClock(actor, index), "SelectClock"); break;
                        }
                        case TurnStage.AwaitingKeyDecision:
                        {
                            var info = match.ObserveItems(actor);
                            int index = selectedPolicy == OpponentPolicy.RandomLegal ? info.AvailableClockIndices[random.Next(info.AvailableClockIndices.Count)]
                                : selectedPolicy == OpponentPolicy.ImmediateEV ? SelectClockEV(info, random) : TimeTheftAi.ChooseKeyClock(info, random);
                            game.Hash(index);
                            Require(index == match.CurrentClock ? match.KeepPeekedClock(actor) : match.ReselectPeekedClock(actor, index), "Key choice"); break;
                        }
                        case TurnStage.AwaitingAction:
                        {
                            var info = match.ObserveItems(actor);
                            TimeAction action = selectedPolicy == OpponentPolicy.RandomLegal ? (TimeAction)random.Next(2)
                                : selectedPolicy == OpponentPolicy.ImmediateEV ? ActionEV(info, FacesFor(info, info.CurrentClock), random) : TimeTheftAi.Decide(info, random);
                            game.Hash((int)action); Require(match.CommitAction(actor, action), "CommitAction"); break;
                        }
                        case TurnStage.Revealing:
                        {
                            var before = match.ObserveItems(actor);
                            var face = match.GetClock(match.CurrentClock).Face.Value;
                            var prediction = TimeTheftAi.Estimate(before, face, match.Action);
                            Require(match.Settle(), "Settle"); game.actions++; game.settlements++;
                            bool same = prediction.OwnLifeMinutes == Life(match, actor) && prediction.OpponentLifeMinutes == Life(match, Other(actor)) &&
                                prediction.BankMinutes == match.BankMinutes && prediction.TransferredMinutes == match.TransferredMinutes &&
                                (prediction.TransferParticipantIsSelf ? actor : Other(actor)) == match.TransferParticipant;
                            foreach (var effect in match.LastItemEffects)
                            {
                                var side = game.sides[(int)effect.Source.Owner];
                                if (!effect.Triggered)
                                {
                                    side.noTrigger[(int)effect.Source.Kind]++;
                                    if (effect.Source.Kind == ItemKind.Screwdriver) { if (match.Action == TimeAction.Ration) side.driverRation++; else side.driverNormal++; }
                                    if (effect.Source.Kind == ItemKind.TimeBypass) side.bypassRation++;
                                }
                                if (effect.Source.Kind == ItemKind.Screwdriver) { same &= effect.Triggered == prediction.DriverTriggered && effect.PreventedMinutes == prediction.PreventedMinutes; if (effect.Triggered && match.TransferredMinutes == 0) side.driverZero++; }
                                if (effect.Source.Kind == ItemKind.TimeBypass)
                                {
                                    same &= effect.Triggered == prediction.BypassTriggered && effect.BypassedMinutes == prediction.BypassedMinutes &&
                                        effect.BankReceivedMinutes == (prediction.BypassTriggered ? prediction.TransferredMinutes - prediction.BypassedMinutes : 0) &&
                                        effect.Recipient == (prediction.BypassTriggered ? (Participant?)Other(match.TransferParticipant) : null);
                                    if (effect.Triggered && effect.BypassedMinutes == 0) side.bypassZero++;
                                    if (effect.Triggered)
                                    {
                                        if (face.Kind == ClockKind.Normal) { side.bypassNormalCollections++; side.bypassNormalMinutes += effect.BypassedMinutes; }
                                        else { side.bypassRedCollections++; side.bypassRedMinutes += effect.BypassedMinutes; }
                                    }
                                }
                            }
                            if (!same) game.mismatch++;
                            game.Hash(match.PlayerLifeMinutes); game.Hash(match.AiLifeMinutes); game.Hash(match.BankMinutes);
                            if (turnKinds.Count >= 2) Increment(game.combinations, string.Join("+", turnKinds.OrderBy(k => k).Select(k => k.ToString())));
                            streak = previousActor == actor ? streak + 1 : 1; previousActor = actor; game.maxConsecutive = Math.Max(game.maxConsecutive, streak);
                            CountEvents(game); break;
                        }
                        case TurnStage.Settling: Require(match.CompleteTurn(), "CompleteTurn"); CountEvents(game); break;
                        default: throw new InvalidOperationException("unexpected stage " + match.Stage);
                    }
                }
                Invariants(match, rules);
                game.completed = match.Stage == TurnStage.GameOver;
                if (!game.completed) game.fault = "bounded simulation limit";
                foreach (var owner in new[] { Participant.Player, Participant.Ai })
                {
                    foreach (var item in match.GetInventory(owner)) game.sides[(int)owner].unused[(int)item.Kind]++;
                    var side = game.sides[(int)owner];
                    for (int i = 0; i < 6; i++) Require(side.grants[i] == side.uses[i] + side.removed[i] + side.unused[i], "item ledger " + owner + "/" + (ItemKind)i);
                    if (!game.completed) continue;
                    Require(match.GetCurrentItemEffects(owner).Count == 0 && match.GetPendingItemEffects(owner).Count == 0, "terminal effects remain");
                    for (int i = 0; i < 5; i++) Require(!match.GetPrivateClock(owner, i).HasValue, "terminal key knowledge remains");
                }
            }
            catch (Exception ex) { game.illegal++; game.fault = ex.ToString(); }
            return game;
        }

        private static int? ChooseItem(AiItemObservation info, OpponentPolicy policy, System.Random random)
        {
            if (policy == OpponentPolicy.Current) return TimeTheftAi.ChooseItem(info, random);
            if (policy == OpponentPolicy.Conservative || info.UsableItemIds.Count == 0) return null;
            if (policy == OpponentPolicy.RandomLegal) return random.Next(2) == 0 ? (int?)null : info.UsableItemIds[random.Next(info.UsableItemIds.Count)];
            if (policy == OpponentPolicy.Aggressive) return info.UsableItemIds[random.Next(info.UsableItemIds.Count)];
            // 즉시 기대값 상대는 미래 차례 견제/보존 가치를 추정하지 않는다.
            var faces = Unknown(info); int active = OwnActiveMask(info);
            double baseline = BestEV(info, faces, active), best = baseline + 1; int? result = null;
            foreach (var item in info.OwnItems)
            {
                if (!info.UsableItemIds.Contains(item.Id)) continue;
                double value = baseline;
                if (item.Kind == ItemKind.Screwdriver || item.Kind == ItemKind.TimeBypass) value = BestEV(info, faces, active | Bit(item.Kind));
                else if (item.Kind == ItemKind.SpareKey)
                {
                    value = 0;
                    for (int i = 0; i < faces.Count; i++) value += Math.Max(BestEV(info, new[] { faces[i] }, active), BestEV(info, faces.Where((f, j) => j != i).ToArray(), active));
                    value = faces.Count == 0 ? baseline : value / faces.Count;
                }
                if (value > best) { best = value; result = item.Id; }
            }
            return result;
        }

        private static int SelectClockEV(AiItemObservation info, System.Random random)
        {
            int active = OwnActiveMask(info); double best = double.NegativeInfinity; var choices = new List<int>();
            foreach (int index in info.AvailableClockIndices)
            {
                double value = BestEV(info, FacesFor(info, index), active);
                if (value > best + .0001) { best = value; choices.Clear(); choices.Add(index); }
                else if (Math.Abs(value - best) <= .0001) choices.Add(index);
            }
            return choices[random.Next(choices.Count)];
        }
        private static TimeAction ActionEV(AiItemObservation info, IReadOnlyList<ClockFace> faces, System.Random random)
        {
            int active = OwnActiveMask(info);
            double collect = EV(info, faces, active, TimeAction.Collect), ration = EV(info, faces, active, TimeAction.Ration);
            return Math.Abs(collect - ration) <= .0001 ? (TimeAction)random.Next(2) : collect > ration ? TimeAction.Collect : TimeAction.Ration;
        }
        private static double BestEV(AiItemObservation info, IReadOnlyList<ClockFace> faces, int mask) => Math.Max(EV(info, faces, mask, TimeAction.Collect), EV(info, faces, mask, TimeAction.Ration));
        private static double EV(AiItemObservation info, IReadOnlyList<ClockFace> faces, int mask, TimeAction action)
        {
            if (faces.Count == 0) return -1e100;
            double value = 0; var game = info.PublicGame;
            foreach (var face in faces)
            {
                var result = TimeTheftSettlement.Calculate(face, action, game.OwnLifeMinutes, game.OpponentLifeMinutes, game.BankMinutes,
                    Has(mask, ItemKind.Screwdriver), Has(mask, ItemKind.TimeBypass), info.Strategy.DriverReductionPercent, info.Strategy.BypassPercent);
                value += result.OwnLifeMinutes == 0 ? -100000 : result.OpponentLifeMinutes == 0 ? 100000 :
                    result.OwnLifeMinutes - game.OwnLifeMinutes - (result.OpponentLifeMinutes - game.OpponentLifeMinutes);
            }
            return value / faces.Count;
        }
        private static IReadOnlyList<ClockFace> FacesFor(AiItemObservation info, int index)
        {
            foreach (var known in info.KnownClocks) if (known.Index == index) return new[] { known.Face };
            return Unknown(info);
        }
        private static List<ClockFace> Unknown(AiItemObservation info)
        {
            var result = new List<ClockFace>(info.RemainingFaces);
            foreach (var known in info.KnownClocks)
            {
                int i = result.FindIndex(f => f.Kind == known.Face.Kind && f.Hours == known.Face.Hours);
                if (i >= 0) result.RemoveAt(i);
            }
            return result;
        }

        private static void AuditUse(Game game, Participant actor, AiItemObservation info, ItemKind chosen)
        {
            int active = (int)Active.Invoke(null, new object[] { info }), bit = Bit(chosen);
            double baseline = Score(info, active, 0), standalone = Score(info, active | bit, bit);
            int available = 0; foreach (var item in info.OwnItems) if (info.UsableItemIds.Contains(item.Id)) available |= Bit(item.Kind);
            var plans = new List<int> { bit };
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind))) if ((int)kind > (int)chosen && Has(available, kind)) plans.Add(bit | Bit(kind));
            int triple = Bit(ItemKind.SpareKey) | Bit(ItemKind.Screwdriver) | Bit(ItemKind.TimeBypass);
            if (chosen == ItemKind.SpareKey && (available & triple) == triple) plans.Add(triple);
            int bestMask = bit; double best = double.NegativeInfinity, bestAlternative = baseline, maxNet = double.NegativeInfinity;
            foreach (int plan in plans)
            {
                double value = Score(info, active | plan, plan), alternative = Score(info, active | (plan & ~bit), plan & ~bit);
                maxNet = Math.Max(maxNet, value - alternative);
                if (value > best) { best = value; bestAlternative = alternative; bestMask = plan; }
            }
            double preservation = (double)Cost.Invoke(null, new object[] { info, chosen });
            if (maxNet + preservation <= .0001) Record(game, actor, info, chosen, "zeroGrossContribution", baseline, standalone, best, bestAlternative, bestMask, maxNet, maxNet + preservation);
            else if (maxNet <= .0001) Record(game, actor, info, chosen, "nonPositiveNetContribution", baseline, standalone, best, bestAlternative, bestMask, maxNet, maxNet + preservation);
            if (chosen == ItemKind.Handcuffs && (double)Tempo.Invoke(null, new object[] { info }) <= 0)
                Record(game, actor, info, chosen, "nonPositiveHandcuffTempo", baseline, standalone, best, bestAlternative, bestMask, maxNet, maxNet + preservation);
        }
        private static void AuditPreservation(Game game, AiItemObservation info)
        {
            int active = (int)Active.Invoke(null, new object[] { info }); double baseline = Score(info, active, 0);
            foreach (var item in info.OwnItems)
            {
                if (!info.UsableItemIds.Contains(item.Id)) continue;
                double standalone = Score(info, active | Bit(item.Kind), Bit(item.Kind));
                if (standalone <= baseline + 1) continue;
                double preservation = (double)Cost.Invoke(null, new object[] { info, item.Kind });
                Record(game, game.match.ActiveParticipant, info, item.Kind, "preservedPositiveStandalone", baseline, standalone,
                    standalone, baseline, Bit(item.Kind), standalone - baseline, standalone - baseline + preservation);
            }
        }
        private static void Record(Game game, Participant actor, AiItemObservation info, ItemKind kind, string category,
            double baseline, double standalone, double score, double alternative, int mask, double net, double gross)
        {
            Increment(game.suspicions, category);
            if (game.examples.Count(e => e.category == category) >= 2) return;
            double preservation = (double)Cost.Invoke(null, new object[] { info, kind });
            double scale = (double)Scale.Invoke(null, new object[] { info });
            game.examples.Add(new Example { category = category, seed = game.seed, first = (int)game.first, opponent = game.policy.ToString(), actor = actor.ToString(),
                item = kind.ToString(), possiblePlanBeforeNoise = MaskName(mask), alternativeForBestPlan = MaskName(mask & ~Bit(kind)), baseline = baseline, standalone = standalone,
                bestPlanScore = score, alternativeScoreForBestPlan = alternative, bestPlanMarginalNet = score - alternative, bestPlanMarginalGross = score - alternative + preservation,
                maxMarginalNetAcrossStartingPlans = net, maxMarginalGrossAcrossStartingPlans = gross, preservationCost = preservation,
                eligibilityThreshold = Math.Max(1, scale * .01), noiseAmplitude = info.Strategy.DecisionNoise * scale * .08,
                batch = info.Batch, turn = info.Turn, remaining = info.RemainingFaces.Count, ownLife = info.PublicGame.OwnLifeMinutes,
                otherLife = info.PublicGame.OpponentLifeMinutes, bank = info.PublicGame.BankMinutes,
                faces = string.Join(",", info.RemainingFaces.Select(f => f.Kind + ":" + f.Hours)), ownItems = string.Join(",", info.OwnItems.Select(i => i.Kind + "#" + i.Id)),
                opponentItems = info.OpponentInventory.RevealsKinds ? string.Join(",", info.OpponentInventory.Kinds) : "hidden count=" + info.OpponentInventory.Count,
                pending = string.Join(",", info.OwnPendingEffects.Select(e => e.Source.Kind.ToString())) });
        }
        private static double Score(AiItemObservation info, int effects, int added) => (double)Plan.Invoke(null, new object[] { info, effects, added });
        private static string MaskName(int mask) => string.Join("+", Enum.GetValues(typeof(ItemKind)).Cast<ItemKind>().Where(k => Has(mask, k)).Select(k => k.ToString()));
        private static int Bit(ItemKind kind) => 1 << (int)kind;
        private static int OwnActiveMask(AiItemObservation info)
        {
            int mask = 0;
            foreach (var effect in info.OwnCurrentEffects)
                if (!effect.Source.IsFoundationTest && effect.RegisteredTurn == info.Turn) mask |= Bit(effect.Source.Kind);
            return mask;
        }
        private static bool Has(int mask, ItemKind kind) => (mask & Bit(kind)) != 0;
        private static int Life(TimeTheftMatch match, Participant actor) => actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes;
        private static Participant Other(Participant actor) => actor == Participant.Player ? Participant.Ai : Participant.Player;
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Increment(Dictionary<string, int> counts, string key) { counts.TryGetValue(key, out int value); counts[key] = value + 1; }
        private static void CountEvents(Game game)
        {
            foreach (var e in game.match.TurnItemEvents)
            {
                if (!game.events.Add(e.Source.Id + ":" + e.Kind)) continue;
                if (e.Kind == ItemTurnEventKind.TurnSkipped) game.skipped++;
                if (e.Kind == ItemTurnEventKind.ItemsBlocked)
                {
                    game.blocked++;
                    var source = game.sides[(int)e.Source.Owner];
                    if (game.match.GetInventory(e.Target).Count == 0) { game.emptyBlocks++; source.duckBlocksEmpty++; }
                    else { game.occupiedBlocks++; source.duckBlocksOccupied++; }
                }
                if (e.Kind == ItemTurnEventKind.Cancelled) game.sides[(int)e.Source.Owner].cancelled[(int)e.Source.Kind]++;
            }
        }
        private static void Invariants(TimeTheftMatch match, GameRules rules)
        {
            Require(match.PlayerLifeMinutes >= 0 && match.AiLifeMinutes >= 0 && match.BankMinutes >= 0, "negative balance");
            Require((long)match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes == rules.InitialLifeHours * 120L, "time conservation");
            Require(match.UsedCount >= 0 && match.UsedCount <= 4 && match.DiscardedCount >= 0 && match.DiscardedCount <= 1, "clock usage count");
            foreach (var actor in new[] { Participant.Player, Participant.Ai }) Require(match.GetInventory(actor).Count <= rules.MaxHeldItems, "inventory cap");
            if (match.Stage == TurnStage.BatchComplete && match.Batch > 0) Require(match.UsedCount == 4 && match.DiscardedCount == 1, "batch end count");
            if (match.Stage == TurnStage.AwaitingItems || match.Stage == TurnStage.AwaitingClock || match.Stage == TurnStage.AwaitingAction || match.Stage == TurnStage.AwaitingKeyDecision)
                for (int i = 0; i < 5; i++) Require(!match.GetClock(i).Face.HasValue, "hidden face exposure");
        }

        private static PolicySummary Summarize(Game[] games, OpponentPolicy policy)
        {
            return new PolicySummary { policy = policy.ToString(), description = policy == OpponentPolicy.Conservative ? "Never uses items; current clock/action policy" :
                policy == OpponentPolicy.ImmediateEV ? "Immediate own-minus-opponent minute EV; key/driver/bypass only, no future denial" :
                policy == OpponentPolicy.Aggressive ? "Uses random legal items until none, current clock/action policy" :
                policy == OpponentPolicy.RandomLegal ? "Random legal use/end, clock and action" : "Current vs current",
                games = games.Length, completed = games.Count(g => g.completed), currentWins = games.Count(g => g.match.Winner == GameWinner.Player),
                currentFirstGames = games.Count(g => g.first == Participant.Player), currentFirstWins = games.Count(g => g.first == Participant.Player && g.match.Winner == GameWinner.Player),
                currentSecondGames = games.Count(g => g.first == Participant.Ai), currentSecondWins = games.Count(g => g.first == Participant.Ai && g.match.Winner == GameWinner.Player),
                firstWins = games.Count(g => g.match.Winner == (g.first == Participant.Player ? GameWinner.Player : GameWinner.Ai)),
                illegalCommands = games.Sum(g => g.illegal), stalled = games.Count(g => !g.completed), predictionMismatch = games.Sum(g => g.mismatch), settlements = games.Sum(g => g.settlements),
                skippedTurns = games.Sum(g => g.skipped), blockedTurns = games.Sum(g => g.blocked), emptyDuckBlocks = games.Sum(g => g.emptyBlocks), occupiedDuckBlocks = games.Sum(g => g.occupiedBlocks),
                currentEmptyDuckBlocks = games.Sum(g => g.sides[0].duckBlocksEmpty), opponentEmptyDuckBlocks = games.Sum(g => g.sides[1].duckBlocksEmpty),
                currentOccupiedDuckBlocks = games.Sum(g => g.sides[0].duckBlocksOccupied), opponentOccupiedDuckBlocks = games.Sum(g => g.sides[1].duckBlocksOccupied),
                gamesWith3PlusConsecutive = games.Count(g => g.maxConsecutive >= 3), gamesWith4PlusConsecutive = games.Count(g => g.maxConsecutive >= 4),
                maxConsecutiveActor = games.Max(g => g.maxConsecutive), actions = Describe(games.Select(g => g.actions)), turns = Describe(games.Select(g => g.match.Turn)), batches = Describe(games.Select(g => g.match.Batch)),
                current = Combine(games.Select(g => g.sides[0])), opponent = Combine(games.Select(g => g.sides[1])),
                combinations = Counts(games.Select(g => g.combinations)), suspicions = Counts(games.Select(g => g.suspicions)) };
        }
        private static Range Describe(IEnumerable<int> sequence)
        {
            var a = sequence.OrderBy(n => n).ToArray();
            return new Range { mean = a.Average(), median = a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2d, min = a[0], max = a[a.Length - 1] };
        }
        private static Counter[] Counts(IEnumerable<Dictionary<string, int>> counts) => counts.SelectMany(c => c).GroupBy(p => p.Key).Select(g => new Counter { name = g.Key, count = g.Sum(p => p.Value) }).OrderByDescending(c => c.count).ToArray();
        private static Side Combine(IEnumerable<Side> sides)
        {
            var result = new Side();
            foreach (var side in sides)
            {
                for (int i = 0; i < 6; i++) { result.grants[i] += side.grants[i]; result.uses[i] += side.uses[i]; result.removed[i] += side.removed[i]; result.unused[i] += side.unused[i]; result.noTrigger[i] += side.noTrigger[i]; result.cancelled[i] += side.cancelled[i]; result.endedAWithUsableHeld[i] += side.endedAWithUsableHeld[i]; }
                result.driverRation += side.driverRation; result.driverNormal += side.driverNormal; result.driverZero += side.driverZero; result.bypassRation += side.bypassRation; result.bypassZero += side.bypassZero;
                result.duckBlocksEmpty += side.duckBlocksEmpty; result.duckBlocksOccupied += side.duckBlocksOccupied;
                result.bypassNormalCollections += side.bypassNormalCollections; result.bypassRedCollections += side.bypassRedCollections;
                result.bypassNormalMinutes += side.bypassNormalMinutes; result.bypassRedMinutes += side.bypassRedMinutes;
            }
            return result;
        }
    }
}
