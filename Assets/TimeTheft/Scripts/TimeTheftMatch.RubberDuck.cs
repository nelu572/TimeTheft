namespace TimeTheft
{
    public sealed partial class TimeTheftMatch
    {
        private static bool IsRealRubberDuck(ItemState item) =>
            item.Kind == ItemKind.RubberDuck && !item.IsFoundationTest;

        private bool HasPendingRubberDuck(Participant target) => pendingItemEffects.Exists(effect =>
            IsRealRubberDuck(effect.Source) && effect.Target == target);

        // 수갑으로 건너뛴 차례에는 호출하지 않는다. 다음 실제 행동의 A에만 제한을 연다.
        private void ActivateRubberDuckForTurn()
        {
            for (int i = pendingItemEffects.Count - 1; i >= 0; i--)
            {
                var effect = pendingItemEffects[i];
                if (!IsRealRubberDuck(effect.Source) || effect.Target != ActiveParticipant) continue;
                pendingItemEffects.RemoveAt(i);
                currentItemEffects.Add(new ItemEffectState(effect.Source, ItemEffectTiming.CurrentTurn,
                    effect.RegisteredTurn, effect.Target));
            }
        }

        public bool IsItemUseBlocked(Participant participant)
        {
            ValidateParticipant(participant);
            return participant == ActiveParticipant && currentItemEffects.Exists(effect =>
                IsRealRubberDuck(effect.Source) && effect.Target == participant);
        }
    }
}
