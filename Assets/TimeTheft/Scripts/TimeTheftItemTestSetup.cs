#if UNITY_EDITOR
using System;

namespace TimeTheft
{
    public enum ItemTestUse { Unavailable, RecordCurrentEffect, RecordFutureEffect }

    // Editor에서 명시적으로 주입하는 보유품. 기본 모드는 구현된 실제 효과를 사용한다.
    // Record* 모드만 공통 기반의 가짜 효과 기록이며 정식 지급 규칙과 무관하다.
    public static class TimeTheftItemTestSetup
    {
        public static int Give(TimeTheftMatch match, Participant owner, ItemKind kind, ItemTestUse use = ItemTestUse.Unavailable)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            return match.AddItemForTesting(owner, kind, use);
        }

        public static void SetUseAllowed(TimeTheftMatch match, Participant owner, bool allowed)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            match.SetItemUseAllowedForTesting(owner, allowed);
        }
    }
}
#endif
