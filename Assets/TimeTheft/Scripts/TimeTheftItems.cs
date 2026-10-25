using System;
using System.Collections.Generic;

namespace TimeTheft
{
    public enum ItemKind { SpareKey, Screwdriver, TimeBypass, Scissors, RubberDuck, Handcuffs }
    public enum ItemEffectTiming { CurrentTurn, FutureTurn }
    public enum ItemUseFailure { None, WrongParticipant, WrongStage, NotOwned, AlreadyConsumed, UseNotAllowed, EffectNotImplemented, AlreadyActive, NoTarget }
    public enum ItemTurnEventKind { ItemsBlocked, RestrictionEnded, TurnSkipped, Cancelled }

    public readonly struct ItemTurnEvent
    {
        public ItemState Source { get; }
        public Participant Target { get; }
        public ItemTurnEventKind Kind { get; }
        internal ItemTurnEvent(ItemState source, Participant target, ItemTurnEventKind kind)
        { Source = source; Target = target; Kind = kind; }
    }
    public enum TimeAccount { Player, Ai, Bank }

    public readonly struct ItemGrantResult
    {
        public Participant Owner { get; }
        public int Batch { get; }
        public int RequestedCount { get; }
        public IReadOnlyList<ItemState> GrantedItems { get; }
        public int SkippedCount => RequestedCount - GrantedItems.Count;
        internal ItemGrantResult(Participant owner, int batch, int requestedCount, IReadOnlyList<ItemState> grantedItems)
        { Owner = owner; Batch = batch; RequestedCount = requestedCount; GrantedItems = grantedItems; }
    }

    // 공개 보유량/종류만 담는다. 상대 ID·등록 효과·시계 지식은 포함하지 않는다.
    public readonly struct ItemInventoryObservation
    {
        public Participant Owner { get; }
        public int Count { get; }
        public bool RevealsKinds { get; }
        public IReadOnlyList<ItemKind> Kinds { get; }
        internal ItemInventoryObservation(Participant owner, int count, bool revealsKinds, IReadOnlyList<ItemKind> kinds)
        { Owner = owner; Count = count; RevealsKinds = revealsKinds; Kinds = kinds; }
    }

    // 한 번의 정산에 확정한 시간 이동 경로. 분할 징수도 같은 단위로 표시한다.
    public readonly struct TimeTransfer
    {
        public TimeAccount From { get; }
        public TimeAccount To { get; }
        public int Minutes { get; }
        internal TimeTransfer(TimeAccount from, TimeAccount to, int minutes)
        { From = from; To = to; Minutes = minutes; }
    }

    public readonly struct ItemState
    {
        public int Id { get; }
        public ItemKind Kind { get; }
        public Participant Owner { get; }
        public bool IsConsumed { get; }
        public bool IsFoundationTest { get; }
        internal ItemState(int id, ItemKind kind, Participant owner, bool consumed, bool foundationTest)
        { Id = id; Kind = kind; Owner = owner; IsConsumed = consumed; IsFoundationTest = foundationTest; }
    }

    // 등록 정보만 보관한다. 종류별 발동 조건은 Match가 처리하며 표시·애니메이션에 의존하지 않는다.
    public readonly struct ItemEffectState
    {
        public ItemState Source { get; }
        public ItemEffectTiming Timing { get; }
        public int RegisteredTurn { get; }
        public Participant? Target { get; }
        internal ItemEffectState(ItemState source, ItemEffectTiming timing, int turn, Participant? target = null)
        { Source = source; Timing = timing; RegisteredTurn = turn; Target = target; }
    }

    public readonly struct ItemUseResult
    {
        public Participant Actor { get; }
        public int ItemId { get; }
        public ItemUseFailure Failure { get; }
        public bool Succeeded => Failure == ItemUseFailure.None && Effect.HasValue;
        public ItemEffectState? Effect { get; }
        public ItemState? RemovedItem { get; }
        internal ItemUseResult(Participant actor, int itemId, ItemUseFailure failure, ItemEffectState? effect = null, ItemState? removedItem = null)
        { Actor = actor; ItemId = itemId; Failure = failure; Effect = effect; RemovedItem = removedItem; }
    }

    // 정산에서 확정한 아이템 결과. 표시부는 이 결과를 읽으며 수명/Bank를 다시 계산하지 않는다.
    public readonly struct ItemEffectResult
    {
        public ItemState Source { get; }
        public bool Triggered { get; }
        public int PreventedMinutes { get; }
        public int BypassedMinutes { get; }
        public int BankReceivedMinutes { get; }
        public Participant? Recipient { get; }
        internal ItemEffectResult(ItemState source, bool triggered, int preventedMinutes,
            int bypassedMinutes = 0, int bankReceivedMinutes = 0, Participant? recipient = null)
        {
            Source = source; Triggered = triggered; PreventedMinutes = preventedMinutes;
            BypassedMinutes = bypassedMinutes; BankReceivedMinutes = bankReceivedMinutes; Recipient = recipient;
        }
    }

    // 자기 보유품/효과와 공개 보유 관찰만 전달한다. 상대 ID·비공개 효과·시계는 주지 않는다.
    public readonly partial struct AiItemObservation
    {
        public AiObservation PublicGame { get; }
        public IReadOnlyList<ItemState> OwnItems { get; }
        public IReadOnlyList<ItemEffectState> OwnCurrentEffects { get; }
        public IReadOnlyList<ItemEffectState> OwnPendingEffects { get; }
        public bool CanUseItems { get; }
        public ItemInventoryObservation OpponentInventory { get; }
        internal AiItemObservation(AiObservation publicGame, IReadOnlyList<ItemState> ownItems,
            IReadOnlyList<ItemEffectState> current, IReadOnlyList<ItemEffectState> pending, bool canUseItems,
            ItemInventoryObservation opponentInventory, AiStrategySettings strategy, TurnStage stage, int batch, int turn,
            int currentClock, IReadOnlyList<ClockFace> remainingFaces, IReadOnlyList<int> availableClockIndices,
            IReadOnlyList<AiKnownClock> knownClocks, IReadOnlyList<int> usableItemIds)
        {
            PublicGame = publicGame; OwnItems = ownItems; OwnCurrentEffects = current; OwnPendingEffects = pending;
            CanUseItems = canUseItems; OpponentInventory = opponentInventory; Strategy = strategy; Stage = stage;
            Batch = batch; Turn = turn; CurrentClock = currentClock; RemainingFaces = remainingFaces;
            AvailableClockIndices = availableClockIndices; KnownClocks = knownClocks; UsableItemIds = usableItemIds;
        }
    }
}
