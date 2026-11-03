using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class AiItemStrategyTests
    {
        private sealed class OrderedRandom : Random
        {
            private readonly bool shuffle;
            public OrderedRandom(bool shuffle = false) { this.shuffle = shuffle; }
            public override int Next(int maxValue) => shuffle ? 0 : maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        // 계획 쌍에는 양의 잡음, 이어 평가하는 단일 계획에는 음의 잡음을 준다.
        private sealed class PairFirstNoise : Random
        {
            private int calls;
            public override double NextDouble() => calls++ % 2 == 0 ? 1 : 0;
        }

        private static GameRules Rules(Participant actor = Participant.Ai, int life = 30, int hours = 8, bool redExtras = false) =>
            new GameRules { InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = actor, InitialLifeHours = life,
                MinimumHours = hours, MaximumHours = hours, ExtraRedProbability = redExtras ? 1 : 0,
                EnableAiItemStrategy = true, AiDecisionNoise = 0, AiItemConservation = 0 };
        private static Participant Other(Participant actor) => actor == Participant.Player ? Participant.Ai : Participant.Player;
        private static TimeTheftMatch Create(GameRules rules, bool shuffle = false) => new TimeTheftMatch(rules, new OrderedRandom(shuffle), new Random(51));
        private static int Give(TimeTheftMatch match, Participant actor, ItemKind kind) => TimeTheftItemTestSetup.Give(match, actor, kind);
        private static void StartBatch(TimeTheftMatch match)
        { Assert.That(match.BeginBatch(), Is.True); Assert.That(match.CloseAndShuffle(), Is.True); Assert.That(match.CompleteShuffle(), Is.True); }
        private static void Act(TimeTheftMatch match, int index, TimeAction action = TimeAction.Ration)
        {
            var actor = match.ActiveParticipant;
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(actor), Is.True);
            Assert.That(match.SelectClock(actor, index), Is.True);
            if (match.Stage == TurnStage.AwaitingKeyDecision) Assert.That(match.KeepPeekedClock(actor), Is.True);
            Assert.That(match.CommitAction(actor, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (match.Stage != TurnStage.GameOver) Assert.That(match.CompleteTurn(), Is.True);
        }
        private static void LeaveOnlyNormal(TimeTheftMatch match) { Act(match, 2); Act(match, 0); }
        private static void LeaveOnlyRed(TimeTheftMatch match) { Act(match, 0); Act(match, 1); }

        [TestCase(5, false, false)]
        [TestCase(4, false, false)]
        [TestCase(3, false, false)]
        [TestCase(2, false, true)]
        [TestCase(5, true, false)]
        [TestCase(4, true, false)]
        [TestCase(3, true, true)]
        [TestCase(2, true, true)]
        public void EmptyOpponentDuckDecisionMatchesRefillBeforeTheNextActualA(int remaining, bool cuffs, bool refills)
        {
            // 상대 0개와 합법 오리 상태를 유지한 채 실제 정산 명령으로 경계 직전까지 진행한다.
            int used = 5 - remaining;
            var rules = Rules(used % 2 == 0 ? Participant.Ai : Participant.Player, hours: 1);
            rules.ItemsPerBatch = 1;
            var match = Create(rules);
            int duck = Give(match, Participant.Ai, ItemKind.RubberDuck);
            int handcuffs = cuffs ? Give(match, Participant.Ai, ItemKind.Handcuffs) : 0;
            StartBatch(match);
            for (int i = 0; i < used; i++) Act(match, i);
            if (cuffs) Assert.That(match.UseItem(Participant.Ai, handcuffs).Succeeded, Is.True);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.RemainingFaces.Count, Is.EqualTo(remaining));
            Assert.That(info.OpponentInventory.Count, Is.Zero);
            Assert.That(info.UsableItemIds, Does.Contain(duck), "상대 0개여도 사용 규칙상 합법");
            Assert.That(TimeTheftAi.ChooseItem(info), refills ? Is.EqualTo(duck) : Is.Null,
                "봉쇄할 다음 실제 A 전에 지급되는 경우만 빈 상대에게 사용한다");

            // AI 예측과 독립적으로, 실제 규칙은 오리 사용을 허용한다. 실제 발동 A의 보유량을 대조한다.
            Assert.That(match.UseItem(Participant.Ai, duck).Succeeded, Is.True);
            int batch = match.Batch;
            Act(match, used);
            if (match.Stage == TurnStage.BatchComplete) StartBatch(match);
            if (match.ActiveParticipant == Participant.Ai)
            {
                Assert.That(cuffs, Is.True);
                Assert.That(match.IsItemUseBlocked(Participant.Player), Is.False);
                Assert.That(match.GetPendingItemEffects(Participant.Ai).Any(e => e.Source.Kind == ItemKind.RubberDuck), Is.True);
                Act(match, match.ObserveItems(Participant.Ai).AvailableClockIndices[0]);
                if (match.Stage == TurnStage.BatchComplete) StartBatch(match);
            }
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.True);
            Assert.That(match.Batch > batch, Is.EqualTo(refills));
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(refills ? 1 : 0));
        }

        [TestCase(0, 3)]
        [TestCase(3, 0)]
        public void EmptyOpponentWithoutPossibleRefillPreservesDuckAtTheBatchBoundary(int perBatch, int capacity)
        {
            var rules = Rules(Participant.Player, hours: 1);
            rules.ItemsPerBatch = perBatch;
            rules.MaxHeldItems = capacity;
            var match = Create(rules);
            int duck = Give(match, Participant.Ai, ItemKind.RubberDuck);
            StartBatch(match);
            for (int i = 0; i < 3; i++) Act(match, i);
            Assert.That(match.ObserveItems(Participant.Ai).RemainingFaces.Count, Is.EqualTo(2));
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Ai)), Is.Null);
            Act(match, 3); StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
        }

        [TestCase(false, .08f, .35f)]
        [TestCase(true, .08f, .35f)]
        [TestCase(false, 1f, 0f)]
        [TestCase(true, 1f, 0f)]
        public void ValuableHandcuffsPlanMustNotSpendAZeroBenefitDuckEvenWithNoise(bool refillEnabled, float noise, float conservation)
        {
            var rules = Rules(hours: 1);
            rules.ItemsPerBatch = refillEnabled ? 3 : 0;
            rules.AiDecisionNoise = noise;
            rules.AiItemConservation = conservation;
            var match = Create(rules);
            int duck = Give(match, Participant.Ai, ItemKind.RubberDuck);
            int cuffs = Give(match, Participant.Ai, ItemKind.Handcuffs);
            StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Ai), new PairFirstNoise()), Is.EqualTo(cuffs),
                "수갑의 이득이나 계획별 잡음이 기여도 0인 오리까지 사용하게 해서는 안 된다");
            Assert.That(UseChosenItems(match, Participant.Ai), Is.EqualTo(new[] { ItemKind.Handcuffs }));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(duck));
        }

        [Test]
        public void ScissorsRemovingTheLastTargetReevaluatesDuckBeforeAnotherPlan()
        {
            var rules = Rules(hours: 1);
            rules.AiDecisionNoise = 1;
            var match = Create(rules);
            int scissors = Give(match, Participant.Ai, ItemKind.Scissors);
            int duck = Give(match, Participant.Ai, ItemKind.RubberDuck);
            int cuffs = Give(match, Participant.Ai, ItemKind.Handcuffs);
            int target = Give(match, Participant.Player, ItemKind.Handcuffs);
            StartBatch(match);
            var result = match.UseItem(Participant.Ai, scissors);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.RemovedItem.Value.Id, Is.EqualTo(target));
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Ai), new PairFirstNoise()), Is.EqualTo(cuffs));
            Assert.That(UseChosenItems(match, Participant.Ai), Is.EqualTo(new[] { ItemKind.Handcuffs }));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(duck));
        }

        [Test]
        public void DuckAndHandcuffsPlanCanBlockItemsGrantedAcrossTheBoundary()
        {
            var rules = Rules(hours: 1);
            rules.ItemsPerBatch = 1;
            var match = Create(rules);
            Give(match, Participant.Ai, ItemKind.RubberDuck);
            Give(match, Participant.Ai, ItemKind.Handcuffs);
            StartBatch(match);
            Act(match, 0); Act(match, 1);
            Assert.That(match.ObserveItems(Participant.Ai).RemainingFaces.Count, Is.EqualTo(3));
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(UseChosenItems(match, Participant.Ai),
                Is.EquivalentTo(new[] { ItemKind.RubberDuck, ItemKind.Handcuffs }));
            Act(match, 2);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.UsedCount, Is.EqualTo(3), "수갑 생략은 시계를 쓰지 않는다");
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.False);
            Act(match, 3); StartBatch(match);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(1));
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.True);
        }

        private static List<ItemKind> UseChosenItems(TimeTheftMatch match, Participant actor, int seed = 4)
        {
            var used = new List<ItemKind>();
            var seen = new HashSet<int>();
            var random = new Random(seed);
            int limit = match.GetInventory(actor).Count;
            for (int i = 0; i <= limit; i++)
            {
                var observation = match.ObserveItems(actor);
                var id = TimeTheftAi.ChooseItem(observation, random);
                if (!id.HasValue) return used;
                Assert.That(observation.UsableItemIds.Contains(id.Value), Is.True, "정책은 모델이 검증한 자기 합법 ID만 선택한다");
                Assert.That(seen.Add(id.Value), Is.True, "같은 ID를 다시 시도하지 않는다");
                Assert.That(i, Is.LessThan(limit), "보유 수보다 더 많은 사용을 시도하지 않는다");
                var result = match.UseItem(actor, id.Value);
                Assert.That(result.Succeeded, Is.True);
                used.Add(result.Effect.Value.Source.Kind);
            }
            Assert.Fail("보유 수 이내에 A 판단이 수렴해야 한다");
            return used;
        }

        [TestCase(ItemKind.SpareKey, true)]
        [TestCase(ItemKind.SpareKey, false)]
        [TestCase(ItemKind.Screwdriver, true)]
        [TestCase(ItemKind.Screwdriver, false)]
        [TestCase(ItemKind.TimeBypass, true)]
        [TestCase(ItemKind.TimeBypass, false)]
        [TestCase(ItemKind.Scissors, true)]
        [TestCase(ItemKind.Scissors, false)]
        [TestCase(ItemKind.RubberDuck, true)]
        [TestCase(ItemKind.RubberDuck, false)]
        [TestCase(ItemKind.Handcuffs, true)]
        [TestCase(ItemKind.Handcuffs, false)]
        public void EachItemHasAUsefulSituationAndAPreservationSituation(ItemKind kind, bool use)
        {
            bool lowLife = use && (kind == ItemKind.SpareKey || kind == ItemKind.Screwdriver);
            bool allRed = !use && (kind == ItemKind.TimeBypass || kind == ItemKind.Handcuffs);
            var rules = Rules(life: lowLife ? 11 : kind == ItemKind.Handcuffs && !use ? 100 : 30,
                hours: kind == ItemKind.Handcuffs && !use ? 12 : 8, redExtras: lowLife || allRed);
            var match = Create(rules);
            int id = Give(match, Participant.Ai, kind);
            if (use && (kind == ItemKind.Scissors || kind == ItemKind.RubberDuck))
                foreach (var threat in new[] { ItemKind.Handcuffs, ItemKind.SpareKey, ItemKind.TimeBypass }) Give(match, Participant.Player, threat);
            StartBatch(match);
            if (kind == ItemKind.Handcuffs && !use)
            {
                for (int i = 0; i < 4; i++) Act(match, i);
                StartBatch(match);
                LeaveOnlyRed(match);
                Assert.That(match.BankMinutes, Is.Zero);
            }
            else if ((!use && (kind == ItemKind.SpareKey || kind == ItemKind.Screwdriver)) ||
                (use && (kind == ItemKind.TimeBypass || kind == ItemKind.Handcuffs))) LeaveOnlyNormal(match);
            else if (kind == ItemKind.TimeBypass && !use) LeaveOnlyRed(match);
            var observation = match.ObserveItems(Participant.Ai);
            var before = match.GetInventory(Participant.Ai).ToArray();
            var chosen = TimeTheftAi.ChooseItem(observation, new Random(8));
            if (use) Assert.That(chosen, Is.EqualTo(id), kind + " 사용 가치가 있는 상황");
            else Assert.That(chosen, Is.Null, kind + " 보존하거나 사용 불가인 상황");
            Assert.That(match.GetInventory(Participant.Ai), Is.EqualTo(before), "평가는 Match를 변경하지 않는다");
        }

        [TestCase(ItemKind.Scissors)]
        [TestCase(ItemKind.RubberDuck)]
        [TestCase(ItemKind.Handcuffs)]
        public void FutureDenialItemsArePreservedWhenTheCurrentCollectionCertainlyWins(ItemKind kind)
        {
            var match = Create(Rules(life: 11));
            int id = Give(match, Participant.Ai, kind);
            foreach (var threat in new[] { ItemKind.Handcuffs, ItemKind.SpareKey, ItemKind.Screwdriver })
                Give(match, Participant.Player, threat);
            StartBatch(match);
            LeaveOnlyNormal(match);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.RemainingFaces.Count, Is.EqualTo(3));
            Assert.That(info.RemainingFaces.All(face => face.Kind == ClockKind.Normal &&
                face.Hours * GameTime.MinutesPerHour >= info.PublicGame.OpponentLifeMinutes), Is.True);
            Assert.That(info.OpponentInventory.Count, Is.EqualTo(3));
            Assert.That(info.UsableItemIds.Contains(id), Is.True, "보존은 불법 대상이나 중복 제한 때문이 아니다");
            Assert.That(TimeTheftAi.ChooseItem(info, new Random(8)), Is.Null,
                "이번 정산에서 승리가 확정이면 미래 차례 견제에 소모하지 않는다");
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(id));
            Assert.That(match.EndItemPhase(Participant.Ai), Is.True);
            int index = TimeTheftAi.SelectClock(match.ObserveItems(Participant.Ai), new Random(8));
            Assert.That(match.SelectClock(Participant.Ai, index), Is.True);
            var action = TimeTheftAi.Decide(match.ObserveItems(Participant.Ai), new Random(8));
            Assert.That(action, Is.EqualTo(TimeAction.Collect));
            Assert.That(match.CommitAction(Participant.Ai, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.Winner, Is.EqualTo(GameWinner.Ai));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(id));
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(3));
            Assert.That(match.GetPendingItemEffects(Participant.Ai), Is.Empty);
        }

        [TestCase(Participant.Player)]
        [TestCase(Participant.Ai)]
        public void PolicyUsesOnlyItsOwnLegalInventoryAndStopsAfterBOrDuckRestriction(Participant actor)
        {
            var match = Create(Rules(actor, life: 11, redExtras: true));
            int driver = Give(match, actor, ItemKind.Screwdriver);
            Give(match, Other(actor), ItemKind.Handcuffs);
            StartBatch(match);
            var info = match.ObserveItems(actor);
            Assert.That(TimeTheftAi.ChooseItem(info, new Random(3)), Is.EqualTo(driver));
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Other(actor)), new Random(3)), Is.Null);
            Assert.That(match.UseItem(actor, driver).Succeeded, Is.True);
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(actor), new Random(3)), Is.Null);
            Assert.That(match.UseItem(actor, driver).Succeeded, Is.False);

            var blocked = Create(Rules(Other(actor)));
            int duck = Give(blocked, Other(actor), ItemKind.RubberDuck);
            Give(blocked, actor, ItemKind.SpareKey);
            StartBatch(blocked);
            Assert.That(blocked.UseItem(Other(actor), duck).Succeeded, Is.True);
            Act(blocked, 0);
            Assert.That(blocked.IsItemUseBlocked(actor), Is.True);
            Assert.That(TimeTheftAi.ChooseItem(blocked.ObserveItems(actor), new Random(3)), Is.Null);
        }

        [TestCase(ItemKind.Screwdriver, ItemKind.TimeBypass)]
        [TestCase(ItemKind.Scissors, ItemKind.Handcuffs)]
        [TestCase(ItemKind.RubberDuck, ItemKind.Handcuffs)]
        public void ValuablePairsUseSequentiallyWithoutRetryingConsumedIds(ItemKind first, ItemKind second)
        {
            var match = Create(Rules(life: first == ItemKind.Screwdriver ? 18 : 30));
            Give(match, Participant.Ai, first); Give(match, Participant.Ai, second);
            foreach (var threat in new[] { ItemKind.Handcuffs, ItemKind.SpareKey, ItemKind.Screwdriver }) Give(match, Participant.Player, threat);
            StartBatch(match);
            // 일반 시계가 즉시 승리라면 우회선 추가 이득이 없다. 양측 수명을 올려 두 정산 아이템의 한계 이득을 검사한다.
            if (first == ItemKind.Screwdriver) { Act(match, 0); Act(match, 1); }
            Assert.That(UseChosenItems(match, Participant.Ai), Is.EquivalentTo(new[] { first, second }));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
            if (first == ItemKind.RubberDuck)
            {
                Act(match, 0);
                Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
                Assert.That(match.UsedCount, Is.EqualTo(1));
                Assert.That(match.GetPendingItemEffects(Participant.Ai).Single().Source.Kind, Is.EqualTo(ItemKind.RubberDuck));
                Act(match, 1);
                Assert.That(match.IsItemUseBlocked(Participant.Player), Is.True);
            }
        }

        [Test]
        public void KeyAndSettlementItemsAreConsideredAsAPlanAndTheDecisionLoopIsBounded()
        {
            var match = Create(Rules(life: 18, redExtras: true));
            foreach (var kind in new[] { ItemKind.SpareKey, ItemKind.Screwdriver, ItemKind.TimeBypass }) Give(match, Participant.Ai, kind);
            StartBatch(match);
            var used = UseChosenItems(match, Participant.Ai);
            Assert.That(used.Contains(ItemKind.SpareKey), Is.True);
            Assert.That(used.Contains(ItemKind.Screwdriver) || used.Contains(ItemKind.TimeBypass), Is.True);
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(Participant.Ai), Is.True);
            int index = TimeTheftAi.SelectClock(match.ObserveItems(Participant.Ai), new Random(5));
            Assert.That(match.SelectClock(Participant.Ai, index), Is.True);
            if (match.Stage == TurnStage.AwaitingKeyDecision)
            {
                int chosen = TimeTheftAi.ChooseKeyClock(match.ObserveItems(Participant.Ai), new Random(5));
                Assert.That(chosen == index ? match.KeepPeekedClock(Participant.Ai) : match.ReselectPeekedClock(Participant.Ai, chosen), Is.True);
            }
            Assert.That(match.CommitAction(Participant.Ai, TimeTheftAi.Decide(match.ObserveItems(Participant.Ai), new Random(5))), Is.True);
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(2160));
        }

        [TestCase(ClockKind.Normal, TimeAction.Collect)]
        [TestCase(ClockKind.Red, TimeAction.Collect)]
        [TestCase(ClockKind.Normal, TimeAction.Ration)]
        [TestCase(ClockKind.Red, TimeAction.Ration)]
        public void PredictionMatchesActualCappedSettlementForBothActorsAndActiveItemCombinations(ClockKind kind, TimeAction action)
        {
            foreach (var actor in new[] { Participant.Player, Participant.Ai })
            for (int mask = 0; mask < 4; mask++)
            {
                var match = Create(Rules(actor, life: 13));
                var ids = new List<int>();
                if ((mask & 1) != 0) ids.Add(Give(match, actor, ItemKind.Screwdriver));
                if ((mask & 2) != 0) ids.Add(Give(match, actor, ItemKind.TimeBypass));
                StartBatch(match);
                foreach (int id in ids) Assert.That(match.UseItem(actor, id).Succeeded, Is.True);
                if (match.Stage == TurnStage.AwaitingItems) match.EndItemPhase(actor);
                Assert.That(match.SelectClock(actor, kind == ClockKind.Red ? 2 : 0), Is.True);
                Assert.That(match.CommitAction(actor, action), Is.True);
                var prediction = TimeTheftAi.Estimate(match.ObserveItems(actor), match.GetClock(match.CurrentClock).Face.Value, action);
                Assert.That(match.Settle(), Is.True);
                Assert.That(prediction.OwnLifeMinutes, Is.EqualTo(actor == Participant.Player ? match.PlayerLifeMinutes : match.AiLifeMinutes));
                Assert.That(prediction.OpponentLifeMinutes, Is.EqualTo(actor == Participant.Player ? match.AiLifeMinutes : match.PlayerLifeMinutes));
                Assert.That(prediction.BankMinutes, Is.EqualTo(match.BankMinutes));
                Assert.That(prediction.TransferredMinutes, Is.EqualTo(match.TransferredMinutes));
                Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(1560));
            }
        }

        [Test]
        public void SharedPredictionCapsBeforeReductionAndRoundsDriverThenBypassInMinutes()
        {
            var odd = TimeTheftSettlement.Calculate(new ClockFace(8, ClockKind.Red), TimeAction.Collect, 61, 1200, 0, true, true, 50, 50);
            Assert.That(new[] { odd.PreventedMinutes, odd.TransferredMinutes, odd.BypassedMinutes, odd.OwnLifeMinutes, odd.OpponentLifeMinutes, odd.BankMinutes },
                Is.EqualTo(new[] { 30, 31, 15, 30, 1215, 16 }));
            var zero = TimeTheftSettlement.Calculate(new ClockFace(8, ClockKind.Red), TimeAction.Collect, 61, 1200, 0, true, true, 100, 50);
            Assert.That(zero.TransferredMinutes, Is.Zero);
            Assert.That(zero.BypassedMinutes, Is.Zero);
            Assert.That(zero.OwnLifeMinutes + zero.OpponentLifeMinutes + zero.BankMinutes, Is.EqualTo(1261));
            var ration = TimeTheftSettlement.Calculate(new ClockFace(8, ClockKind.Red), TimeAction.Ration, 61, 1200, 19, true, true, 50, 50);
            Assert.That(ration.TransferredMinutes, Is.EqualTo(19));
            Assert.That(ration.DriverTriggered || ration.BypassTriggered, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KeyUsesOnlyItsFirstPrivatePeekAndCanKeepOrReselect(bool red)
        {
            var match = Create(Rules(life: 11, redExtras: true));
            int key = Give(match, Participant.Ai, ItemKind.SpareKey);
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Ai, key).Succeeded, Is.True);
            int first = red ? 2 : 0;
            Assert.That(match.SelectClock(Participant.Ai, first), Is.True);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.KnownClocks.Single().Index, Is.EqualTo(first));
            int chosen = TimeTheftAi.ChooseKeyClock(info, new Random(3));
            if (!red)
            {
                Assert.That(chosen, Is.EqualTo(first), "일반 징수로 즉시 승리 가능한 시계는 유지한다");
                Assert.That(match.KeepPeekedClock(Participant.Ai), Is.True);
                Assert.That(TimeTheftAi.Decide(match.ObserveItems(Participant.Ai), new Random(3)), Is.EqualTo(TimeAction.Collect));
            }
            else
            {
                Assert.That(chosen, Is.Not.EqualTo(first), "확인된 빨강보다 미확인 대안의 기대 손익이 낫다");
                Assert.That(match.ReselectPeekedClock(Participant.Ai, chosen), Is.True);
                Assert.That(match.GetPrivateClock(Participant.Ai, chosen), Is.Null);
                Assert.That(match.ReselectPeekedClock(Participant.Ai, first), Is.False);
            }
            Assert.That(match.GetPrivateClock(Participant.Player, first), Is.Null);
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CDecisionUsesDriverRiskReductionAndRedBypassPaysTheOpponent(bool bypass)
        {
            var match = Create(Rules(life: 11, redExtras: true));
            int key = Give(match, Participant.Ai, ItemKind.SpareKey);
            int driver = Give(match, Participant.Ai, ItemKind.Screwdriver);
            int wire = bypass ? Give(match, Participant.Ai, ItemKind.TimeBypass) : 0;
            StartBatch(match);
            match.UseItem(Participant.Ai, key); match.UseItem(Participant.Ai, driver);
            if (bypass) match.UseItem(Participant.Ai, wire);
            match.SelectClock(Participant.Ai, 2); match.KeepPeekedClock(Participant.Ai);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(TimeTheftAi.Decide(info, new Random(4)), Is.EqualTo(TimeAction.Collect));
            var collect = TimeTheftAi.Estimate(info, info.KnownClocks.Single().Face, TimeAction.Collect);
            Assert.That(collect.TransferredMinutes, Is.EqualTo(30));
            Assert.That(collect.OpponentLifeMinutes, Is.EqualTo(bypass ? 75 : 60));
            Assert.That(collect.BankMinutes, Is.EqualTo(bypass ? 1215 : 1230));
        }

        [Test]
        public void CanonicalPublicDistributionCannotEncodeHiddenShufflePositions()
        {
            var a = Create(Rules(life: 18)); var b = Create(Rules(life: 18), shuffle: true);
            foreach (var kind in new[] { ItemKind.SpareKey, ItemKind.Screwdriver, ItemKind.TimeBypass }) { Give(a, Participant.Ai, kind); Give(b, Participant.Ai, kind); }
            StartBatch(a); StartBatch(b);
            var one = a.ObserveItems(Participant.Ai); var two = b.ObserveItems(Participant.Ai);
            Assert.That(one.RemainingFaces, Is.EqualTo(two.RemainingFaces));
            Assert.That(one.KnownClocks, Is.Empty);
            Assert.That(TimeTheftAi.ChooseItem(one, new Random(66)), Is.EqualTo(TimeTheftAi.ChooseItem(two, new Random(66))));
            a.EndItemPhase(Participant.Ai); b.EndItemPhase(Participant.Ai);
            Assert.That(TimeTheftAi.SelectClock(a.ObserveItems(Participant.Ai), new Random(67)), Is.EqualTo(TimeTheftAi.SelectClock(b.ObserveItems(Participant.Ai), new Random(67))));
            Assert.That(Enumerable.Range(0, 5).All(i => !a.GetClock(i).Face.HasValue && !b.GetClock(i).Face.HasValue), Is.True);
        }

        [Test]
        public void HiddenOpponentKindsDoNotAffectAChoice()
        {
            var rules = Rules(); rules.RevealOpponentItems = false;
            var a = Create(rules); var b = Create(rules);
            foreach (var kind in new[] { ItemKind.Scissors, ItemKind.RubberDuck }) { Give(a, Participant.Ai, kind); Give(b, Participant.Ai, kind); }
            foreach (var kind in new[] { ItemKind.SpareKey, ItemKind.Screwdriver, ItemKind.TimeBypass }) Give(a, Participant.Player, kind);
            foreach (var kind in new[] { ItemKind.Handcuffs, ItemKind.Handcuffs, ItemKind.Handcuffs }) Give(b, Participant.Player, kind);
            StartBatch(a); StartBatch(b);
            var one = a.ObserveItems(Participant.Ai); var two = b.ObserveItems(Participant.Ai);
            Assert.That(one.OpponentInventory.Count, Is.EqualTo(3));
            Assert.That(one.OpponentInventory.Kinds, Is.Empty); Assert.That(two.OpponentInventory.Kinds, Is.Empty);
            Assert.That(TimeTheftAi.ChooseItem(one, new Random(5)), Is.EqualTo(TimeTheftAi.ChooseItem(two, new Random(5))));
        }

        [Test]
        public void OpponentsPrivatePeekIsAbsentFromTheAiSnapshot()
        {
            var a = Create(Rules(Participant.Player)); var b = Create(Rules(Participant.Player));
            foreach (var match in new[] { a, b })
            {
                Give(match, Participant.Player, ItemKind.SpareKey); Give(match, Participant.Player, ItemKind.Handcuffs);
                Give(match, Participant.Ai, ItemKind.Scissors); Give(match, Participant.Ai, ItemKind.RubberDuck);
                StartBatch(match);
                Assert.That(match.UseItem(Participant.Player, 1).Succeeded, Is.True);
                Assert.That(match.EndItemPhase(Participant.Player), Is.True, "미사용 수갑을 보존하고 B로 진행한다");
            }
            Assert.That(a.SelectClock(Participant.Player, 0), Is.True);
            Assert.That(b.SelectClock(Participant.Player, 2), Is.True);
            Assert.That(a.ReselectPeekedClock(Participant.Player, 1), Is.True);
            Assert.That(b.ReselectPeekedClock(Participant.Player, 1), Is.True);
            foreach (var match in new[] { a, b })
            {
                Assert.That(match.CommitAction(Participant.Player, TimeAction.Ration), Is.True);
                Assert.That(match.Settle(), Is.True);
                Assert.That(match.CompleteTurn(), Is.True);
            }
            Assert.That(a.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
            Assert.That(b.GetPrivateClock(Participant.Player, 2).HasValue, Is.True);
            var one = a.ObserveItems(Participant.Ai); var two = b.ObserveItems(Participant.Ai);
            Assert.That(one.KnownClocks, Is.Empty); Assert.That(two.KnownClocks, Is.Empty);
            Assert.That(one.RemainingFaces, Is.EqualTo(two.RemainingFaces));
            Assert.That(TimeTheftAi.ChooseItem(one, new Random(9)), Is.EqualTo(TimeTheftAi.ChooseItem(two, new Random(9))));
        }

        [Test]
        public void KnowledgeAndDistributionAreReadOnlySnapshotsAndDoNotLeakToNewBatches()
        {
            var match = Create(Rules()); int key = Give(match, Participant.Ai, ItemKind.SpareKey);
            StartBatch(match); match.UseItem(Participant.Ai, key); match.SelectClock(Participant.Ai, 0);
            var snapshot = match.ObserveItems(Participant.Ai);
            Assert.Throws<NotSupportedException>(() => ((IList<ClockFace>)snapshot.RemainingFaces).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<AiKnownClock>)snapshot.KnownClocks).Clear());
            match.ReselectPeekedClock(Participant.Ai, 1);
            match.CommitAction(Participant.Ai, TimeAction.Ration); match.Settle(); match.CompleteTurn();
            Act(match, 2); Act(match, 3); Act(match, 4);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            StartBatch(match);
            Assert.That(match.ObserveItems(Participant.Ai).KnownClocks, Is.Empty);
            Assert.That(snapshot.KnownClocks.Single().Index, Is.Zero);
            Assert.That(snapshot.RemainingFaces.Count, Is.EqualTo(5));
            Assert.That(match.ObserveItems(Participant.Ai).RemainingFaces.Count, Is.EqualTo(5));
        }

        [Test]
        public void TuningSnapshotAndSeededChoicesAreStableWithoutMutatingRulesOrMatch()
        {
            var rules = Rules(life: 18); rules.AiDecisionNoise = .2f; rules.AiItemConservation = .4f;
            var match = Create(rules); Give(match, Participant.Ai, ItemKind.SpareKey); Give(match, Participant.Ai, ItemKind.Screwdriver);
            rules.EnableAiItemStrategy = false; rules.AiDecisionNoise = 1; rules.AiItemConservation = 1; rules.ScrewdriverReductionPercent = 0;
            StartBatch(match);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.Strategy.Enabled, Is.True);
            Assert.That(info.Strategy.DecisionNoise, Is.EqualTo(.2).Within(.000001));
            Assert.That(info.Strategy.ItemConservation, Is.EqualTo(.4).Within(.000001));
            Assert.That(info.Strategy.DriverReductionPercent, Is.EqualTo(50));
            var inventory = match.GetInventory(Participant.Ai).ToArray();
            for (int seed = 0; seed < 20; seed++)
                Assert.That(TimeTheftAi.ChooseItem(info, new Random(seed)), Is.EqualTo(TimeTheftAi.ChooseItem(info, new Random(seed))));
            Assert.That(match.GetInventory(Participant.Ai), Is.EqualTo(inventory));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
        }

        [TestCase(-.1f, 0)]
        [TestCase(1.1f, 0)]
        [TestCase(float.NaN, 0)]
        [TestCase(float.PositiveInfinity, 0)]
        [TestCase(-.1f, 1)]
        [TestCase(1.1f, 1)]
        [TestCase(float.NaN, 1)]
        [TestCase(float.PositiveInfinity, 1)]
        public void InvalidAiTuningIsRejected(float value, int field)
        {
            var rules = Rules();
            if (field == 0) rules.AiDecisionNoise = value; else rules.AiItemConservation = value;
            Assert.Throws<ArgumentException>(() => Create(rules));
        }
    }
}
