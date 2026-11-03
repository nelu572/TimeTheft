using System;

namespace TimeTheft
{
    // 실제 D 정산과 AI의 가상 평가가 함께 쓰는 정수 분 계산. 상태를 변경하지 않는다.
    public readonly struct TimeSettlement
    {
        public int OwnLifeMinutes { get; }
        public int OpponentLifeMinutes { get; }
        public int BankMinutes { get; }
        public int TransferredMinutes { get; }
        public int PreventedMinutes { get; }
        public int BypassedMinutes { get; }
        public bool TransferParticipantIsSelf { get; }
        public bool DriverTriggered { get; }
        public bool BypassTriggered { get; }
        internal TimeSettlement(int own, int opponent, int bank, int transferred, int prevented, int bypassed,
            bool self, bool driverTriggered, bool bypassTriggered)
        {
            OwnLifeMinutes = own; OpponentLifeMinutes = opponent; BankMinutes = bank;
            TransferredMinutes = transferred; PreventedMinutes = prevented; BypassedMinutes = bypassed;
            TransferParticipantIsSelf = self; DriverTriggered = driverTriggered; BypassTriggered = bypassTriggered;
        }
    }

    public static class TimeTheftSettlement
    {
        public static TimeSettlement Calculate(ClockFace face, TimeAction action, int ownLifeMinutes,
            int opponentLifeMinutes, int bankMinutes, bool driver, bool bypass, int reductionPercent, int bypassPercent)
        {
            if (face.Hours < 1 || (face.Kind != ClockKind.Normal && face.Kind != ClockKind.Red) ||
                (action != TimeAction.Collect && action != TimeAction.Ration) || ownLifeMinutes < 0 || opponentLifeMinutes < 0 ||
                bankMinutes < 0 || (long)ownLifeMinutes + opponentLifeMinutes + bankMinutes > int.MaxValue ||
                reductionPercent < 0 || reductionPercent > 100 || bypassPercent < 0 || bypassPercent > 100)
                throw new ArgumentException("정산 입력값을 확인하세요.");
            bool ration = action == TimeAction.Ration;
            bool self = ration ? face.Kind == ClockKind.Normal : face.Kind == ClockKind.Red;
            long requested = (long)face.Hours * (ration ? GameTime.RationMinutesPerHour : GameTime.MinutesPerHour);
            int actual = (int)Math.Min(requested, ration ? bankMinutes : self ? ownLifeMinutes : opponentLifeMinutes);
            bool driverTriggered = driver && !ration && face.Kind == ClockKind.Red;
            int prevented = driverTriggered ? (int)((long)actual * reductionPercent / 100) : 0;
            actual -= prevented;
            bool bypassTriggered = bypass && !ration;
            int bypassed = bypassTriggered ? (int)((long)actual * bypassPercent / 100) : 0;
            int own = ownLifeMinutes, opponent = opponentLifeMinutes, bank = bankMinutes;
            if (ration)
            {
                if (self) own += actual; else opponent += actual;
                bank -= actual;
            }
            else
            {
                if (self) { own -= actual; opponent += bypassed; }
                else { opponent -= actual; own += bypassed; }
                bank += actual - bypassed;
            }
            return new TimeSettlement(own, opponent, bank, actual, prevented, bypassed, self, driverTriggered, bypassTriggered);
        }
    }
}
