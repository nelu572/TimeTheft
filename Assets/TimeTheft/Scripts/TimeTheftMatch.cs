using System;
using System.Collections.Generic;

namespace TimeTheft
{
    public enum Participant { Player, Ai }
    public enum TimeAction { Collect, Ration }
    public enum ClockKind { Normal, Red }
    public enum TurnStage { BatchComplete, BatchPreview, Shuffling, AwaitingAction, Revealing, Settling, GameOver, AwaitingClock, AwaitingItems, AwaitingKeyDecision }
    public enum GameWinner { None, Player, Ai }
    public enum ClockVisibility { Hidden, Open, Used, Discarded }

    public static class GameTime
    {
        public const int MinutesPerHour = 60;
        public const int RationMinutesPerHour = 20;

        public static string Format(int minutes)
        {
            if (minutes < 0) throw new ArgumentOutOfRangeException(nameof(minutes));
            return (minutes / MinutesPerHour).ToString("00") + ":" + (minutes % MinutesPerHour).ToString("00");
        }
    }

    public readonly struct ClockFace
    {
        public int Hours { get; }
        public ClockKind Kind { get; }
        public ClockFace(int hours, ClockKind kind) { Hours = hours; Kind = kind; }
    }

    public readonly struct ClockState
    {
        public ClockVisibility Visibility { get; }
        public ClockFace? Face { get; }
        public ClockState(ClockVisibility visibility, ClockFace? face) { Visibility = visibility; Face = face; }
    }

    // 위치·순서·숨은 시계 참조를 포함하지 않는 공개 정보만 AI에 전달한다.
    public readonly struct AiObservation
    {
        public int OwnLifeMinutes { get; }
        public int OpponentLifeMinutes { get; }
        public int BankMinutes { get; }
        public int NormalCount { get; }
        public int RedCount { get; }
        public int NormalHours { get; }
        public int RedHours { get; }
        public AiObservation(int ownLifeMinutes, int opponentLifeMinutes, int bankMinutes, int normalCount, int redCount, int normalHours, int redHours)
        {
            OwnLifeMinutes = ownLifeMinutes; OpponentLifeMinutes = opponentLifeMinutes; BankMinutes = bankMinutes;
            NormalCount = normalCount; RedCount = redCount; NormalHours = normalHours; RedHours = redHours;
        }
    }

    public static partial class TimeTheftAi
    {
        // 후보의 위치 ID만 받는다. 비공개 숫자·종류와 무관하게 균등 선택한다.
        public static int SelectClock(IReadOnlyList<int> availableIndices, Random random)
        {
            if (availableIndices == null || random == null) throw new ArgumentNullException();
            if (availableIndices.Count == 0) throw new ArgumentException("선택할 시계가 없습니다.");
            return availableIndices[random.Next(availableIndices.Count)];
        }

        // 공개된 종류별 평균으로 분 단위 수명 우위 변화를 추정한다. 실제 정산은 모델의 정수 연산뿐이다.
        public static TimeAction Decide(AiObservation info, double riskTolerance, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (info.BankMinutes == 0) return TimeAction.Collect;
            double count = Math.Max(1, info.NormalCount + info.RedCount);
            double normalHours = (double)info.NormalHours / Math.Max(1, info.NormalCount);
            double redHours = (double)info.RedHours / Math.Max(1, info.RedCount);
            double collect = (info.NormalCount * Math.Min(info.OpponentLifeMinutes, normalHours * GameTime.MinutesPerHour)
                - info.RedCount * Math.Min(info.OwnLifeMinutes, redHours * GameTime.MinutesPerHour)) / count;
            // 빨간 배급은 상대에게 지급하므로 이득에서 뺀다. Bank 부족도 종류별로 반영한다.
            double ration = (info.NormalCount * Math.Min(info.BankMinutes, normalHours * GameTime.RationMinutesPerHour)
                - info.RedCount * Math.Min(info.BankMinutes, redHours * GameTime.RationMinutesPerHour)) / count;
            double danger = info.OwnLifeMinutes < info.OpponentLifeMinutes ? 0.15 : 0;
            double scale = Math.Max(GameTime.MinutesPerHour, Math.Max(Math.Abs(collect), Math.Abs(ration)));
            double probability = Math.Max(.05, Math.Min(.95, .5 + (collect - ration) / (2 * scale) + riskTolerance - .5 - danger));
            return random.NextDouble() < probability ? TimeAction.Collect : TimeAction.Ration;
        }
    }

    // 수치와 상태의 유일한 소유자. Unity 오브젝트나 특정 시계 모델을 알지 못한다.
    public sealed partial class TimeTheftMatch
    {
        public const int ClocksPerBatch = 5;
        public const int TurnsPerBatch = 4;
        public const int InitialContributionMinutes = 10 * GameTime.MinutesPerHour;
        private readonly GameRules rules;
        private readonly Random random;
        private readonly Random itemRandom;
        private readonly ClockFace[] clocks = new ClockFace[ClocksPerBatch];
        private readonly bool[] used = new bool[ClocksPerBatch];
        private readonly List<ItemState> items = new List<ItemState>();
        private readonly List<ItemEffectState> currentItemEffects = new List<ItemEffectState>();
        private readonly List<ItemEffectState> pendingItemEffects = new List<ItemEffectState>();
        private readonly List<ItemEffectResult> lastItemEffects = new List<ItemEffectResult>();
        private readonly List<TimeTransfer> lastTransfers = new List<TimeTransfer>();
        private readonly List<ItemTurnEvent> turnItemEvents = new List<ItemTurnEvent>();
        private readonly List<ClockFace> publicRemainingFaces = new List<ClockFace>();
        private readonly bool[] itemUseAllowed = { true, true };
#if UNITY_EDITOR
        private readonly Dictionary<int, ItemTestUse> testItemUses = new Dictionary<int, ItemTestUse>();
#endif
        private int nextItemId = 1;
        private int normalCount, redCount, normalHours, redHours;

        public TurnStage Stage { get; private set; } = TurnStage.BatchComplete;
        public Participant ActiveParticipant { get; private set; }
        public int PlayerLifeMinutes { get; private set; }
        public int AiLifeMinutes { get; private set; }
        public int BankMinutes { get; private set; }
        public int Batch { get; private set; }
        public int Turn { get; private set; } = 1;
        public int UsedCount { get; private set; }
        public int DiscardedCount { get; private set; }
        public int CurrentClock { get; private set; } = -1;
        public TimeAction Action { get; private set; }
        public int TransferredMinutes { get; private set; }
        public Participant TransferParticipant { get; private set; }
        public GameWinner Winner { get; private set; }
        public IReadOnlyList<ItemEffectResult> LastItemEffects => lastItemEffects.ToArray();
        public IReadOnlyList<TimeTransfer> LastTransfers => lastTransfers.ToArray();
        public IReadOnlyList<ItemTurnEvent> TurnItemEvents => turnItemEvents.ToArray();
        public bool HasScrewdriverProtection => currentItemEffects.Exists(effect => IsRealScrewdriver(effect.Source) &&
            effect.Source.Owner == ActiveParticipant && effect.RegisteredTurn == Turn);
        public bool HasTimeBypass => currentItemEffects.Exists(effect => IsRealTimeBypass(effect.Source) &&
            effect.Source.Owner == ActiveParticipant && effect.RegisteredTurn == Turn);

        public TimeTheftMatch(GameRules rules, Random random, Random itemRandom = null)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (random == null) throw new ArgumentNullException(nameof(random));
            rules.Validate();
            this.rules = rules.Copy();
            this.random = random;
            this.itemRandom = itemRandom ?? new Random();
            PlayerLifeMinutes = AiLifeMinutes = rules.InitialLifeHours * GameTime.MinutesPerHour - InitialContributionMinutes;
            BankMinutes = InitialContributionMinutes * 2;
            ActiveParticipant = rules.FirstParticipant;
        }

        public bool BeginBatch()
        {
            if (Stage != TurnStage.BatchComplete) return false;
            UsedCount = DiscardedCount = 0;
            CurrentClock = -1;
            ClearSpareKeyKnowledge();
            Array.Clear(used, 0, used.Length);
            normalCount = redCount = normalHours = redHours = 0;
            publicRemainingFaces.Clear();
            for (int i = 0; i < ClocksPerBatch; i++)
            {
                var kind = i < 2 ? ClockKind.Normal : i == 2 ? ClockKind.Red :
                    random.NextDouble() < rules.ExtraRedProbability ? ClockKind.Red : ClockKind.Normal;
                clocks[i] = new ClockFace(rules.GenerateHours(random), kind);
                publicRemainingFaces.Add(clocks[i]);
                if (kind == ClockKind.Normal) { normalCount++; normalHours += clocks[i].Hours; }
                else { redCount++; redHours += clocks[i].Hours; }
            }
            Shuffle();
            Batch++;
            GrantItemsForBatch();
            Stage = TurnStage.BatchPreview;
            return true;
        }

        public bool CloseAndShuffle()
        {
            if (Stage != TurnStage.BatchPreview) return false;
            Stage = TurnStage.Shuffling;
            Shuffle();
            return true;
        }

        public bool CompleteShuffle()
        {
            if (Stage != TurnStage.Shuffling) return false;
            OpenItemPhase();
            return true;
        }

        public IReadOnlyList<ItemState> GetInventory(Participant owner)
        {
            ValidateParticipant(owner);
            return items.FindAll(item => item.Owner == owner && !item.IsConsumed).AsReadOnly();
        }

        public IReadOnlyList<ItemEffectState> GetCurrentItemEffects(Participant owner)
        {
            ValidateParticipant(owner);
            return currentItemEffects.FindAll(effect => effect.Source.Owner == owner).AsReadOnly();
        }

        public IReadOnlyList<ItemEffectState> GetPendingItemEffects(Participant owner)
        {
            ValidateParticipant(owner);
            return pendingItemEffects.FindAll(effect => effect.Source.Owner == owner).AsReadOnly();
        }

        public bool CanUseItems(Participant actor) => actor == ActiveParticipant && Stage == TurnStage.AwaitingItems &&
            itemUseAllowed[(int)actor] && !IsItemUseBlocked(actor);

        public ItemUseFailure CheckItemUse(Participant actor, int itemId)
        {
            if (actor != ActiveParticipant) return ItemUseFailure.WrongParticipant;
            if (IsItemUseBlocked(actor)) return ItemUseFailure.UseNotAllowed;
            if (Stage != TurnStage.AwaitingItems) return ItemUseFailure.WrongStage;
            int index = items.FindIndex(item => item.Id == itemId && item.Owner == actor);
            if (index < 0) return ItemUseFailure.NotOwned;
            if (items[index].IsConsumed) return ItemUseFailure.AlreadyConsumed;
            if (!itemUseAllowed[(int)actor]) return ItemUseFailure.UseNotAllowed;
            if (IsRealScrewdriver(items[index]) && HasScrewdriverProtection) return ItemUseFailure.AlreadyActive;
            if (IsRealTimeBypass(items[index]) && HasTimeBypass) return ItemUseFailure.AlreadyActive;
            if (!items[index].IsFoundationTest)
            {
                if (items[index].Kind == ItemKind.SpareKey && HasSpareKeyReadiness) return ItemUseFailure.AlreadyActive;
                if (items[index].Kind == ItemKind.RubberDuck && HasPendingRubberDuck(Other(actor))) return ItemUseFailure.AlreadyActive;
                if (items[index].Kind == ItemKind.Handcuffs && HasPendingHandcuffs(Other(actor))) return ItemUseFailure.AlreadyActive;
                if (items[index].Kind == ItemKind.Scissors) return CheckScissorsUse();
            }
            if (TryGetItemEffectTiming(itemId, out _)) return ItemUseFailure.None;
            return ItemUseFailure.EffectNotImplemented;
        }

        public ItemUseResult UseItem(Participant actor, int itemId)
        {
            var failure = CheckItemUse(actor, itemId);
            if (failure != ItemUseFailure.None) return new ItemUseResult(actor, itemId, failure);
            int index = items.FindIndex(item => item.Id == itemId);
            ItemState item = items[index];
            var consumed = new ItemState(item.Id, item.Kind, item.Owner, true, item.IsFoundationTest);
            TryGetItemEffectTiming(itemId, out var timing);
            bool delayed = !item.IsFoundationTest && (item.Kind == ItemKind.RubberDuck || item.Kind == ItemKind.Handcuffs);
            var effect = new ItemEffectState(consumed, timing, Turn, delayed ? (Participant?)Other(actor) : null);
            // 모든 검증 뒤 소모한다. 즉시 제거와 상대 다음 실제 차례 효과를 D 정산 효과와 구분한다.
            items[index] = consumed;
            ItemState? removed = null;
            if (!item.IsFoundationTest && item.Kind == ItemKind.Scissors) removed = RemoveScissorsTarget();
            else if (timing == ItemEffectTiming.CurrentTurn) currentItemEffects.Add(effect);
            else pendingItemEffects.Add(effect);
            if (GetInventory(actor).Count == 0) EndItemPhase(actor);
            return new ItemUseResult(actor, itemId, ItemUseFailure.None, effect, removed);
        }

        private bool TryGetItemEffectTiming(int itemId, out ItemEffectTiming timing)
        {
            timing = ItemEffectTiming.CurrentTurn;
#if UNITY_EDITOR
            if (testItemUses.TryGetValue(itemId, out var use) && use != ItemTestUse.Unavailable)
            {
                timing = use == ItemTestUse.RecordCurrentEffect ? ItemEffectTiming.CurrentTurn : ItemEffectTiming.FutureTurn;
                return true;
            }
#endif
            var index = items.FindIndex(item => item.Id == itemId && !item.IsFoundationTest);
            if (index < 0) return false;
            if (items[index].Kind == ItemKind.RubberDuck || items[index].Kind == ItemKind.Handcuffs) timing = ItemEffectTiming.FutureTurn;
            return true;
        }

        private static bool IsRealScrewdriver(ItemState item) => item.Kind == ItemKind.Screwdriver && !item.IsFoundationTest;
        private static bool IsRealTimeBypass(ItemState item) => item.Kind == ItemKind.TimeBypass && !item.IsFoundationTest;

        public bool EndItemPhase(Participant actor)
        {
            if (actor != ActiveParticipant || Stage != TurnStage.AwaitingItems) return false;
            Stage = TurnStage.AwaitingClock;
            return true;
        }

        private void OpenItemPhase()
        {
            ResolveHandcuffedTurns();
            ActivateRubberDuckForTurn();
            Stage = TurnStage.AwaitingItems;
            if (IsItemUseBlocked(ActiveParticipant))
                foreach (var effect in currentItemEffects)
                    if (!effect.Source.IsFoundationTest && effect.Source.Kind == ItemKind.RubberDuck && effect.Target == ActiveParticipant)
                        turnItemEvents.Add(new ItemTurnEvent(effect.Source, ActiveParticipant, ItemTurnEventKind.ItemsBlocked));
            if (GetInventory(ActiveParticipant).Count == 0 || !CanUseItems(ActiveParticipant)) EndItemPhase(ActiveParticipant);
        }

        private static void ValidateParticipant(Participant participant)
        {
            if (participant != Participant.Player && participant != Participant.Ai) throw new ArgumentOutOfRangeException(nameof(participant));
        }

#if UNITY_EDITOR
        internal int AddItemForTesting(Participant owner, ItemKind kind, ItemTestUse use)
        {
            ValidateParticipant(owner);
            if (!Enum.IsDefined(typeof(ItemKind), kind) || !Enum.IsDefined(typeof(ItemTestUse), use)) throw new ArgumentOutOfRangeException();
            if (Turn != 1 || (Stage != TurnStage.BatchComplete && Stage != TurnStage.BatchPreview))
                throw new InvalidOperationException("검증용 보유품은 첫 묶음의 관찰 전/중에만 주입하세요.");
            int id = nextItemId++;
            items.Add(new ItemState(id, kind, owner, false, use != ItemTestUse.Unavailable));
            testItemUses.Add(id, use);
            return id;
        }

        internal void SetItemUseAllowedForTesting(Participant owner, bool allowed)
        {
            ValidateParticipant(owner);
            itemUseAllowed[(int)owner] = allowed;
        }
#endif

        public bool CommitAction(Participant actor, TimeAction action)
        {
            if (Stage != TurnStage.AwaitingAction || actor != ActiveParticipant ||
                (action != TimeAction.Collect && action != TimeAction.Ration)) return false;
            Action = action;
            TransferredMinutes = 0;
            lastItemEffects.Clear();
            lastTransfers.Clear();
            Stage = TurnStage.Revealing;
            return true;
        }

        public bool CanSelectClock(Participant actor, int index) => Stage == TurnStage.AwaitingClock &&
            actor == ActiveParticipant && index >= 0 && index < ClocksPerBatch && !used[index] && DiscardedCount == 0;

        public int[] GetAvailableClockIndices()
        {
            var result = new List<int>(ClocksPerBatch - UsedCount);
            if (Stage != TurnStage.AwaitingAction && Stage != TurnStage.AwaitingClock) return result.ToArray();
            for (int i = 0; i < ClocksPerBatch; i++) if (!used[i]) result.Add(i);
            return result.ToArray();
        }

        public bool SelectClock(Participant actor, int index)
        {
            if (!CanSelectClock(actor, index)) return false;
            CurrentClock = index;
            if (BeginSpareKeySelection(index)) return true;
            Stage = TurnStage.AwaitingAction;
            return true;
        }

        public bool Settle()
        {
            if (Stage != TurnStage.Revealing) return false;
            ClockFace face = clocks[CurrentClock];
            bool ration = Action == TimeAction.Ration;
            var settlement = TimeTheftSettlement.Calculate(face, Action, LifeMinutes(ActiveParticipant), LifeMinutes(Other(ActiveParticipant)),
                BankMinutes, HasScrewdriverProtection, HasTimeBypass, rules.ScrewdriverReductionPercent, rules.TimeBypassPercent);
            TransferParticipant = settlement.TransferParticipantIsSelf ? ActiveParticipant : Other(ActiveParticipant);
            TransferredMinutes = settlement.TransferredMinutes;
            foreach (var effect in currentItemEffects)
            {
                if (!IsRealScrewdriver(effect.Source) || effect.Source.Owner != ActiveParticipant || effect.RegisteredTurn != Turn) continue;
                lastItemEffects.Add(new ItemEffectResult(effect.Source, settlement.DriverTriggered, settlement.PreventedMinutes));
            }
            int bypassed = 0;
            bool bypassActive = false;
            foreach (var effect in currentItemEffects)
            {
                if (!IsRealTimeBypass(effect.Source) || effect.Source.Owner != ActiveParticipant || effect.RegisteredTurn != Turn) continue;
                // A의 사용 순서와 무관하게 드라이버가 확정한 실제 손실에서만 나눈다.
                bypassActive = settlement.BypassTriggered;
                bypassed = settlement.BypassedMinutes;
                lastItemEffects.Add(new ItemEffectResult(effect.Source, bypassActive, 0, bypassed,
                    bypassActive ? TransferredMinutes - bypassed : 0,
                    bypassActive ? (Participant?)Other(TransferParticipant) : null));
            }
            if (ration) lastTransfers.Add(new TimeTransfer(TimeAccount.Bank, Account(TransferParticipant), TransferredMinutes));
            else
            {
                if (bypassActive) lastTransfers.Add(new TimeTransfer(Account(TransferParticipant), Account(Other(TransferParticipant)), bypassed));
                lastTransfers.Add(new TimeTransfer(Account(TransferParticipant), TimeAccount.Bank, TransferredMinutes - bypassed));
            }
            // 중간 잔액으로 승패를 판정하지 않고 세 계정의 최종값을 한 번에 반영한다.
            int player = PlayerLifeMinutes, ai = AiLifeMinutes, bank = BankMinutes;
            foreach (var transfer in lastTransfers)
            {
                if (transfer.From == TimeAccount.Player) player -= transfer.Minutes;
                else if (transfer.From == TimeAccount.Ai) ai -= transfer.Minutes;
                else bank -= transfer.Minutes;
                if (transfer.To == TimeAccount.Player) player += transfer.Minutes;
                else if (transfer.To == TimeAccount.Ai) ai += transfer.Minutes;
                else bank += transfer.Minutes;
            }
            PlayerLifeMinutes = player; AiLifeMinutes = ai; BankMinutes = bank;
            if (face.Kind == ClockKind.Normal) { normalCount--; normalHours -= face.Hours; }
            else { redCount--; redHours -= face.Hours; }
            used[CurrentClock] = true;
            UsedCount++;
            int publicIndex = publicRemainingFaces.FindIndex(known => known.Hours == face.Hours && known.Kind == face.Kind);
            publicRemainingFaces.RemoveAt(publicIndex);
            ForgetUsedSpareKeyClock(CurrentClock);
            foreach (var effect in currentItemEffects)
            {
                if (effect.Source.IsFoundationTest) continue;
                if (effect.Source.Kind == ItemKind.SpareKey && effect.Source.Owner == ActiveParticipant)
                    lastItemEffects.Add(new ItemEffectResult(effect.Source, true, 0));
                if (effect.Source.Kind == ItemKind.RubberDuck && effect.Target == ActiveParticipant)
                    turnItemEvents.Add(new ItemTurnEvent(effect.Source, ActiveParticipant, ItemTurnEventKind.RestrictionEnded));
            }
            currentItemEffects.Clear();
            Winner = PlayerLifeMinutes == 0 ? GameWinner.Ai : AiLifeMinutes == 0 ? GameWinner.Player : GameWinner.None;
            if (Winner != GameWinner.None)
            {
                foreach (var effect in pendingItemEffects)
                    if (!effect.Source.IsFoundationTest && effect.Target.HasValue)
                        turnItemEvents.Add(new ItemTurnEvent(effect.Source, effect.Target.Value, ItemTurnEventKind.Cancelled));
                pendingItemEffects.Clear();
                ClearSpareKeyKnowledge();
            }
            Stage = Winner == GameWinner.None ? TurnStage.Settling : TurnStage.GameOver;
            return true;
        }

        public bool CompleteTurn()
        {
            if (Stage != TurnStage.Settling) return false;
            turnItemEvents.Clear();
            ActiveParticipant = Other(ActiveParticipant);
            Turn++;
            CurrentClock = -1;
            if (UsedCount == TurnsPerBatch)
            {
                DiscardedCount = 1;
                Stage = TurnStage.BatchComplete;
            }
            else OpenItemPhase();
            return true;
        }

        // 비공개/폐기 시계는 공개 API에서도 숫자·종류를 반환하지 않는다.
        public ClockState GetClock(int index)
        {
            if (index < 0 || index >= ClocksPerBatch) throw new ArgumentOutOfRangeException(nameof(index));
            if (Stage == TurnStage.BatchPreview) return new ClockState(ClockVisibility.Open, clocks[index]);
            if (index == CurrentClock && (Stage == TurnStage.Revealing || Stage == TurnStage.Settling || Stage == TurnStage.GameOver))
                return new ClockState(ClockVisibility.Open, clocks[index]);
            if (used[index]) return new ClockState(ClockVisibility.Used, null);
            if (DiscardedCount == 1) return new ClockState(ClockVisibility.Discarded, null);
            return new ClockState(ClockVisibility.Hidden, null);
        }

        public AiObservation Observe(Participant participant) => new AiObservation(LifeMinutes(participant), LifeMinutes(Other(participant)),
            BankMinutes, normalCount, redCount, normalHours, redHours);

        private int LifeMinutes(Participant participant) => participant == Participant.Player ? PlayerLifeMinutes : AiLifeMinutes;
        private static Participant Other(Participant participant) => participant == Participant.Player ? Participant.Ai : Participant.Player;
        private static TimeAccount Account(Participant participant) => participant == Participant.Player ? TimeAccount.Player : TimeAccount.Ai;
        private void Shuffle()
        {
            for (int i = clocks.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                ClockFace old = clocks[i]; clocks[i] = clocks[j]; clocks[j] = old;
            }
        }
    }
}
