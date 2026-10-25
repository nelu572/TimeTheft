using System;
using System.Collections.Generic;

namespace TimeTheft
{
    public enum Participant { Player, Ai }
    public enum TimeAction { Collect, Ration }
    public enum ClockKind { Normal, Red }
    public enum TurnStage { BatchComplete, BatchPreview, Shuffling, AwaitingAction, Revealing, Settling, GameOver, AwaitingClock }
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

    public static class TimeTheftAi
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
    public sealed class TimeTheftMatch
    {
        public const int ClocksPerBatch = 5;
        public const int TurnsPerBatch = 4;
        public const int InitialContributionMinutes = 10 * GameTime.MinutesPerHour;
        private readonly GameRules rules;
        private readonly Random random;
        private readonly ClockFace[] clocks = new ClockFace[ClocksPerBatch];
        private readonly bool[] used = new bool[ClocksPerBatch];
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

        public TimeTheftMatch(GameRules rules, Random random)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (random == null) throw new ArgumentNullException(nameof(random));
            rules.Validate();
            this.rules = rules.Copy();
            this.random = random;
            PlayerLifeMinutes = AiLifeMinutes = rules.InitialLifeHours * GameTime.MinutesPerHour - InitialContributionMinutes;
            BankMinutes = InitialContributionMinutes * 2;
            ActiveParticipant = rules.FirstParticipant;
        }

        public bool BeginBatch()
        {
            if (Stage != TurnStage.BatchComplete) return false;
            UsedCount = DiscardedCount = 0;
            CurrentClock = -1;
            Array.Clear(used, 0, used.Length);
            normalCount = redCount = normalHours = redHours = 0;
            for (int i = 0; i < ClocksPerBatch; i++)
            {
                var kind = i < 2 ? ClockKind.Normal : i == 2 ? ClockKind.Red :
                    random.NextDouble() < rules.ExtraRedProbability ? ClockKind.Red : ClockKind.Normal;
                clocks[i] = new ClockFace(rules.GenerateHours(random), kind);
                if (kind == ClockKind.Normal) { normalCount++; normalHours += clocks[i].Hours; }
                else { redCount++; redHours += clocks[i].Hours; }
            }
            Shuffle();
            Batch++;
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
            Stage = TurnStage.AwaitingClock;
            return true;
        }

        public bool CommitAction(Participant actor, TimeAction action)
        {
            if (Stage != TurnStage.AwaitingAction || actor != ActiveParticipant ||
                (action != TimeAction.Collect && action != TimeAction.Ration)) return false;
            Action = action;
            TransferredMinutes = 0;
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
            Stage = TurnStage.AwaitingAction;
            return true;
        }

        public bool Settle()
        {
            if (Stage != TurnStage.Revealing) return false;
            ClockFace face = clocks[CurrentClock];
            bool ration = Action == TimeAction.Ration;
            bool self = ration ? face.Kind == ClockKind.Normal : face.Kind == ClockKind.Red;
            TransferParticipant = self ? ActiveParticipant : Other(ActiveParticipant);
            int requested = face.Hours * (ration ? GameTime.RationMinutesPerHour : GameTime.MinutesPerHour);
            TransferredMinutes = Math.Min(requested, ration ? BankMinutes : LifeMinutes(TransferParticipant));
            int change = ration ? TransferredMinutes : -TransferredMinutes;
            if (TransferParticipant == Participant.Player) PlayerLifeMinutes += change;
            else AiLifeMinutes += change;
            BankMinutes -= change;
            if (face.Kind == ClockKind.Normal) { normalCount--; normalHours -= face.Hours; }
            else { redCount--; redHours -= face.Hours; }
            used[CurrentClock] = true;
            UsedCount++;
            Winner = PlayerLifeMinutes == 0 ? GameWinner.Ai : AiLifeMinutes == 0 ? GameWinner.Player : GameWinner.None;
            Stage = Winner == GameWinner.None ? TurnStage.Settling : TurnStage.GameOver;
            return true;
        }

        public bool CompleteTurn()
        {
            if (Stage != TurnStage.Settling) return false;
            ActiveParticipant = Other(ActiveParticipant);
            Turn++;
            CurrentClock = -1;
            if (UsedCount == TurnsPerBatch)
            {
                DiscardedCount = 1;
                Stage = TurnStage.BatchComplete;
            }
            else Stage = TurnStage.AwaitingClock;
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
