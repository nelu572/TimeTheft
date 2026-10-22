using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class MatchRulesTests
    {
        [TestCase(ClockAction.Keep, ClockAction.Keep, false)]
        [TestCase(ClockAction.Keep, ClockAction.Swap, true)]
        [TestCase(ClockAction.Swap, ClockAction.Keep, true)]
        [TestCase(ClockAction.Swap, ClockAction.Swap, false)]
        public void ExchangeOccursOnlyWhenOneSideSwaps(ClockAction player, ClockAction ai, bool exchange)
        {
            var match = AtDecision(30, 2, 9, ai);
            Assert.That(match.CommitAction(player), Is.True);
            Assert.That(match.AiAction, Is.EqualTo(ai));
            Assert.That(match.DidExchange, Is.EqualTo(exchange));
            Assert.That(match.ResolveExchange(), Is.True);
            Assert.That(match.PlayerClock, Is.EqualTo(exchange ? 1 : 0));
            Assert.That(match.AiClock, Is.EqualTo(exchange ? 0 : 1));
            Assert.That(match.DiscardedClock, Is.EqualTo(2));

            Assert.That(match.Settle(), Is.True);
            Assert.That(match.PlayerLoss, Is.EqualTo(exchange ? 9 : 2));
            Assert.That(match.AiLoss, Is.EqualTo(exchange ? 2 : 9));
            Assert.That(match.PlayerLife, Is.EqualTo(exchange ? 21 : 28));
            Assert.That(match.AiLife, Is.EqualTo(exchange ? 28 : 21));
            Assert.That(match.Pot, Is.EqualTo(11));
        }

        [TestCase(5, 12, 2, 5, 2)]
        [TestCase(5, 1, 12, 1, 5)]
        [TestCase(5, 12, 12, 5, 5)]
        [TestCase(30, 1, 12, 1, 12)]
        public void LossUsesFinalClockAndCannotExceedLife(int life, int playerValue, int aiValue,
            int playerLoss, int aiLoss)
        {
            var match = AtDecision(life, playerValue, aiValue, ClockAction.Keep);
            Settle(match, ClockAction.Keep);
            Assert.That(match.PlayerLoss, Is.EqualTo(playerLoss));
            Assert.That(match.AiLoss, Is.EqualTo(aiLoss));
            Assert.That(match.PlayerLife, Is.EqualTo(life - playerLoss));
            Assert.That(match.AiLife, Is.EqualTo(life - aiLoss));
            Assert.That(match.Pot, Is.EqualTo(playerLoss + aiLoss));
        }

        [TestCase(2, 12, MatchWinner.Player, 10, 0)]
        [TestCase(12, 2, MatchWinner.Ai, 0, 10)]
        public void SoleSurvivorReceivesEntirePot(int playerValue, int aiValue, MatchWinner winner,
            int finalPlayerLife, int finalAiLife)
        {
            var match = AtDecision(5, playerValue, aiValue, ClockAction.Keep);
            Settle(match, ClockAction.Keep);
            Assert.That(match.Winner, Is.EqualTo(winner));
            Assert.That(match.Pot, Is.EqualTo(7));
            Assert.That(match.Award, Is.Zero);
            Assert.That(match.FinishRound(), Is.True);
            Assert.That(match.Stage, Is.EqualTo(RoundStage.GameOver));
            Assert.That(match.PlayerLife, Is.EqualTo(finalPlayerLife));
            Assert.That(match.AiLife, Is.EqualTo(finalAiLife));
            Assert.That(match.Award, Is.EqualTo(7));
            Assert.That(match.Pot, Is.Zero);
            RejectWithoutMutation(match, () => match.FinishRound());
            RejectWithoutMutation(match, () => match.BeginRound());
        }

        [Test]
        public void SimultaneousEliminationIsDrawAndHasNoPayout()
        {
            var match = AtDecision(5, 5, 12, ClockAction.Keep);
            Settle(match, ClockAction.Keep);
            Assert.That(match.Winner, Is.EqualTo(MatchWinner.Draw));
            Assert.That(match.FinishRound(), Is.True);
            Assert.That(match.Stage, Is.EqualTo(RoundStage.GameOver));
            Assert.That(match.PlayerLife, Is.Zero);
            Assert.That(match.AiLife, Is.Zero);
            Assert.That(match.Award, Is.Zero);
            Assert.That(match.Pot, Is.EqualTo(10));
            RejectWithoutMutation(match, () => match.BeginRound());
        }

        [Test]
        public void EveryPlayerSelectionLeavesDifferentAiAndDiscardedClocks()
        {
            for (int player = 0; player < 3; player++)
            for (int aiChoice = 0; aiChoice < 2; aiChoice++)
            {
                var match = new TimeTheftMatch(new PrototypeRules(),
                    new ScriptedRandom(new[] { 1, 6, 12 }, aiChoice));
                Assert.That(match.BeginRound(), Is.True);
                Assert.That(match.OpenSelection(), Is.True);
                Assert.That(match.SelectClock(player), Is.True);
                Assert.That(match.PlayerClock, Is.EqualTo(player));
                CollectionAssert.AreEquivalent(new[] { 0, 1, 2 },
                    new[] { match.PlayerClock, match.AiClock, match.DiscardedClock });
            }
        }

        [Test]
        public void GeneratedValuesStayInInclusiveOneToTwelveRange()
        {
            var rules = new PrototypeRules();
            var random = new Random(261022);
            var seen = new HashSet<int>();
            for (int i = 0; i < 2000; i++)
            {
                int[] values = PrototypePolicy.GenerateClocks(rules, random);
                Assert.That(values, Has.Length.EqualTo(3));
                foreach (int value in values)
                {
                    Assert.That(value, Is.InRange(1, 12));
                    seen.Add(value);
                }
            }
            Assert.That(seen.Count, Is.EqualTo(12));
        }

        [Test]
        public void FixedConfiguredRangeIsRespected()
        {
            var rules = new PrototypeRules { MinimumValue = 7, MaximumValue = 7 };
            CollectionAssert.AreEqual(new[] { 7, 7, 7 }, PrototypePolicy.GenerateClocks(rules, new Random(1)));
        }

        [TestCase(0, 1, 12)]
        [TestCase(30, 0, 12)]
        [TestCase(30, 1, 13)]
        [TestCase(30, 8, 7)]
        public void InvalidRuleBoundsAreRejected(int life, int minimum, int maximum)
        {
            Assert.Throws<ArgumentException>(() => new TimeTheftMatch(new PrototypeRules
            {
                InitialLife = life, MinimumValue = minimum, MaximumValue = maximum
            }, new Random(1)));
        }

        [Test]
        public void OutOfOrderDuplicateAndInvalidInputsDoNotMutateMatch()
        {
            var match = new TimeTheftMatch(new PrototypeRules(), new Random(481));
            RejectWithoutMutation(match, () => match.OpenSelection());
            RejectWithoutMutation(match, () => match.SelectClock(0));
            RejectWithoutMutation(match, () => match.OpenDecision());
            RejectWithoutMutation(match, () => match.CommitAction(ClockAction.Keep));
            RejectWithoutMutation(match, () => match.ResolveExchange());
            RejectWithoutMutation(match, () => match.Settle());
            RejectWithoutMutation(match, () => match.FinishRound());

            Assert.That(match.BeginRound(), Is.True);
            RejectWithoutMutation(match, () => match.BeginRound());
            RejectWithoutMutation(match, () => match.SelectClock(0));
            Assert.That(match.OpenSelection(), Is.True);
            RejectWithoutMutation(match, () => match.OpenSelection());
            RejectWithoutMutation(match, () => match.SelectClock(-1));
            RejectWithoutMutation(match, () => match.SelectClock(3));
            Assert.That(match.SelectClock(2), Is.True);
            RejectWithoutMutation(match, () => match.SelectClock(1));
            RejectWithoutMutation(match, () => match.CommitAction(ClockAction.Swap));
            Assert.That(match.OpenDecision(), Is.True);
            RejectWithoutMutation(match, () => match.OpenDecision());
            RejectWithoutMutation(match, () => match.CommitAction((ClockAction)2));
            RejectWithoutMutation(match, () => match.Settle());
            Assert.That(match.CommitAction(ClockAction.Keep), Is.True);
            RejectWithoutMutation(match, () => match.CommitAction(ClockAction.Swap));
            RejectWithoutMutation(match, () => match.FinishRound());
            Assert.That(match.ResolveExchange(), Is.True);
            RejectWithoutMutation(match, () => match.ResolveExchange());
            Assert.That(match.Settle(), Is.True);
            RejectWithoutMutation(match, () => match.Settle());
            Assert.That(match.FinishRound(), Is.True);
            RejectWithoutMutation(match, () => match.FinishRound());
        }

        [Test]
        public void RepeatedRoundsConserveAllLifeAndAlwaysTerminate()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var rules = new PrototypeRules();
                var match = new TimeTheftMatch(rules, new Random(seed));
                int lastPlayerLife = rules.InitialLife;
                int lastAiLife = rules.InitialLife;
                int lastPot = 0;
                while (match.Stage != RoundStage.GameOver)
                {
                    Assert.That(match.Round, Is.LessThan(rules.InitialLife), "seed " + seed);
                    Assert.That(match.BeginRound(), Is.True);
                    Assert.That(match.OpenSelection(), Is.True);
                    Assert.That(match.SelectClock(match.Round % 3), Is.True);
                    Assert.That(match.OpenDecision(), Is.True);
                    Settle(match, match.Round % 2 == 0 ? ClockAction.Keep : ClockAction.Swap);
                    Assert.That(match.PlayerLife, Is.InRange(0, lastPlayerLife - 1));
                    Assert.That(match.AiLife, Is.InRange(0, lastAiLife - 1));
                    Assert.That(match.Pot - lastPot,
                        Is.EqualTo(lastPlayerLife - match.PlayerLife + lastAiLife - match.AiLife));
                    Assert.That(match.PlayerLife + match.AiLife + match.Pot, Is.EqualTo(60));
                    lastPlayerLife = match.PlayerLife;
                    lastAiLife = match.AiLife;
                    lastPot = match.Pot;
                    Assert.That(match.FinishRound(), Is.True);
                    Assert.That(match.PlayerLife + match.AiLife + match.Pot, Is.EqualTo(60));
                }
                Assert.That(match.Winner, Is.Not.EqualTo(MatchWinner.None));
                if (match.Winner == MatchWinner.Draw)
                {
                    Assert.That(match.Award, Is.Zero);
                    Assert.That(match.Pot, Is.EqualTo(60));
                }
                else
                {
                    Assert.That(Math.Max(match.PlayerLife, match.AiLife), Is.EqualTo(60));
                    Assert.That(Math.Min(match.PlayerLife, match.AiLife), Is.Zero);
                    Assert.That(match.Award, Is.EqualTo(lastPot));
                }
                RejectWithoutMutation(match, () => match.BeginRound());
            }
        }

        [Test]
        public void AiDecisionDoesNotDependOnOtherHiddenClockValues()
        {
            var first = AtDecision(30, 1, 8, ClockAction.Keep, 2, 0.52);
            var second = AtDecision(30, 12, 8, ClockAction.Keep, 11, 0.52);
            Assert.That(first.CommitAction(ClockAction.Keep), Is.True);
            Assert.That(second.CommitAction(ClockAction.Keep), Is.True);
            Assert.That(first.AiAction, Is.EqualTo(second.AiAction));
        }

        [Test]
        public void AiIsStochasticAndMoreLikelyToSwapHighValues()
        {
            var rules = new PrototypeRules();
            var lowRandom = new Random(635);
            var highRandom = new Random(635);
            int lowSwaps = 0;
            int highSwaps = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (ClockAi.Decide(1, rules, lowRandom) == ClockAction.Swap) lowSwaps++;
                if (ClockAi.Decide(12, rules, highRandom) == ClockAction.Swap) highSwaps++;
            }
            Assert.That(lowSwaps, Is.InRange(1, 999));
            Assert.That(highSwaps, Is.InRange(1, 999));
            Assert.That(highSwaps, Is.GreaterThan(lowSwaps));
        }

        [Test]
        public void SameSeedAndInputsReproduceWholeMatch()
        {
            var rules = new PrototypeRules();
            var first = new TimeTheftMatch(rules, new Random(201026));
            var second = new TimeTheftMatch(rules, new Random(201026));
            do
            {
                PlayRound(first, first.Round % 3, first.Round % 2 == 0 ? ClockAction.Keep : ClockAction.Swap);
                PlayRound(second, second.Round % 3, second.Round % 2 == 0 ? ClockAction.Keep : ClockAction.Swap);
                Assert.That(first.DebugSnapshot(), Is.EqualTo(second.DebugSnapshot()));
            } while (first.Stage != RoundStage.GameOver);
        }

        private static TimeTheftMatch AtDecision(int life, int playerValue, int aiValue, ClockAction aiAction,
            int discardedValue = 4, double? draw = null)
        {
            var rules = new PrototypeRules { InitialLife = life };
            if (!draw.HasValue)
            {
                rules.AiSwapAtMinimum = rules.AiSwapAtMaximum = aiAction == ClockAction.Swap ? 1f : 0f;
            }
            var match = new TimeTheftMatch(rules,
                new ScriptedRandom(new[] { playerValue, aiValue, discardedValue }, 0, draw ?? 0.5));
            Assert.That(match.BeginRound(), Is.True);
            Assert.That(match.OpenSelection(), Is.True);
            Assert.That(match.SelectClock(0), Is.True);
            Assert.That(match.OpenDecision(), Is.True);
            return match;
        }

        private static void Settle(TimeTheftMatch match, ClockAction action)
        {
            Assert.That(match.CommitAction(action), Is.True);
            Assert.That(match.ResolveExchange(), Is.True);
            Assert.That(match.Settle(), Is.True);
        }

        private static void PlayRound(TimeTheftMatch match, int selectedIndex, ClockAction action)
        {
            Assert.That(match.BeginRound(), Is.True);
            Assert.That(match.OpenSelection(), Is.True);
            Assert.That(match.SelectClock(selectedIndex), Is.True);
            Assert.That(match.OpenDecision(), Is.True);
            Settle(match, action);
            Assert.That(match.FinishRound(), Is.True);
        }

        private static void RejectWithoutMutation(TimeTheftMatch match, Func<bool> operation)
        {
            string before = match.DebugSnapshot();
            Assert.That(operation(), Is.False);
            Assert.That(match.DebugSnapshot(), Is.EqualTo(before));
        }

        private sealed class ScriptedRandom : Random
        {
            private readonly Queue<int> values;
            private readonly int aiIndex;
            private readonly double decision;

            public ScriptedRandom(IEnumerable<int> values, int aiIndex, double decision = 0.5)
            {
                this.values = new Queue<int>(values);
                this.aiIndex = aiIndex;
                this.decision = decision;
            }

            public override int Next(int minValue, int maxValue)
            {
                int value = values.Dequeue();
                Assert.That(value, Is.InRange(minValue, maxValue - 1));
                return value;
            }

            public override int Next(int maxValue)
            {
                Assert.That(aiIndex, Is.InRange(0, maxValue - 1));
                return aiIndex;
            }

            public override double NextDouble() => decision;
        }
    }
}
