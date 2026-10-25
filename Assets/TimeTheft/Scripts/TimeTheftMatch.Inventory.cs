using System;
using System.Collections.Generic;

namespace TimeTheft
{
    public sealed partial class TimeTheftMatch
    {
        private int lastItemGrantBatch;
        private readonly List<ItemGrantResult> lastItemGrants = new List<ItemGrantResult>();
        public IReadOnlyList<ItemGrantResult> LastItemGrants => lastItemGrants.ToArray();

        // 성공한 묶음 시작에만 호출한다. 차례 생략/추가 행동/연출 재시도는 지급하지 않는다.
        private void GrantItemsForBatch()
        {
            if (Batch <= lastItemGrantBatch) return;
            lastItemGrantBatch = Batch;
            lastItemGrants.Clear();
            int requested = Batch == 1 ? rules.InitialItemCount : rules.ItemsPerBatch;
            foreach (var owner in new[] { Participant.Player, Participant.Ai })
            {
                int available = Math.Max(0, rules.MaxHeldItems - GetInventory(owner).Count);
                int count = Math.Min(requested, available);
                var granted = new List<ItemState>();
                for (int i = 0; i < count; i++)
                {
                    var item = new ItemState(nextItemId++, rules.GenerateItemKind(itemRandom), owner, false, false);
                    items.Add(item);
                    granted.Add(item);
                }
                lastItemGrants.Add(new ItemGrantResult(owner, Batch, requested, granted.AsReadOnly()));
            }
        }

        public ItemInventoryObservation ObserveInventory(Participant viewer, Participant owner)
        {
            ValidateParticipant(viewer);
            ValidateParticipant(owner);
            var held = GetInventory(owner);
            bool reveal = viewer == owner || rules.RevealOpponentItems;
            var kinds = new List<ItemKind>();
            if (reveal) foreach (var item in held) kinds.Add(item.Kind);
            return new ItemInventoryObservation(owner, held.Count, reveal, kinds.AsReadOnly());
        }
    }
}
