using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class ItemGrantRulesTests
    {
        private sealed class OrderedClockRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private sealed class CyclingItemRandom : Random
        {
            private int choice;
            public readonly List<int> Bounds = new List<int>();
            public override int Next(int maxValue)
            {
                Bounds.Add(maxValue);
                return choice++ % maxValue;
            }
        }

        private static Participant Other(Participant owner) => owner == Participant.Player ? Participant.Ai : Participant.Player;
        private static GameRules Rules() => new GameRules
        {
            EnableAiItemStrategy = false, MinimumHours = 1, MaximumHours = 1, ExtraRedProbability = 0
        };
        private static int[] Only(ItemKind kind)
        {
            var weights = new int[6];
            weights[(int)kind] = 1;
            return weights;
        }
        private static TimeTheftMatch NewMatch(GameRules rules = null, Random itemRandom = null) =>
            new TimeTheftMatch(rules ?? Rules(), new OrderedClockRandom(), itemRandom ?? new Random(193));
        private static int Total(TimeTheftMatch match) => match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes;

        private static void StartBatch(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }

        private static void Act(TimeTheftMatch match, int index = -1, TimeAction action = TimeAction.Ration)
        {
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, index < 0 ? match.GetAvailableClockIndices()[0] : index), Is.True);
            if (match.Stage == TurnStage.AwaitingKeyDecision) Assert.That(match.KeepPeekedClock(match.ActiveParticipant), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (match.Stage != TurnStage.GameOver) Assert.That(match.CompleteTurn(), Is.True);
        }

        private static void FinishBatch(TimeTheftMatch match)
        {
            int remaining = 5;
            while (match.Stage != TurnStage.BatchComplete && remaining-- > 0)
            {
                Assert.That(match.Stage, Is.Not.EqualTo(TurnStage.GameOver));
                Act(match);
            }
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            Assert.That(match.UsedCount, Is.EqualTo(4));
            Assert.That(match.DiscardedCount, Is.EqualTo(1));
        }

        [Test]
        public void DefaultPolicyStartsWithTwoPerParticipantWithoutAlsoGivingTheRepeatGrant()
        {
            var rules = new GameRules();
            Assert.That(new[] { rules.InitialItemCount, rules.ItemsPerBatch, rules.MaxHeldItems }, Is.EqualTo(new[] { 2, 1, 3 }));
            Assert.That(rules.RevealOpponentItems, Is.True);
            Assert.That(rules.ItemWeights, Is.Empty);
            var match = NewMatch(rules);
            Assert.That(match.GetInventory(Participant.Player), Is.Empty, "시계 묶음을 열기 전에는 아직 지급하지 않는다");
            Assert.That(match.LastItemGrants, Is.Empty);
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchPreview));
            foreach (var owner in new[] { Participant.Player, Participant.Ai })
            {
                var inventory = match.GetInventory(owner);
                Assert.That(inventory.Count, Is.EqualTo(2));
                Assert.That(inventory.All(item => item.Owner == owner && !item.IsConsumed && !item.IsFoundationTest), Is.True);
                var grant = match.LastItemGrants.Single(result => result.Owner == owner);
                Assert.That(grant.Batch, Is.EqualTo(1));
                Assert.That(grant.RequestedCount, Is.EqualTo(2));
                Assert.That(grant.GrantedItems.Select(item => item.Id), Is.EqualTo(inventory.Select(item => item.Id)));
                Assert.That(grant.SkippedCount, Is.Zero);
            }
            Assert.That(match.GetInventory(Participant.Player).Concat(match.GetInventory(Participant.Ai)).Select(item => item.Id).Distinct().Count(), Is.EqualTo(4));
            Assert.That(Total(match), Is.EqualTo(3600));
        }

        [TestCase(2, 1, 3, 2, 3)]
        [TestCase(5, 4, 3, 3, 3)]
        [TestCase(2, 1, 0, 0, 0)]
        [TestCase(0, 1, 3, 0, 1)]
        [TestCase(2, 0, 3, 2, 2)]
        [TestCase(int.MaxValue, int.MaxValue, 3, 3, 3)]
        public void ConfigurableCountsRespectRemainingCapacityForBothParticipants(int initial, int repeat, int cap, int firstCount, int nextCount)
        {
            var rules = Rules();
            rules.InitialItemCount = initial; rules.ItemsPerBatch = repeat; rules.MaxHeldItems = cap;
            var match = NewMatch(rules);
            StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(firstCount));
            Assert.That(match.GetInventory(Participant.Ai).Count, Is.EqualTo(firstCount));
            FinishBatch(match);
            StartBatch(match);
            foreach (var owner in new[] { Participant.Player, Participant.Ai })
            {
                Assert.That(match.GetInventory(owner).Count, Is.EqualTo(nextCount));
                var grant = match.LastItemGrants.Single(result => result.Owner == owner);
                Assert.That(grant.RequestedCount, Is.EqualTo(repeat));
                Assert.That(grant.GrantedItems.Count, Is.EqualTo(nextCount - firstCount));
                Assert.That(grant.SkippedCount, Is.EqualTo(repeat - (nextCount - firstCount)));
            }
        }

        [Test]
        public void GrantsHappenOnlyAtSuccessfulBatchStartsAndFullInventoriesDoNotDrawAgain()
        {
            var random = new CyclingItemRandom();
            var match = NewMatch(itemRandom: random);
            Assert.That(match.BeginBatch(), Is.True);
            var first = match.LastItemGrants;
            var firstIds = first.SelectMany(grant => grant.GrantedItems).Select(item => item.Id).ToArray();
            for (int i = 0; i < 3; i++) Assert.That(match.BeginBatch(), Is.False);
            Assert.That(random.Bounds.Count, Is.EqualTo(4));
            match.CloseAndShuffle(); match.CompleteShuffle();
            Assert.That(match.BeginBatch(), Is.False);
            Act(match);
            Assert.That(match.BeginBatch(), Is.False);
            Assert.That(match.LastItemGrants.SelectMany(grant => grant.GrantedItems).Select(item => item.Id), Is.EqualTo(firstIds));
            Assert.That(random.Bounds.Count, Is.EqualTo(4));
            FinishBatch(match);
            StartBatch(match);
            Assert.That(random.Bounds.Count, Is.EqualTo(6));
            Assert.That(first.SelectMany(grant => grant.GrantedItems).Select(item => item.Id), Is.EqualTo(firstIds), "지난 지급 결과는 후속 묶음에서도 동일한 스냅샷이다");
            FinishBatch(match);
            StartBatch(match);
            Assert.That(random.Bounds.Count, Is.EqualTo(6), "가득 찬 보유품에는 추첨도 추가 ID도 만들지 않는다");
            Assert.That(match.LastItemGrants.All(grant => grant.GrantedItems.Count == 0 && grant.SkippedCount == 1), Is.True);
        }

        [Test]
        public void UniformPolicyDrawsFromAllSixKindsAndDuplicateKindsKeepSeparateIds()
        {
            var random = new CyclingItemRandom();
            var rules = Rules(); rules.InitialItemCount = 3;
            var match = NewMatch(rules, random);
            StartBatch(match);
            var granted = match.LastItemGrants.SelectMany(result => result.GrantedItems).ToArray();
            Assert.That(granted.Select(item => item.Kind).OrderBy(kind => kind), Is.EqualTo(Enum.GetValues(typeof(ItemKind)).Cast<ItemKind>().OrderBy(kind => kind)));
            Assert.That(random.Bounds, Is.EqualTo(Enumerable.Repeat(6, 6)));
            rules = Rules(); rules.ItemWeights = Only(ItemKind.Screwdriver);
            var duplicate = NewMatch(rules);
            StartBatch(duplicate);
            var items = duplicate.GetInventory(Participant.Player).Concat(duplicate.GetInventory(Participant.Ai)).ToArray();
            Assert.That(items.All(item => item.Kind == ItemKind.Screwdriver), Is.True);
            Assert.That(items.Select(item => item.Id).Distinct().Count(), Is.EqualTo(4));
            Assert.That(items.All(item => item.Id > 0), Is.True);
        }

        [Test]
        public void UsedItemsLeaveCapacityWhileRejectedDuplicateUseDoesNotChangeGrantsOrBalances()
        {
            var rules = Rules(); rules.ItemWeights = Only(ItemKind.Screwdriver);
            var match = NewMatch(rules);
            StartBatch(match);
            var held = match.GetInventory(Participant.Player).ToArray();
            var granted = match.LastItemGrants.SelectMany(grant => grant.GrantedItems).Select(item => item.Id).ToArray();
            Assert.That(match.UseItem(Participant.Ai, held[0].Id).Failure, Is.EqualTo(ItemUseFailure.WrongParticipant));
            Assert.That(match.UseItem(Participant.Player, held[0].Id).Succeeded, Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.UseItem(Participant.Player, held[1].Id).Failure, Is.EqualTo(ItemUseFailure.AlreadyActive));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(held[1].Id));
            Assert.That(Total(match), Is.EqualTo(3600));
            match.EndItemPhase(Participant.Player);
            Assert.That(match.UseItem(Participant.Player, held[1].Id).Failure, Is.EqualTo(ItemUseFailure.WrongStage));
            Assert.That(match.LastItemGrants.SelectMany(grant => grant.GrantedItems).Select(item => item.Id), Is.EqualTo(granted));
            FinishBatch(match);
            StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2));
            Assert.That(match.GetInventory(Participant.Ai).Count, Is.EqualTo(3));
            Assert.That(match.GetInventory(Participant.Player).All(item => item.Id != held[0].Id), Is.True);
            Assert.That(match.LastItemGrants.SelectMany(grant => grant.GrantedItems).All(item => !granted.Contains(item.Id)), Is.True);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void AutomaticallyGrantedScissorsRemoveOneUnusedIdAndBothSlotsRefillOnlyAtNextBatch(Participant actor)
        {
            var rules = Rules(); rules.FirstParticipant = actor; rules.InitialItemCount = 1;
            rules.ItemWeights = Only(ItemKind.Scissors);
            var match = NewMatch(rules);
            StartBatch(match);
            var scissors = match.GetInventory(actor).Single();
            var target = match.GetInventory(Other(actor)).Single();
            var firstGrant = match.LastItemGrants;
            var result = match.UseItem(actor, scissors.Id);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.RemovedItem.Value.Id, Is.EqualTo(target.Id));
            Assert.That(result.RemovedItem.Value.IsConsumed, Is.True);
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetInventory(Other(actor)), Is.Empty);
            Assert.That(match.ObserveInventory(actor, Other(actor)).Count, Is.Zero);
            Assert.That(firstGrant.Single(grant => grant.Owner == Other(actor)).GrantedItems.Single().IsConsumed, Is.False, "지급 결과와 현재 보유 상태는 서로 다른 스냅샷이다");
            Assert.That(match.LastItemGrants.All(grant => grant.Batch == 1), Is.True);
            FinishBatch(match);
            StartBatch(match);
            Assert.That(match.GetInventory(actor).Count, Is.EqualTo(1));
            Assert.That(match.GetInventory(Other(actor)).Count, Is.EqualTo(1));
            Assert.That(match.GetInventory(actor).Single().Id, Is.GreaterThan(Math.Max(scissors.Id, target.Id)));
            Assert.That(Total(match), Is.EqualTo(3600));
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void HandcuffSkipsAndExtraActionsDoNotGrantItems(Participant actor)
        {
            var rules = Rules(); rules.FirstParticipant = actor; rules.ItemWeights = Only(ItemKind.Handcuffs);
            var match = NewMatch(rules);
            StartBatch(match);
            var cuffs = match.GetInventory(actor).ToArray();
            var originalIds = match.LastItemGrants.SelectMany(grant => grant.GrantedItems).Select(item => item.Id).ToArray();
            Assert.That(match.UseItem(actor, cuffs[0].Id).Succeeded, Is.True);
            Act(match);
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.Turn, Is.EqualTo(3));
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.GetInventory(actor).Single().Id, Is.EqualTo(cuffs[1].Id));
            Assert.That(match.UseItem(actor, cuffs[1].Id).Succeeded, Is.True);
            Act(match);
            Assert.That(match.Turn, Is.EqualTo(5));
            Assert.That(match.UsedCount, Is.EqualTo(2));
            Assert.That(match.GetInventory(actor), Is.Empty);
            Assert.That(match.GetInventory(Other(actor)).Count, Is.EqualTo(2));
            Assert.That(match.LastItemGrants.SelectMany(grant => grant.GrantedItems).Select(item => item.Id), Is.EqualTo(originalIds));
            FinishBatch(match);
            StartBatch(match);
            Assert.That(match.GetInventory(actor).Count, Is.EqualTo(1));
            Assert.That(match.GetInventory(Other(actor)).Count, Is.EqualTo(3));
            Assert.That(match.LastItemGrants.All(grant => grant.Batch == 2 && grant.GrantedItems.Count == 1), Is.True);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void CuffCarriedAcrossBatchDoesNotDuplicateTheNewBatchGrant(Participant actor)
        {
            var rules = Rules(); rules.FirstParticipant = Other(actor); rules.InitialItemCount = 1;
            rules.ItemWeights = Only(ItemKind.Handcuffs);
            var random = new CyclingItemRandom();
            var match = NewMatch(rules, random);
            StartBatch(match);
            int cuff = match.GetInventory(actor).Single().Id;
            Act(match); Act(match); Act(match);
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.UseItem(actor, cuff).Succeeded, Is.True);
            Act(match);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            Assert.That(match.BeginBatch(), Is.True);
            var ids = match.LastItemGrants.SelectMany(grant => grant.GrantedItems).Select(item => item.Id).ToArray();
            int draws = random.Bounds.Count;
            match.CloseAndShuffle(); match.CompleteShuffle();
            Assert.That(match.ActiveParticipant, Is.EqualTo(actor));
            Assert.That(match.UsedCount, Is.Zero);
            Assert.That(match.GetInventory(actor).Count, Is.EqualTo(1));
            Assert.That(match.GetInventory(Other(actor)).Count, Is.EqualTo(2));
            Assert.That(match.LastItemGrants.SelectMany(grant => grant.GrantedItems).Select(item => item.Id), Is.EqualTo(ids));
            Assert.That(random.Bounds.Count, Is.EqualTo(draws));
        }

        [Test]
        public void ExplicitTestInjectionAndAutomaticGrantsShareIdsButRemainDifferentSources()
        {
            var match = NewMatch();
            int injected = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.SpareKey, ItemTestUse.RecordCurrentEffect);
            StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(3));
            Assert.That(match.GetInventory(Participant.Player).Single(item => item.Id == injected).IsFoundationTest, Is.True);
            Assert.That(match.LastItemGrants.SelectMany(grant => grant.GrantedItems).All(item => item.Id != injected && !item.IsFoundationTest), Is.True);
            Assert.That(match.GetInventory(Participant.Player).Concat(match.GetInventory(Participant.Ai)).Select(item => item.Id).Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void FreshMatchResetsIdsAndGrantRecordsAndSameSeedReplaysTheSameInventory()
        {
            var rules = Rules();
            var first = NewMatch(rules, new Random(717));
            StartBatch(first);
            var original = first.LastItemGrants.SelectMany(grant => grant.GrantedItems).ToArray();
            FinishBatch(first); StartBatch(first);
            var restarted = NewMatch(rules, new Random(717));
            Assert.That(restarted.Batch, Is.Zero);
            Assert.That(restarted.LastItemGrants, Is.Empty);
            Assert.That(restarted.GetInventory(Participant.Player), Is.Empty);
            StartBatch(restarted);
            var replay = restarted.LastItemGrants.SelectMany(grant => grant.GrantedItems).ToArray();
            Assert.That(replay.Select(item => item.Id), Is.EqualTo(original.Select(item => item.Id)));
            Assert.That(replay.Select(item => item.Kind), Is.EqualTo(original.Select(item => item.Kind)));
            Assert.That(replay.Select(item => item.Owner), Is.EqualTo(original.Select(item => item.Owner)));
            Assert.That(restarted.LastItemGrants.All(grant => grant.Batch == 1), Is.True);
        }

        [Test]
        public void GrantSettingsAreCopiedBeforeTheFirstAndFollowingBatch()
        {
            var rules = Rules(); rules.ItemWeights = Only(ItemKind.Screwdriver);
            var match = NewMatch(rules);
            rules.InitialItemCount = 0; rules.ItemsPerBatch = 0; rules.MaxHeldItems = 0; rules.RevealOpponentItems = false;
            rules.ItemWeights[(int)ItemKind.Screwdriver] = 0;
            rules.ItemWeights[(int)ItemKind.Scissors] = 99;
            StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2));
            Assert.That(match.GetInventory(Participant.Player).All(item => item.Kind == ItemKind.Screwdriver), Is.True);
            Assert.That(match.ObserveInventory(Participant.Player, Participant.Ai).RevealsKinds, Is.True);
            FinishBatch(match); StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(3));
            Assert.That(match.GetInventory(Participant.Player).All(item => item.Kind == ItemKind.Screwdriver), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ItemGrantsNeverConsumeClockRandomness(bool enabled)
        {
            var rules = Rules();
            if (!enabled) { rules.InitialItemCount = 0; rules.ItemsPerBatch = 0; }
            var match = new TimeTheftMatch(rules, new Random(41), new Random(99));
            var baselineRules = Rules(); baselineRules.InitialItemCount = 0; baselineRules.ItemsPerBatch = 0;
            var baseline = new TimeTheftMatch(baselineRules, new Random(41), new Random(2));
            for (int batch = 0; batch < 3; batch++)
            {
                Assert.That(match.BeginBatch(), Is.True); Assert.That(baseline.BeginBatch(), Is.True);
                for (int i = 0; i < 5; i++)
                    Assert.That(new[] { (int)match.GetClock(i).Face.Value.Kind, match.GetClock(i).Face.Value.Hours },
                        Is.EqualTo(new[] { (int)baseline.GetClock(i).Face.Value.Kind, baseline.GetClock(i).Face.Value.Hours }));
                match.CloseAndShuffle(); match.CompleteShuffle(); baseline.CloseAndShuffle(); baseline.CompleteShuffle();
                FinishBatch(match); FinishBatch(baseline);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpponentObservationFollowsTheExplicitVisibilityPolicyWithoutIdsOrClockValues(bool reveal)
        {
            var rules = Rules(); rules.RevealOpponentItems = reveal;
            var match = NewMatch(rules);
            StartBatch(match);
            foreach (var owner in new[] { Participant.Player, Participant.Ai })
            {
                var own = match.ObserveInventory(owner, owner);
                var opponent = match.ObserveInventory(owner, Other(owner));
                Assert.That(own.Owner, Is.EqualTo(owner));
                Assert.That(own.Count, Is.EqualTo(2));
                Assert.That(own.RevealsKinds, Is.True);
                Assert.That(own.Kinds, Is.EqualTo(match.GetInventory(owner).Select(item => item.Kind)));
                Assert.That(opponent.Owner, Is.EqualTo(Other(owner)));
                Assert.That(opponent.Count, Is.EqualTo(2));
                Assert.That(opponent.RevealsKinds, Is.EqualTo(reveal));
                Assert.That(opponent.Kinds, Is.EqualTo(reveal ? match.GetInventory(Other(owner)).Select(item => item.Kind) : Enumerable.Empty<ItemKind>()));
                var ai = match.ObserveItems(owner);
                Assert.That(ai.OpponentInventory.Count, Is.EqualTo(opponent.Count));
                Assert.That(ai.OpponentInventory.Kinds, Is.EqualTo(opponent.Kinds));
                Assert.That(ai.OwnItems.All(item => item.Owner == owner), Is.True);
                Assert.That(TimeTheftAi.ChooseItem(ai), Is.Null);
            }
            Assert.That(Enumerable.Range(0, 5).All(index => !match.GetClock(index).Face.HasValue), Is.True);
            var propertyTypes = typeof(ItemInventoryObservation).GetProperties().Select(property => property.PropertyType).ToArray();
            Assert.That(propertyTypes.Any(type => type == typeof(ItemState) || type == typeof(TimeTheftMatch) || type == typeof(ClockFace) || type == typeof(ClockFace[])), Is.False);
        }

        [Test]
        public void InventoryAndGrantSnapshotsDoNotChangeAfterConsumptionOrExposeMutableCollections()
        {
            var rules = Rules(); rules.ItemWeights = Only(ItemKind.Screwdriver);
            var match = NewMatch(rules);
            StartBatch(match);
            var before = match.ObserveInventory(Participant.Player, Participant.Player);
            var aiBefore = match.ObserveItems(Participant.Ai);
            var grant = match.LastItemGrants.Single(result => result.Owner == Participant.Player);
            Assert.Throws<NotSupportedException>(() => ((IList<ItemKind>)before.Kinds).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<ItemState>)grant.GrantedItems).Clear());
            match.UseItem(Participant.Player, match.GetInventory(Participant.Player)[0].Id);
            Assert.That(before.Count, Is.EqualTo(2));
            Assert.That(before.Kinds.Count, Is.EqualTo(2));
            Assert.That(aiBefore.OpponentInventory.Count, Is.EqualTo(2));
            Assert.That(grant.GrantedItems.Count, Is.EqualTo(2));
            Assert.That(grant.GrantedItems.All(item => !item.IsConsumed), Is.True);
            Assert.That(match.ObserveInventory(Participant.Ai, Participant.Player).Count, Is.EqualTo(1));
            Assert.That(match.ObserveItems(Participant.Ai).OpponentInventory.Count, Is.EqualTo(1));
        }

        [Test]
        public void DisabledGrantsPreserveEmptyAutomaticAAndDoNotGrantAfterGameOver()
        {
            var rules = Rules(); rules.InitialItemCount = 0; rules.ItemsPerBatch = 0;
            rules.InitialLifeHours = 11;
            var match = NewMatch(rules);
            StartBatch(match);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
            Act(match, 0, TimeAction.Collect);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.GameOver));
            Assert.That(match.BeginBatch(), Is.False);
            Assert.That(match.CompleteTurn(), Is.False);
            Assert.That(match.LastItemGrants.All(grant => grant.GrantedItems.Count == 0), Is.True);
            Assert.That(Total(match), Is.EqualTo(1320));
        }

        [TestCase(-1, 1, 3)]
        [TestCase(2, -1, 3)]
        [TestCase(2, 1, -1)]
        public void NegativeGrantCountsOrCapacityAreRejected(int initial, int repeat, int cap)
        {
            var rules = Rules(); rules.InitialItemCount = initial; rules.ItemsPerBatch = repeat; rules.MaxHeldItems = cap;
            Assert.Throws<ArgumentException>(() => NewMatch(rules));
        }

        private static IEnumerable<TestCaseData> InvalidWeights()
        {
            yield return new TestCaseData(new object[] { new[] { 1 } });
            yield return new TestCaseData(new object[] { new[] { 1, 1, 1, 1, 1, 1, 1 } });
            yield return new TestCaseData(new object[] { new[] { 1, 0, -1, 0, 0, 0 } });
            yield return new TestCaseData(new object[] { new int[6] });
            yield return new TestCaseData(new object[] { new[] { int.MaxValue, 1, 0, 0, 0, 0 } });
        }

        [TestCaseSource(nameof(InvalidWeights))]
        public void InvalidWeightShapeValuesOrTotalAreRejected(int[] weights)
        {
            var rules = Rules(); rules.ItemWeights = weights;
            Assert.Throws<ArgumentException>(() => NewMatch(rules));
        }
    }
}
