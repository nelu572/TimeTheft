using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class AiPlanContributionTests
    {
        private sealed class OrderedRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => .99;
        }

        // 무효 단일 계획은 잡음을 뽑지 않는다. 그 다음 쌍에 +, 유효 단일에 -를 준다.
        private sealed class PairFirstNoise : Random
        {
            private int calls;
            public override double NextDouble() => calls++ % 2 == 0 ? 1 : 0;
        }

        private static readonly MethodInfo Plan = typeof(TimeTheftAi).GetMethod("PlanValue", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo Active = typeof(TimeTheftAi).GetMethod("ActiveEffects", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo Preservation = typeof(TimeTheftAi).GetMethod("PreservationCost", BindingFlags.Static | BindingFlags.NonPublic);
        private static int Bit(ItemKind kind) => 1 << (int)kind;
        private static double Value(AiItemObservation info, int added) => (double)Plan.Invoke(null,
            new object[] { info, (int)Active.Invoke(null, new object[] { info }) | added, added });
        private static double Cost(AiItemObservation info, ItemKind kind) => (double)Preservation.Invoke(null, new object[] { info, kind });

        private static GameRules Rules(int life = 30, int hours = 8, bool red = false) => new GameRules
        {
            InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = Participant.Ai,
            InitialLifeHours = life, MinimumHours = hours, MaximumHours = hours,
            ExtraRedProbability = red ? 1 : 0, AiDecisionNoise = 1, AiItemConservation = 0
        };
        private static TimeTheftMatch Create(GameRules rules) => new TimeTheftMatch(rules, new OrderedRandom(), new Random(961));
        private static int Give(TimeTheftMatch match, Participant owner, ItemKind kind) => TimeTheftItemTestSetup.Give(match, owner, kind);
        private static void Start(TimeTheftMatch match)
        {
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
        }
        private static void Act(TimeTheftMatch match, int index)
        {
            var actor = match.ActiveParticipant;
            if (match.Stage == TurnStage.AwaitingItems) Assert.That(match.EndItemPhase(actor), Is.True);
            Assert.That(match.SelectClock(actor, index), Is.True);
            if (match.Stage == TurnStage.AwaitingKeyDecision) Assert.That(match.KeepPeekedClock(actor), Is.True);
            Assert.That(match.CommitAction(actor, TimeAction.Ration), Is.True);
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.CompleteTurn(), Is.True);
        }

        [TestCase(ItemKind.SpareKey)]
        [TestCase(ItemKind.Screwdriver)]
        public void CertainNormalClocksDoNotSpendUselessInsuranceOrInformationWithAUsefulBypass(ItemKind filler)
        {
            var match = Create(Rules());
            int useless = Give(match, Participant.Ai, filler);
            int useful = Give(match, Participant.Ai, ItemKind.TimeBypass);
            Start(match); Act(match, 2); Act(match, 0);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.RemainingFaces.All(f => f.Kind == ClockKind.Normal && f.Hours == 8), Is.True);
            Assert.That(info.UsableItemIds, Does.Contain(useless));
            Assert.That(Value(info, Bit(filler)) + Cost(info, filler), Is.EqualTo(Value(info, 0)).Within(.0001),
                "사용 당시 공개 구성에서 보험/정보가 어떤 결과도 개선하지 않는다");
            Assert.That(Value(info, Bit(ItemKind.TimeBypass)), Is.GreaterThan(Value(info, 0)));
            Assert.That(TimeTheftAi.ChooseItem(info, new PairFirstNoise()), Is.EqualTo(useful),
                "쌍의 다른 품목 이득이나 잡음에 기여 없는 앞 품목이 묻히면 안 된다");
        }

        [Test]
        public void HarmfulOrUnusedRedBypassDoesNotRideAlongWithUsefulScissors()
        {
            var rules = Rules(life: 100, hours: 12, red: true);
            rules.ScrewdriverReductionPercent = 100;
            var match = Create(rules);
            int bypass = Give(match, Participant.Ai, ItemKind.TimeBypass);
            int scissors = Give(match, Participant.Ai, ItemKind.Scissors);
            Give(match, Participant.Player, ItemKind.Screwdriver);
            Start(match); Act(match, 0); Act(match, 1);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.RemainingFaces.All(f => f.Kind == ClockKind.Red), Is.True);
            Assert.That(Value(info, Bit(ItemKind.TimeBypass)) + Cost(info, ItemKind.TimeBypass), Is.EqualTo(Value(info, 0)).Within(.0001),
                "빨간 징수를 악화시키는 우회선 대신 배급을 택하므로 추가 효용이 없다");
            Assert.That(Value(info, Bit(ItemKind.Scissors)), Is.GreaterThan(Value(info, 0)), "상대의 100% 드라이버 제거는 가치가 있다");
            Assert.That(TimeTheftAi.ChooseItem(info, new PairFirstNoise()), Is.EqualTo(scissors));
            Assert.That(match.GetInventory(Participant.Ai).Any(i => i.Id == bypass), Is.True);
        }

        [Test]
        public void RemovingAZeroThreatItemDoesNotRideAlongWithUsefulHandcuffs()
        {
            var match = Create(Rules(hours: 1));
            int scissors = Give(match, Participant.Ai, ItemKind.Scissors);
            int cuffs = Give(match, Participant.Ai, ItemKind.Handcuffs);
            Give(match, Participant.Player, ItemKind.Screwdriver);
            Start(match); Act(match, 2); Act(match, 0);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.OpponentInventory.Count, Is.EqualTo(1));
            Assert.That(info.UsableItemIds, Does.Contain(scissors), "상대가 있으므로 가위 자체는 합법이다");
            Assert.That(Value(info, Bit(ItemKind.Scissors)) + Cost(info, ItemKind.Scissors), Is.EqualTo(Value(info, 0)).Within(.0001));
            Assert.That(Value(info, Bit(ItemKind.Handcuffs)), Is.GreaterThan(Value(info, 0)));
            Assert.That(TimeTheftAi.ChooseItem(info, new PairFirstNoise()), Is.EqualTo(cuffs));
        }

        [Test]
        public void APreviouslyKnownBestClockDoesNotNeedAnotherKeyWithAUsefulBypass()
        {
            var match = Create(Rules());
            int firstKey = Give(match, Participant.Ai, ItemKind.SpareKey);
            int secondKey = Give(match, Participant.Ai, ItemKind.SpareKey);
            int bypass = Give(match, Participant.Ai, ItemKind.TimeBypass);
            Start(match);
            Assert.That(match.UseItem(Participant.Ai, firstKey).Succeeded, Is.True);
            Assert.That(match.EndItemPhase(Participant.Ai), Is.True);
            Assert.That(match.SelectClock(Participant.Ai, 3), Is.True);
            Assert.That(match.ReselectPeekedClock(Participant.Ai, 0), Is.True);
            Assert.That(match.CommitAction(Participant.Ai, TimeAction.Ration), Is.True);
            Assert.That(match.Settle() && match.CompleteTurn(), Is.True);
            Act(match, 1);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.KnownClocks.Single().Index, Is.EqualTo(3));
            Assert.That(info.RemainingFaces.Any(f => f.Kind == ClockKind.Red), Is.True, "전체 분포가 동일한 경우만 검사하지 않는다");
            Assert.That(info.UsableItemIds, Does.Contain(secondKey));
            Assert.That(Value(info, Bit(ItemKind.SpareKey)) + Cost(info, ItemKind.SpareKey), Is.EqualTo(Value(info, 0)).Within(.0001),
                "이미 아는 최선의 일반 시계를 바로 고를 수 있어 추가 열쇠가 필요 없다");
            Assert.That(TimeTheftAi.ChooseItem(info, new PairFirstNoise()), Is.EqualTo(bypass));
        }

        [Test]
        public void RepresentativePlanContributionMatrixRecordsSinglesPairsAndTheSupportedTriple()
        {
            var rows = new List<string>();
            foreach (string scenario in new[] { "normalOnly", "redHeavy", "terminal", "knownKey", "emptyRefill", "emptyNoRefill" })
            {
                var rules = Rules(life: scenario == "terminal" ? 11 : 30, hours: 8, red: scenario == "redHeavy");
                rules.AiDecisionNoise = .08f; rules.AiItemConservation = .35f;
                rules.MaxHeldItems = 7; // 전 품목 동시 비교를 위한 검증 전용 상한. 제품 기본값은 그대로다.
                rules.FirstParticipant = scenario == "emptyRefill" ? Participant.Player : Participant.Ai;
                rules.ItemsPerBatch = scenario == "emptyRefill" ? 1 : 0;
                var match = Create(rules);
                foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind))) Give(match, Participant.Ai, kind);
                int extraKey = scenario == "knownKey" ? Give(match, Participant.Ai, ItemKind.SpareKey) : 0;
                if (!scenario.StartsWith("empty", StringComparison.Ordinal))
                    foreach (var kind in new[] { ItemKind.SpareKey, ItemKind.Handcuffs, ItemKind.Screwdriver }) Give(match, Participant.Player, kind);
                Start(match);
                if (scenario == "normalOnly" || scenario == "terminal") { Act(match, 2); Act(match, 0); }
                if (scenario == "emptyRefill") { Act(match, 0); Act(match, 1); Act(match, 2); }
                if (scenario == "knownKey")
                {
                    Assert.That(match.UseItem(Participant.Ai, extraKey).Succeeded, Is.True);
                    Assert.That(match.EndItemPhase(Participant.Ai) && match.SelectClock(Participant.Ai, 3), Is.True);
                    Assert.That(match.ReselectPeekedClock(Participant.Ai, 0), Is.True);
                    Assert.That(match.CommitAction(Participant.Ai, TimeAction.Ration), Is.True);
                    Assert.That(match.Settle() && match.CompleteTurn(), Is.True);
                    Act(match, 1);
                }
                var info = match.ObserveItems(Participant.Ai);
                var plans = new List<int>();
                foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind))) plans.Add(Bit(kind));
                for (int a = 0; a < 6; a++) for (int b = a + 1; b < 6; b++) plans.Add((1 << a) | (1 << b));
                plans.Add(Bit(ItemKind.SpareKey) | Bit(ItemKind.Screwdriver) | Bit(ItemKind.TimeBypass));
                double baseline = Value(info, 0);
                var scores = new List<string>();
                foreach (int mask in plans)
                {
                    double score = Value(info, mask);
                    double cost = 0;
                    bool legal = true;
                    var contributions = new List<string>();
                    foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
                    {
                        if ((mask & Bit(kind)) == 0) continue;
                        cost += Cost(info, kind);
                        legal &= info.OwnItems.Any(i => i.Kind == kind && info.UsableItemIds.Contains(i.Id));
                        contributions.Add("\"" + kind + "\":" + F(score - Value(info, mask & ~Bit(kind))));
                    }
                    scores.Add("{\"mask\":" + mask + ",\"legalAtObservation\":" + (legal ? "true" : "false")
                        + ",\"afterPreservation\":" + F(score) + ",\"beforePreservation\":" + F(score + cost)
                        + ",\"deltaFromNoUse\":" + F(score - baseline) + ",\"marginal\":{" + string.Join(",", contributions) + "}}");
                }
                rows.Add("{\"scenario\":\"" + scenario + "\",\"testOnlyCapacity\":7,\"remaining\":" + info.RemainingFaces.Count
                    + ",\"ownLife\":" + info.PublicGame.OwnLifeMinutes + ",\"otherLife\":" + info.PublicGame.OpponentLifeMinutes
                    + ",\"bank\":" + info.PublicGame.BankMinutes + ",\"known\":" + info.KnownClocks.Count
                    + ",\"otherItems\":" + info.OpponentInventory.Count + ",\"baseline\":" + F(baseline) + ",\"plans\":[" + string.Join(",", scores) + "]}");
                Assert.That(scores.Count, Is.EqualTo(22));
                Assert.That(double.IsNaN(baseline) || double.IsInfinity(baseline), Is.False);
            }
            TestContext.WriteLine("{\"audit\":\"AiPlanContributions\",\"scoreMeaning\":\"현재 AI의 공개정보 기반 추정값이며 미래 턴 완전탐색이 아님\",\"scenarios\":[" + string.Join(",", rows) + "]}");
        }

        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
