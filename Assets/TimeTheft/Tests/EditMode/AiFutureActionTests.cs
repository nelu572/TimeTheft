using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class AiFutureActionTests
    {
        private sealed class OrderedClockRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        private sealed class SplitRandom : Random
        {
            private readonly Random values, positions;
            public SplitRandom(int valueSeed, int positionSeed)
            { values = new Random(valueSeed); positions = new Random(positionSeed); }
            public override int Next(int maxValue) => positions.Next(maxValue);
            public override int Next(int minValue, int maxValue) => values.Next(minValue, maxValue);
            public override double NextDouble() => values.NextDouble();
        }

        private sealed class ItemDrawRandom : Random
        {
            private readonly int[] values;
            private int index;
            public ItemDrawRandom(params int[] values) { this.values = values; }
            public override int Next(int maxValue)
            {
                Assert.That(index, Is.LessThan(values.Length), "검증용 지급 난수의 예정된 추첨 횟수");
                int value = values[index++];
                Assert.That(value, Is.InRange(0, maxValue - 1));
                return value;
            }
        }

        private sealed class CountingRandom : Random
        {
            public int Calls { get; private set; }
            public CountingRandom(int seed) : base(seed) { }
            public override int Next(int maxValue) { Calls++; return base.Next(maxValue); }
            public override int Next(int minValue, int maxValue) { Calls++; return base.Next(minValue, maxValue); }
            public override double NextDouble() { Calls++; return base.NextDouble(); }
        }

        [TestCase(0f, .2)]
        [TestCase(.5f, .4)]
        [TestCase(1f, .6)]
        public void NextBatchPriorMatchesTheGuaranteedCompositionAndUniformNumbers(float extraRed, double red)
        {
            var match = new TimeTheftMatch(new GameRules { ExtraRedProbability = extraRed }, new Random(17));
            var prior = match.ObserveItems(Participant.Player).Strategy.NextBatchClocks;
            Assert.That(prior.Sum(p => p.Probability), Is.EqualTo(1).Within(1e-12));
            Assert.That(prior.Where(p => p.Face.Kind == ClockKind.Red).Sum(p => p.Probability), Is.EqualTo(red).Within(1e-12));
            for (int hours = 1; hours <= 12; hours++)
            {
                int number = hours;
                Assert.That(prior.Where(p => p.Face.Hours == number && p.Face.Kind == ClockKind.Red).Sum(p => p.Probability),
                    Is.EqualTo(red / 12).Within(1e-12));
                Assert.That(prior.Where(p => p.Face.Hours == number && p.Face.Kind == ClockKind.Normal).Sum(p => p.Probability),
                    Is.EqualTo((1 - red) / 12).Within(1e-12));
            }
            Assert.That(prior.Sum(p => p.Probability * p.Face.Hours), Is.EqualTo(6.5).Within(1e-12));
        }

        [Test]
        public void NextBatchPriorPreservesWeightedNumbersAndExcludesZeroWeightValues()
        {
            var rules = new GameRules { MinimumHours = 2, MaximumHours = 5,
                NumberWeights = new[] { 1, 0, 3, 0 }, ExtraRedProbability = .25f };
            var match = new TimeTheftMatch(rules, new Random(23));
            var prior = match.ObserveItems(Participant.Player).Strategy.NextBatchClocks;
            Assert.That(prior.All(p => p.Face.Hours == 2 || p.Face.Hours == 4), Is.True);
            Assert.That(prior.Where(p => p.Face.Hours == 2).Sum(p => p.Probability), Is.EqualTo(.25).Within(1e-12));
            Assert.That(prior.Where(p => p.Face.Hours == 4).Sum(p => p.Probability), Is.EqualTo(.75).Within(1e-12));
            Assert.That(prior.Where(p => p.Face.Kind == ClockKind.Red).Sum(p => p.Probability), Is.EqualTo(.3).Within(1e-12));
            Assert.That(prior.Sum(p => p.Probability * p.Face.Hours), Is.EqualTo(3.5).Within(1e-12));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LargeRangePriorPreservesMassAndMeanWithoutReadingTheFutureDraw(bool weighted)
        {
            var rules = new GameRules { MinimumHours = 1, MaximumHours = weighted ? 64 : 1000,
                ExtraRedProbability = .75f };
            double expectedMean = 500.5;
            if (weighted)
            {
                rules.NumberWeights = new int[64];
                rules.NumberWeights[0] = 3;
                rules.NumberWeights[3] = 1;
                rules.NumberWeights[15] = 4;
                rules.NumberWeights[44] = 1;
                rules.NumberWeights[63] = 2;
                expectedMean = 244d / 11;
            }
            var clockRandom = new CountingRandom(101);
            var itemRandom = new CountingRandom(303);
            var match = new TimeTheftMatch(rules, clockRandom, itemRandom);
            var prior = match.ObserveItems(Participant.Player).Strategy.NextBatchClocks;
            Assert.That(prior.All(p => p.Probability >= 0 && p.Face.Hours >= 1 && p.Face.Hours <= rules.MaximumHours), Is.True);
            Assert.That(prior.Sum(p => p.Probability), Is.EqualTo(1).Within(1e-12));
            Assert.That(prior.Where(p => p.Face.Kind == ClockKind.Red).Sum(p => p.Probability), Is.EqualTo(.5).Within(1e-12));
            Assert.That(prior.Sum(p => p.Probability * p.Face.Hours), Is.EqualTo(expectedMean).Within(1e-10));
            Assert.That(clockRandom.Calls + itemRandom.Calls, Is.Zero,
                "공개 생성 규칙의 기대값 조회는 실제 묶음·지급 RNG를 전진시키지 않는다");
        }

        [Test]
        public void NextBatchPriorIsReadOnlyAndKeepsTheMatchRulesSnapshot()
        {
            var rules = new GameRules { MinimumHours = 2, MaximumHours = 5,
                NumberWeights = new[] { 1, 0, 3, 0 }, ExtraRedProbability = .25f };
            var match = new TimeTheftMatch(rules, new Random(101), new Random(303));
            var before = match.ObserveItems(Participant.Player).Strategy.NextBatchClocks;
            var expected = before.ToArray();
            Assert.Throws<NotSupportedException>(() => ((IList<AiClockPrior>)before).Clear());
            rules.NumberWeights[0] = 100;
            rules.ExtraRedProbability = 1;
            rules.MinimumHours = 200;
            rules.MaximumHours = 300;
            Assert.That(match.BeginBatch(), Is.True, "외부 설정 변경 뒤에도 Match는 시작 규칙 사본을 사용한다");
            var after = match.ObserveItems(Participant.Player).Strategy.NextBatchClocks;
            Assert.That(before.Select(p => p.Face), Is.EqualTo(expected.Select(p => p.Face)));
            Assert.That(before.Select(p => p.Probability), Is.EqualTo(expected.Select(p => p.Probability)));
            Assert.That(after.Select(p => p.Face), Is.EqualTo(expected.Select(p => p.Face)));
            Assert.That(after.Select(p => p.Probability), Is.EqualTo(expected.Select(p => p.Probability)));
        }

        private static void StartBatch(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }

        private static void EndItems(TimeTheftMatch match)
        {
            if (match.Stage == TurnStage.AwaitingItems)
                Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }

        private static void SettleAndContinue(TimeTheftMatch match, TimeAction action)
        {
            Assert.That(match.CommitAction(match.ActiveParticipant, action), Is.True);
            Assert.That(match.Settle(), Is.True);
            if (match.Stage != TurnStage.GameOver)
                Assert.That(match.CompleteTurn(), Is.True);
        }

        private static void Act(TimeTheftMatch match, int index, TimeAction action)
        {
            EndItems(match);
            Assert.That(match.SelectClock(match.ActiveParticipant, index), Is.True);
            SettleAndContinue(match, action);
        }

        // 기획 원문이나 외부 JSON을 기대값으로 읽지 않는다. 정상 초기화와 명시된 합법 명령만 재생한다.
        private static TimeTheftMatch KnownNormalCuffs(int seed, int keyId, int known, int replacement, int opponentClock)
        {
            var match = new TimeTheftMatch(new GameRules(), new Random(seed), new Random(seed ^ 0x2471));
            StartBatch(match);
            Assert.That(match.UseItem(Participant.Player, keyId).Succeeded, Is.True);
            EndItems(match);
            Assert.That(match.SelectClock(Participant.Player, known), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
            Assert.That(match.ReselectPeekedClock(Participant.Player, replacement), Is.True);
            SettleAndContinue(match, TimeAction.Ration);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Act(match, opponentClock, TimeAction.Collect);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.UsedCount, Is.EqualTo(2));
            return match;
        }

        [TestCase(60604, 2, 1, 3, 2, 1080, 1440, 1080)]
        [TestCase(61452, 1, 1, 3, 2, 600, 1440, 1560)]
        [TestCase(61546, 2, 3, 2, 1, 780, 1380, 1440)]
        [TestCase(62212, 1, 3, 4, 1, 540, 1300, 1760)]
        [TestCase(85099, 1, 0, 3, 2, 840, 1440, 1320)]
        [TestCase(85155, 1, 0, 3, 2, 540, 1320, 1740)]
        public void KnownNormalMustNotMakeAnAllRedAdditionalActionLookValuable(
            int seed, int keyId, int known, int replacement, int opponentClock, int own, int opponent, int bank)
        {
            var match = KnownNormalCuffs(seed, keyId, known, replacement, opponentClock);
            var info = match.ObserveItems(Participant.Player);
            var cuff = info.OwnItems.Single();
            Assert.That(cuff.Kind, Is.EqualTo(ItemKind.Handcuffs));
            Assert.That(info.UsableItemIds, Does.Contain(cuff.Id), "규칙상 사용 가능하지만 전략적으로 보존한다");
            Assert.That(new[] { info.PublicGame.OwnLifeMinutes, info.PublicGame.OpponentLifeMinutes, info.PublicGame.BankMinutes },
                Is.EqualTo(new[] { own, opponent, bank }));
            Assert.That(info.RemainingFaces.Count, Is.EqualTo(3));
            Assert.That(info.RemainingFaces.Count(f => f.Kind == ClockKind.Normal), Is.EqualTo(1));
            Assert.That(info.KnownClocks.Single().Index, Is.EqualTo(known));
            Assert.That(info.KnownClocks.Single().Face.Kind, Is.EqualTo(ClockKind.Normal));
            for (int decisionSeed = 0; decisionSeed < 16; decisionSeed++)
                Assert.That(TimeTheftAi.ChooseItem(info, new Random(decisionSeed)), Is.Null,
                    "현재의 유일한 일반을 소비한 뒤 빨강 처리와 묶음 선공이 바뀌는 반례 / " + seed);
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(cuff.Id));
        }

        private static TimeTheftMatch TwoNormalCollections(int seed, int firstNormal, int red)
        {
            var match = new TimeTheftMatch(new GameRules(), new Random(seed), new Random(seed ^ 0x2471));
            StartBatch(match);
            Act(match, firstNormal, TimeAction.Collect);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Act(match, red, TimeAction.Ration);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            var info = match.ObserveItems(Participant.Player);
            Assert.That(info.RemainingFaces.Count, Is.EqualTo(3));
            Assert.That(info.RemainingFaces.All(f => f.Kind == ClockKind.Normal), Is.True);
            int[] amounts = info.RemainingFaces.Select(f => f.Hours * 60).OrderBy(x => x).ToArray();
            Assert.That(info.PublicGame.OpponentLifeMinutes, Is.GreaterThan(amounts[2]),
                "현재 D는 모든 후보에서 비종료");
            Assert.That(info.PublicGame.OpponentLifeMinutes, Is.LessThanOrEqualTo(amounts[0] + amounts[1]),
                "추가 D는 모든 두 시계 조합에서 종료");
            return match;
        }

        [TestCase(50005, 2, 3, 1360, 1020)]
        [TestCase(81085, 2, 0, 1260, 780)]
        [TestCase(81133, 1, 2, 1400, 600)]
        [TestCase(81146, 0, 3, 1420, 540)]
        public void DuckIsPreservedWhenCuffedAdditionalCollectionEndsBeforeTheTargetsActualA(
            int seed, int firstNormal, int red, int own, int opponent)
        {
            var match = TwoNormalCollections(seed, firstNormal, red);
            var inventory = match.GetInventory(Participant.Player);
            int cuff = inventory.Single(i => i.Kind == ItemKind.Handcuffs).Id;
            int duck = inventory.Single(i => i.Kind == ItemKind.RubberDuck).Id;
            Assert.That(new[] { match.PlayerLifeMinutes, match.AiLifeMinutes }, Is.EqualTo(new[] { own, opponent }));
            Assert.That(match.UseItem(Participant.Player, cuff).Succeeded, Is.True);
            var before = match.ObserveItems(Participant.Player);
            Assert.That(before.UsableItemIds, Does.Contain(duck));
            Assert.That(before.OwnPendingEffects.Single().Source.Kind, Is.EqualTo(ItemKind.Handcuffs));
            for (int decisionSeed = 0; decisionSeed < 16; decisionSeed++)
                Assert.That(TimeTheftAi.ChooseItem(before, new Random(decisionSeed)), Is.Null,
                    "수갑 추가 행동의 종료 전에 상대 실제 A가 없으므로 오리 가치는 0");

            for (int action = 0; action < 2; action++)
            {
                Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
                if (match.Stage == TurnStage.AwaitingItems)
                {
                    Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Player), new Random(action)), Is.Null);
                    EndItems(match);
                }
                int selected = TimeTheftAi.SelectClock(match.ObserveItems(Participant.Player), new Random(action + 10));
                Assert.That(match.SelectClock(Participant.Player, selected), Is.True);
                var choice = TimeTheftAi.Decide(match.ObserveItems(Participant.Player), new Random(action + 20));
                Assert.That(choice, Is.EqualTo(TimeAction.Collect));
                SettleAndContinue(match, choice);
                if (action == 0)
                {
                    Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
                    Assert.That(match.TurnItemEvents.Single().Kind, Is.EqualTo(ItemTurnEventKind.TurnSkipped));
                }
            }
            Assert.That(match.Winner, Is.EqualTo(GameWinner.Player));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(own));
            Assert.That(match.AiLifeMinutes, Is.Zero);
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(duck));
        }

        [TestCase(81085, 2, 0)]
        [TestCase(81146, 0, 3)]
        public void UsefulCuffsStillSecureTheTwoActionWinWithoutAddingADeadDuck(int seed, int firstNormal, int red)
        {
            var match = TwoNormalCollections(seed, firstNormal, red);
            var info = match.ObserveItems(Participant.Player);
            int cuff = info.OwnItems.Single(i => i.Kind == ItemKind.Handcuffs).Id;
            int duck = info.OwnItems.Single(i => i.Kind == ItemKind.RubberDuck).Id;
            Assert.That(TimeTheftAi.ChooseItem(info, new Random(0)), Is.EqualTo(cuff),
                "수갑의 실제 추가 징수 승리는 보존한다");
            Assert.That(match.UseItem(Participant.Player, cuff).Succeeded, Is.True);
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Player), new Random(0)), Is.Null);
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(duck));
        }

        [Test]
        public void FutureEvaluationDependsOnPublicCompositionRatherThanUnseenPositions()
        {
            int differentLayouts = 0;
            for (int seed = 0; seed < 16; seed++)
            {
                var first = new TimeTheftMatch(new GameRules(), new SplitRandom(seed, seed + 1000),
                    new Random(50005 ^ 0x2471));
                var second = new TimeTheftMatch(new GameRules(), new SplitRandom(seed, seed + 9000),
                    new Random(50005 ^ 0x2471));
                StartBatch(first);
                StartBatch(second);
                var one = first.ObserveItems(Participant.Player);
                var two = second.ObserveItems(Participant.Player);
                Assert.That(one.RemainingFaces, Is.EqualTo(two.RemainingFaces));
                Assert.That(one.OwnItems.Select(i => i.Kind), Is.EqualTo(new[] { ItemKind.RubberDuck, ItemKind.Handcuffs }));
                Assert.That(two.OwnItems, Is.EqualTo(one.OwnItems));
                Assert.That(two.KnownClocks, Is.Empty);
                Assert.That(TimeTheftAi.ChooseItem(two, new Random(seed)), Is.EqualTo(TimeTheftAi.ChooseItem(one, new Random(seed))));
                EndItems(first);
                EndItems(second);
                int a = TimeTheftAi.SelectClock(first.ObserveItems(Participant.Player), new Random(seed + 1));
                int b = TimeTheftAi.SelectClock(second.ObserveItems(Participant.Player), new Random(seed + 1));
                Assert.That(a, Is.EqualTo(b));
                Assert.That(first.SelectClock(Participant.Player, a), Is.True);
                Assert.That(second.SelectClock(Participant.Player, b), Is.True);
                Assert.That(first.CommitAction(Participant.Player, TimeAction.Ration), Is.True);
                Assert.That(second.CommitAction(Participant.Player, TimeAction.Ration), Is.True);
                if (!first.GetClock(a).Face.Value.Equals(second.GetClock(b).Face.Value)) differentLayouts++;
            }
            Assert.That(differentLayouts, Is.GreaterThan(0), "독립된 위치 RNG로 실제 비공개 배치가 다른 표본");
        }

        [TestCase(0)]
        [TestCase(5)]
        public void DuckRetainsItsValueForRefillAfterTheCuffedAdditionalAction(int futureOpponentKind)
        {
            var rules = new GameRules { MinimumHours = 1, MaximumHours = 1, ExtraRedProbability = 0 };
            // 정상 지급만 사용한다. 미래 지급 종류는 AI 관찰에 전달하지 않으며 이 둘을 모두 막을 수 있어야 한다.
            var match = new TimeTheftMatch(rules, new OrderedClockRandom(),
                new ItemDrawRandom(4, 5, 1, 2, 3, futureOpponentKind));
            StartBatch(match);
            Act(match, 0, TimeAction.Ration);
            Assert.That(match.UseItem(Participant.Ai, 3).Succeeded, Is.True);
            Assert.That(match.UseItem(Participant.Ai, 4).Succeeded, Is.True);
            Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
            Act(match, 1, TimeAction.Ration);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.ObserveItems(Participant.Player).RemainingFaces.Count, Is.EqualTo(3));
            Assert.That(match.UseItem(Participant.Player, 2).Succeeded, Is.True);
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Player), new Random(0)), Is.EqualTo(1),
                "현재 상대 재고가 비어도 다음 실제 A 전 정상 지급이 있으므로 오리는 유효");
            Assert.That(match.UseItem(Participant.Player, 1).Succeeded, Is.True);
            Act(match, 2, TimeAction.Ration);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False, "수갑 생략은 오리를 소모하지 않는다");
            Act(match, 3, TimeAction.Ration);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.BatchComplete));
            StartBatch(match);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.GetInventory(Participant.Ai).Single().Kind, Is.EqualTo((ItemKind)futureOpponentKind));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.True);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
        }
    }
}

