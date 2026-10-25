using System;

namespace TimeTheft
{
    public sealed partial class TimeTheftMatch
    {
        private readonly ClockFace?[,] spareKeyKnowledge = new ClockFace?[2, ClocksPerBatch];
        private int spareKeyKnowledgeBatch;

        public bool HasSpareKeyReadiness => currentItemEffects.Exists(effect =>
            effect.Source.Kind == ItemKind.SpareKey && !effect.Source.IsFoundationTest &&
            effect.Source.Owner == ActiveParticipant && effect.RegisteredTurn == Turn);

        // 공개 시계 상태와 별도인 참가자 전용 지식. 사용·폐기된 시계는 반환하지 않는다.
        public ClockFace? GetPrivateClock(Participant viewer, int index)
        {
            if ((viewer != Participant.Player && viewer != Participant.Ai) || index < 0 || index >= ClocksPerBatch ||
                spareKeyKnowledgeBatch != Batch || Stage == TurnStage.GameOver || DiscardedCount != 0 || used[index]) return null;
            return spareKeyKnowledge[(int)viewer, index];
        }

        private bool BeginSpareKeySelection(int index)
        {
            if (!HasSpareKeyReadiness || index < 0 || index >= ClocksPerBatch || used[index]) return false;
            if (spareKeyKnowledgeBatch != Batch) ClearSpareKeyKnowledge();
            spareKeyKnowledgeBatch = Batch;
            spareKeyKnowledge[(int)ActiveParticipant, index] = clocks[index];
            CurrentClock = index;
            Stage = TurnStage.AwaitingKeyDecision;
            return true;
        }

        public bool KeepPeekedClock(Participant actor)
        {
            if (!CanCompleteSpareKeySelection(actor)) return false;
            Stage = TurnStage.AwaitingAction;
            return true;
        }

        public bool ReselectPeekedClock(Participant actor, int index)
        {
            if (!CanCompleteSpareKeySelection(actor) || index < 0 || index >= ClocksPerBatch ||
                index == CurrentClock || used[index]) return false;
            // 첫 시계는 사용하지 않는다. 두 번째 시계의 값은 지식에 추가하지 않는다.
            CurrentClock = index;
            Stage = TurnStage.AwaitingAction;
            return true;
        }

        private bool CanCompleteSpareKeySelection(Participant actor) => actor == ActiveParticipant &&
            Stage == TurnStage.AwaitingKeyDecision && HasSpareKeyReadiness &&
            GetPrivateClock(actor, CurrentClock).HasValue;

        private void ForgetUsedSpareKeyClock(int index)
        {
            spareKeyKnowledge[(int)Participant.Player, index] = null;
            spareKeyKnowledge[(int)Participant.Ai, index] = null;
        }

        private void ClearSpareKeyKnowledge()
        {
            Array.Clear(spareKeyKnowledge, 0, spareKeyKnowledge.Length);
            spareKeyKnowledgeBatch = 0;
        }
    }
}
