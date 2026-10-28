using System;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class ScissorsRulesTests
    {
        private sealed class OrderedRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private sealed class ItemRandom : Random
        {
            private readonly int choice;
            public int Calls { get; private set; }
            public ItemRandom(int choice = 0) { this.choice = choice; }
            public override int Next(int maxValue) { Calls++; return Math.Min(choice, maxValue - 1); }
        }

        private static Participant Other(Participant actor) => actor == Participant.Player ? Participant.Ai : Participant.Player;
        private static GameRules Rules(Participant first = Participant.Player) => new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0,
            FirstParticipant = first, InitialLifeHours = 35, MinimumHours = 8, MaximumHours = 8,
            ExtraRedProbability = 0
        };
        private static TimeTheftMatch NewMatch(Participant first, ItemRandom random = null) =>
            new TimeTheftMatch(Rules(first), new OrderedRandom(), random ?? new ItemRandom());
        private static int Give(TimeTheftMatch match, Participant actor, ItemKind kind,
            ItemTestUse use = ItemTestUse.Unavailable) => TimeTheftItemTestSetup.Give(match, actor, kind, use);

        private static void StartBatch(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }

        private static void Resolve(TimeTheftMatch match, int index, TimeAction action = TimeAction.Ration)
        {
            Participant actor = match.ActiveParticipant;
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(actor), Is.True);
            Assert.That(match.SelectClock(actor, index), Is.True);
            Assert.That(match.CommitAction(actor, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (match.Stage != TurnStage.GameOver) Assert.That(match.CompleteTurn(), Is.True);
        }

        private static int[] Balances(TimeTheftMatch match) => new[]
            { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes };

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void RemovesOnlyTheDrawnUnusedItemIdAmongDuplicateKindsAndConsumesScissors(Participant actor)
        {
            var random = new ItemRandom(1);
            var match = NewMatch(actor, random);
            int scissors = Give(match, actor, ItemKind.Scissors);
            int first = Give(match, Other(actor), ItemKind.Screwdriver);
            int second = Give(match, Other(actor), ItemKind.Screwdriver);
            int third = Give(match, Other(actor), ItemKind.TimeBypass);
            StartBatch(match);
            var before = Balances(match);

            ItemUseResult result = match.UseItem(actor, scissors);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Effect.Value.Source.Id, Is.EqualTo(scissors));
            Assert.That(result.Effect.Value.Source.IsConsumed, Is.True);
            Assert.That(result.RemovedItem.HasValue, Is.True);
            Assert.That(result.RemovedItem.Value.Id, Is.EqualTo(second));
            Assert.That(result.RemovedItem.Value.Kind, Is.EqualTo(ItemKind.Screwdriver));
            Assert.That(result.RemovedItem.Value.Owner, Is.EqualTo(Other(actor)));
            Assert.That(result.RemovedItem.Value.IsConsumed, Is.True);
            Assert.That(match.GetInventory(Other(actor)).Select(item => item.Id), Is.EqualTo(new[] { first, third }));
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(Balances(match), Is.EqualTo(before));
            Assert.That(random.Calls, Is.EqualTo(1));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void NoTargetRejectsBeforeConsumptionRandomDrawOrAnyStateMutation(Participant actor)
        {
            var random = new ItemRandom();
            var match = NewMatch(actor, random);
            int scissors = Give(match, actor, ItemKind.Scissors);
            StartBatch(match);
            var before = Balances(match);
            var observation = match.ObserveItems(actor);

            Assert.That(match.CheckItemUse(actor, scissors), Is.EqualTo(ItemUseFailure.NoTarget));
            var result = match.UseItem(actor, scissors);

            Assert.That(result.Failure, Is.EqualTo(ItemUseFailure.NoTarget));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Effect.HasValue || result.RemovedItem.HasValue, Is.False);
            Assert.That(match.GetInventory(actor).Single().Id, Is.EqualTo(scissors));
            Assert.That(match.GetInventory(actor).Single().IsConsumed, Is.False);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.Turn, Is.EqualTo(1));
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(Balances(match), Is.EqualTo(before));
            Assert.That(match.ObserveItems(actor).OwnItems.Select(item => item.Id),
                Is.EqualTo(observation.OwnItems.Select(item => item.Id)));
            Assert.That(random.Calls, Is.Zero);
        }

        [Test]
        public void AdditionalScissorsCannotConsumeAfterTheLastTargetWasRemoved()
        {
            var random = new ItemRandom();
            var match = NewMatch(Participant.Player, random);
            int first = Give(match, Participant.Player, ItemKind.Scissors);
            int second = Give(match, Participant.Player, ItemKind.Scissors);
            int target = Give(match, Participant.Ai, ItemKind.RubberDuck);
            StartBatch(match);

            Assert.That(match.UseItem(Participant.Player, first).RemovedItem.Value.Id, Is.EqualTo(target));
            var result = match.UseItem(Participant.Player, second);

            Assert.That(result.Failure, Is.EqualTo(ItemUseFailure.NoTarget));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(second));
            Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(random.Calls, Is.EqualTo(1));
        }

        [Test]
        public void MultipleImmediateScissorsUsesRemoveDifferentIdsWithoutAccumulatingAnEffect()
        {
            var match = NewMatch(Participant.Player);
            int[] scissors = Enumerable.Range(0, 3).Select(_ => Give(match, Participant.Player, ItemKind.Scissors)).ToArray();
            int[] victims = Enumerable.Range(0, 3).Select(_ => Give(match, Participant.Ai, ItemKind.SpareKey)).ToArray();
            StartBatch(match);

            int[] removed = scissors.Select(id => match.UseItem(Participant.Player, id).RemovedItem.Value.Id).ToArray();

            Assert.That(removed, Is.EqualTo(victims));
            Assert.That(removed.Distinct().Count(), Is.EqualTo(3));
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void RemovesUnusedInventoryWithoutCancellingAnAlreadyRegisteredFutureEffect(Participant actor)
        {
            Participant victim = Other(actor);
            var match = NewMatch(victim);
            int scissors = Give(match, actor, ItemKind.Scissors);
            int used = Give(match, victim, ItemKind.Screwdriver, ItemTestUse.RecordFutureEffect);
            int unused = Give(match, victim, ItemKind.TimeBypass);
            StartBatch(match);
            Assert.That(match.UseItem(victim, used).Succeeded, Is.True);
            Resolve(match, 0);
            var pending = match.GetPendingItemEffects(victim).Single();

            ItemUseResult result = match.UseItem(actor, scissors);

            Assert.That(result.RemovedItem.Value.Id, Is.EqualTo(unused));
            Assert.That(result.RemovedItem.Value.Id, Is.Not.EqualTo(used));
            Assert.That(match.GetPendingItemEffects(victim).Single().Source.Id, Is.EqualTo(pending.Source.Id));
            Assert.That(match.GetPendingItemEffects(victim).Single().RegisteredTurn, Is.EqualTo(pending.RegisteredTurn));
            Assert.That(match.GetInventory(victim), Is.Empty);
        }

        [Test]
        public void AnOpponentWithOnlyConsumedPendingItemsDoesNotProvideARemovalTarget()
        {
            var match = NewMatch(Participant.Ai);
            int scissors = Give(match, Participant.Player, ItemKind.Scissors);
            int used = Give(match, Participant.Ai, ItemKind.TimeBypass, ItemTestUse.RecordFutureEffect);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Ai, used).Succeeded, Is.True);
            Resolve(match, 0);

            Assert.That(match.UseItem(Participant.Player, scissors).Failure, Is.EqualTo(ItemUseFailure.NoTarget));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(scissors));
            Assert.That(match.GetPendingItemEffects(Participant.Ai).Single().Source.Id, Is.EqualTo(used));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void WrongActorOwnedIdAndUnknownIdsCannotRemoveAnything(Participant actor)
        {
            var random = new ItemRandom();
            var match = NewMatch(actor, random);
            int own = Give(match, actor, ItemKind.Scissors);
            int enemy = Give(match, Other(actor), ItemKind.Scissors);
            StartBatch(match);

            Assert.That(match.UseItem(Other(actor), enemy).Failure, Is.EqualTo(ItemUseFailure.WrongParticipant));
            Assert.That(match.UseItem(actor, enemy).Failure, Is.EqualTo(ItemUseFailure.NotOwned));
            Assert.That(match.UseItem(actor, int.MaxValue).Failure, Is.EqualTo(ItemUseFailure.NotOwned));
            Assert.That(match.GetInventory(actor).Single().Id, Is.EqualTo(own));
            Assert.That(match.GetInventory(Other(actor)).Single().Id, Is.EqualTo(enemy));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(random.Calls, Is.Zero);
        }

        [Test]
        public void ConsumedScissorsAndUseAfterAEndsAreAtomicRejections()
        {
            var random = new ItemRandom();
            var match = NewMatch(Participant.Player, random);
            int used = Give(match, Participant.Player, ItemKind.Scissors);
            int remaining = Give(match, Participant.Player, ItemKind.Scissors);
            Give(match, Participant.Ai, ItemKind.SpareKey);
            int target = Give(match, Participant.Ai, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, used).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, used).Failure, Is.EqualTo(ItemUseFailure.AlreadyConsumed));
            Assert.That(match.EndItemPhase(Participant.Player), Is.True);

            Assert.That(match.UseItem(Participant.Player, remaining).Failure, Is.EqualTo(ItemUseFailure.WrongStage));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(remaining));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(target));
            Assert.That(random.Calls, Is.EqualTo(1));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void RemovedItemIdIsRejectedAsConsumedOnItsOwnersNextA(Participant actor)
        {
            var match = NewMatch(actor);
            int scissors = Give(match, actor, ItemKind.Scissors);
            int removed = Give(match, Other(actor), ItemKind.Screwdriver);
            int retained = Give(match, Other(actor), ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(actor, scissors).RemovedItem.Value.Id, Is.EqualTo(removed));
            Resolve(match, 0);

            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.UseItem(Other(actor), removed).Failure, Is.EqualTo(ItemUseFailure.AlreadyConsumed));
            Assert.That(match.GetInventory(Other(actor)).Single().Id, Is.EqualTo(retained));
            Assert.That(match.GetCurrentItemEffects(Other(actor)), Is.Empty);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void ScissorsDoesNotChangeDriverThenBypassRedCollectionOrTotalTime(Participant actor)
        {
            var match = NewMatch(actor);
            int scissors = Give(match, actor, ItemKind.Scissors);
            int driver = Give(match, actor, ItemKind.Screwdriver);
            int bypass = Give(match, actor, ItemKind.TimeBypass);
            Give(match, Other(actor), ItemKind.SpareKey);
            StartBatch(match);
            int total = Balances(match).Sum();
            Assert.That(match.UseItem(actor, scissors).Succeeded, Is.True);
            Assert.That(match.UseItem(actor, driver).Succeeded, Is.True);
            Assert.That(match.UseItem(actor, bypass).Succeeded, Is.True);
            Assert.That(match.SelectClock(actor, 2), Is.True);
            Assert.That(match.CommitAction(actor, TimeAction.Collect), Is.True);
            Assert.That(match.Settle(), Is.True);

            Assert.That(match.TransferredMinutes, Is.EqualTo(240));
            Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.Screwdriver).PreventedMinutes, Is.EqualTo(240));
            Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.TimeBypass).BypassedMinutes, Is.EqualTo(120));
            Assert.That(match.LastItemEffects.Any(effect => effect.Source.Kind == ItemKind.Scissors), Is.False);
            Assert.That(Balances(match).Sum(), Is.EqualTo(total));
            Assert.That(Balances(match).All(minutes => minutes >= 0), Is.True);
            Assert.That(match.BankMinutes, Is.EqualTo(1920));
        }

        [Test]
        public void RandomRemovalDoesNotAdvanceTheHiddenClockRandomStream()
        {
            var random = new ItemRandom();
            var match = new TimeTheftMatch(Rules(), new Random(17), random);
            var baseline = new TimeTheftMatch(Rules(), new Random(17));
            int scissors = Give(match, Participant.Player, ItemKind.Scissors);
            Give(match, Participant.Ai, ItemKind.SpareKey);
            StartBatch(match); StartBatch(baseline);
            Assert.That(match.UseItem(Participant.Player, scissors).Succeeded, Is.True);
            for (int index = 0; index < 4; index++)
            {
                foreach (var game in new[] { match, baseline })
                {
                    if (game.Stage == TurnStage.AwaitingItems) Assert.That(game.EndItemPhase(game.ActiveParticipant), Is.True);
                    Assert.That(game.SelectClock(game.ActiveParticipant, index), Is.True);
                    Assert.That(game.CommitAction(game.ActiveParticipant, TimeAction.Ration), Is.True);
                }
                Assert.That(match.GetClock(index).Face.Value.Hours, Is.EqualTo(baseline.GetClock(index).Face.Value.Hours));
                Assert.That(match.GetClock(index).Face.Value.Kind, Is.EqualTo(baseline.GetClock(index).Face.Value.Kind));
                foreach (var game in new[] { match, baseline })
                {
                    Assert.That(game.Settle(), Is.True);
                    Assert.That(game.CompleteTurn(), Is.True);
                }
            }
            Assert.That(match.BeginBatch() && baseline.BeginBatch(), Is.True);
            for (int index = 0; index < 5; index++)
            {
                Assert.That(match.GetClock(index).Face.Value.Hours, Is.EqualTo(baseline.GetClock(index).Face.Value.Hours));
                Assert.That(match.GetClock(index).Face.Value.Kind, Is.EqualTo(baseline.GetClock(index).Face.Value.Kind));
            }
            Assert.That(random.Calls, Is.EqualTo(1));
        }

        [Test]
        public void OnlyTheRemovedItemIsReturnedAndTheAiStillObservesOnlyItsOwnInventory()
        {
            var match = NewMatch(Participant.Player);
            int scissors = Give(match, Participant.Player, ItemKind.Scissors);
            int removed = Give(match, Participant.Ai, ItemKind.RubberDuck);
            int hidden = Give(match, Participant.Ai, ItemKind.Handcuffs);
            StartBatch(match);
            var prior = match.ObserveItems(Participant.Player);
            Assert.That(prior.OwnItems.Single().Id, Is.EqualTo(scissors));

            var result = match.UseItem(Participant.Player, scissors);

            Assert.That(result.RemovedItem.Value.Id, Is.EqualTo(removed));
            Assert.That(result.RemovedItem.Value.Id, Is.Not.EqualTo(hidden));
            Assert.That(match.ObserveItems(Participant.Player).OwnItems, Is.Empty);
            Assert.That(match.ObserveItems(Participant.Ai).OwnItems.Single().Id, Is.EqualTo(hidden));
            Assert.That(Enumerable.Range(0, 5).All(index => !match.GetClock(index).Face.HasValue), Is.True);
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Ai)).HasValue, Is.False);
        }

        [Test]
        public void RestartStartsWithNoRemovedItemsEffectsOrGrantedInventory()
        {
            var match = NewMatch(Participant.Player);
            int scissors = Give(match, Participant.Player, ItemKind.Scissors);
            Give(match, Participant.Ai, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, scissors).Succeeded, Is.True);

            var restarted = NewMatch(Participant.Player);

            Assert.That(restarted.GetInventory(Participant.Player), Is.Empty);
            Assert.That(restarted.GetInventory(Participant.Ai), Is.Empty);
            Assert.That(restarted.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(restarted.GetPendingItemEffects(Participant.Player), Is.Empty);
            Assert.That(restarted.LastItemEffects, Is.Empty);
            Assert.That(restarted.LastTransfers, Is.Empty);
            Assert.That(Balances(restarted), Is.EqualTo(new[] { 1200, 1200, 1800 }));
        }
    }
}
