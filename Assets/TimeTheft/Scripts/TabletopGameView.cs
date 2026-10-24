using System;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    public sealed class TabletopGameView : MonoBehaviour
    {
        [SerializeField] private TabletopClockView[] clocks;
        [SerializeField] private TabletopFocusCamera focusCamera;
        [Header("결정 단계에만 표시하는 작은 화면 고정 선택지")]
        [SerializeField] private TabletopDecisionUI decisionUI;
        [Header("공개 행동·교환 판정·계수기 변화량 (월드 표시)")]
        [SerializeField] private TabletopRoundFeedback roundFeedback;
        [Header("씬에 배치한 계수기와 보관 장치")]
        [SerializeField] private TextMeshPro playerLife, aiLife, pot;
        [Tooltip("통합 계수기의 HH:00 표시와 Player/Opponent 고유 색상을 유지한다. 규칙 값은 기존 시간 단위 정수다.")]
        [SerializeField] private bool useHoursMinutesDisplay;
        [SerializeField] private TabletopFocusTarget[] inspectionTargets;
        [Header("종료 시에만 표시하는 결과 전표·다시하기")]
        [SerializeField] private TextMeshPro status, detail, restartLabel;
        [SerializeField] private TabletopFocusTarget restart;
        [SerializeField] private GameObject resultDisplay;
        [Header("작은 라운드 표시와 H 키 도움말")]
        [SerializeField] private TextMeshPro roundLabel;
        [SerializeField] private GameObject helpCard;
        [Header("시계 배분 목적지 · 씬에서 위치/회전 편집")]
        [SerializeField] private Transform playerClockSlot, aiClockSlot, discardClockSlot;
        [SerializeField, Min(.05f)] private float allocationDuration = .65f;
        [SerializeField, Min(0)] private float allocationLift = .08f;
        [SerializeField, Min(0)] private float allocationArc = .12f;
        [SerializeField, Min(0)] private float aiSelectionPause = .45f;
        [Header("교환 궤적 (카메라·게임 규칙과 독립)")]
        [SerializeField, Min(0)] private float exchangeArc = .65f;
        [SerializeField, Min(0)] private float exchangeLift = .08f;
        [Header("계수기 변화 피드백")]
        [SerializeField] private SpriteRenderer playerIndicator, aiIndicator, potIndicator;
        [SerializeField, Range(0, .2f)] private float numberPulseScale = .065f;
        [SerializeField, Range(1, 2)] private float indicatorBrightness = 1.45f;
        private Action restartGame;
        private bool initialized;
        private bool hasSelectedClock;
        private int shownPlayer, shownAi, shownPot, fromPlayer, fromAi, fromPot, toPlayer, toAi, toPot;
        private int exchangePlayer, exchangeAi;
        private Vector3 exchangePlayerStart, exchangeAiStart, exchangeSide;
        private Vector3 playerNumberScale, aiNumberScale, potNumberScale;
        private Color playerIndicatorColor, aiIndicatorColor, potIndicatorColor;
        private bool exchanging;
        private int movingClock = -1;
        private bool discarding;
        private Vector3 allocationStart, allocationEnd, allocationSide;
        private Quaternion allocationStartRotation, allocationEndRotation, exchangePlayerRotation, exchangeAiRotation;
        private readonly Vector3[] dealStarts = new Vector3[3], dealEnds = new Vector3[3];
        private readonly Quaternion[] dealRotations = new Quaternion[3], homeRotations = new Quaternion[3];

        public TabletopClockView[] Clocks => clocks;
        public TabletopFocusCamera FocusCamera => focusCamera;
        public TabletopDecisionUI DecisionUI => decisionUI;
        public TabletopRoundFeedback RoundFeedback => roundFeedback;
        public TabletopFocusTarget Restart => restart;
        public TextMeshPro PlayerLife => playerLife;
        public TextMeshPro AiLife => aiLife;
        public TextMeshPro Pot => pot;
        public TextMeshPro Status => status;
        public TextMeshPro Detail => detail;
        public TextMeshPro RoundLabel => roundLabel;
        public bool HelpVisible => helpCard.activeSelf;
        public bool IsResultVisible { get; private set; }
        public bool IsCountingTime { get; private set; }
        public Transform PlayerClockSlot => playerClockSlot;
        public Transform AiClockSlot => aiClockSlot;
        public Transform DiscardClockSlot => discardClockSlot;
        public float AllocationDuration => allocationDuration;
        public float AiSelectionPause => aiSelectionPause;

        public void Initialize(Action<int> select, Action<ClockAction> choose, Action newGame)
        {
            if (clocks == null || clocks.Length != 3 || !focusCamera || !decisionUI || !roundFeedback || !playerLife || !aiLife || !pot || !status || !detail || !restart || !restartLabel || !resultDisplay || !roundLabel || !helpCard)
                throw new InvalidOperationException("TabletopGameView의 시계·장치·결과 전표·도움말 참조를 연결하세요.");
            if (!playerClockSlot || !aiClockSlot || !discardClockSlot)
                throw new InvalidOperationException("시계 배분 목적지 3개를 연결하세요.");
            for (int i = 0; i < clocks.Length; i++)
            {
                if (!clocks[i]) throw new InvalidOperationException("회중시계 3개를 연결하세요.");
                clocks[i].Initialize(i, select, choose);
            }
            decisionUI.Initialize(choose, focusCamera);
            if (initialized) restart.Clicked -= restartGame;
            restartGame = newGame;
            restart.Clicked += restartGame;
            if (!initialized)
            {
                playerNumberScale = playerLife.transform.localScale;
                aiNumberScale = aiLife.transform.localScale;
                potNumberScale = pot.transform.localScale;
                playerIndicatorColor = playerIndicator ? playerIndicator.color : Color.white;
                aiIndicatorColor = aiIndicator ? aiIndicator.color : Color.white;
                potIndicatorColor = potIndicator ? potIndicator.color : Color.white;
            }
            initialized = true;
        }

        public void ResetTable(TimeTheftMatch match)
        {
            focusCamera.ReturnToOverview();
            CancelPresentation();
            IsResultVisible = false;
            resultDisplay.SetActive(false);
            restart.SetInputEnabled(false);
            status.text = detail.text = "";
            helpCard.SetActive(!hasSelectedClock);
            Numbers(match);
        }

        public void PrepareDeal(int roundNumber)
        {
            roundFeedback.Clear();
            decisionUI.SetVisible(false);
            focusCamera.ReturnToOverview();
            roundLabel.text = "R " + roundNumber.ToString("00");
            IsResultVisible = false;
            Inspectable(false);
            for (int i = 0; i < clocks.Length; i++)
            {
                var clock = clocks[i];
                dealStarts[i] = clock.transform.position;
                dealRotations[i] = clock.transform.rotation;
                clock.ResetPresentation();
                dealEnds[i] = clock.transform.position;
                homeRotations[i] = clock.transform.rotation;
                clock.transform.SetPositionAndRotation(dealStarts[i], dealRotations[i]);
                clock.Show(0, false, "", TableView.Gold);
            }
        }

        public void DealFrame(float t)
        {
            for (int i = 0; i < clocks.Length; i++)
                clocks[i].transform.SetPositionAndRotation(Vector3.Lerp(dealStarts[i], dealEnds[i], t),
                    Quaternion.Slerp(dealRotations[i], homeRotations[i], t));
        }

        public void OpenSelection(TimeTheftMatch match)
        {
            bool playerTurn = match.Stage == RoundStage.Selecting;
            for (int i = 0; i < clocks.Length; i++)
            {
                if (i == match.PlayerClock || i == match.AiClock) continue;
                clocks[i].Show(0, false, playerTurn ? (match.PlayerSelectsFirst ? "먼저 선택" : "남은 시계 선택")
                    : "상대가 고르는 중", TableView.Gold, match.CanSelectClock(i));
            }
            // 비배율 연출은 FixedUpdate 없이도 끝난다. 입력을 열기 전에 이동한 Collider를 맞춘다.
            Physics.SyncTransforms();
            Inspectable(playerTurn);
        }

        public void BeginAllocation(int index, bool player)
        {
            if (player)
            {
                hasSelectedClock = true;
                helpCard.SetActive(false);
            }
            focusCamera.ReturnToOverview();
            Inspectable(false);
            foreach (var clock in clocks)
            {
                if (clock.FocusTarget.InputEnabled) clock.Show(0, false, "", TableView.Gold);
                else clock.FocusTarget.SetInputEnabled(false);
            }
            clocks[index].SetSelected(player);
            clocks[index].Show(0, false, player ? "내 시계" : "상대 시계", player ? TableView.PlayerColor : TableView.AiColor);
            BeginClockMove(index, player ? playerClockSlot : aiClockSlot, false);
        }

        public void BeginDiscard(int index)
        {
            clocks[index].Show(0, false, "", TableView.Muted);
            BeginClockMove(index, discardClockSlot, true);
        }

        private void BeginClockMove(int index, Transform destination, bool discard)
        {
            movingClock = index;
            discarding = discard;
            allocationStart = clocks[index].transform.position;
            allocationEnd = destination.position;
            allocationStartRotation = clocks[index].transform.rotation;
            allocationEndRotation = destination.rotation;
            allocationSide = Vector3.Cross(Vector3.up, allocationEnd - allocationStart).normalized * allocationArc;
        }

        public void AllocationFrame(float t)
        {
            if (movingClock < 0) return;
            t = Mathf.Clamp01(t);
            float arc = Mathf.Sin(t * Mathf.PI);
            // 공용 줄의 다른 시계를 가로지르지 않도록 먼저 소유자 쪽으로 당긴다.
            Vector3 control = new Vector3(allocationStart.x, allocationEnd.y, allocationEnd.z);
            Vector3 position = discarding ? Vector3.Lerp(allocationStart, allocationEnd, t)
                : Vector3.Lerp(Vector3.Lerp(allocationStart, control, t), Vector3.Lerp(control, allocationEnd, t), t);
            clocks[movingClock].transform.SetPositionAndRotation(
                position + allocationSide * arc + Vector3.up * (allocationLift * arc),
                Quaternion.Slerp(allocationStartRotation, allocationEndRotation, t));
            if (t < 1) return;
            if (discarding) clocks[movingClock].Hide();
            movingClock = -1;
        }

        public void RevealPlayer(TimeTheftMatch match)
        {
            clocks[match.PlayerClock].Show(match.PlayerValue, true, "내 시계", TableView.PlayerColor);
        }

        public void OpenDecision(TimeTheftMatch match)
        {
            decisionUI.SetVisible(true);
            clocks[match.AiClock].Show(0, false, "상대 선택 중", TableView.AiColor);
            Physics.SyncTransforms();
            Inspectable(true);
        }

        public void RevealActions(TimeTheftMatch match)
        {
            decisionUI.SetVisible(false);
            foreach (var clock in clocks) clock.Actions(false);
            Inspectable(false);
            focusCamera.ReturnToOverview();
            clocks[match.PlayerClock].PlayActionFeedback(match.PlayerAction);
            clocks[match.AiClock].PlayActionFeedback(match.AiAction);
            clocks[match.PlayerClock].OwnerLabel.text = "나 · " + TableView.ActionName(match.PlayerAction);
            clocks[match.AiClock].OwnerLabel.text = "상대 · " + TableView.ActionName(match.AiAction);
            roundFeedback.ShowActions(match.PlayerAction, match.AiAction);
        }

        public void ShowExchangeVerdict(TimeTheftMatch match) => roundFeedback.ShowExchange(match.DidExchange);

        public void Exchange(TimeTheftMatch match, int oldPlayer, int oldAi)
        {
            if (match.DidExchange)
            {
                clocks[oldPlayer].SetSelected(false);
                clocks[oldAi].SetSelected(false);
                exchangePlayer = oldPlayer;
                exchangeAi = oldAi;
                exchangePlayerStart = clocks[oldPlayer].transform.position;
                exchangeAiStart = clocks[oldAi].transform.position;
                exchangePlayerRotation = clocks[oldPlayer].transform.rotation;
                exchangeAiRotation = clocks[oldAi].transform.rotation;
                exchangeSide = Vector3.Cross(Vector3.up, exchangeAiStart - exchangePlayerStart).normalized * exchangeArc;
                exchanging = true;
                clocks[oldPlayer].Show(0, false, "상대에게", TableView.AiColor);
                clocks[oldAi].Show(0, false, "나에게", TableView.PlayerColor);
            }
        }

        public void ExchangeFrame(float t)
        {
            if (!exchanging) return;
            t = Mathf.Clamp01(t);
            float arc = Mathf.Sin(t * Mathf.PI);
            Vector3 rise = Vector3.up * (arc * exchangeLift);
            clocks[exchangePlayer].transform.position = Vector3.Lerp(exchangePlayerStart, exchangeAiStart, t) + exchangeSide * arc + rise;
            clocks[exchangeAi].transform.position = Vector3.Lerp(exchangeAiStart, exchangePlayerStart, t) - exchangeSide * arc + rise;
            clocks[exchangePlayer].transform.rotation = Quaternion.Slerp(exchangePlayerRotation, exchangeAiRotation, t);
            clocks[exchangeAi].transform.rotation = Quaternion.Slerp(exchangeAiRotation, exchangePlayerRotation, t);
            if (t >= 1) exchanging = false;
        }

        public void RevealFinal(TimeTheftMatch match)
        {
            clocks[match.PlayerClock].Show(match.PlayerValue, true, "내 최종 시계", TableView.PlayerColor);
            clocks[match.AiClock].Show(match.AiValue, true, "상대 최종 시계", TableView.AiColor);
            roundFeedback.ShowValues();
        }

        public void Settle(TimeTheftMatch match)
        {
            roundFeedback.ShowSettlement(match.PlayerLoss, match.AiLoss);
            BeginNumberChange(match);
        }

        public void HoldRoundResult() => roundFeedback.HoldResult();

        public void ShowAward(TimeTheftMatch match) => roundFeedback.ShowAward(match.Winner == MatchWinner.Player, match.Award);

        public void CloseRound() => roundFeedback.PrepareNextRound();

        public void RoundCloseFrame(float t)
        {
            foreach (var clock in clocks)
                if (clock.gameObject.activeSelf) clock.CloseFrame(t);
        }

        public void Numbers(TimeTheftMatch match)
        {
            SetNumbers(match.PlayerLife, match.AiLife, match.Pot);
            IsCountingTime = false;
            ResetNumberFeedback();
        }

        public void BeginNumberChange(TimeTheftMatch match)
        {
            fromPlayer = shownPlayer;
            fromAi = shownAi;
            fromPot = shownPot;
            toPlayer = match.PlayerLife;
            toAi = match.AiLife;
            toPot = match.Pot;
            IsCountingTime = true;
        }

        public void NumbersFrame(float t)
        {
            if (!IsCountingTime) return;
            t = Mathf.Clamp01(t);
            roundFeedback.TransferFrame(t);
            int player = Mathf.RoundToInt(Mathf.Lerp(fromPlayer, toPlayer, t));
            int ai = Mathf.RoundToInt(Mathf.Lerp(fromAi, toAi, t));
            // 표시 중에도 수명 + 판돈 총량을 보존한다. 독립 반올림 오차를 만들지 않는다.
            int stake = fromPot + (fromPlayer - player) + (fromAi - ai);
            SetNumbers(player, ai, t >= 1 ? toPot : stake);
            float pulse = Mathf.Sin(t * Mathf.PI);
            NumberFeedback(playerLife, playerIndicator, playerNumberScale, playerIndicatorColor, fromPlayer != toPlayer ? pulse : 0);
            NumberFeedback(aiLife, aiIndicator, aiNumberScale, aiIndicatorColor, fromAi != toAi ? pulse : 0);
            NumberFeedback(pot, potIndicator, potNumberScale, potIndicatorColor, fromPot != toPot ? pulse : 0);
            if (t >= 1)
            {
                IsCountingTime = false;
                ResetNumberFeedback();
            }
        }

        private void SetNumbers(int player, int ai, int stake)
        {
            shownPlayer = player;
            shownAi = ai;
            shownPot = stake;
            if (!playerLife || !aiLife || !pot) return;
            playerLife.text = FormatTime(player);
            aiLife.text = FormatTime(ai);
            pot.text = FormatTime(stake);
            if (!useHoursMinutesDisplay)
            {
                playerLife.color = player <= 12 ? TableView.AiColor : TableView.Cream;
                aiLife.color = ai <= 12 ? TableView.AiColor : TableView.Cream;
            }
        }

        private string FormatTime(int hours) => useHoursMinutesDisplay ? hours.ToString("00") + ":00" : hours + "h";

        private void NumberFeedback(TextMeshPro number, SpriteRenderer indicator, Vector3 scale, Color color, float pulse)
        {
            if (!number) return;
            number.transform.localScale = scale * (1 + numberPulseScale * pulse);
            if (!indicator) return;
            Color bright = color * indicatorBrightness;
            bright.a = color.a;
            indicator.color = Color.Lerp(color, bright, pulse);
        }

        private void ResetNumberFeedback()
        {
            NumberFeedback(playerLife, playerIndicator, playerNumberScale, playerIndicatorColor, 0);
            NumberFeedback(aiLife, aiIndicator, aiNumberScale, aiIndicatorColor, 0);
            NumberFeedback(pot, potIndicator, potNumberScale, potIndicatorColor, 0);
        }

        public void CancelPresentation()
        {
            if (!initialized) return;
            if (decisionUI) decisionUI.SetVisible(false);
            if (roundFeedback) roundFeedback.Clear();
            if (focusCamera) focusCamera.ReturnToOverview();
            exchanging = IsCountingTime = false;
            movingClock = -1;
            Inspectable(false);
            foreach (var clock in clocks)
                if (clock) clock.ResetPresentation();
            ResetNumberFeedback();
        }

        public void Result(TimeTheftMatch match)
        {
            Numbers(match);
            IsResultVisible = true;
            helpCard.SetActive(false);
            resultDisplay.SetActive(true);
            restart.SetInputEnabled(true);
            restartLabel.text = "다시하기";
            Inspectable(true);
            status.text = match.Winner == MatchWinner.Player ? "당신이 살아남았습니다" : match.Winner == MatchWinner.Ai ? "당신의 시간이 다했습니다" : "무승부 · 두 사람 모두 0h";
            detail.text = match.Winner == MatchWinner.Draw ? "남은 판돈 " + match.Pot + "h · 획득자 없음"
                    : (match.Winner == MatchWinner.Player ? "나" : "상대") + "에게 판돈 " + match.Award + "h 지급";
            status.color = match.Winner == MatchWinner.Player ? TableView.PlayerColor : TableView.AiColor;
        }

        public void ToggleHelp()
        {
            helpCard.SetActive(!helpCard.activeSelf);
            if (helpCard.activeSelf) focusCamera.ReturnToOverview();
        }

        private void Inspectable(bool enabled)
        {
            foreach (var target in inspectionTargets)
                if (target) target.SetInputEnabled(enabled);
        }

        private void OnDestroy() { if (initialized && restart) restart.Clicked -= restartGame; }
    }
}
