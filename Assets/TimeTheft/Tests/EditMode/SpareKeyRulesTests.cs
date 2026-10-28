using System;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class SpareKeyRulesTests
    {
        private sealed class OrderedRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private static TimeTheftMatch NewMatch(Participant actor = Participant.Player, int life = 30)
        {
            return new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = actor, InitialLifeHours = life + 5,
                MinimumHours = 8, MaximumHours = 8, ExtraRedProbability = 0 }, new OrderedRandom());
        }

        private static Participant Other(Participant actor) => actor == Participant.Player ? Participant.Ai : Participant.Player;
        private static int Total(TimeTheftMatch match) => match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes;

        private static void StartBatch(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }

        private static void FinishSelected(TimeTheftMatch match, TimeAction action = TimeAction.Ration)
        {
            Assert.That(match.CommitAction(match.ActiveParticipant, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (match.Stage != TurnStage.GameOver) Assert.That(match.CompleteTurn(), Is.True);
        }

        private static void Act(TimeTheftMatch match, int index)
        {
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, index), Is.True);
            if (match.Stage == TurnStage.AwaitingKeyDecision) Assert.That(match.KeepPeekedClock(match.ActiveParticipant), Is.True);
            FinishSelected(match);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void UseConsumesInAAndFirstSelectionRevealsOnlyToTheUser(Participant actor)
        {
            var match = NewMatch(actor);
            int key = TimeTheftItemTestSetup.Give(match, actor, ItemKind.SpareKey);
            StartBatch(match);
            var publicBefore = match.Observe(Other(actor));
            Assert.That(match.GetPrivateClock(actor, 2), Is.Null);
            var result = match.UseItem(actor, key);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Effect.Value.Source.IsConsumed, Is.True);
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.HasSpareKeyReadiness, Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.SelectClock(actor, 2), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
            Assert.That(match.GetPrivateClock(actor, 2).Value.Kind, Is.EqualTo(ClockKind.Red));
            Assert.That(match.GetPrivateClock(actor, 2).Value.Hours, Is.EqualTo(8));
            Assert.That(match.GetPrivateClock(Other(actor), 2), Is.Null);
            Assert.That(match.GetPrivateClock(actor, 0), Is.Null);
            Assert.That(Enumerable.Range(0, 5).All(index => !match.GetClock(index).Face.HasValue), Is.True);
            Assert.That(match.Observe(Other(actor)), Is.EqualTo(publicBefore));
            Assert.That(match.ObserveItems(Other(actor)).PublicGame, Is.EqualTo(publicBefore));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.CommitAction(actor, TimeAction.Collect), Is.False);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void KeepingPeekedClockAllowsExistingActionAndForgetsUsedKnowledge(Participant actor)
        {
            var match = NewMatch(actor);
            int key = TimeTheftItemTestSetup.Give(match, actor, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(actor, key).Succeeded, Is.True);
            Assert.That(match.SelectClock(actor, 0), Is.True);
            Assert.That(match.KeepPeekedClock(actor), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(match.CurrentClock, Is.Zero);
            Assert.That(match.GetClock(0).Face.HasValue, Is.False);
            Assert.That(match.GetPrivateClock(actor, 0).HasValue, Is.True);
            Assert.That(match.ReselectPeekedClock(actor, 2), Is.False);
            Assert.That(match.KeepPeekedClock(actor), Is.False);
            FinishSelected(match);
            Assert.That(match.GetPrivateClock(actor, 0), Is.Null);
            Assert.That(match.HasSpareKeyReadiness, Is.False);
            Assert.That(match.GetCurrentItemEffects(actor), Is.Empty);
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void ReselectingOnceDoesNotConsumeTheFirstClockOrRevealTheSecond(Participant actor)
        {
            var match = NewMatch(actor);
            int key = TimeTheftItemTestSetup.Give(match, actor, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(actor, key).Succeeded, Is.True);
            Assert.That(match.SelectClock(actor, 0), Is.True);
            Assert.That(match.ReselectPeekedClock(actor, 2), Is.True);
            Assert.That(match.CurrentClock, Is.EqualTo(2));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(match.GetPrivateClock(actor, 0).HasValue, Is.True);
            Assert.That(match.GetPrivateClock(actor, 2), Is.Null);
            Assert.That(match.GetClock(0).Face.HasValue || match.GetClock(2).Face.HasValue, Is.False);
            Assert.That(match.ReselectPeekedClock(actor, 1), Is.False);
            Assert.That(match.SelectClock(actor, 1), Is.False);
            Assert.That(match.UseItem(actor, key).Succeeded, Is.False);
            FinishSelected(match);
            Assert.That(match.GetClock(0).Visibility, Is.EqualTo(ClockVisibility.Hidden));
            Assert.That(match.GetClock(2).Visibility, Is.EqualTo(ClockVisibility.Used));
            Assert.That(match.GetAvailableClockIndices().Contains(0), Is.True);
            Assert.That(match.GetAvailableClockIndices().Contains(2), Is.False);
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [Test]
        public void IllegalKeepAndReselectionDoNotAlterSelectedClockOrKnowledge()
        {
            var match = NewMatch();
            int key = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            Assert.That(match.KeepPeekedClock(Participant.Player), Is.False);
            Assert.That(match.ReselectPeekedClock(Participant.Player, 0), Is.False);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, key).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Player, 0), Is.True);
            Assert.That(match.KeepPeekedClock(Participant.Ai), Is.False);
            foreach (int index in new[] { -1, 5, 0, int.MaxValue })
                Assert.That(match.ReselectPeekedClock(Participant.Player, index), Is.False);
            Assert.That(match.ReselectPeekedClock(Participant.Ai, 1), Is.False);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
            Assert.That(match.CurrentClock, Is.Zero);
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
            Assert.That(match.GetPrivateClock(Participant.Player, -1), Is.Null);
            Assert.That(match.GetPrivateClock((Participant)99, 0), Is.Null);
            Assert.That(Total(match), Is.EqualTo(4200));
            Assert.That(match.KeepPeekedClock(Participant.Player), Is.True);
        }

        [Test]
        public void AUsedClockCannotBeChosenDuringKeyReselection()
        {
            var match = NewMatch(Participant.Ai);
            int key = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            StartBatch(match);
            Act(match, 0);
            Assert.That(match.UseItem(Participant.Player, key).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Player, 1), Is.True);
            Assert.That(match.ReselectPeekedClock(Participant.Player, 0), Is.False);
            Assert.That(match.CurrentClock, Is.EqualTo(1));
            Assert.That(match.GetPrivateClock(Participant.Player, 0), Is.Null);
            Assert.That(match.ReselectPeekedClock(Participant.Player, 2), Is.True);
        }

        [Test]
        public void SameTurnDuplicateIsRetainedAndUnusedPrivateKnowledgeSurvivesToNextOwnTurn()
        {
            var match = NewMatch();
            int first = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            int second = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, first).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, second).Failure, Is.EqualTo(ItemUseFailure.AlreadyActive));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(second));
            Assert.That(match.EndItemPhase(Participant.Player), Is.True);
            Assert.That(match.SelectClock(Participant.Player, 0), Is.True);
            Assert.That(match.ReselectPeekedClock(Participant.Player, 1), Is.True);
            FinishSelected(match);
            Assert.That(match.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
            Assert.That(match.GetPrivateClock(Participant.Ai, 0), Is.Null);
            Act(match, 3);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
            Assert.That(match.UseItem(Participant.Player, second).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Player, 2), Is.True);
            Assert.That(match.GetPrivateClock(Participant.Player, 2).Value.Kind, Is.EqualTo(ClockKind.Red));
            Assert.That(match.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
            Assert.That(match.KeepPeekedClock(Participant.Player), Is.True);
        }

        [Test]
        public void EachActorKeepsOnlyTheirOwnPeeksEvenWhenBothUseKeysInOneBatch()
        {
            var match = NewMatch();
            int playerKey = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            int aiKey = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, playerKey).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Player, 0), Is.True);
            Assert.That(match.ReselectPeekedClock(Participant.Player, 1), Is.True);
            FinishSelected(match);
            Assert.That(match.UseItem(Participant.Ai, aiKey).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Ai, 2), Is.True);
            Assert.That(match.ReselectPeekedClock(Participant.Ai, 3), Is.True);
            FinishSelected(match);
            Assert.That(match.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
            Assert.That(match.GetPrivateClock(Participant.Player, 2), Is.Null);
            Assert.That(match.GetPrivateClock(Participant.Ai, 2).HasValue, Is.True);
            Assert.That(match.GetPrivateClock(Participant.Ai, 0), Is.Null);
            Assert.That(match.ObserveItems(Participant.Player).OwnCurrentEffects, Is.Empty);
            Assert.That(match.ObserveItems(Participant.Ai).OwnCurrentEffects, Is.Empty);
        }

        [Test]
        public void BatchDiscardAndNewBatchClearKnowledgeWithoutUsingThePeekedClock()
        {
            var match = NewMatch();
            int key = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, key).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Player, 0), Is.True);
            Assert.That(match.ReselectPeekedClock(Participant.Player, 1), Is.True);
            FinishSelected(match);
            Act(match, 2);
            Act(match, 3);
            Assert.That(match.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
            Act(match, 4);
            Assert.That(match.UsedCount, Is.EqualTo(4));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
            Assert.That(match.GetPrivateClock(Participant.Player, 0), Is.Null);
            StartBatch(match);
            Assert.That(match.Batch, Is.EqualTo(2));
            Assert.That(Enumerable.Range(0, 5).All(index => !match.GetPrivateClock(Participant.Player, index).HasValue), Is.True);
            Assert.That(match.HasSpareKeyReadiness, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KeySelectionDoesNotChangeDriverBypassSettlement(bool red)
        {
            var match = NewMatch();
            int key = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            int driver = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            int bypass = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, driver).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, key).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, bypass).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Player, 2), Is.True);
            Assert.That(red ? match.KeepPeekedClock(Participant.Player) : match.ReselectPeekedClock(Participant.Player, 0), Is.True);
            FinishSelected(match, TimeAction.Collect);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(red ? 960 : 1440));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(red ? 1320 : 720));
            Assert.That(match.BankMinutes, Is.EqualTo(red ? 1920 : 2040));
            Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.Screwdriver).PreventedMinutes,
                Is.EqualTo(red ? 240 : 0));
            Assert.That(match.LastItemEffects.Single(effect => effect.Source.Kind == ItemKind.TimeBypass).BypassedMinutes,
                Is.EqualTo(red ? 120 : 240));
            Assert.That(Total(match), Is.EqualTo(4200));
        }

        [Test]
        public void SyntheticKeyRecordCannotPeekAndNoKeyLeavesNormalSelectionFlow()
        {
            var match = NewMatch();
            int key = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey, ItemTestUse.RecordCurrentEffect);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, key).Succeeded, Is.True);
            Assert.That(match.HasSpareKeyReadiness, Is.False);
            Assert.That(match.SelectClock(Participant.Player, 0), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(match.GetPrivateClock(Participant.Player, 0), Is.Null);
            Assert.That(match.KeepPeekedClock(Participant.Player), Is.False);
            FinishSelected(match);
            Act(match, 1);
            Assert.That(match.HasSpareKeyReadiness, Is.False);
        }

        [Test]
        public void GameOverAndNewMatchDoNotRetainPendingKeyKnowledge()
        {
            var match = NewMatch(life: 11);
            int key = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, key).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Player, 0), Is.True);
            Assert.That(match.ReselectPeekedClock(Participant.Player, 2), Is.True);
            FinishSelected(match, TimeAction.Collect);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.GetPrivateClock(Participant.Player, 0), Is.Null);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            var restarted = NewMatch();
            StartBatch(restarted);
            Assert.That(restarted.GetPrivateClock(Participant.Player, 0), Is.Null);
            Assert.That(restarted.HasSpareKeyReadiness, Is.False);
            Assert.That(restarted.GetInventory(Participant.Player), Is.Empty);
        }
    }
}
