using System;

namespace TimeTheft
{
    public enum RoundStage { Dealing, Selecting, Inspecting, Deciding, Revealing, Exchanging, Settling, RoundComplete, GameOver }
    public enum ClockAction { Keep, Swap }
    public enum MatchWinner { None, Player, Ai, Draw }

    public static class PrototypePolicy
    {
        // 임시 정책의 변경 지점: 선생성·플레이어 선선택·비공개 폐기.
        public static int[] GenerateClocks(PrototypeRules rules, Random random)
        {
            return new[] { random.Next(rules.MinimumValue, rules.MaximumValue + 1),
                random.Next(rules.MinimumValue, rules.MaximumValue + 1),
                random.Next(rules.MinimumValue, rules.MaximumValue + 1) };
        }

        public static int SelectAiClock(int playerIndex, Random random)
        {
            int choice = random.Next(2);
            for (int i = 0; i < 3; i++)
                if (i != playerIndex && choice-- == 0) return i;
            throw new InvalidOperationException();
        }

        public static int ActualLoss(int life, int clockValue) => Math.Min(life, clockValue);
        public static MatchWinner DetermineWinner(int playerLife, int aiLife)
        {
            if (playerLife == 0 && aiLife == 0) return MatchWinner.Draw;
            if (playerLife == 0) return MatchWinner.Ai;
            if (aiLife == 0) return MatchWinner.Player;
            return MatchWinner.None;
        }
    }

    public static class ClockAi
    {
        // 자신의 값만 받는다. 상대/폐기 시계와 Match에 접근할 수 없다.
        public static ClockAction Decide(int ownValue, PrototypeRules rules, Random random)
        {
            float range = Math.Max(1, rules.MaximumValue - rules.MinimumValue);
            float t = (ownValue - rules.MinimumValue) / range;
            double probability = rules.AiSwapAtMinimum + (rules.AiSwapAtMaximum - rules.AiSwapAtMinimum) * t;
            return random.NextDouble() < probability ? ClockAction.Swap : ClockAction.Keep;
        }
    }

    public sealed class TimeTheftMatch
    {
        private readonly PrototypeRules rules;
        private readonly Random random;
        private int[] clocks = new int[3];
        public RoundStage Stage { get; private set; }
        public int Round { get; private set; }
        public int PlayerLife { get; private set; }
        public int AiLife { get; private set; }
        public int Pot { get; private set; }
        public int Award { get; private set; }
        public int PlayerClock { get; private set; } = -1;
        public int AiClock { get; private set; } = -1;
        public int DiscardedClock => 3 - PlayerClock - AiClock;
        public int PlayerValue => clocks[PlayerClock];
        public int AiValue => clocks[AiClock];
        public int PlayerLoss { get; private set; }
        public int AiLoss { get; private set; }
        public ClockAction PlayerAction { get; private set; }
        public ClockAction AiAction { get; private set; }
        public bool DidExchange => PlayerAction != AiAction;
        public MatchWinner Winner { get; private set; }

        public TimeTheftMatch(PrototypeRules rules, Random random)
        {
            if (rules == null || random == null) throw new ArgumentNullException();
            if (rules.InitialLife < 1 || rules.MinimumValue < 1 || rules.MaximumValue > 12 || rules.MinimumValue > rules.MaximumValue)
                throw new ArgumentException("수명과 시계 값 범위를 확인하세요.");
            this.rules = rules;
            this.random = random;
            PlayerLife = AiLife = rules.InitialLife;
            Stage = RoundStage.RoundComplete;
        }

        public bool BeginRound()
        {
            if (Stage != RoundStage.RoundComplete || Winner != MatchWinner.None) return false;
            clocks = PrototypePolicy.GenerateClocks(rules, random);
            Round++;
            PlayerClock = AiClock = -1;
            PlayerLoss = AiLoss = 0;
            Stage = RoundStage.Dealing;
            return true;
        }

        public bool OpenSelection() => Advance(RoundStage.Dealing, RoundStage.Selecting);

        public bool SelectClock(int index)
        {
            if (Stage != RoundStage.Selecting || index < 0 || index > 2) return false;
            PlayerClock = index;
            AiClock = PrototypePolicy.SelectAiClock(index, random);
            Stage = RoundStage.Inspecting;
            return true;
        }

        public bool OpenDecision() => Advance(RoundStage.Inspecting, RoundStage.Deciding);

        public bool CommitAction(ClockAction action)
        {
            if (Stage != RoundStage.Deciding || (action != ClockAction.Keep && action != ClockAction.Swap)) return false;
            PlayerAction = action;
            AiAction = ClockAi.Decide(AiValue, rules, random);
            Stage = RoundStage.Revealing;
            return true;
        }

        public bool ResolveExchange()
        {
            if (!Advance(RoundStage.Revealing, RoundStage.Exchanging)) return false;
            if (DidExchange)
            {
                int oldPlayer = PlayerClock;
                PlayerClock = AiClock;
                AiClock = oldPlayer;
            }
            return true;
        }

        public bool Settle()
        {
            if (!Advance(RoundStage.Exchanging, RoundStage.Settling)) return false;
            PlayerLoss = PrototypePolicy.ActualLoss(PlayerLife, PlayerValue);
            AiLoss = PrototypePolicy.ActualLoss(AiLife, AiValue);
            PlayerLife -= PlayerLoss;
            AiLife -= AiLoss;
            Pot += PlayerLoss + AiLoss;
            Winner = PrototypePolicy.DetermineWinner(PlayerLife, AiLife);
            return true;
        }

        public bool FinishRound()
        {
            if (Stage != RoundStage.Settling) return false;
            Stage = Winner == MatchWinner.None ? RoundStage.RoundComplete : RoundStage.GameOver;
            if (Winner == MatchWinner.Player || Winner == MatchWinner.Ai)
            {
                Award = Pot;
                if (Winner == MatchWinner.Player) PlayerLife += Award;
                else AiLife += Award;
                Pot = 0;
            }
            // 동시 탈락은 무승부. 획득자 없이 판돈을 결과에 남긴다.
            return true;
        }

        public string DebugSnapshot() => $"{Stage} / ROUND {Round}\n시계 [ {string.Join(", ", clocks)} ]\n보유 인덱스 P:{PlayerClock} AI:{AiClock}\n행동 P:{PlayerAction} AI:{AiAction}\n수명 P:{PlayerLife} AI:{AiLife} / 판돈:{Pot} / 보상:{Award} / {Winner}";

        private bool Advance(RoundStage expected, RoundStage next)
        {
            if (Stage != expected) return false;
            Stage = next;
            return true;
        }
    }
}
