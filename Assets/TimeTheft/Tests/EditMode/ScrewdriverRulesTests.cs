using System;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class ScrewdriverRulesTests
    {
        private sealed class OrderedRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private sealed class ShrinkThenRecoverRandom : Random
        {
            private int generated;
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue)
            {
                int position = generated++;
                return (position < 15 && position % 5 >= 2) || position == 22 ? 12 : 1;
            }
            public override double NextDouble() => generated < 15 ? 0 : .99;
        }

        private static TimeTheftMatch NewMatch(Participant actor = Participant.Player, int life = 30,
            int hours = 8, int reduction = 50)
        {
            return new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = actor, InitialLifeHours = life,
                MinimumHours = hours, MaximumHours = hours, ExtraRedProbability = 0,
                ScrewdriverReductionPercent = reduction }, new OrderedRandom());
        }

        private static void StartBatch(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }

        private static void Resolve(TimeTheftMatch match, int index, TimeAction action, bool complete = true)
        {
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, index), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (complete && match.Stage != TurnStage.GameOver) Assert.That(match.CompleteTurn(), Is.True);
        }

        private static int OwnLife(TimeTheftMatch match, Participant actor) =>
            actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes;

        private static int Total(TimeTheftMatch match) => match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes;

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void RealScrewdriverConsumesImmediatelyAndRegistersOnlyThisActorsTurn(Participant actor)
        {
            var match = NewMatch(actor);
            int id = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            StartBatch(match);
            var result = match.UseItem(actor, id);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Effect.Value.Source.Id, Is.EqualTo(id));
            Assert.That(result.Effect.Value.Source.Owner, Is.EqualTo(actor));
            Assert.That(result.Effect.Value.Source.IsConsumed, Is.True);
            Assert.That(result.Effect.Value.Source.IsFoundationTest, Is.False);
            Assert.That(result.Effect.Value.Timing, Is.EqualTo(ItemEffectTiming.CurrentTurn));
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor).Single().Source.Id, Is.EqualTo(id));
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(match.HasScrewdriverProtection, Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(Total(match), Is.EqualTo(3600));
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
        }

        [TestCase(Participant.Player, 30, 8, 240)]
        [TestCase(Participant.Ai, 30, 8, 240)]
        [TestCase(Participant.Player, 13, 10, 90)]
        [TestCase(Participant.Ai, 13, 10, 90)]
        public void RedCollectionCapsAtLifeBeforeHalvingAndMovesOnlyActualLossToBank(
            Participant actor, int life, int hours, int actualLoss)
        {
            var match = NewMatch(actor, life, hours);
            int id = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(actor, id).Succeeded, Is.True);
            int beforeOwn = OwnLife(match, actor), beforeOther = OwnLife(match, actor == Participant.Player ? Participant.Ai : Participant.Player);
            int beforeBank = match.BankMinutes, beforeTotal = Total(match);
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(OwnLife(match, actor), Is.EqualTo(beforeOwn - actualLoss));
            Assert.That(OwnLife(match, actor == Participant.Player ? Participant.Ai : Participant.Player), Is.EqualTo(beforeOther));
            Assert.That(match.TransferredMinutes, Is.EqualTo(actualLoss));
            Assert.That(match.TransferParticipant, Is.EqualTo(actor));
            Assert.That(match.BankMinutes - beforeBank, Is.EqualTo(actualLoss));
            Assert.That(Total(match), Is.EqualTo(beforeTotal));
            var effect = match.LastItemEffects.Single();
            Assert.That(effect.Source.Id, Is.EqualTo(id));
            Assert.That(effect.Triggered, Is.True);
            Assert.That(effect.PreventedMinutes, Is.EqualTo(actualLoss));
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.HasScrewdriverProtection, Is.False);
            Assert.That(match.Winner, Is.EqualTo(GameWinner.None), "원래 손실이 잔여 수명을 넘더라도 경감 후 생존한다");
            Assert.That(match.Settle(), Is.False, "정산 연타는 경감·이전을 반복하지 않는다");
            Assert.That(Total(match), Is.EqualTo(beforeTotal));
        }

        [TestCase(Participant.Player, 0, TimeAction.Collect)]
        [TestCase(Participant.Ai, 0, TimeAction.Collect)]
        [TestCase(Participant.Player, 0, TimeAction.Ration)]
        [TestCase(Participant.Ai, 0, TimeAction.Ration)]
        [TestCase(Participant.Player, 2, TimeAction.Ration)]
        [TestCase(Participant.Ai, 2, TimeAction.Ration)]
        public void NormalCollectionAndEitherRationConsumeWithoutChangingBaselineTransfer(
            Participant actor, int index, TimeAction action)
        {
            var match = NewMatch(actor);
            var baseline = NewMatch(actor);
            int id = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            StartBatch(match); StartBatch(baseline);
            Assert.That(match.UseItem(actor, id).Succeeded, Is.True);
            Resolve(match, index, action, false);
            Resolve(baseline, index, action, false);
            Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes, match.TransferredMinutes },
                Is.EqualTo(new[] { baseline.PlayerLifeMinutes, baseline.AiLifeMinutes, baseline.BankMinutes, baseline.TransferredMinutes }));
            Assert.That(match.LastItemEffects.Single().Source.Id, Is.EqualTo(id));
            Assert.That(match.LastItemEffects.Single().Triggered, Is.False);
            Assert.That(match.LastItemEffects.Single().PreventedMinutes, Is.Zero);
            Assert.That(match.GetInventory(actor), Is.Empty, "미발동 아이템을 돌려주지 않는다");
            Assert.That(match.HasScrewdriverProtection, Is.False);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void SecondRealScrewdriverIsRejectedAtomicallyButOtherTestItemsCanStillBeUsed(Participant actor)
        {
            var match = NewMatch(actor);
            int first = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            int second = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            int other = TimeTheftItemTestSetup.Give(match, actor, ItemKind.SpareKey, ItemTestUse.RecordCurrentEffect);
            StartBatch(match);
            Assert.That(match.UseItem(actor, first).Succeeded, Is.True);
            Assert.That(match.UseItem(actor, first).Failure, Is.EqualTo(ItemUseFailure.AlreadyConsumed));
            var inventory = match.GetInventory(actor).ToArray();
            var effects = match.GetCurrentItemEffects(actor).ToArray();
            int total = Total(match);
            Assert.That(match.CheckItemUse(actor, second), Is.EqualTo(ItemUseFailure.AlreadyActive));
            var denied = match.UseItem(actor, second);
            Assert.That(denied.Succeeded, Is.False);
            Assert.That(denied.Failure, Is.EqualTo(ItemUseFailure.AlreadyActive));
            Assert.That(match.GetInventory(actor), Is.EqualTo(inventory));
            Assert.That(match.GetCurrentItemEffects(actor), Is.EqualTo(effects));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(Total(match), Is.EqualTo(total));
            Assert.That(match.UseItem(actor, other).Succeeded, Is.True);
            Assert.That(match.GetInventory(actor).Single().Id, Is.EqualTo(second));
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.LastItemEffects.Count(e => e.Source.Kind == ItemKind.Screwdriver), Is.EqualTo(1));
            Assert.That(match.TransferredMinutes, Is.EqualTo(240));
        }

        [Test]
        public void ForeignActorForeignIdAndPostACommandsCannotConsumeOrRegisterProtection()
        {
            var match = NewMatch();
            int own = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            int other = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Ai, other).Failure, Is.EqualTo(ItemUseFailure.WrongParticipant));
            Assert.That(match.UseItem(Participant.Player, other).Failure, Is.EqualTo(ItemUseFailure.NotOwned));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(own));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(other));
            Assert.That(match.HasScrewdriverProtection, Is.False);
            Assert.That(match.EndItemPhase(Participant.Player), Is.True);
            Assert.That(match.UseItem(Participant.Player, own).Failure, Is.EqualTo(ItemUseFailure.WrongStage));
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
        }

        [Test]
        public void ProtectionDoesNotApplyToOpponentsActionOrSurviveIntoNextBatch()
        {
            var match = NewMatch(hours: 1);
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            StartBatch(match);
            match.UseItem(Participant.Player, id);
            Resolve(match, 2, TimeAction.Collect);
            Assert.That(match.HasScrewdriverProtection, Is.False);
            int ownBefore = match.PlayerLifeMinutes;
            Resolve(match, 0, TimeAction.Collect);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(ownBefore - 60), "상대의 일반 징수를 대신 방어하지 않는다");
            Assert.That(match.LastItemEffects, Is.Empty);
            Resolve(match, 1, TimeAction.Ration);
            Resolve(match, 3, TimeAction.Ration);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            StartBatch(match);
            ownBefore = match.PlayerLifeMinutes;
            int bankBefore = match.BankMinutes;
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(ownBefore - 60));
            Assert.That(match.BankMinutes, Is.EqualTo(bankBefore + 60));
            Assert.That(match.LastItemEffects, Is.Empty);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
        }

        [Test]
        public void OddMinuteCapFloorsPreventionAndChargesTheRemainingThirtyOneMinutes()
        {
            var rules = new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, InitialLifeHours = 12, MinimumHours = 1, MaximumHours = 12,
                ExtraRedProbability = .5f, ScrewdriverReductionPercent = 50 };
            var match = new TimeTheftMatch(rules, new ShrinkThenRecoverRandom());
            var ids = Enumerable.Range(0, 7).Select(_ => TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver)).ToArray();
            for (int batch = 0; batch < 3; batch++)
            {
                StartBatch(match);
                // 일반은 1h, 빨강은 12h다. 상대의 일반 배급은 내 수명을 바꾸지 않고 Bank를 과도하게 소진하지 않는다.
                Assert.That(match.UseItem(Participant.Player, ids[batch * 2]).Succeeded, Is.True);
                Resolve(match, 2, TimeAction.Collect);
                Resolve(match, 0, TimeAction.Ration);
                Assert.That(match.UseItem(Participant.Player, ids[batch * 2 + 1]).Succeeded, Is.True);
                Resolve(match, 3, TimeAction.Collect);
                Resolve(match, 1, TimeAction.Ration);
            }
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1));
            StartBatch(match);
            Resolve(match, 0, TimeAction.Ration);
            Resolve(match, 1, TimeAction.Ration);
            Resolve(match, 3, TimeAction.Ration);
            Resolve(match, 4, TimeAction.Ration);
            StartBatch(match);
            Resolve(match, 0, TimeAction.Ration);
            Resolve(match, 1, TimeAction.Ration);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(61));
            Assert.That(match.UseItem(Participant.Player, ids[6]).Succeeded, Is.True);
            int bank = match.BankMinutes, total = Total(match);
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.LastItemEffects.Single().PreventedMinutes, Is.EqualTo(30));
            Assert.That(match.TransferredMinutes, Is.EqualTo(31));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(30));
            Assert.That(match.BankMinutes, Is.EqualTo(bank + 31));
            Assert.That(Total(match), Is.EqualTo(total));
        }

        [Test]
        public void TerminalNonTriggerAndFreshMatchClearProtectionWithoutReturningItem()
        {
            var match = NewMatch(life: 11, hours: 12);
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            StartBatch(match);
            match.UseItem(Participant.Player, id);
            Resolve(match, 0, TimeAction.Collect, false);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.Winner, Is.EqualTo(GameWinner.Player));
            Assert.That(match.HasScrewdriverProtection, Is.False);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(match.LastItemEffects.Single().Triggered, Is.False);
            Assert.That(match.CompleteTurn(), Is.False);
            var restarted = NewMatch();
            Assert.That(restarted.GetInventory(Participant.Player), Is.Empty);
            Assert.That(restarted.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(restarted.LastItemEffects, Is.Empty);
            Assert.That(restarted.HasScrewdriverProtection, Is.False);
        }

        [TestCase(0, 480)]
        [TestCase(25, 360)]
        [TestCase(100, 0)]
        public void ConfigurableIntegerPercentChangesOnlyTheCappedTransfer(int percent, int loss)
        {
            var match = NewMatch(reduction: percent);
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            StartBatch(match);
            match.UseItem(Participant.Player, id);
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.TransferredMinutes, Is.EqualTo(loss));
            Assert.That(match.LastItemEffects.Single().Triggered, Is.True);
            Assert.That(match.LastItemEffects.Single().PreventedMinutes, Is.EqualTo(480 - loss));
            Assert.That(Total(match), Is.EqualTo(3600));
        }

        [Test]
        public void RunningMatchKeepsItsReductionSnapshotAndLargeIntegerLossDoesNotOverflow()
        {
            var rules = new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, ScrewdriverReductionPercent = 50, MinimumHours = 8, MaximumHours = 8 };
            var match = new TimeTheftMatch(rules, new OrderedRandom());
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            rules.ScrewdriverReductionPercent = 100;
            StartBatch(match);
            match.UseItem(Participant.Player, id);
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.TransferredMinutes, Is.EqualTo(240));

            int maximumHours = int.MaxValue / (TimeTheftMatch.ClocksPerBatch * GameTime.MinutesPerHour);
            var large = NewMatch(life: int.MaxValue / (2 * GameTime.MinutesPerHour), hours: maximumHours);
            int largeId = TimeTheftItemTestSetup.Give(large, Participant.Player, ItemKind.Screwdriver);
            StartBatch(large);
            large.UseItem(Participant.Player, largeId);
            int total = Total(large);
            Resolve(large, 2, TimeAction.Collect, false);
            Assert.That(large.TransferredMinutes, Is.EqualTo(maximumHours * 30));
            Assert.That(large.LastItemEffects.Single().PreventedMinutes, Is.EqualTo(maximumHours * 30));
            Assert.That(Total(large), Is.EqualTo(total));
            Assert.That(large.PlayerLifeMinutes, Is.GreaterThanOrEqualTo(0));
            Assert.That(large.BankMinutes, Is.GreaterThanOrEqualTo(0));
        }

        [TestCase(-1)]
        [TestCase(101)]
        public void InvalidReductionPercentIsRejectedBeforeGameStarts(int percent)
        {
            Assert.Throws<ArgumentException>(() => NewMatch(reduction: percent));
        }
    }
}
