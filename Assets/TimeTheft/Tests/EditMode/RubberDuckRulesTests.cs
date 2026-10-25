using System;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class RubberDuckRulesTests
    {
        private sealed class OrderedRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private static Participant Other(Participant actor) => actor == Participant.Player ? Participant.Ai : Participant.Player;

        private static TimeTheftMatch NewMatch(Participant first = Participant.Player, int life = 30, int hours = 1) =>
            new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = first, InitialLifeHours = life,
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

        private static int Total(TimeTheftMatch match) => match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes;

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void UseConsumesImmediatelyAndBlocksOnlyTargetsNextActualAction(Participant actor)
        {
            var target = Other(actor);
            var match = NewMatch(actor);
            int duck = TimeTheftItemTestSetup.Give(match, actor, ItemKind.RubberDuck);
            int item = TimeTheftItemTestSetup.Give(match, target, ItemKind.Screwdriver);
            StartBatch(match);
            int registeredTurn = match.Turn;
            var result = match.UseItem(actor, duck);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Effect.Value.Source.IsConsumed, Is.True);
            Assert.That(result.Effect.Value.Target, Is.EqualTo(target));
            Assert.That(result.Effect.Value.Timing, Is.EqualTo(ItemEffectTiming.FutureTurn));
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.GetPendingItemEffects(actor).Single().Source.Id, Is.EqualTo(duck));
            Assert.That(match.IsItemUseBlocked(target), Is.False);
            Resolve(match, 0);
            Assert.That(match.ActiveParticipant, Is.EqualTo(target));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock), "봉쇄된 A는 자동 통과한다");
            Assert.That(match.IsItemUseBlocked(target), Is.True);
            Assert.That(match.IsItemUseBlocked(actor), Is.False);
            Assert.That(match.CanUseItems(target), Is.False);
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            var active = match.GetCurrentItemEffects(actor).Single();
            Assert.That(active.Target, Is.EqualTo(target));
            Assert.That(active.RegisteredTurn, Is.EqualTo(registeredTurn));
            Assert.That(active.Timing, Is.EqualTo(ItemEffectTiming.CurrentTurn));
            Assert.That(match.UseItem(target, item).Failure, Is.EqualTo(ItemUseFailure.UseNotAllowed));
            Assert.That(match.GetInventory(target).Single().Id, Is.EqualTo(item));
            Resolve(match, 1);
            Assert.That(match.IsItemUseBlocked(target), Is.False);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Resolve(match, 2);
            Assert.That(match.ActiveParticipant, Is.EqualTo(target));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.CheckItemUse(target, item), Is.EqualTo(ItemUseFailure.None));
            Assert.That(Total(match), Is.EqualTo(3600));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void EmptyTargetInventoryDoesNotDelayOrExtendRestriction(Participant actor)
        {
            var match = NewMatch(actor);
            var target = Other(actor);
            int duck = TimeTheftItemTestSetup.Give(match, actor, ItemKind.RubberDuck);
            StartBatch(match);
            Assert.That(match.UseItem(actor, duck).Succeeded, Is.True);
            Resolve(match, 0);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.IsItemUseBlocked(target), Is.True);
            Resolve(match, 1);
            Assert.That(match.IsItemUseBlocked(target), Is.False);
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
        }

        [TestCase(ItemKind.SpareKey)]
        [TestCase(ItemKind.Screwdriver)]
        [TestCase(ItemKind.TimeBypass)]
        [TestCase(ItemKind.Scissors)]
        [TestCase(ItemKind.RubberDuck)]
        [TestCase(ItemKind.Handcuffs)]
        public void EveryNewItemCommandIsBlockedWithoutConsumingOrMutatingGame(ItemKind kind)
        {
            var match = NewMatch();
            int duck = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            int targetItem = TimeTheftItemTestSetup.Give(match, Participant.Ai, kind);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, duck).Succeeded, Is.True);
            Resolve(match, 0);
            var before = new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes,
                match.Turn, match.UsedCount, match.CurrentClock };
            var effects = match.GetCurrentItemEffects(Participant.Player).ToArray();
            Assert.That(match.UseItem(Participant.Ai, targetItem).Failure, Is.EqualTo(ItemUseFailure.UseNotAllowed));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(targetItem));
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.EqualTo(effects));
            Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes,
                match.Turn, match.UsedCount, match.CurrentClock }, Is.EqualTo(before));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }

        [Test]
        public void DuplicatePendingDuckIsRejectedAndLeavesSecondInventoryEntryUntouched()
        {
            var match = NewMatch();
            int first = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            int second = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, first).Succeeded, Is.True);
            var pending = match.GetPendingItemEffects(Participant.Player).ToArray();
            Assert.That(match.UseItem(Participant.Player, first).Failure, Is.EqualTo(ItemUseFailure.AlreadyConsumed));
            Assert.That(match.UseItem(Participant.Player, second).Failure, Is.EqualTo(ItemUseFailure.AlreadyActive));
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.EqualTo(pending));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(second));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void DuckUsedOnFourthClockSurvivesBatchBoundaryUntilTargetsAction(Participant actor)
        {
            var target = Other(actor);
            var match = NewMatch(target);
            int duck = TimeTheftItemTestSetup.Give(match, actor, ItemKind.RubberDuck);
            int targetItem = TimeTheftItemTestSetup.Give(match, target, ItemKind.TimeBypass);
            StartBatch(match);
            Resolve(match, 0);
            Resolve(match, 1);
            Resolve(match, 2);
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.UseItem(actor, duck).Succeeded, Is.True);
            Resolve(match, 3);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            Assert.That(match.UsedCount, Is.EqualTo(4));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
            Assert.That(match.IsItemUseBlocked(target), Is.False);
            Assert.That(match.GetPendingItemEffects(actor).Single().Target, Is.EqualTo(target));
            StartBatch(match);
            Assert.That(match.Batch, Is.EqualTo(2));
            Assert.That(match.ActiveParticipant, Is.EqualTo(target));
            Assert.That(match.IsItemUseBlocked(target), Is.True);
            Assert.That(match.UseItem(target, targetItem).Failure, Is.EqualTo(ItemUseFailure.UseNotAllowed));
            Resolve(match, 0);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.GetPendingItemEffects(actor), Is.Empty);
            Assert.That(Total(match), Is.EqualTo(3600));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HandcuffsSkipDoesNotConsumeDuckRestrictionBeforeActualAction(bool cuffsFirst)
        {
            var match = NewMatch();
            int duck = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            int cuffs = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs);
            int victimItem = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, cuffsFirst ? cuffs : duck).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, cuffsFirst ? duck : cuffs).Succeeded, Is.True);
            Resolve(match, 0);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.UsedCount, Is.EqualTo(1), "생략된 차례는 시계를 쓰지 않는다");
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            Assert.That(match.GetPendingItemEffects(Participant.Player).Single().Source.Kind, Is.EqualTo(ItemKind.RubberDuck));
            Resolve(match, 1);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.True);
            Assert.That(match.UseItem(Participant.Ai, victimItem).Failure, Is.EqualTo(ItemUseFailure.UseNotAllowed));
            Resolve(match, 2);
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.Empty);
        }

        [Test]
        public void DriverAndBypassOnUsersTurnRemainUnchangedWhileDuckWaitsForOpponent()
        {
            var match = NewMatch(hours: 8);
            int duck = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            int driver = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            int bypass = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, duck).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, driver).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, bypass).Succeeded, Is.True);
            Resolve(match, 2, TimeAction.Collect);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(960));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(1320));
            Assert.That(match.BankMinutes, Is.EqualTo(1320));
            Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.Screwdriver).PreventedMinutes, Is.EqualTo(240));
            Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.TimeBypass).BypassedMinutes, Is.EqualTo(120));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.True);
            Assert.That(Total(match), Is.EqualTo(3600));
        }

        [Test]
        public void GameOverClearsPendingRestrictionAndNewMatchStartsWithoutAnyItemState()
        {
            var match = NewMatch(life: 11, hours: 8);
            int duck = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, duck).Succeeded, Is.True);
            Resolve(match, 0, TimeAction.Collect, false);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.Winner, Is.EqualTo(GameWinner.Player));
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            var restarted = NewMatch();
            Assert.That(restarted.GetInventory(Participant.Player), Is.Empty);
            Assert.That(restarted.GetPendingItemEffects(Participant.Player), Is.Empty);
            Assert.That(restarted.IsItemUseBlocked(Participant.Player), Is.False);
            Assert.That(restarted.IsItemUseBlocked(Participant.Ai), Is.False);
        }

        [Test]
        public void GameOverOnBlockedTargetsActionClearsActiveRestriction()
        {
            var match = NewMatch(life: 11, hours: 8);
            int duck = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            int item = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, duck).Succeeded, Is.True);
            Resolve(match, 2);
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.True);
            Resolve(match, 0, TimeAction.Collect, false);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(item));
        }

        [Test]
        public void SyntheticFoundationDuckRecordsNeverActivateARealRestriction()
        {
            var match = NewMatch();
            int fake = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck, ItemTestUse.RecordFutureEffect);
            int item = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, fake).Succeeded, Is.True);
            Resolve(match, 0);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            Assert.That(match.CheckItemUse(Participant.Ai, item), Is.EqualTo(ItemUseFailure.None));
            Assert.That(match.GetPendingItemEffects(Participant.Player).Single().Source.IsFoundationTest, Is.True);
        }

        [Test]
        public void AiObservationKeepsOwnInventoryAndPublicClockCompositionWhileBlocked()
        {
            var match = NewMatch();
            int duck = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            int item = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            StartBatch(match);
            match.UseItem(Participant.Player, duck);
            Resolve(match, 0);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.CanUseItems, Is.False);
            Assert.That(info.OwnItems.Single().Id, Is.EqualTo(item));
            Assert.That(info.OwnCurrentEffects, Is.Empty, "상대 소유 효과나 상대 인벤토리를 AI 자기 소유 데이터에 섞지 않는다");
            Assert.That(info.PublicGame.NormalCount + info.PublicGame.RedCount, Is.EqualTo(4));
            Assert.That(Enumerable.Range(0, 5).All(index => !match.GetClock(index).Face.HasValue), Is.True);
            Assert.That(TimeTheftAi.ChooseItem(info), Is.Null);
        }
    }
}
