using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class TimeTheftRulesTests
    {
        private sealed class OrderedRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private sealed class DistinctHoursRandom : Random
        {
            private int nextHours = 2;
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue)
            {
                int result = nextHours;
                nextHours += 2;
                return result;
            }
            public override double NextDouble() => .99;
        }

        private sealed class MiddleDecisionRandom : Random
        {
            public override double NextDouble() => .5;
        }

        private static TimeTheftMatch Prepared(int life = 30, int hours = 1, Participant first = Participant.Player)
        {
            var match = new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, InitialLifeHours = life, MinimumHours = hours, MaximumHours = hours,
                ExtraRedProbability = 0, FirstParticipant = first }, new OrderedRandom());
            StartBatch(match);
            return match;
        }

        private static void StartBatch(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }

        private static void Act(TimeTheftMatch match, TimeAction action, int index = -1)
        {
            Assert.That(match.SelectClock(match.ActiveParticipant, index < 0 ? match.GetAvailableClockIndices()[0] : index), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (match.Stage != TurnStage.GameOver) Assert.That(match.CompleteTurn(), Is.True);
        }

        [Test]
        public void EqualMandatoryContributionsMoveTwentyHoursAsIntegerMinutesWithoutCreatingTime()
        {
            var match = new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0 }, new Random(15));
            Assert.That(TimeTheftMatch.InitialContributionMinutes, Is.EqualTo(600));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.BankMinutes, Is.EqualTo(1200));
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
        }

        [Test]
        public void FiveClocksGuaranteeCompositionAndInitiallyExposeEveryNumberAndKind()
        {
            for (int seed = 1; seed <= 150; seed++)
            {
                var match = new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0 }, new Random(seed));
                match.BeginBatch();
                var clocks = Enumerable.Range(0, 5).Select(match.GetClock).ToArray();
                Assert.That(clocks.All(c => c.Visibility == ClockVisibility.Open && c.Face.HasValue), Is.True);
                Assert.That(clocks.Count(c => c.Face.Value.Kind == ClockKind.Normal), Is.InRange(2, 4));
                Assert.That(clocks.Count(c => c.Face.Value.Kind == ClockKind.Red), Is.InRange(1, 3));
                Assert.That(clocks.All(c => c.Face.Value.Hours >= 1 && c.Face.Value.Hours <= 12), Is.True);
            }
        }

        [Test]
        public void ShuffleAndClockSelectionHideIdentitiesUntilActionIsCommitted()
        {
            var match = new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0 }, new Random(8));
            match.BeginBatch();
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Collect), Is.False);
            match.CloseAndShuffle();
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Ration), Is.False);
            match.CompleteShuffle();
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Collect), Is.False, "시계를 먼저 선택해야 행동을 확정한다");
            Assert.That(match.SelectClock(Participant.Ai, 0), Is.False);
            Assert.That(match.SelectClock(Participant.Player, 0), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(match.CurrentClock, Is.EqualTo(0));
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True,
                "시계 선택만으로는 숫자나 종류가 공개되지 않는다");
            Assert.That(match.CommitAction(Participant.Ai, TimeAction.Collect), Is.False);
            Assert.That(match.CommitAction(Participant.Player, (TimeAction)17), Is.False);
            Assert.That(match.SelectClock(Participant.Player, 1), Is.False, "선택한 시계는 행동 전에 교체할 수 없다");
            Assert.That(match.Settle(), Is.False);
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Collect), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.Revealing));
            Assert.That(match.GetClock(0).Face.HasValue, Is.True);
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Ration), Is.False);
            Assert.That(match.Action, Is.EqualTo(TimeAction.Collect));
            Assert.That(Enumerable.Range(1, 4).All(i => !match.GetClock(i).Face.HasValue), Is.True);
            match.Settle();
            int total = match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes;
            Assert.That(match.Settle(), Is.False);
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(total));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void NormalCollectionMovesOnlyOpponentsLifeToBank(Participant actor)
        {
            var match = Prepared(30, 6, actor);
            match.SelectClock(actor, 0);
            match.CommitAction(actor, TimeAction.Collect);
            match.Settle();
            Assert.That(match.TransferParticipant, Is.Not.EqualTo(actor));
            Assert.That(match.TransferredMinutes, Is.EqualTo(360));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(actor == Participant.Player ? 1200 : 840));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(actor == Participant.Player ? 840 : 1200));
            Assert.That(match.BankMinutes, Is.EqualTo(1560));
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void RedCollectionMovesOnlyActorsLifeAndHasNoFee(Participant actor)
        {
            var match = Prepared(30, 6, actor);
            match.SelectClock(actor, 2);
            match.CommitAction(actor, TimeAction.Collect);
            Assert.That(match.GetClock(2).Face.Value.Kind, Is.EqualTo(ClockKind.Red));
            match.Settle();
            Assert.That(match.TransferParticipant, Is.EqualTo(actor));
            Assert.That(match.TransferredMinutes, Is.EqualTo(360));
            Assert.That(match.BankMinutes, Is.EqualTo(1560));
            Assert.That(actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes, Is.EqualTo(840));
            Assert.That(actor == Participant.Player ? match.AiLifeMinutes : match.PlayerLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
        }

        [TestCase(1, 20, "00:20")]
        [TestCase(2, 40, "00:40")]
        [TestCase(4, 80, "01:20")]
        [TestCase(7, 140, "02:20")]
        [TestCase(12, 240, "04:00")]
        public void RationPaysExactlyOneThirdInMinutesWithoutHourRounding(int hours, int minutes, string formatted)
        {
            var match = Prepared(hours: hours);
            Act(match, TimeAction.Ration, 0);
            Assert.That(match.TransferredMinutes, Is.EqualTo(minutes));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1200 + minutes));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.BankMinutes, Is.EqualTo(1200 - minutes));
            Assert.That(GameTime.Format(match.TransferredMinutes), Is.EqualTo(formatted));
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void NormalRationPaysOnlyTheActorFromBank(Participant actor)
        {
            var match = Prepared(hours: 7, first: actor);
            Act(match, TimeAction.Ration, 0);
            Assert.That(match.TransferParticipant, Is.EqualTo(actor));
            Assert.That(match.TransferredMinutes, Is.EqualTo(140));
            Assert.That(actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes, Is.EqualTo(1340));
            Assert.That(actor == Participant.Player ? match.AiLifeMinutes : match.PlayerLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.BankMinutes, Is.EqualTo(1060));
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
        }

        [Test]
        public void RationCapsAtMinuteBankBalanceIncludingZero()
        {
            var match = Prepared(hours: 7);
            for (int turn = 0; turn < 8; turn++)
            {
                if (match.Stage == TurnStage.BatchComplete) StartBatch(match);
                Act(match, TimeAction.Ration);
            }
            Assert.That(match.BankMinutes, Is.EqualTo(80), "140분 배급 8회 후 01:20이 남는다");
            StartBatch(match);
            int beforePlayer = match.PlayerLifeMinutes, beforeAi = match.AiLifeMinutes;
            match.SelectClock(Participant.Player, 2);
            Assert.That(match.GetClock(2).Face.HasValue, Is.False);
            match.CommitAction(Participant.Player, TimeAction.Ration);
            Assert.That(match.GetClock(2).Face.Value.Kind, Is.EqualTo(ClockKind.Red));
            match.Settle();
            Assert.That(match.TransferredMinutes, Is.EqualTo(80));
            Assert.That(match.TransferParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(beforePlayer));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(beforeAi + 80));
            Assert.That(match.BankMinutes, Is.Zero);
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
            match.CompleteTurn();
            match.SelectClock(Participant.Ai, 0);
            match.CommitAction(Participant.Ai, TimeAction.Ration);
            match.Settle();
            Assert.That(match.TransferredMinutes, Is.Zero);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(beforePlayer));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(beforeAi + 80));
            Assert.That(match.BankMinutes, Is.Zero);
            Assert.That(match.Winner, Is.EqualTo(GameWinner.None));
            Assert.That(TimeTheftAi.Decide(match.Observe(Participant.Ai), .5, new Random(1)), Is.EqualTo(TimeAction.Collect));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void RedRationGivesOpponentMinutesWithoutDamagingActor(Participant actor)
        {
            var match = Prepared(hours: 7, first: actor);
            int beforeOwn = actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes;
            Act(match, TimeAction.Ration, 2);
            Assert.That(match.TransferParticipant, Is.Not.EqualTo(actor));
            Assert.That(match.TransferredMinutes, Is.EqualTo(140));
            Assert.That(actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes, Is.EqualTo(beforeOwn));
            Assert.That(actor == Participant.Player ? match.AiLifeMinutes : match.PlayerLifeMinutes, Is.EqualTo(1340));
            Assert.That(match.BankMinutes, Is.EqualTo(1060));
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
        }

        [Test]
        public void FourActionsAlternateTwiceEachDiscardLastUnopenedAndRegenerate()
        {
            var match = Prepared();
            for (int i = 0; i < 4; i++)
            {
                Assert.That(match.ActiveParticipant, Is.EqualTo(i % 2 == 0 ? Participant.Player : Participant.Ai));
                Act(match, TimeAction.Ration);
            }
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            Assert.That(match.UsedCount, Is.EqualTo(4));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
            Assert.That(match.GetClock(4).Visibility, Is.EqualTo(ClockVisibility.Discarded));
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Collect), Is.False);
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.Batch, Is.EqualTo(2));
            Assert.That(match.Turn, Is.EqualTo(5));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.DiscardedCount, Is.Zero);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(Enumerable.Range(0, 5).All(i => match.GetClock(i).Face.HasValue), Is.True);
        }

        [Test]
        public void ExcessCollectionEndsImmediatelyAtZeroWithoutAwardingBank()
        {
            var match = Prepared(11, 12);
            Act(match, TimeAction.Collect);
            Assert.That(match.TransferredMinutes, Is.EqualTo(60));
            Assert.That(match.AiLifeMinutes, Is.Zero);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(60));
            Assert.That(match.BankMinutes, Is.EqualTo(1260));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.Winner, Is.EqualTo(GameWinner.Player));
            Assert.That(match.CompleteTurn(), Is.False);
            Assert.That(match.BeginBatch(), Is.False);
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Collect), Is.False);
        }

        [Test]
        public void RedCollectionCanEndActorsLifeWithOnlyRemainingTwentyMinutesMoved()
        {
            var match = Prepared(11);
            Act(match, TimeAction.Ration);
            Act(match, TimeAction.Collect);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(20));
            Act(match, TimeAction.Collect);
            Assert.That(match.PlayerLifeMinutes, Is.Zero);
            Assert.That(match.TransferredMinutes, Is.EqualTo(20));
            Assert.That(match.Winner, Is.EqualTo(GameWinner.Ai));
            Assert.That(match.BankMinutes, Is.EqualTo(1260));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(60));
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(1320));
        }

        [Test]
        public void AiUsesPublicCompositionAndCannotDistinguishHiddenOrders()
        {
            var rules = new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, MinimumHours = 5, MaximumHours = 5, ExtraRedProbability = 0 };
            for (int seed = 1; seed < 50; seed++)
            {
                var match = new TimeTheftMatch(rules, new Random(seed));
                match.BeginBatch(); match.CloseAndShuffle(); match.CompleteShuffle();
                var observed = match.Observe(Participant.Ai);
                Assert.That(observed.OwnLifeMinutes, Is.EqualTo(1200));
                Assert.That(observed.OpponentLifeMinutes, Is.EqualTo(1200));
                Assert.That(observed.BankMinutes, Is.EqualTo(1200));
                Assert.That(observed.NormalCount, Is.EqualTo(4));
                Assert.That(observed.RedCount, Is.EqualTo(1));
                Assert.That(observed.NormalHours, Is.EqualTo(20));
                Assert.That(observed.RedHours, Is.EqualTo(5));
                Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
                var samePublic = new AiObservation(1200, 1200, 1200, 4, 1, 20, 5);
                Assert.That(TimeTheftAi.Decide(observed, .5, new Random(71)),
                    Is.EqualTo(TimeTheftAi.Decide(samePublic, .5, new Random(71))));
                Assert.That(TimeTheftAi.SelectClock(match.GetAvailableClockIndices(), new Random(71)),
                    Is.EqualTo(TimeTheftAi.SelectClock(new[] { 0, 1, 2, 3, 4 }, new Random(71))),
                    "비공개 순서가 달라도 같은 후보와 난수면 같은 위치를 선택한다");
            }
        }

        [Test]
        public void AiTreatsRedRationAsOpponentGainRatherThanOwnRecovery()
        {
            // 양측 수명이 1h라 징수 실제량은 어느 종류든 60분이다.
            // 빨강 쪽 숫자가 더 크면 배급 순이익이 음수이므로 징수 쪽을 더 선호해야 한다.
            var redCostsMore = new AiObservation(60, 60, 1200, 1, 1, 1, 3);
            var normalGivesMore = new AiObservation(60, 60, 1200, 1, 1, 3, 1);
            Assert.That(TimeTheftAi.Decide(redCostsMore, .5, new MiddleDecisionRandom()), Is.EqualTo(TimeAction.Collect));
            Assert.That(TimeTheftAi.Decide(normalGivesMore, .5, new MiddleDecisionRandom()), Is.EqualTo(TimeAction.Ration));
        }

        [Test]
        public void ClockSelectionRequiresCorrectTurnAndUnusedIndexAndPrecedesAction()
        {
            var match = Prepared();
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Ration), Is.False, "시계 선택 전 행동 확정 거부");
            Assert.That(match.CanSelectClock(Participant.Player, 3), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(match.SelectClock(Participant.Ai, 3), Is.False);
            Assert.That(match.SelectClock(Participant.Player, -1), Is.False);
            Assert.That(match.SelectClock(Participant.Player, 5), Is.False);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.GetAvailableClockIndices(), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(match.SelectClock(Participant.Player, 3), Is.True);
            Assert.That(match.CurrentClock, Is.EqualTo(3));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(match.GetClock(3).Face.HasValue, Is.False);
            Assert.That(match.SelectClock(Participant.Player, 4), Is.False, "같은 턴의 두 번째 시계 클릭 거부");
            Assert.That(match.CurrentClock, Is.EqualTo(3));
            Assert.That(match.CommitAction(Participant.Ai, TimeAction.Ration), Is.False);
            Assert.That(match.CommitAction(Participant.Player, TimeAction.Ration), Is.True);
            match.Settle(); match.CompleteTurn();
            Assert.That(match.GetClock(3).Visibility, Is.EqualTo(ClockVisibility.Used));
            Assert.That(match.GetClock(3).Face.HasValue, Is.False);
            Assert.That(match.GetAvailableClockIndices(), Is.EqualTo(new[] { 0, 1, 2, 4 }));
            Assert.That(match.SelectClock(Participant.Ai, 3), Is.False, "사용한 인덱스는 재선택할 수 없다");
            Assert.That(match.SelectClock(Participant.Player, 4), Is.False);
            Assert.That(match.CanSelectClock(Participant.Ai, 4), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }

        [Test]
        public void NonSequentialSelectionRevealsAndSettlesTheActualClickedClock()
        {
            var match = new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, MinimumHours = 1, MaximumHours = 12,
                ExtraRedProbability = 0 }, new DistinctHoursRandom());
            match.BeginBatch(); match.CloseAndShuffle(); match.CompleteShuffle();
            Assert.That(match.SelectClock(Participant.Player, 4), Is.True);
            Assert.That(match.CurrentClock, Is.EqualTo(4));
            Assert.That(match.GetClock(4).Face.HasValue, Is.False);
            match.CommitAction(Participant.Player, TimeAction.Collect);
            Assert.That(match.GetClock(4).Face.Value.Hours, Is.EqualTo(10));
            Assert.That(match.GetClock(4).Face.Value.Kind, Is.EqualTo(ClockKind.Normal));
            Assert.That(Enumerable.Range(0, 4).All(i => !match.GetClock(i).Face.HasValue), Is.True);
            match.Settle(); match.CompleteTurn();
            Assert.That(match.TransferredMinutes, Is.EqualTo(600));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(600));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.BankMinutes, Is.EqualTo(1800));
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.GetClock(0).Visibility, Is.EqualTo(ClockVisibility.Hidden));
            Assert.That(match.GetClock(4).Visibility, Is.EqualTo(ClockVisibility.Used));
            Act(match, TimeAction.Collect, 0);
            Assert.That(match.TransferredMinutes, Is.EqualTo(120));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1080));
            Assert.That(match.BankMinutes, Is.EqualTo(1920));
        }

        [TestCase(Participant.Player, 1, 3)]
        [TestCase(Participant.Player, 3, 1)]
        [TestCase(Participant.Ai, 1, 3)]
        [TestCase(Participant.Ai, 3, 1)]
        public void LastTwoRemainSelectableAndOnlyTheUnchosenClockIsDiscarded(
            Participant first, int chosen, int discarded)
        {
            var match = Prepared(first: first);
            Act(match, TimeAction.Ration, 4);
            Act(match, TimeAction.Ration, 0);
            Act(match, TimeAction.Ration, 2);
            Assert.That(match.UsedCount, Is.EqualTo(3));
            Assert.That(match.GetAvailableClockIndices(), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.CommitAction(match.ActiveParticipant, TimeAction.Ration), Is.False);
            Assert.That(match.CanSelectClock(match.ActiveParticipant, 1), Is.True);
            Assert.That(match.CanSelectClock(match.ActiveParticipant, 3), Is.True);
            Assert.That(match.GetClock(1).Face.HasValue || match.GetClock(3).Face.HasValue, Is.False);
            Assert.That(match.SelectClock(match.ActiveParticipant, chosen), Is.True);
            Assert.That(match.CurrentClock, Is.EqualTo(chosen));
            Assert.That(match.GetClock(chosen).Face.HasValue, Is.False);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            match.CommitAction(match.ActiveParticipant, TimeAction.Ration);
            Assert.That(match.GetClock(chosen).Face.HasValue, Is.True);
            Assert.That(match.GetClock(discarded).Visibility, Is.EqualTo(ClockVisibility.Hidden));
            Assert.That(match.GetClock(discarded).Face.HasValue, Is.False);
            match.Settle();
            Assert.That(match.GetClock(discarded).Face.HasValue, Is.False);
            match.CompleteTurn();
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            Assert.That(match.UsedCount, Is.EqualTo(4));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
            Assert.That(match.GetClock(chosen).Visibility, Is.EqualTo(ClockVisibility.Used));
            Assert.That(match.GetClock(discarded).Visibility, Is.EqualTo(ClockVisibility.Discarded));
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, discarded), Is.False);
            Assert.That(match.GetAvailableClockIndices(), Is.Empty);
            match.BeginBatch();
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.DiscardedCount, Is.Zero);
            Assert.That(Enumerable.Range(0, 5).All(i => match.GetClock(i).Face.HasValue), Is.True);
        }

        [Test]
        public void AiClockChoiceUsesOnlyCandidatesAndCanPickEitherRemainingClock()
        {
            var parameters = typeof(TimeTheftAi).GetMethod(nameof(TimeTheftAi.SelectClock), new[] { typeof(System.Collections.Generic.IReadOnlyList<int>), typeof(Random) }).GetParameters();
            Assert.That(parameters.Select(p => p.ParameterType).ToArray(),
                Is.EqualTo(new[] { typeof(IReadOnlyList<int>), typeof(Random) }));
            var counts = new int[5];
            var random = new Random(538);
            var candidates = new[] { 1, 3 };
            for (int i = 0; i < 2000; i++)
            {
                int selected = TimeTheftAi.SelectClock(candidates, random);
                Assert.That(candidates, Does.Contain(selected));
                counts[selected]++;
            }
            Assert.That(counts[1], Is.InRange(850, 1150));
            Assert.That(counts[3], Is.InRange(850, 1150));
            Assert.That(candidates, Is.EqualTo(new[] { 1, 3 }), "AI가 후보 배열을 바꾸지 않는다");
            Assert.Throws<ArgumentException>(() => TimeTheftAi.SelectClock(new int[0], random));
        }

        [Test]
        public void ManySeedGamesConserveTimeStayNonnegativeAndCanFinish()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var match = new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0 }, new Random(seed));
                var ai = new Random(seed ^ 0x51a7);
                int turns = 0;
                while (match.Stage != TurnStage.GameOver && turns++ < 2000)
                {
                    if (match.Stage == TurnStage.BatchComplete) StartBatch(match);
                    var action = TimeTheftAi.Decide(match.Observe(match.ActiveParticipant), .5, ai);
                    int index = TimeTheftAi.SelectClock(match.GetAvailableClockIndices(), ai);
                    Act(match, action, index);
                    Assert.That(match.PlayerLifeMinutes, Is.GreaterThanOrEqualTo(0));
                    Assert.That(match.AiLifeMinutes, Is.GreaterThanOrEqualTo(0));
                    Assert.That(match.BankMinutes, Is.GreaterThanOrEqualTo(0));
                    Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
                }
                Assert.That(match.Winner, Is.Not.EqualTo(GameWinner.None), "seed=" + seed);
            }
        }

        [Test]
        public void WeightedNumbersAndRuleSnapshotsAreConfigurableAndStable()
        {
            var rules = new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, MinimumHours = 2, MaximumHours = 4, NumberWeights = new[] { 0, 1, 0 } };
            var match = new TimeTheftMatch(rules, new Random(3));
            rules.NumberWeights[0] = 999;
            rules.MinimumHours = 100;
            match.BeginBatch();
            Assert.That(Enumerable.Range(0, 5).All(i => match.GetClock(i).Face.Value.Hours == 3), Is.True);
        }

        [TestCase(0, "00:00")]
        [TestCase(1, "00:01")]
        [TestCase(20, "00:20")]
        [TestCase(59, "00:59")]
        [TestCase(60, "01:00")]
        [TestCase(140, "02:20")]
        [TestCase(1440, "24:00")]
        [TestCase(2960, "49:20")]
        [TestCase(6000, "100:00")]
        public void TimeFormatKeepsIntegerMinutesAndDoesNotWrapAtTwentyFourHours(int minutes, string expected)
        {
            Assert.That(GameTime.Format(minutes), Is.EqualTo(expected));
        }

        [TestCase(10, 1, 12)]
        [TestCase(0, 1, 12)]
        [TestCase(30, 0, 12)]
        [TestCase(30, 5, 4)]
        public void InvalidRulesAreRejected(int life, int min, int max)
        {
            Assert.Throws<ArgumentException>(() => new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, InitialLifeHours = life, MinimumHours = min, MaximumHours = max }, new Random()));
        }

        [Test]
        public void InitialMinuteTotalAndClockConversionCannotOverflowIntegerStorage()
        {
            Assert.Throws<ArgumentException>(() => new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, InitialLifeHours = int.MaxValue }, new Random()));
            Assert.Throws<ArgumentException>(() => new TimeTheftMatch(new GameRules { EnableAiItemStrategy = false, InitialItemCount = 0, ItemsPerBatch = 0, MaximumHours = int.MaxValue }, new Random()));
        }
    }
}
