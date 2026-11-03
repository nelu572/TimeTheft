using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class TimeTheftItemFoundationTests
    {
        private sealed class OrderedRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private static TimeTheftMatch NewMatch(Participant first = Participant.Player, int life = 30, int hours = 1)
        {
            return new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = first, InitialLifeHours = life + 5,
                MinimumHours = hours, MaximumHours = hours, ExtraRedProbability = 0 }, new OrderedRandom());
        }

        private static void StartBatch(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }

        private static void Act(TimeTheftMatch match, TimeAction action = TimeAction.Ration, int index = -1)
        {
            Assert.That(match.SelectClock(match.ActiveParticipant, index < 0 ? match.GetAvailableClockIndices()[0] : index), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (match.Stage != TurnStage.GameOver) Assert.That(match.CompleteTurn(), Is.True);
        }

        private static void RejectedWithoutMutation(TimeTheftMatch match, Participant actor, int id, ItemUseFailure failure)
        {
            var stage = match.Stage;
            var participant = match.ActiveParticipant;
            int turn = match.Turn, used = match.UsedCount, selected = match.CurrentClock;
            var balances = new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes };
            var playerItems = match.GetInventory(Participant.Player).ToArray();
            var aiItems = match.GetInventory(Participant.Ai).ToArray();
            var effects = match.GetCurrentItemEffects(Participant.Player).Concat(match.GetCurrentItemEffects(Participant.Ai)).ToArray();
            var pending = match.GetPendingItemEffects(Participant.Player).Concat(match.GetPendingItemEffects(Participant.Ai)).ToArray();
            var result = match.UseItem(actor, id);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(failure));
            Assert.That(result.Effect.HasValue, Is.False);
            Assert.That(match.Stage, Is.EqualTo(stage));
            Assert.That(match.ActiveParticipant, Is.EqualTo(participant));
            Assert.That(match.Turn, Is.EqualTo(turn));
            Assert.That(match.UsedCount, Is.EqualTo(used));
            Assert.That(match.CurrentClock, Is.EqualTo(selected));
            Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes }, Is.EqualTo(balances));
            Assert.That(match.GetInventory(Participant.Player), Is.EqualTo(playerItems));
            Assert.That(match.GetInventory(Participant.Ai), Is.EqualTo(aiItems));
            Assert.That(match.GetCurrentItemEffects(Participant.Player).Concat(match.GetCurrentItemEffects(Participant.Ai)), Is.EqualTo(effects));
            Assert.That(match.GetPendingItemEffects(Participant.Player).Concat(match.GetPendingItemEffects(Participant.Ai)), Is.EqualTo(pending));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void EmptyInventoriesAutomaticallyPassItemPhaseForBothParticipants(Participant first)
        {
            var match = NewMatch(first);
            StartBatch(match);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.GetInventory(first), Is.Empty);
            Assert.That(match.EndItemPhase(first), Is.False, "무아이템 진행에 추가 종료 입력이 필요하지 않다");
            Act(match);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.ActiveParticipant, Is.Not.EqualTo(first));
            Assert.That(match.GetInventory(match.ActiveParticipant), Is.Empty);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void CommonCommandConsumesDistinctTestItemsRepeatedlyAndPassesAfterLastItem(Participant actor)
        {
            var match = NewMatch(actor);
            int first = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver, ItemTestUse.RecordCurrentEffect);
            int second = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver, ItemTestUse.RecordCurrentEffect);
            int third = TimeTheftItemTestSetup.Give(match, actor, ItemKind.Screwdriver, ItemTestUse.RecordFutureEffect);
            int other = TimeTheftItemTestSetup.Give(match, actor == Participant.Player ? Participant.Ai : Participant.Player, ItemKind.SpareKey);
            Assert.That(new[] { first, second, third, other }.Distinct().Count(), Is.EqualTo(4));
            StartBatch(match);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.SelectClock(actor, 0), Is.False);
            Assert.That(match.CommitAction(actor, TimeAction.Collect), Is.False);
            var result = match.UseItem(actor, first);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Actor, Is.EqualTo(actor));
            Assert.That(result.ItemId, Is.EqualTo(first));
            Assert.That(result.Effect.Value.Source.IsConsumed && result.Effect.Value.Source.IsFoundationTest, Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            RejectedWithoutMutation(match, actor, first, ItemUseFailure.AlreadyConsumed);
            Assert.That(match.UseItem(actor, second).Succeeded, Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.UseItem(actor, third).Succeeded, Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetCurrentItemEffects(actor).Select(effect => effect.Source.Id), Is.EqualTo(new[] { first, second }));
            Assert.That(match.GetPendingItemEffects(actor).Single().Source.Id, Is.EqualTo(third));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes }, Is.EqualTo(new[] { 1200, 1200, 1800 }));
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
        }

        [TestCase(ItemKind.SpareKey)]
        [TestCase(ItemKind.Scissors)]
        [TestCase(ItemKind.RubberDuck)]
        [TestCase(ItemKind.Handcuffs)]
        [TestCase(ItemKind.Screwdriver)]
        [TestCase(ItemKind.TimeBypass)]
        public void AllSixRealItemKindsUseTheCommonConsumptionCommand(ItemKind kind)
        {
            var match = NewMatch();
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, kind);
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player).Single().IsFoundationTest, Is.False);
            Assert.That(match.UseItem(Participant.Player, id).Succeeded, Is.True);
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }

        [Test]
        public void WrongActorUnknownIdForeignItemAndBlockedUseCannotMutateMatch()
        {
            var match = NewMatch();
            int own = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver, ItemTestUse.RecordCurrentEffect);
            int foreign = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Scissors, ItemTestUse.RecordCurrentEffect);
            RejectedWithoutMutation(match, Participant.Player, own, ItemUseFailure.WrongStage);
            StartBatch(match);
            RejectedWithoutMutation(match, Participant.Ai, foreign, ItemUseFailure.WrongParticipant);
            RejectedWithoutMutation(match, (Participant)99, own, ItemUseFailure.WrongParticipant);
            RejectedWithoutMutation(match, Participant.Player, -1, ItemUseFailure.NotOwned);
            RejectedWithoutMutation(match, Participant.Player, int.MaxValue, ItemUseFailure.NotOwned);
            RejectedWithoutMutation(match, Participant.Player, foreign, ItemUseFailure.NotOwned);
            TimeTheftItemTestSetup.SetUseAllowed(match, Participant.Player, false);
            Assert.That(match.CanUseItems(Participant.Player), Is.False);
            RejectedWithoutMutation(match, Participant.Player, own, ItemUseFailure.UseNotAllowed);
            Assert.That(match.EndItemPhase(Participant.Player), Is.True);
            RejectedWithoutMutation(match, Participant.Player, own, ItemUseFailure.WrongStage);
        }

        [Test]
        public void EndingItemPhaseIsIrreversibleForThisTurnAndKeepsUnusedItemsForNextOwnTurn()
        {
            var match = NewMatch();
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey, ItemTestUse.RecordCurrentEffect);
            StartBatch(match);
            Assert.That(match.EndItemPhase(Participant.Ai), Is.False);
            Assert.That(match.EndItemPhase(Participant.Player), Is.True);
            Assert.That(match.EndItemPhase(Participant.Player), Is.False);
            RejectedWithoutMutation(match, Participant.Player, id, ItemUseFailure.WrongStage);
            Act(match);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock), "상대는 보유품이 없으므로 자동 통과한다");
            Act(match);
            Assert.That(match.Turn, Is.EqualTo(3));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(id));
            Assert.That(match.CheckItemUse(Participant.Player, id), Is.EqualTo(ItemUseFailure.None));
        }

        [Test]
        public void TestEffectRecordsDoNotChangeCollectionOrSkipTurnsAndCurrentRecordsExpireAtSettlement()
        {
            var match = NewMatch();
            var baseline = NewMatch();
            int current = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver, ItemTestUse.RecordCurrentEffect);
            int future = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs, ItemTestUse.RecordFutureEffect);
            StartBatch(match); StartBatch(baseline);
            Assert.That(match.UseItem(Participant.Player, current).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Player, future).Succeeded, Is.True);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Has.Count.EqualTo(1));
            Act(match, TimeAction.Collect, 2);
            Act(baseline, TimeAction.Collect, 2);
            Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes, match.BankMinutes, match.TransferredMinutes },
                Is.EqualTo(new[] { baseline.PlayerLifeMinutes, baseline.AiLifeMinutes, baseline.BankMinutes, baseline.TransferredMinutes }));
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.GetPendingItemEffects(Participant.Player).Single().Source.Id, Is.EqualTo(future));
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.Turn, Is.EqualTo(2), "수갑 종류의 테스트 기록이 실제 차례를 생략하지 않는다");
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }

        [Test]
        public void FutureRecordsSurviveBatchReplacementWithoutInventingActivationRules()
        {
            var match = NewMatch();
            int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck, ItemTestUse.RecordFutureEffect);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, id).Succeeded, Is.True);
            for (int turn = 0; turn < 4; turn++) Act(match);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
            var pending = match.GetPendingItemEffects(Participant.Player).Single();
            Assert.That(pending.Source.Id, Is.EqualTo(id));
            Assert.That(pending.RegisteredTurn, Is.EqualTo(1));
            StartBatch(match);
            Assert.That(match.Batch, Is.EqualTo(2));
            Assert.That(match.GetPendingItemEffects(Participant.Player).Single(), Is.EqualTo(pending));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }

        [Test]
        public void TerminalSettlementClearsCurrentAndFutureRecordsWithoutNeedingCompleteTurn()
        {
            var match = NewMatch(life: 11, hours: 12);
            int current = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver, ItemTestUse.RecordCurrentEffect);
            int future = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass, ItemTestUse.RecordFutureEffect);
            StartBatch(match);
            match.UseItem(Participant.Player, current);
            match.UseItem(Participant.Player, future);
            Act(match, TimeAction.Collect, 0);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.Winner, Is.EqualTo(GameWinner.Player));
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.CompleteTurn(), Is.False);
            RejectedWithoutMutation(match, Participant.Player, current, ItemUseFailure.WrongStage);
        }

        [Test]
        public void ItemObservationsAreReadOnlyOwnSnapshotsAndAiEndsPhaseWithoutConsumingItems()
        {
            var match = NewMatch(Participant.Ai);
            int aiFirst = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver, ItemTestUse.RecordCurrentEffect);
            int aiSecond = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.SpareKey, ItemTestUse.RecordFutureEffect);
            int hiddenOpponent = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Scissors);
            StartBatch(match);
            var before = match.ObserveItems(Participant.Ai);
            Assert.That(before.OwnItems.Select(item => item.Id), Is.EqualTo(new[] { aiFirst, aiSecond }));
            Assert.That(before.OwnItems.Any(item => item.Id == hiddenOpponent || item.Owner != Participant.Ai), Is.False);
            Assert.That(before.PublicGame.NormalCount, Is.EqualTo(match.Observe(Participant.Ai).NormalCount));
            Assert.That(before.PublicGame.RedHours, Is.EqualTo(match.Observe(Participant.Ai).RedHours));
            Assert.That(before.CanUseItems, Is.True);
            Assert.Throws<NotSupportedException>(() => ((IList<ItemState>)before.OwnItems).Clear());
            Assert.That(match.UseItem(Participant.Ai, aiFirst).Succeeded, Is.True);
            var after = match.ObserveItems(Participant.Ai);
            Assert.That(before.OwnItems.Count, Is.EqualTo(2), "이전 관찰은 후속 명령으로 바뀌지 않는다");
            Assert.That(before.OwnCurrentEffects, Is.Empty);
            Assert.That(after.OwnItems.Single().Id, Is.EqualTo(aiSecond));
            Assert.That(after.OwnCurrentEffects.Single().Source.Owner, Is.EqualTo(Participant.Ai));
            Assert.That(TimeTheftAi.ChooseItem(after), Is.Null, "규칙 회귀의 비활성 전략 설정을 따른다");
            Assert.That(match.EndItemPhase(Participant.Ai), Is.True);
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(aiSecond));
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
            Assert.That(typeof(TimeTheftAi).GetMethod(nameof(TimeTheftAi.ChooseItem), new[] { typeof(AiItemObservation) }).GetParameters().Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(AiItemObservation) }), "AI 정책에 Match나 숨은 배열을 전달하지 않는다");
        }

        [Test]
        public void TestInjectionIsExplicitSetupOnlyAndFreshMatchHasNoGrantOrEffectState()
        {
            var match = NewMatch();
            TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Scissors, ItemTestUse.RecordCurrentEffect);
            StartBatch(match);
            var before = match.GetInventory(Participant.Player).ToArray();
            Assert.Throws<InvalidOperationException>(() => TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs));
            Assert.That(match.GetInventory(Participant.Player), Is.EqualTo(before));
            var fresh = NewMatch();
            Assert.That(fresh.GetInventory(Participant.Player), Is.Empty);
            Assert.That(fresh.GetInventory(Participant.Ai), Is.Empty);
            Assert.That(fresh.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(fresh.GetPendingItemEffects(Participant.Player), Is.Empty);
            StartBatch(fresh);
            Assert.That(fresh.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }
    }
}
