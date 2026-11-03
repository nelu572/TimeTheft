namespace TimeTheft
{
    public sealed partial class TimeTheftMatch
    {
        private bool HasPendingHandcuffs(Participant target) => pendingItemEffects.Exists(effect =>
            effect.Source.Kind == ItemKind.Handcuffs && !effect.Source.IsFoundationTest && effect.Target == target);

        // 행동 기회를 건너뛸 뿐 시계를 쓰거나 정산하지 않는다. 오리는 다음 실제 행동까지 대기한다.
        private void ResolveHandcuffedTurns()
        {
            int index;
            while ((index = pendingItemEffects.FindIndex(effect => effect.Source.Kind == ItemKind.Handcuffs &&
                !effect.Source.IsFoundationTest && effect.Target == ActiveParticipant)) >= 0)
            {
                var effect = pendingItemEffects[index];
                pendingItemEffects.RemoveAt(index);
                turnItemEvents.Add(new ItemTurnEvent(effect.Source, ActiveParticipant, ItemTurnEventKind.TurnSkipped));
                ActiveParticipant = Other(ActiveParticipant);
                Turn++;
            }
        }
    }
}
