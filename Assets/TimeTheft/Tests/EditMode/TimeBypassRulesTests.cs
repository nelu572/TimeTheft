using System;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class TimeBypassRulesTests
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
            int hours = 8, int bypass = 50, int driver = 50)
        {
            return new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = actor, InitialLifeHours = life + 5,
                MinimumHours = hours, MaximumHours = hours, ExtraRedProbability = 0,
                TimeBypassPercent = bypass, ScrewdriverReductionPercent = driver }, new OrderedRandom());
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

        private static Participant Other(Participant actor) => actor == Participant.Player ? Participant.Ai : Participant.Player;
        private static int Life(TimeTheftMatch match, Participant actor) => actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes;
        private static TimeAccount Account(Participant actor) => actor == Participant.Player ? TimeAccount.Player : TimeAccount.Ai;
        private static int Total(TimeTheftMatch match) => match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes;
        private static ItemEffectResult BypassResult(TimeTheftMatch match) => match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.TimeBypass);

        private static void AssertSplit(TimeTheftMatch match, Participant source, int bypass, int bank)
        {
            var transfers = match.LastTransfers;
            Assert.That(transfers.Count, Is.EqualTo(2));
            Assert.That(transfers.All(transfer => transfer.From == Account(source) && transfer.Minutes >= 0), Is.True);
            Assert.That(transfers.Single(transfer => transfer.To == Account(Other(source))).Minutes, Is.EqualTo(bypass));
            Assert.That(transfers.Single(transfer => transfer.To == TimeAccount.Bank).Minutes, Is.EqualTo(bank));
            Assert.That(transfers.Sum(transfer => transfer.Minutes), Is.EqualTo(match.TransferredMinutes));
            Assert.That(BypassResult(match).BypassedMinutes, Is.EqualTo(bypass));
            Assert.That(BypassResult(match).BankReceivedMinutes, Is.EqualTo(bank));
            Assert.That(BypassResult(match).Recipient, Is.EqualTo(Other(source)));
        }

        private static TimeTheftMatch PrepareOneMinuteLife(bool finalDriver, out int bypassId, out int driverId)
        {
            var rules = new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, InitialLifeHours = 17, MinimumHours = 1, MaximumHours = 12,
                ExtraRedProbability = .5f, ScrewdriverReductionPercent = 50, TimeBypassPercent = 50 };
            var match = new TimeTheftMatch(rules, new ShrinkThenRecoverRandom());
            var drivers = Enumerable.Range(0, finalDriver ? 7 : 6)
                .Select(_ => TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver)).ToArray();
            bypassId = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
            driverId = finalDriver ? drivers[6] : 0;
            for (int batch = 0; batch < 3; batch++)
            {
                StartBatch(match);
                Assert.That(match.UseItem(Participant.Player, drivers[batch * 2]).Succeeded, Is.True);
                Resolve(match, 2, TimeAction.Collect);
                Resolve(match, 0, TimeAction.Ration);
                Assert.That(match.UseItem(Participant.Player, drivers[batch * 2 + 1]).Succeeded, Is.True);
                Resolve(match, 3, TimeAction.Collect);
                Resolve(match, 1, TimeAction.Ration);
            }
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1));
            return match;
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void RealBypassConsumesAndRegistersOnlyCurrentTurnBeforeAnyClockIsKnown(Participant actor)
        {
            var match = NewMatch(actor);
            int id = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            StartBatch(match);
            var result = match.UseItem(actor, id);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Effect.Value.Source.Id, Is.EqualTo(id));
            Assert.That(result.Effect.Value.Source.IsConsumed, Is.True);
            Assert.That(result.Effect.Value.Source.IsFoundationTest, Is.False);
            Assert.That(result.Effect.Value.Timing, Is.EqualTo(ItemEffectTiming.CurrentTurn));
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor).Single().Source.Id, Is.EqualTo(id));
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(match.HasTimeBypass, Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.LastTransfers, Is.Empty);
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [TestCase(Participant.Player, 0)]
        [TestCase(Participant.Player, 2)]
        [TestCase(Participant.Ai, 0)]
        [TestCase(Participant.Ai, 2)]
        public void CollectionSplitsActualLossTowardTheVictimsOpponentAndBank(Participant actor, int index)
        {
            var match = NewMatch(actor);
            int id = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            StartBatch(match);
            Assert.That(match.UseItem(actor, id).Succeeded, Is.True);
            var source = index == 2 ? actor : Other(actor);
            Resolve(match, index, TimeAction.Collect, false);
            Assert.That(Life(match, source), Is.EqualTo(720));
            Assert.That(Life(match, Other(source)), Is.EqualTo(1440));
            Assert.That(match.BankMinutes, Is.EqualTo(2040));
            Assert.That(match.TransferParticipant, Is.EqualTo(source));
            Assert.That(match.TransferredMinutes, Is.EqualTo(480));
            Assert.That(BypassResult(match).Triggered, Is.True);
            Assert.That(BypassResult(match).PreventedMinutes, Is.Zero);
            AssertSplit(match, source, 240, 240);
            Assert.That(Total(match), Is.EqualTo(4200));
            Assert.That(match.HasTimeBypass, Is.False);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.Settle(), Is.False);
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [TestCase(Participant.Player, 0)]
        [TestCase(Participant.Player, 2)]
        [TestCase(Participant.Ai, 0)]
        [TestCase(Participant.Ai, 2)]
        public void EitherRationConsumesBypassWithoutChangingAnyBaselineTransfer(Participant actor, int index)
        {
            var match = NewMatch(actor);
            var baseline = NewMatch(actor);
            int id = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            StartBatch(match); StartBatch(baseline);
            match.UseItem(actor, id);
            Resolve(match, index, TimeAction.Ration, false);
            Resolve(baseline, index, TimeAction.Ration, false);
            Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes, match.TransferredMinutes },
                Is.EqualTo(new[] { baseline.PlayerLifeMinutes, baseline.AiLifeMinutes, baseline.BankMinutes, baseline.TransferredMinutes }));
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(BypassResult(match).Triggered, Is.False);
            Assert.That(BypassResult(match).BypassedMinutes, Is.Zero);
            Assert.That(BypassResult(match).BankReceivedMinutes, Is.Zero);
            Assert.That(BypassResult(match).Recipient, Is.Null);
            var transfer = match.LastTransfers.Single();
            Assert.That(transfer.From, Is.EqualTo(TimeAccount.Bank));
            Assert.That(transfer.To, Is.EqualTo(Account(index == 0 ? actor : Other(actor))));
            Assert.That(transfer.Minutes, Is.EqualTo(160));
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [TestCase(Participant.Player, 0)]
        [TestCase(Participant.Player, 2)]
        [TestCase(Participant.Ai, 0)]
        [TestCase(Participant.Ai, 2)]
        public void InsufficientVictimLifeCapsTheWholeLossBeforeSplittingAndThenEndsGame(Participant actor, int index)
        {
            var match = NewMatch(actor, life: 13, hours: 10);
            int id = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            StartBatch(match);
            match.UseItem(actor, id);
            var source = index == 2 ? actor : Other(actor);
            Resolve(match, index, TimeAction.Collect, false);
            Assert.That(match.TransferredMinutes, Is.EqualTo(180));
            Assert.That(Life(match, source), Is.Zero);
            Assert.That(Life(match, Other(source)), Is.EqualTo(270));
            Assert.That(match.BankMinutes, Is.EqualTo(1890));
            AssertSplit(match, source, 90, 90);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.Winner, Is.EqualTo(source == Participant.Player ? GameWinner.Ai : GameWinner.Player));
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.HasTimeBypass, Is.False);
            Assert.That(Total(match), Is.EqualTo(2160));
            Assert.That(match.CompleteTurn(), Is.False);
        }

        [TestCase(Participant.Player, 0, false)]
        [TestCase(Participant.Player, 0, true)]
        [TestCase(Participant.Player, 2, false)]
        [TestCase(Participant.Player, 2, true)]
        [TestCase(Participant.Ai, 0, false)]
        [TestCase(Participant.Ai, 0, true)]
        [TestCase(Participant.Ai, 2, false)]
        [TestCase(Participant.Ai, 2, true)]
        public void DriverThenBypassCalculationIsIndependentOfItemUseOrder(Participant actor, int index, bool bypassFirst)
        {
            var match = NewMatch(actor, life: 13, hours: 10);
            int driver = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            int bypass = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            StartBatch(match);
            Assert.That(match.UseItem(actor, bypassFirst ? bypass : driver).Succeeded, Is.True);
            Assert.That(match.UseItem(actor, bypassFirst ? driver : bypass).Succeeded, Is.True);
            Resolve(match, index, TimeAction.Collect, false);
            bool red = index == 2;
            var source = red ? actor : Other(actor);
            int loss = red ? 90 : 180;
            var prevention = match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.Screwdriver);
            Assert.That(prevention.Triggered, Is.EqualTo(red));
            Assert.That(prevention.PreventedMinutes, Is.EqualTo(red ? 90 : 0));
            Assert.That(match.TransferredMinutes, Is.EqualTo(loss));
            AssertSplit(match, source, loss / 2, loss / 2);
            Assert.That(Life(match, source), Is.EqualTo(red ? 90 : 0));
            Assert.That(Life(match, Other(source)), Is.EqualTo(red ? 225 : 270));
            Assert.That(match.BankMinutes, Is.EqualTo(red ? 1845 : 1890));
            Assert.That(match.Winner, Is.EqualTo(red ? GameWinner.None : actor == Participant.Player ? GameWinner.Player : GameWinner.Ai));
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(Total(match), Is.EqualTo(2160));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void ZeroLossAfterSupportedDriverConfigurationProducesOnlyZeroTransfers(Participant actor)
        {
            // 50% 경감과 양수 수명에서는 손실 0이 나오지 않는다. 기존 조절 기능의 독립 fixture로 경계를 만든다.
            Assert.That(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0 }.ScrewdriverReductionPercent, Is.EqualTo(50));
            var match = NewMatch(actor, driver: 100);
            int driver = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            int bypass = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            StartBatch(match);
            match.UseItem(actor, driver); match.UseItem(actor, bypass);
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.TransferredMinutes, Is.Zero);
            Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.Screwdriver).PreventedMinutes, Is.EqualTo(480));
            Assert.That(BypassResult(match).Triggered, Is.True);
            AssertSplit(match, actor, 0, 0);
            Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes }, Is.EqualTo(new[] { 1200, 1200, 1800 }));
            Assert.That(match.Winner, Is.EqualTo(GameWinner.None));
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
        }

        [TestCase(false, 61, 30, 31)]
        [TestCase(true, 31, 15, 16)]
        public void OddMinuteLossSplitsAfterDriverRoundingWithoutCreatingTime(bool protect, int loss, int bypass, int bank)
        {
            int bypassId, driverId;
            var match = PrepareOneMinuteLife(protect, out bypassId, out driverId);
            StartBatch(match);
            Resolve(match, 0, TimeAction.Ration);
            Resolve(match, 1, TimeAction.Ration);
            Resolve(match, 3, TimeAction.Ration);
            Resolve(match, 4, TimeAction.Ration);
            StartBatch(match);
            Resolve(match, 0, TimeAction.Ration);
            Resolve(match, 1, TimeAction.Ration);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(61));
            if (protect) Assert.That(match.UseItem(Participant.Player, driverId).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, bypassId).Succeeded, Is.True);
            int total = Total(match), bankBefore = match.BankMinutes, opponentBefore = match.AiLifeMinutes;
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.TransferredMinutes, Is.EqualTo(loss));
            AssertSplit(match, Participant.Player, bypass, bank);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(61 - loss));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(opponentBefore + bypass));
            Assert.That(match.BankMinutes, Is.EqualTo(bankBefore + bank));
            Assert.That(Total(match), Is.EqualTo(total));
            if (protect) Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.Screwdriver).PreventedMinutes, Is.EqualTo(30));
        }

        [Test]
        public void OneMinuteLossCannotGenerateBypassTimeAndEndsOnlyAfterFinalBalances()
        {
            int bypassId, driverId;
            var match = PrepareOneMinuteLife(true, out bypassId, out driverId);
            StartBatch(match);
            match.UseItem(Participant.Player, driverId); match.UseItem(Participant.Player, bypassId);
            int bank = match.BankMinutes, opponent = match.AiLifeMinutes, total = Total(match);
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.Screwdriver).PreventedMinutes, Is.Zero);
            Assert.That(match.TransferredMinutes, Is.EqualTo(1));
            AssertSplit(match, Participant.Player, 0, 1);
            Assert.That(match.PlayerLifeMinutes, Is.Zero);
            Assert.That(match.AiLifeMinutes, Is.EqualTo(opponent));
            Assert.That(match.BankMinutes, Is.EqualTo(bank + 1));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(Total(match), Is.EqualTo(total));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void DuplicateBypassIsRejectedWithoutConsumingButOtherItemKindsRemainUsable(Participant actor)
        {
            var match = NewMatch(actor);
            int first = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            int second = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            int driver = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            int fake = TimeTheftItemTestSetup.Give(match, actor, ItemKind.SpareKey, ItemTestUse.RecordCurrentEffect);
            StartBatch(match);
            match.UseItem(actor, first);
            var inventory = match.GetInventory(actor).ToArray();
            var effects = match.GetCurrentItemEffects(actor).ToArray();
            Assert.That(match.UseItem(actor, first).Failure, Is.EqualTo(ItemUseFailure.AlreadyConsumed));
            var rejected = match.UseItem(actor, second);
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Failure, Is.EqualTo(ItemUseFailure.AlreadyActive));
            Assert.That(match.GetInventory(actor), Is.EqualTo(inventory));
            Assert.That(match.GetCurrentItemEffects(actor), Is.EqualTo(effects));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(Total(match), Is.EqualTo(4200));
            Assert.That(match.UseItem(actor, driver).Succeeded, Is.True);
            Assert.That(match.UseItem(actor, fake).Succeeded, Is.True);
            Assert.That(match.GetInventory(actor).Single().Id, Is.EqualTo(second));
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.LastItemEffects.Count(effect => effect.Source.Kind == ItemKind.TimeBypass), Is.EqualTo(1));
            AssertSplit(match, actor, 120, 120);
        }

        [Test]
        public void InvalidActorForeignIdAndCommandsAfterACannotConsumeOrRegisterBypass()
        {
            var match = NewMatch();
            int own = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
            int foreign = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.TimeBypass);
            Assert.That(match.UseItem(Participant.Player, own).Failure, Is.EqualTo(ItemUseFailure.WrongStage));
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Ai, foreign).Failure, Is.EqualTo(ItemUseFailure.WrongParticipant));
            Assert.That(match.UseItem(Participant.Player, foreign).Failure, Is.EqualTo(ItemUseFailure.NotOwned));
            Assert.That(match.UseItem(Participant.Player, -1).Failure, Is.EqualTo(ItemUseFailure.NotOwned));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(own));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(foreign));
            Assert.That(match.HasTimeBypass, Is.False);
            Assert.That(match.EndItemPhase(Participant.Player), Is.True);
            Assert.That(match.UseItem(Participant.Player, own).Failure, Is.EqualTo(ItemUseFailure.WrongStage));
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [Test]
        public void BypassExpiresBeforeOpponentTurnAndDoesNotReturnAfterBatchReplacement()
        {
            var match = NewMatch();
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
            StartBatch(match);
            match.UseItem(Participant.Player, id);
            Resolve(match, 0, TimeAction.Collect);
            Assert.That(match.HasTimeBypass, Is.False);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            var priorTransfers = match.LastTransfers;
            Assert.That(priorTransfers.Count, Is.EqualTo(2));
            Assert.That(match.SelectClock(Participant.Ai, 2), Is.True);
            Assert.That(match.CommitAction(Participant.Ai, TimeAction.Collect), Is.True);
            Assert.That(match.LastTransfers, Is.Empty);
            Assert.That(priorTransfers.Count, Is.EqualTo(2), "기존 결과 스냅샷은 다음 명령에 의해 변하지 않는다");
            int bank = match.BankMinutes, aiLife = match.AiLifeMinutes;
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.AiLifeMinutes, Is.EqualTo(aiLife - 480));
            Assert.That(match.BankMinutes, Is.EqualTo(bank + 480));
            Assert.That(match.LastTransfers.Count, Is.EqualTo(1));
            Assert.That(match.LastItemEffects, Is.Empty);
            match.CompleteTurn();
            Resolve(match, 1, TimeAction.Ration);
            Resolve(match, 3, TimeAction.Ration);
            Assert.That(match.UsedCount, Is.EqualTo(4));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
            Assert.That(match.GetClock(4).Face, Is.Null);
            StartBatch(match);
            Assert.That(match.HasTimeBypass, Is.False);
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            int victim = match.AiLifeMinutes;
            bank = match.BankMinutes;
            Resolve(match, 0, TimeAction.Collect, false);
            Assert.That(match.BankMinutes - bank, Is.EqualTo(Math.Min(victim, 480)));
            Assert.That(match.LastTransfers.Count, Is.EqualTo(1));
            Assert.That(match.LastItemEffects, Is.Empty);
            Assert.That(Total(match), Is.EqualTo(4200));
            var restarted = NewMatch();
            Assert.That(restarted.HasTimeBypass, Is.False);
            Assert.That(restarted.GetInventory(Participant.Player), Is.Empty);
            Assert.That(restarted.LastItemEffects, Is.Empty);
            Assert.That(restarted.LastTransfers, Is.Empty);
        }

        [Test]
        public void SyntheticBypassRecordsRemainInertInsteadOfEnablingActualEffect()
        {
            var match = NewMatch();
            int current = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass, ItemTestUse.RecordCurrentEffect);
            int future = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass, ItemTestUse.RecordFutureEffect);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, current).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, future).Succeeded, Is.True);
            Assert.That(match.HasTimeBypass, Is.False);
            Resolve(match, 0, TimeAction.Collect, false);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(720));
            Assert.That(match.BankMinutes, Is.EqualTo(2280));
            Assert.That(match.LastTransfers.Count, Is.EqualTo(1));
            Assert.That(match.LastItemEffects, Is.Empty);
            Assert.That(match.GetPendingItemEffects(Participant.Player).Single().Source.Id, Is.EqualTo(future));
        }

        [TestCase(0, 0, 480)]
        [TestCase(1, 4, 476)]
        [TestCase(50, 240, 240)]
        [TestCase(100, 480, 0)]
        public void ConfigurableIntegerPercentAlwaysGivesTheRemainderToBank(int percent, int redirected, int bank)
        {
            var match = NewMatch(bypass: percent);
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
            StartBatch(match);
            match.UseItem(Participant.Player, id);
            Resolve(match, 0, TimeAction.Collect, false);
            AssertSplit(match, Participant.Ai, redirected, bank);
            Assert.That(match.BankMinutes, Is.EqualTo(1800 + bank));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1200 + redirected));
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [TestCase(-1)]
        [TestCase(101)]
        public void InvalidBypassPercentIsRejectedBeforeStartingGame(int percent)
        {
            Assert.Throws<ArgumentException>(() => NewMatch(bypass: percent));
        }

        [Test]
        public void ReductionSnapshotAndLargeIntegerSplitRemainStableAndConserveTime()
        {
            var rules = new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, TimeBypassPercent = 75, MinimumHours = 8, MaximumHours = 8 };
            var match = new TimeTheftMatch(rules, new OrderedRandom());
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
            rules.TimeBypassPercent = 0;
            StartBatch(match);
            match.UseItem(Participant.Player, id);
            Resolve(match, 0, TimeAction.Collect, false);
            AssertSplit(match, Participant.Ai, 360, 120);
            int hours = int.MaxValue / (TimeTheftMatch.ClocksPerBatch * GameTime.MinutesPerHour);
            var large = NewMatch(life: int.MaxValue / (2 * GameTime.MinutesPerHour) - 5, hours: hours);
            id = TimeTheftItemTestSetup.Give(large, Participant.Player, ItemKind.TimeBypass);
            StartBatch(large);
            large.UseItem(Participant.Player, id);
            int total = Total(large);
            Resolve(large, 0, TimeAction.Collect, false);
            AssertSplit(large, Participant.Ai, hours * 30, hours * 30);
            Assert.That(Total(large), Is.EqualTo(total));
            Assert.That(large.PlayerLifeMinutes, Is.GreaterThanOrEqualTo(0));
            Assert.That(large.AiLifeMinutes, Is.GreaterThanOrEqualTo(0));
            Assert.That(large.BankMinutes, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void AiObservationKeepsOnlyOwnItemsAndPublicBalancesAfterSplit()
        {
            var match = NewMatch(Participant.Ai);
            int id = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.TimeBypass);
            int held = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            int hidden = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Scissors);
            StartBatch(match);
            var before = match.ObserveItems(Participant.Ai);
            Assert.That(before.OwnItems.Select(item => item.Id), Is.EqualTo(new[] { id, held }));
            Assert.That(before.OwnItems.Any(item => item.Id == hidden), Is.False);
            Assert.That(TimeTheftAi.ChooseItem(before), Is.Null, "규칙 회귀의 비활성 전략 설정을 따른다");
            match.UseItem(Participant.Ai, id);
            Resolve(match, 0, TimeAction.Collect, false);
            var after = match.ObserveItems(Participant.Ai);
            Assert.That(after.OwnItems.Single().Id, Is.EqualTo(held));
            Assert.That(before.OwnItems, Has.Count.EqualTo(2));
            Assert.That(after.PublicGame.OwnLifeMinutes, Is.EqualTo(1440));
            Assert.That(after.PublicGame.OpponentLifeMinutes, Is.EqualTo(720));
            Assert.That(after.PublicGame.BankMinutes, Is.EqualTo(2040));
            Assert.That(after.PublicGame.NormalCount, Is.EqualTo(3));
            Assert.That(after.PublicGame.NormalHours, Is.EqualTo(24));
            Assert.That(after.PublicGame.RedCount, Is.EqualTo(1));
            Assert.That(after.PublicGame.RedHours, Is.EqualTo(8));
            Assert.That(Enumerable.Range(1, 4).All(i => !match.GetClock(i).Face.HasValue), Is.True);
        }
    }
}
