using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class AiItemSimulationTests
    {
        private const int FirstSeed = 10000;
        private const int SeedCount = 64;
        private const int MaxActions = 500;
        private const int MaxCommands = 12000;

        private sealed class GameResult
        {
            public int Seed, Turns, Actions, Batches, Commands, IllegalCommands, MultiItemTurns;
            public int DriverBypassTurns, KeySettlementTurns, DuckHandcuffsTurns, SkippedTurns, BlockedTurns;
            public int CollectActions, RationActions, FinalBank, TotalMinutes, Capacity;
            public bool Completed;
            public Participant First;
            public GameWinner Winner;
            public string Fault;
            public ulong Trace = 14695981039346656037UL;
            public readonly int[] Uses = new int[6];
            public readonly int[] Unused = new int[6];
            public readonly int[] NonTriggered = new int[6];

            public string Fingerprint => Seed + ":" + First + ":" + Winner + ":" + Completed + ":" + Turns + ":" +
                Actions + ":" + Batches + ":" + Commands + ":" + Trace.ToString("x16") + ":" +
                string.Join(",", Uses) + ":" + string.Join(",", Unused) + ":" + string.Join(",", NonTriggered);

            public void Hash(int value)
            {
                unchecked { Trace = (Trace ^ (uint)value) * 1099511628211UL; }
            }

            public bool Command(bool accepted, string description)
            {
                if (accepted) return true;
                IllegalCommands++;
                Fault = description;
                return false;
            }
        }

        [Test]
        public void SeededSelfPlayCompletesWithoutIllegalCommandsOrBrokenBalances()
        {
            var games = new List<GameResult>();
            for (int seed = FirstSeed; seed < FirstSeed + SeedCount; seed++)
                foreach (Participant first in new[] { Participant.Player, Participant.Ai })
                    games.Add(Play(seed, first));

            TestContext.WriteLine(Summary(games));
            Assert.That(games.Sum(game => game.IllegalCommands), Is.Zero,
                string.Join("; ", games.Where(game => game.IllegalCommands != 0).Select(game => game.Seed + "/" + game.First + ": " + game.Fault)));
            Assert.That(games.Count(game => game.Completed), Is.EqualTo(SeedCount * 2),
                "종료하지 못한 경기: " + string.Join("; ", games.Where(game => !game.Completed)
                    .Select(game => game.Seed + "/" + game.First + ": " + game.Actions + " actions, " + game.Fault)));
            Assert.That(games.Sum(game => game.Uses.Sum()), Is.GreaterThan(0), "실제 전략이 지급품을 사용해야 한다");
        }

        [Test]
        public void ReplayingSeedsWithFreshMatchesReproducesCommandsAndResults()
        {
            foreach (int seed in new[] { FirstSeed, FirstSeed + 7, FirstSeed + 29, FirstSeed + 63 })
            foreach (Participant first in new[] { Participant.Player, Participant.Ai })
            {
                var firstRun = Play(seed, first);
                var replay = Play(seed, first);
                Assert.That(firstRun.Completed && replay.Completed, Is.True, seed + "/" + first);
                Assert.That(firstRun.IllegalCommands + replay.IllegalCommands, Is.Zero);
                Assert.That(replay.Fingerprint, Is.EqualTo(firstRun.Fingerprint), "시계/아이템/참가자 판단 RNG를 같은 시드로 재생한다");
            }
        }

        private static GameResult Play(int seed, Participant first)
        {
            // 제품 기본 지급량/상한/분포/경감률/우회율을 바꾸지 않는 양측 동일 전략 경기다.
            var rules = new GameRules { FirstParticipant = first };
            var match = new TimeTheftMatch(rules, new Random(seed), new Random(seed ^ 0x2471));
            var decisionRandom = new[] { new Random(seed ^ 0x51a7), new Random(seed ^ 0x93d1) };
            int total = rules.InitialLifeHours * 2 * GameTime.MinutesPerHour;
            var result = new GameResult { Seed = seed, First = first, TotalMinutes = total, Capacity = rules.MaxHeldItems };
            var usedIds = new HashSet<int>();
            var turnKinds = new HashSet<ItemKind>();
            int trackedTurn = match.Turn;
            int turnUses = 0;
            AssertFreshMatch(match);

            while (match.Stage != TurnStage.GameOver && result.Actions < MaxActions && result.Commands < MaxCommands)
            {
                AssertInvariants(match, total, rules.MaxHeldItems);
                if (trackedTurn != match.Turn) { turnKinds.Clear(); turnUses = 0; trackedTurn = match.Turn; }
                result.Commands++;
                result.Hash((int)match.Stage); result.Hash(match.Turn); result.Hash((int)match.ActiveParticipant);
                Participant actor = match.ActiveParticipant;
                var random = decisionRandom[(int)actor];
                bool accepted;
                switch (match.Stage)
                {
                    case TurnStage.BatchComplete:
                        accepted = match.BeginBatch();
                        if (!result.Command(accepted, "BeginBatch")) return Finish(result, match);
                        break;
                    case TurnStage.BatchPreview:
                        accepted = match.CloseAndShuffle();
                        if (!result.Command(accepted, "CloseAndShuffle")) return Finish(result, match);
                        break;
                    case TurnStage.Shuffling:
                        accepted = match.CompleteShuffle();
                        if (!result.Command(accepted, "CompleteShuffle")) return Finish(result, match);
                        CountTurnEvents(match, result);
                        break;
                    case TurnStage.AwaitingItems:
                    {
                        var observation = match.ObserveItems(actor);
                        int? itemId = TimeTheftAi.ChooseItem(observation, random);
                        if (!itemId.HasValue)
                        {
                            result.Hash(-1);
                            if (!result.Command(match.EndItemPhase(actor), "EndItemPhase")) return Finish(result, match);
                            break;
                        }
                        result.Hash(itemId.Value);
                        if (!result.Command(observation.UsableItemIds.Contains(itemId.Value) && usedIds.Add(itemId.Value),
                            "AI repeated or selected an unavailable item ID " + itemId.Value)) return Finish(result, match);
                        ItemUseResult use = match.UseItem(actor, itemId.Value);
                        if (!result.Command(use.Succeeded, "UseItem " + itemId.Value + ": " + use.Failure)) return Finish(result, match);
                        ItemKind kind = use.Effect.Value.Source.Kind;
                        result.Uses[(int)kind]++;
                        turnUses++;
                        turnKinds.Add(kind);
                        Assert.That(match.GetInventory(actor).Any(item => item.Id == itemId.Value), Is.False);
                        if (use.RemovedItem.HasValue)
                            Assert.That(match.GetInventory(use.RemovedItem.Value.Owner).Any(item => item.Id == use.RemovedItem.Value.Id), Is.False);
                        break;
                    }
                    case TurnStage.AwaitingClock:
                    {
                        var observation = match.ObserveItems(actor);
                        int index = TimeTheftAi.SelectClock(observation, random);
                        result.Hash(index);
                        if (!result.Command(observation.AvailableClockIndices.Contains(index) && match.SelectClock(actor, index),
                            "SelectClock " + index)) return Finish(result, match);
                        break;
                    }
                    case TurnStage.AwaitingKeyDecision:
                    {
                        var observation = match.ObserveItems(actor);
                        int index = TimeTheftAi.ChooseKeyClock(observation, random);
                        result.Hash(index);
                        accepted = index == match.CurrentClock ? match.KeepPeekedClock(actor) : match.ReselectPeekedClock(actor, index);
                        if (!result.Command(accepted, "ChooseKeyClock " + index)) return Finish(result, match);
                        Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
                        break;
                    }
                    case TurnStage.AwaitingAction:
                    {
                        var action = TimeTheftAi.Decide(match.ObserveItems(actor), random);
                        result.Hash((int)action);
                        if (!result.Command(match.CommitAction(actor, action), "CommitAction " + action)) return Finish(result, match);
                        if (action == TimeAction.Collect) result.CollectActions++; else result.RationActions++;
                        break;
                    }
                    case TurnStage.Revealing:
                        if (!result.Command(match.Settle(), "Settle")) return Finish(result, match);
                        result.Actions++;
                        result.Hash(match.PlayerLifeMinutes); result.Hash(match.AiLifeMinutes); result.Hash(match.BankMinutes);
                        if (turnUses >= 2) result.MultiItemTurns++;
                        if (turnKinds.Contains(ItemKind.Screwdriver) && turnKinds.Contains(ItemKind.TimeBypass)) result.DriverBypassTurns++;
                        if (turnKinds.Contains(ItemKind.SpareKey) && (turnKinds.Contains(ItemKind.Screwdriver) || turnKinds.Contains(ItemKind.TimeBypass))) result.KeySettlementTurns++;
                        if (turnKinds.Contains(ItemKind.RubberDuck) && turnKinds.Contains(ItemKind.Handcuffs)) result.DuckHandcuffsTurns++;
                        foreach (var effect in match.LastItemEffects)
                            if (!effect.Triggered) result.NonTriggered[(int)effect.Source.Kind]++;
                        foreach (var itemEvent in match.TurnItemEvents)
                            if (itemEvent.Kind == ItemTurnEventKind.Cancelled) result.NonTriggered[(int)itemEvent.Source.Kind]++;
                        break;
                    case TurnStage.Settling:
                        if (!result.Command(match.CompleteTurn(), "CompleteTurn")) return Finish(result, match);
                        CountTurnEvents(match, result);
                        break;
                    default:
                        result.IllegalCommands++;
                        result.Fault = "Unexpected stage " + match.Stage;
                        return Finish(result, match);
                }
            }
            if (match.Stage != TurnStage.GameOver) result.Fault = "bounded simulation limit";
            return Finish(result, match);
        }

        private static GameResult Finish(GameResult result, TimeTheftMatch match)
        {
            result.Completed = match.Stage == TurnStage.GameOver;
            result.Winner = match.Winner; result.Turns = match.Turn; result.Batches = match.Batch; result.FinalBank = match.BankMinutes;
            foreach (var owner in new[] { Participant.Player, Participant.Ai })
            {
                foreach (var item in match.GetInventory(owner)) result.Unused[(int)item.Kind]++;
                if (!result.Completed) continue;
                Assert.That(match.GetCurrentItemEffects(owner), Is.Empty);
                Assert.That(match.GetPendingItemEffects(owner), Is.Empty);
                for (int i = 0; i < TimeTheftMatch.ClocksPerBatch; i++) Assert.That(match.GetPrivateClock(owner, i), Is.Null);
            }
            AssertInvariants(match, result.TotalMinutes, result.Capacity);
            return result;
        }

        private static void CountTurnEvents(TimeTheftMatch match, GameResult result)
        {
            foreach (var itemEvent in match.TurnItemEvents)
            {
                if (itemEvent.Kind == ItemTurnEventKind.TurnSkipped) result.SkippedTurns++;
                if (itemEvent.Kind == ItemTurnEventKind.ItemsBlocked) result.BlockedTurns++;
            }
        }

        private static void AssertFreshMatch(TimeTheftMatch match)
        {
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            Assert.That(match.Batch, Is.Zero); Assert.That(match.Turn, Is.EqualTo(1));
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(match.LastItemEffects, Is.Empty); Assert.That(match.LastItemGrants, Is.Empty); Assert.That(match.LastTransfers, Is.Empty);
            foreach (var owner in new[] { Participant.Player, Participant.Ai })
            {
                Assert.That(match.GetInventory(owner), Is.Empty);
                Assert.That(match.GetCurrentItemEffects(owner), Is.Empty); Assert.That(match.GetPendingItemEffects(owner), Is.Empty);
            }
        }

        private static void AssertInvariants(TimeTheftMatch match, int total, int capacity)
        {
            Assert.That(match.PlayerLifeMinutes, Is.GreaterThanOrEqualTo(0));
            Assert.That(match.AiLifeMinutes, Is.GreaterThanOrEqualTo(0));
            Assert.That(match.BankMinutes, Is.GreaterThanOrEqualTo(0));
            Assert.That((long)match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(total));
            Assert.That(match.UsedCount, Is.InRange(0, TimeTheftMatch.TurnsPerBatch));
            Assert.That(match.DiscardedCount, Is.InRange(0, 1));
            foreach (var owner in new[] { Participant.Player, Participant.Ai })
                Assert.That(match.GetInventory(owner).Count, Is.LessThanOrEqualTo(capacity));
            if (match.Stage == TurnStage.BatchComplete && match.Batch > 0)
            {
                Assert.That(match.UsedCount, Is.EqualTo(4)); Assert.That(match.DiscardedCount, Is.EqualTo(1));
            }
            if (match.Stage != TurnStage.AwaitingItems && match.Stage != TurnStage.AwaitingClock &&
                match.Stage != TurnStage.AwaitingAction && match.Stage != TurnStage.AwaitingKeyDecision) return;
            Assert.That(Enumerable.Range(0, TimeTheftMatch.ClocksPerBatch).All(i => !match.GetClock(i).Face.HasValue), Is.True);
        }

        private static string Summary(IReadOnlyList<GameResult> games)
        {
            int firstWins = games.Count(game => game.Completed &&
                (game.First == Participant.Player ? game.Winner == GameWinner.Player : game.Winner == GameWinner.Ai));
            var builder = new StringBuilder("{\"simulation\":\"AiItemSelfPlay\",\"firstSeed\":").Append(FirstSeed)
                .Append(",\"seedCount\":").Append(SeedCount).Append(",\"games\":").Append(games.Count)
                .Append(",\"completedGames\":").Append(games.Count(game => game.Completed))
                .Append(",\"completionPercent\":").Append((100.0 * games.Count(game => game.Completed) / games.Count).ToString("F2", CultureInfo.InvariantCulture))
                .Append(",\"stalledGames\":").Append(games.Count(game => !game.Completed))
                .Append(",\"illegalCommands\":").Append(games.Sum(game => game.IllegalCommands))
                .Append(",\"firstWins\":").Append(firstWins)
                .Append(",\"secondWins\":").Append(games.Count(game => game.Completed) - firstWins);
            AddRange(builder, "actions", games.Select(game => game.Actions));
            AddRange(builder, "turns", games.Select(game => game.Turns));
            AddRange(builder, "batches", games.Select(game => game.Batches));
            builder.Append(",\"collectActions\":").Append(games.Sum(game => game.CollectActions))
                .Append(",\"rationActions\":").Append(games.Sum(game => game.RationActions))
                .Append(",\"multiItemTurns\":").Append(games.Sum(game => game.MultiItemTurns))
                .Append(",\"driverBypassTurns\":").Append(games.Sum(game => game.DriverBypassTurns))
                .Append(",\"keySettlementTurns\":").Append(games.Sum(game => game.KeySettlementTurns))
                .Append(",\"duckHandcuffsTurns\":").Append(games.Sum(game => game.DuckHandcuffsTurns))
                .Append(",\"skippedTurns\":").Append(games.Sum(game => game.SkippedTurns))
                .Append(",\"itemBlockedTurns\":").Append(games.Sum(game => game.BlockedTurns))
                .Append(",\"meanFinalBankMinutes\":").Append(games.Average(game => game.FinalBank).ToString("F2", CultureInfo.InvariantCulture));
            AddKinds(builder, "itemUses", games.Select(game => game.Uses));
            AddKinds(builder, "unusedItems", games.Select(game => game.Unused));
            AddKinds(builder, "nonTriggered", games.Select(game => game.NonTriggered));
            return builder.Append('}').ToString();
        }

        private static void AddRange(StringBuilder builder, string name, IEnumerable<int> values)
        {
            var array = values.ToArray();
            builder.Append(",\"").Append(name).Append("\":{\"mean\":")
                .Append(array.Average().ToString("F2", CultureInfo.InvariantCulture))
                .Append(",\"min\":").Append(array.Min()).Append(",\"max\":").Append(array.Max()).Append('}');
        }

        private static void AddKinds(StringBuilder builder, string name, IEnumerable<int[]> counts)
        {
            var arrays = counts.ToArray();
            builder.Append(",\"").Append(name).Append("\":{");
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append('"').Append((ItemKind)i).Append("\":").Append(arrays.Sum(array => array[i]));
            }
            builder.Append('}');
        }
    }
}
