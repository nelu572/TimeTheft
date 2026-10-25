using System;

namespace TimeTheft
{
    public sealed partial class TimeTheftMatch
    {
        // 잠정 정책: 상대의 숨겨진 보유 목록을 노출하지 않고 미사용품 한 개를 균등 추첨한다.
        private ItemUseFailure CheckScissorsUse() => items.Exists(item =>
            item.Owner == Other(ActiveParticipant) && !item.IsConsumed)
            ? ItemUseFailure.None : ItemUseFailure.NoTarget;

        private ItemState RemoveScissorsTarget()
        {
            var candidates = items.FindAll(item => item.Owner == Other(ActiveParticipant) && !item.IsConsumed);
            if (candidates.Count == 0) throw new InvalidOperationException("제거할 미사용 아이템이 없습니다.");
            ItemState target = candidates[itemRandom.Next(candidates.Count)];
            var removed = new ItemState(target.Id, target.Kind, target.Owner, true, target.IsFoundationTest);
            items[items.FindIndex(item => item.Id == target.Id)] = removed;
            // 사용해 이미 등록한 효과는 보유품과 별개이며 가위로 취소하지 않는다.
            return removed;
        }
    }
}
