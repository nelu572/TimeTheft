using System;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class HandcuffsRulesTests
    {
        private sealed class OrderedRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private static Participant Other(Participant actor) => actor == Participant.Player ? Participant.Ai : Participant.Player;
        private static TimeTheftMatch NewMatch(Participant first = Participant.Player, int life = 30, int hours = 1) =>
            new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = first, InitialLifeHours = life + 5,
                MinimumHours = hours, MaximumHours = hours, ExtraRedProbability = 0 }, new OrderedRandom());

        private static void StartBatch(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }

        private static void Resolve(TimeTheftMatch match, int index, TimeAction action = TimeAction.Ration, bool complete = true)
        {
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, index), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (complete && match.Stage != TurnStage.GameOver) Assert.That(match.CompleteTurn(), Is.True);
        }

        private static int[] Balances(TimeTheftMatch match) => new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes };
        private static int Total(TimeTheftMatch match) => Balances(match).Sum();

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void UseConsumesAndRegistersOnlyTheOpponentsNextOpportunity(Participant actor)
        {
            var match = NewMatch(actor);
            int cuff = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            StartBatch(match);
            var before = Balances(match);
            var result = match.UseItem(actor, cuff);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Effect.Value.Source.IsConsumed, Is.True);
            Assert.That(result.Effect.Value.Timing, Is.EqualTo(ItemEffectTiming.FutureTurn));
            Assert.That(result.Effect.Value.Target, Is.EqualTo(Other(actor)));
            Assert.That(result.Effect.Value.RegisteredTurn, Is.EqualTo(1));
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.GetPendingItemEffects(actor).Single().Source.Id, Is.EqualTo(cuff));
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.Turn, Is.EqualTo(1));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(Balances(match), Is.EqualTo(before));
            Assert.That(match.TurnItemEvents, Is.Empty);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void SkipReturnsToUserWithoutAnotherClockOrTransfer(Participant actor)
        {
            var match = NewMatch(actor);
            int cuff = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            int targetItem = TimeTheftItemTestSetup.Give(match, Other(actor), ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(actor, cuff).Succeeded, Is.True);
            Resolve(match, 0, TimeAction.Ration, false);
            var settled = Balances(match);
            int transferred = match.TransferredMinutes;
            Assert.That(match.CompleteTurn(), Is.True);
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.Turn, Is.EqualTo(3));
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.DiscardedCount, Is.Zero);
            Assert.That(Balances(match), Is.EqualTo(settled));
            Assert.That(match.TransferredMinutes, Is.EqualTo(transferred));
            Assert.That(match.GetClock(0).Visibility, Is.EqualTo(ClockVisibility.Used));
            Assert.That(Enumerable.Range(1, 4).All(i => match.GetClock(i).Visibility == ClockVisibility.Hidden), Is.True);
            var skip = match.TurnItemEvents.Single();
            Assert.That(skip.Source.Id, Is.EqualTo(cuff));
            Assert.That(skip.Target, Is.EqualTo(Other(actor)));
            Assert.That(skip.Kind, Is.EqualTo(ItemTurnEventKind.TurnSkipped));
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(match.GetInventory(Other(actor)).Single().Id, Is.EqualTo(targetItem));
            Assert.That(match.SelectClock(Other(actor), 1), Is.False, "건너뛴 AI/플레이어 명령은 진행하지 않는다");
            Assert.That(match.CommitAction(Other(actor), TimeAction.Collect), Is.False);
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(match.CompleteTurn(), Is.False, "생략 자체는 추가 정산 완료 명령을 열지 않는다");
            Resolve(match, 1);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Other(actor)));
            Assert.That(match.Turn, Is.EqualTo(4));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.TurnItemEvents, Is.Empty);
            Assert.That(match.CheckItemUse(Other(actor), targetItem), Is.EqualTo(ItemUseFailure.None));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void DuplicatePendingCuffDoesNotConsumeAndOtherKindsStillWork(Participant actor)
        {
            var match = NewMatch(actor);
            int first = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            int second = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            int driver = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(actor, first).Succeeded, Is.True);
            var before = Balances(match);
            Assert.That(match.UseItem(actor, second).Failure, Is.EqualTo(ItemUseFailure.AlreadyActive));
            Assert.That(match.GetInventory(actor).Any(i => i.Id == second), Is.True);
            Assert.That(match.GetPendingItemEffects(actor).Count, Is.EqualTo(1));
            Assert.That(Balances(match), Is.EqualTo(before));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.UseItem(actor, driver).Succeeded, Is.True);
            Assert.That(match.HasScrewdriverProtection, Is.True);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void LaterOpportunityAllowsAnotherCuffAfterTheFirstHasEnded(Participant actor)
        {
            var match = NewMatch(actor);
            int first = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            int second = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            StartBatch(match);
            Assert.That(match.UseItem(actor, first).Succeeded, Is.True);
            Resolve(match, 0);
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.CheckItemUse(actor, second), Is.EqualTo(ItemUseFailure.None));
            Assert.That(match.UseItem(actor, second).Succeeded, Is.True);
            Resolve(match, 1);
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.Turn, Is.EqualTo(5));
            Assert.That(match.UsedCount, Is.EqualTo(2));
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(match.TurnItemEvents.Single().Source.Id, Is.EqualTo(second));
            Resolve(match, 2);
            Resolve(match, 3);
            Assert.That(match.UsedCount, Is.EqualTo(4));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
            Assert.That(match.GetClock(4).Visibility, Is.EqualTo(ClockVisibility.Discarded));
            Assert.That(match.GetClock(4).Face, Is.Null);
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void LastActualActionCarriesCuffAcrossBatchAndDoesNotUseNewClock(Participant actor)
        {
            var match = NewMatch(Other(actor));
            int cuff = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            StartBatch(match);
            Resolve(match, 0);
            Resolve(match, 1);
            Resolve(match, 2);
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.UseItem(actor, cuff).Succeeded, Is.True);
            Resolve(match, 3);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            Assert.That(match.UsedCount, Is.EqualTo(4));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
            Assert.That(match.GetPendingItemEffects(actor).Single().Source.Id, Is.EqualTo(cuff));
            int beforeTurn = match.Turn;
            var before = Balances(match);
            StartBatch(match);
            Assert.That(match.Batch, Is.EqualTo(2));
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.Turn, Is.EqualTo(beforeTurn + 1));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.DiscardedCount, Is.Zero);
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(Balances(match), Is.EqualTo(before));
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(match.TurnItemEvents.Single().Kind, Is.EqualTo(ItemTurnEventKind.TurnSkipped));
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DuckWaitsForActualActionWhenCuffsSkipTheNextOpportunity(bool cuffFirst)
        {
            var match = NewMatch();
            int cuff = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs);
            int duck = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            int target = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.TimeBypass);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, cuffFirst ? cuff : duck).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, cuffFirst ? duck : cuff).Succeeded, Is.True);
            Resolve(match, 0);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            Assert.That(match.GetPendingItemEffects(Participant.Player).Single().Source.Id, Is.EqualTo(duck));
            Resolve(match, 1);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.True);
            Assert.That(match.UseItem(Participant.Ai, target).Failure, Is.EqualTo(ItemUseFailure.UseNotAllowed));
            Resolve(match, 2);
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void CuffDoesNotChangeDriverAndBypassRedCollectionOrTotal(Participant actor)
        {
            var match = NewMatch(actor, hours: 8);
            int cuff = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            int driver = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver);
            int bypass = TimeTheftItemTestSetup.Give(match, actor, ItemKind.TimeBypass);
            StartBatch(match);
            Assert.That(match.UseItem(actor, cuff).Succeeded, Is.True);
            Assert.That(match.UseItem(actor, bypass).Succeeded, Is.True);
            Assert.That(match.UseItem(actor, driver).Succeeded, Is.True);
            Resolve(match, 2, TimeAction.Collect, false);
            Assert.That(match.TransferredMinutes, Is.EqualTo(240));
            Assert.That(match.LastItemEffects.Single(e => e.Source.Kind == ItemKind.Screwdriver).PreventedMinutes, Is.EqualTo(240));
            Assert.That(match.LastItemEffects.Single(e => e.Source.Kind == ItemKind.TimeBypass).BypassedMinutes, Is.EqualTo(120));
            Assert.That(match.BankMinutes, Is.EqualTo(1920));
            Assert.That(actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes, Is.EqualTo(960));
            Assert.That(actor == Participant.Player ? match.AiLifeMinutes : match.PlayerLifeMinutes, Is.EqualTo(1320));
            var before = Balances(match);
            Assert.That(match.CompleteTurn(), Is.True);
            Assert.That(Balances(match), Is.EqualTo(before));
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void TerminalSettlementClearsPendingCuffWithoutSkippingAnEndedGame(Participant actor)
        {
            var match = NewMatch(actor, life: 11, hours: 8);
            int cuff = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Handcuffs);
            StartBatch(match);
            Assert.That(match.UseItem(actor, cuff).Succeeded, Is.True);
            Resolve(match, 0, TimeAction.Collect);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.Winner, Is.EqualTo(actor == Participant.Player ? GameWinner.Player : GameWinner.Ai));
            Assert.That(match.Turn, Is.EqualTo(1));
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.TurnItemEvents.Count, Is.EqualTo(1));
            Assert.That(match.TurnItemEvents.Single().Kind, Is.EqualTo(ItemTurnEventKind.Cancelled));
            Assert.That(match.CompleteTurn(), Is.False);
            Assert.That(Total(match), Is.EqualTo(1920));
            var restart = NewMatch(actor);
            Assert.That(restart.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(restart.GetInventory(actor), Is.Empty);
            Assert.That(restart.TurnItemEvents, Is.Empty);
            StartBatch(restart);
            Resolve(restart, 0);
            Assert.That(restart.ActiveParticipant, Is.EqualTo(Other(actor)));
            Assert.That(restart.Turn, Is.EqualTo(2));
        }

        [Test]
        public void SpareKeyChoiceStaysPrivateAndOnlyUsedClockCountsBeforeSkip()
        {
            var match = NewMatch();
            int cuff = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs);
            int key = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, key).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, cuff).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Player, 2), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
            Assert.That(match.GetPrivateClock(Participant.Player, 2).Value.Kind, Is.EqualTo(ClockKind.Red));
            Assert.That(match.GetPrivateClock(Participant.Ai, 2), Is.Null);
            Assert.That(match.GetClock(2).Face, Is.Null);
            Assert.That(match.ReselectPeekedClock(Participant.Player, 0), Is.True);
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Ration), Is.True);
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.CompleteTurn(), Is.True);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.GetClock(0).Visibility, Is.EqualTo(ClockVisibility.Used));
            Assert.That(match.GetClock(2).Visibility, Is.EqualTo(ClockVisibility.Hidden));
            Assert.That(match.GetPrivateClock(Participant.Player, 2).Value.Kind, Is.EqualTo(ClockKind.Red));
            Assert.That(match.GetPrivateClock(Participant.Ai, 2), Is.Null);
            Assert.That(match.GetClock(2).Face, Is.Null);
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [Test]
        public void WrongActorAndCommandsAfterARejectWithoutConsumption()
        {
            var match = NewMatch();
            int cuff = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs);
            int aiCuff = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Handcuffs);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Ai, aiCuff).Failure, Is.EqualTo(ItemUseFailure.WrongParticipant));
            Assert.That(match.UseItem(Participant.Player, aiCuff).Failure, Is.EqualTo(ItemUseFailure.NotOwned));
            Assert.That(match.UseItem(Participant.Player, -1).Failure, Is.EqualTo(ItemUseFailure.NotOwned));
            Assert.That(match.EndItemPhase(Participant.Player), Is.True);
            Assert.That(match.UseItem(Participant.Player, cuff).Failure, Is.EqualTo(ItemUseFailure.WrongStage));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(cuff));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(aiCuff));
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [Test]
        public void SyntheticFutureCuffNeverSkipsAndAiUsesOnlyPublicObservation()
        {
            var match = NewMatch(Participant.Ai);
            int cuff = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Handcuffs, ItemTestUse.RecordFutureEffect);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Ai, cuff).Succeeded, Is.True);
            Resolve(match, 0);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.Turn, Is.EqualTo(2));
            Assert.That(match.TurnItemEvents, Is.Empty);
            var observed = match.ObserveItems(Participant.Ai);
            Assert.That(TimeTheftAi.ChooseItem(observed), Is.Null);
            Assert.That(observed.OwnPendingEffects.Single().Source.Id, Is.EqualTo(cuff));
            Assert.That(observed.OwnPendingEffects.Single().Source.IsFoundationTest, Is.True);
            Assert.That(observed.OwnPendingEffects.Single().Target, Is.Null);
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
        }
    }
}
