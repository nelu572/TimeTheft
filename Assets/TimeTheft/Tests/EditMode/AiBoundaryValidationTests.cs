using System;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class AiBoundaryValidationTests
    {
        private sealed class SplitRandom : Random
        {
            private readonly Random values, positions;
            public SplitRandom(int seed, int shuffleSeed) { values = new Random(seed); positions = new Random(shuffleSeed); }
            public override int Next(int maxValue) => positions.Next(maxValue);
            public override int Next(int minValue, int maxValue) => values.Next(minValue, maxValue);
            public override double NextDouble() => values.NextDouble();
        }

        // 1분 등 초기 설정으로 만들 수 없는 잔액은 무상태 정산기의 경계 입력으로 구분한다.
        [Test]
        public void SyntheticMinuteBoundariesConserveEveryAccountAndCapTheActualTransfer()
        {
            int checks = 0;
            foreach (int own in new[] { 1, 20, 61, 1200 })
            foreach (int opponent in new[] { 1, 59, 1200 })
            foreach (int hours in new[] { 1, 12 })
            foreach (int bank in new[] { 0, hours * 20 - 1, hours * 20, hours * 20 + 1, 1200 })
            foreach (ClockKind kind in Enum.GetValues(typeof(ClockKind)))
            foreach (TimeAction action in Enum.GetValues(typeof(TimeAction)))
            for (int mask = 0; mask < 4; mask++)
            foreach (var percentages in new[] { new[] { 0, 0 }, new[] { 37, 29 }, new[] { 50, 50 }, new[] { 100, 100 } })
            {
                bool driver = (mask & 1) != 0, bypass = (mask & 2) != 0;
                var result = TimeTheftSettlement.Calculate(new ClockFace(hours, kind), action, own, opponent, bank,
                    driver, bypass, percentages[0], percentages[1]);
                Assert.That((long)result.OwnLifeMinutes + result.OpponentLifeMinutes + result.BankMinutes,
                    Is.EqualTo((long)own + opponent + bank));
                Assert.That(result.OwnLifeMinutes, Is.GreaterThanOrEqualTo(0));
                Assert.That(result.OpponentLifeMinutes, Is.GreaterThanOrEqualTo(0));
                Assert.That(result.BankMinutes, Is.GreaterThanOrEqualTo(0));
                if (action == TimeAction.Ration)
                {
                    Assert.That(result.TransferredMinutes, Is.EqualTo(Math.Min(hours * 20, bank)));
                    Assert.That(result.PreventedMinutes + result.BypassedMinutes, Is.Zero);
                    Assert.That(result.DriverTriggered || result.BypassTriggered, Is.False);
                }
                else
                {
                    int available = Math.Min(hours * 60, kind == ClockKind.Red ? own : opponent);
                    Assert.That(result.TransferredMinutes + result.PreventedMinutes, Is.EqualTo(available));
                    Assert.That(result.BypassedMinutes, Is.InRange(0, result.TransferredMinutes));
                    Assert.That(result.BankMinutes - bank + result.BypassedMinutes, Is.EqualTo(result.TransferredMinutes));
                    Assert.That(result.DriverTriggered, Is.EqualTo(driver && kind == ClockKind.Red));
                    Assert.That(result.BypassTriggered, Is.EqualTo(bypass));
                    if (kind == ClockKind.Red) Assert.That(own - result.OwnLifeMinutes, Is.EqualTo(result.TransferredMinutes));
                    else Assert.That(opponent - result.OpponentLifeMinutes, Is.EqualTo(result.TransferredMinutes));
                }
                checks++;
            }
            TestContext.WriteLine("SyntheticSettlementBoundaries=" + checks);
        }

        [Test]
        public void ReachableMatchesAgreeWithAiPredictionAcrossLowLifeBankBoundariesAndKnownLastTwo()
        {
            int checks = 0;
            foreach (Participant actor in Enum.GetValues(typeof(Participant)))
            foreach (int life in new[] { 11, 13, 30 })
            foreach (int hours in new[] { 1, 7, 12 })
            foreach (float redProbability in new[] { 0f, 1f })
            foreach (int prefix in new[] { 0, 3, 7 })
            for (int mask = 0; mask < 4; mask++)
            foreach (TimeAction action in Enum.GetValues(typeof(TimeAction)))
            {
                // prefix가 홀수면 반대 참가자부터 시작해 검사할 A의 소유자를 맞춘다.
                var rules = new GameRules { InitialLifeHours = life, MinimumHours = hours, MaximumHours = hours,
                    FirstParticipant = prefix % 2 == 0 ? actor : Other(actor), ExtraRedProbability = redProbability,
                    InitialItemCount = 0, ItemsPerBatch = 0, ScrewdriverReductionPercent = 37, TimeBypassPercent = 29 };
                var match = new TimeTheftMatch(rules, new Random(1600 + checks), new Random(733));
                int key = TimeTheftItemTestSetup.Give(match, actor, ItemKind.SpareKey);
                int driver = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
                int wire = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
                StartBatch(match);
                for (int step = 0; step < prefix; step++)
                {
                    ActRation(match);
                    if (match.Stage == TurnStage.BatchComplete) StartBatch(match);
                }
                Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
                Assert.That(match.UseItem(actor, key).Succeeded, Is.True);
                if ((mask & 1) != 0) Assert.That(match.UseItem(actor, driver).Succeeded, Is.True);
                if ((mask & 2) != 0) Assert.That(match.UseItem(actor, wire).Succeeded, Is.True);
                if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(actor), Is.True);
                int index = match.GetAvailableClockIndices().Last();
                Assert.That(match.SelectClock(actor, index), Is.True);
                Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
                var face = match.GetPrivateClock(actor, index).Value;
                Assert.That(match.GetPrivateClock(Other(actor), index), Is.Null);
                Assert.That(match.KeepPeekedClock(actor), Is.True);
                var estimate = TimeTheftAi.Estimate(match.ObserveItems(actor), face, action);
                Assert.That(match.CommitAction(actor, action), Is.True);
                Assert.That(match.Settle(), Is.True);
                Assert.That(estimate.OwnLifeMinutes, Is.EqualTo(actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes));
                Assert.That(estimate.OpponentLifeMinutes, Is.EqualTo(actor == Participant.Player ? match.AiLifeMinutes : match.PlayerLifeMinutes));
                Assert.That(estimate.BankMinutes, Is.EqualTo(match.BankMinutes));
                Assert.That(estimate.TransferredMinutes, Is.EqualTo(match.TransferredMinutes));
                Assert.That(estimate.TransferParticipantIsSelf, Is.EqualTo(match.TransferParticipant == actor));
                Assert.That(estimate.PreventedMinutes, Is.EqualTo(match.LastItemEffects.Where(e => e.Source.Kind == ItemKind.Screwdriver).Sum(e => e.PreventedMinutes)));
                Assert.That(estimate.BypassedMinutes, Is.EqualTo(match.LastItemEffects.Where(e => e.Source.Kind == ItemKind.TimeBypass).Sum(e => e.BypassedMinutes)));
                Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(life * 120));
                Assert.That(match.GetPrivateClock(actor, index), Is.Null, "사용한 시계의 비공개 지식은 지운다");
                Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
                checks++;
            }
            TestContext.WriteLine("ReachablePredictionComparisons=" + checks);
        }

        [Test]
        public void SeededPublicEquivalenceCannotUseHiddenPositionsOrOpponentKinds()
        {
            int distinctHiddenLayouts = 0;
            for (int seed = 0; seed < 32; seed++)
            {
                var rules = new GameRules { InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = Participant.Ai,
                    RevealOpponentItems = false, MinimumHours = 1, MaximumHours = 12 };
                var first = new TimeTheftMatch(rules, new SplitRandom(seed, seed + 1000), new Random(42));
                var second = new TimeTheftMatch(rules, new SplitRandom(seed, seed + 9000), new Random(42));
                foreach (var match in new[] { first, second })
                    foreach (var kind in new[] { ItemKind.SpareKey, ItemKind.Scissors, ItemKind.RubberDuck })
                        TimeTheftItemTestSetup.Give(match, Participant.Ai, kind);
                foreach (var kind in new[] { ItemKind.SpareKey, ItemKind.Screwdriver, ItemKind.TimeBypass })
                    TimeTheftItemTestSetup.Give(first, Participant.Player, kind);
                foreach (var kind in new[] { ItemKind.Handcuffs, ItemKind.Handcuffs, ItemKind.Handcuffs })
                    TimeTheftItemTestSetup.Give(second, Participant.Player, kind);
                StartBatch(first); StartBatch(second);
                var one = first.ObserveItems(Participant.Ai); var two = second.ObserveItems(Participant.Ai);
                Assert.That(one.RemainingFaces, Is.EqualTo(two.RemainingFaces));
                Assert.That(one.OpponentInventory.Kinds, Is.Empty); Assert.That(two.OpponentInventory.Kinds, Is.Empty);
                Assert.That(TimeTheftAi.ChooseItem(one, new Random(seed)), Is.EqualTo(TimeTheftAi.ChooseItem(two, new Random(seed))));
                Assert.That(first.EndItemPhase(Participant.Ai) && second.EndItemPhase(Participant.Ai), Is.True);
                int a = TimeTheftAi.SelectClock(first.ObserveItems(Participant.Ai), new Random(seed + 1));
                int b = TimeTheftAi.SelectClock(second.ObserveItems(Participant.Ai), new Random(seed + 1));
                Assert.That(a, Is.EqualTo(b));
                Assert.That(first.SelectClock(Participant.Ai, a) && second.SelectClock(Participant.Ai, b), Is.True);
                Assert.That(TimeTheftAi.Decide(first.ObserveItems(Participant.Ai), new Random(seed + 2)),
                    Is.EqualTo(TimeTheftAi.Decide(second.ObserveItems(Participant.Ai), new Random(seed + 2))));
                Assert.That(first.CommitAction(Participant.Ai, TimeAction.Ration) && second.CommitAction(Participant.Ai, TimeAction.Ration), Is.True);
                if (!first.GetClock(a).Face.Value.Equals(second.GetClock(b).Face.Value)) distinctHiddenLayouts++;
            }
            Assert.That(distinctHiddenLayouts, Is.GreaterThan(0), "같은 공개 정보라도 숨긴 물리 배치는 실제로 다르다");
        }

        private static Participant Other(Participant actor) => actor == Participant.Player ? Participant.Ai : Participant.Player;
        private static void StartBatch(TimeTheftMatch match)
        { Assert.That(match.BeginBatch() && match.CloseAndShuffle() && match.CompleteShuffle(), Is.True); }
        private static void ActRation(TimeTheftMatch match)
        {
            var actor = match.ActiveParticipant;
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(actor), Is.True);
            Assert.That(match.SelectClock(actor, match.GetAvailableClockIndices()[0]), Is.True);
            Assert.That(match.CommitAction(actor, TimeAction.Ration) && match.Settle(), Is.True);
            Assert.That(match.CompleteTurn(), Is.True);
        }
    }
}
