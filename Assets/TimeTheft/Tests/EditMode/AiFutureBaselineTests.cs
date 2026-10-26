using System;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class AiFutureBaselineTests
    {
        [Test]
        public void CurrentProtectionMustNotReceiveAnUncomparedFutureHandcuffsBenefit()
        {
            const int seed = 100016;
            var match = new TimeTheftMatch(new GameRules { FirstParticipant = Participant.Ai },
                new Random(seed), new Random(seed ^ 0x2471));

            // 미사용 시드에서 자연 도달한 두 번째 묶음. 모든 재현 명령은 공개 Match API만 사용한다.
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
            Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, 3), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, TimeAction.Ration), Is.True);
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.CompleteTurn(), Is.True);
            Assert.That(match.UseItem(match.ActiveParticipant, 1).Succeeded, Is.True);
            Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, 4), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, TimeAction.Collect), Is.True);
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.CompleteTurn(), Is.True);
            Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, 0), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, TimeAction.Ration), Is.True);
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.CompleteTurn(), Is.True);
            Assert.That(match.EndItemPhase(match.ActiveParticipant), Is.True);
            Assert.That(match.SelectClock(match.ActiveParticipant, 2), Is.True);
            Assert.That(match.CommitAction(match.ActiveParticipant, TimeAction.Ration), Is.True);
            Assert.That(match.Settle(), Is.True);
            Assert.That(match.CompleteTurn(), Is.True);
            Assert.That(match.BeginBatch(), Is.True);
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);

            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(new[] { match.Batch, match.Turn, match.UsedCount, match.DiscardedCount, match.CurrentClock },
                Is.EqualTo(new[] { 2, 5, 0, 0, -1 }));
            Assert.That(new[] { match.AiLifeMinutes, match.PlayerLifeMinutes, match.BankMinutes },
                Is.EqualTo(new[] { 720, 1500, 1380 }));
            var before = match.ObserveItems(Participant.Ai);
            Assert.That(before.RemainingFaces.Select(f => f.Hours), Is.EqualTo(new[] { 2, 6, 6, 11, 12 }));
            Assert.That(before.RemainingFaces.Select(f => f.Kind), Is.EqualTo(new[]
                { ClockKind.Normal, ClockKind.Normal, ClockKind.Normal, ClockKind.Red, ClockKind.Red }));
            Assert.That(before.OwnItems.Select(i => i.Id), Is.EqualTo(new[] { 3, 4, 6 }));
            Assert.That(before.OwnItems.Select(i => i.Kind), Is.EqualTo(new[]
                { ItemKind.Handcuffs, ItemKind.Handcuffs, ItemKind.Screwdriver }));
            Assert.That(before.UsableItemIds, Is.EquivalentTo(new[] { 3, 4, 6 }));
            Assert.That(before.KnownClocks, Is.Empty);
            Assert.That(before.OwnCurrentEffects, Is.Empty);
            Assert.That(before.OwnPendingEffects, Is.Empty);

            var ownItems = match.GetInventory(Participant.Ai).ToArray();
            var otherItems = match.GetInventory(Participant.Player).ToArray();
            var ownEffects = match.GetCurrentItemEffects(Participant.Ai).ToArray();
            var otherEffects = match.GetCurrentItemEffects(Participant.Player).ToArray();
            var ownPending = match.GetPendingItemEffects(Participant.Ai).ToArray();
            var otherPending = match.GetPendingItemEffects(Participant.Player).ToArray();
            var visibleClocks = Enumerable.Range(0, TimeTheftMatch.ClocksPerBatch).Select(match.GetClock).ToArray();
            for (int decisionSeed = 0; decisionSeed < 16; decisionSeed++)
                Assert.That(TimeTheftAi.ChooseItem(before, new Random(decisionSeed)), Is.Null,
                    "수갑 없는 경로에도 같은 현재 D 이후의 미래 기준을 적용하여 드라이버·수갑을 보존한다 / " + decisionSeed);

            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.Winner, Is.EqualTo(GameWinner.None));
            Assert.That(new[] { match.Batch, match.Turn, match.UsedCount, match.DiscardedCount, match.CurrentClock },
                Is.EqualTo(new[] { 2, 5, 0, 0, -1 }));
            Assert.That(new[] { match.AiLifeMinutes, match.PlayerLifeMinutes, match.BankMinutes },
                Is.EqualTo(new[] { 720, 1500, 1380 }));
            Assert.That(match.GetInventory(Participant.Ai), Is.EqualTo(ownItems));
            Assert.That(match.GetInventory(Participant.Player), Is.EqualTo(otherItems));
            Assert.That(match.GetCurrentItemEffects(Participant.Ai), Is.EqualTo(ownEffects));
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.EqualTo(otherEffects));
            Assert.That(match.GetPendingItemEffects(Participant.Ai), Is.EqualTo(ownPending));
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.EqualTo(otherPending));
            Assert.That(Enumerable.Range(0, TimeTheftMatch.ClocksPerBatch).Select(match.GetClock), Is.EqualTo(visibleClocks));
            var after = match.ObserveItems(Participant.Ai);
            Assert.That(after.RemainingFaces, Is.EqualTo(before.RemainingFaces));
            Assert.That(after.AvailableClockIndices, Is.EqualTo(before.AvailableClockIndices));
            Assert.That(after.KnownClocks, Is.EqualTo(before.KnownClocks));
            Assert.That(after.UsableItemIds, Is.EqualTo(before.UsableItemIds));
        }
    }
}
