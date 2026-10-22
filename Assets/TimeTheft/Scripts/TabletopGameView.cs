using System;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    public sealed class TabletopGameView : MonoBehaviour
    {
        [SerializeField] private TabletopClockView[] clocks;
        [SerializeField] private TabletopFocusCamera focusCamera;
        [Header("씬에 배치한 계수기와 보관 장치")]
        [SerializeField] private TextMeshPro playerLife, aiLife, pot;
        [SerializeField] private TabletopFocusTarget[] inspectionTargets;
        [Header("종료 시에만 표시하는 결과 전표·다시하기")]
        [SerializeField] private TextMeshPro status, detail, restartLabel;
        [SerializeField] private TabletopFocusTarget restart;
        [SerializeField] private GameObject resultDisplay;
        [Header("작은 라운드 표시와 H 키 도움말")]
        [SerializeField] private TextMeshPro roundLabel;
        [SerializeField] private GameObject helpCard;
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

        public TabletopClockView[] Clocks => clocks;
        public TabletopFocusCamera FocusCamera => focusCamera;
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

        public void Initialize(Action<int> select, Action<ClockAction> choose, Action newGame)
        {
            if (clocks == null || clocks.Length != 3 || !focusCamera || !playerLife || !aiLife || !pot || !status || !detail || !restart || !restartLabel || !playerIndicator || !aiIndicator || !potIndicator || !resultDisplay || !roundLabel || !helpCard)
                throw new InvalidOperationException("TabletopGameView의 시계·장치·결과 전표·도움말 참조를 연결하세요.");
            for (int i = 0; i < clocks.Length; i++)
            {
                if (!clocks[i]) throw new InvalidOperationException("회중시계 3개를 연결하세요.");
                clocks[i].Initialize(i, select, choose);
            }
            if (initialized) restart.Clicked -= restartGame;
            restartGame = newGame;
            restart.Clicked += restartGame;
            if (!initialized)
            {
                playerNumberScale = playerLife.transform.localScale;
                aiNumberScale = aiLife.transform.localScale;
                potNumberScale = pot.transform.localScale;
                playerIndicatorColor = playerIndicator.color;
                aiIndicatorColor = aiIndicator.color;
                potIndicatorColor = potIndicator.color;
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
            roundLabel.text = "R " + roundNumber.ToString("00");
            IsResultVisible = false;
            Inspectable(false);
            foreach (var clock in clocks)
            {
                clock.ResetPresentation();
                clock.Show(0, false, "", TableView.Gold);
            }
        }

        public void OpenSelection()
        {
            foreach (var clock in clocks) clock.Show(0, false, "", TableView.Gold, true);
            Inspectable(true);
        }

        public void Allocate(TimeTheftMatch match)
        {
            hasSelectedClock = true;
            helpCard.SetActive(false);
            Inspectable(false);
            clocks[match.PlayerClock].SetSelected(true);
            clocks[match.PlayerClock].Show(0, false, "내 시계", TableView.PlayerColor);
            clocks[match.AiClock].Show(0, false, "상대 시계", TableView.AiColor);
            clocks[match.DiscardedClock].Hide();
        }

        public void RevealPlayer(TimeTheftMatch match)
        {
            clocks[match.PlayerClock].Show(match.PlayerValue, true, "내 시계", TableView.PlayerColor);
        }

        public void OpenDecision(TimeTheftMatch match)
        {
            clocks[match.PlayerClock].Actions(true);
            clocks[match.AiClock].Show(0, false, "상대 선택 중", TableView.AiColor);
            Inspectable(true);
        }

        public void RevealActions(TimeTheftMatch match)
        {
            foreach (var clock in clocks) clock.Actions(false);
            Inspectable(false);
            focusCamera.ReturnToOverview();
            clocks[match.PlayerClock].PlayActionFeedback(match.PlayerAction);
            clocks[match.AiClock].PlayActionFeedback(match.AiAction);
            clocks[match.PlayerClock].OwnerLabel.text = "나 · " + TableView.ActionName(match.PlayerAction);
            clocks[match.AiClock].OwnerLabel.text = "상대 · " + TableView.ActionName(match.AiAction);
        }

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
            if (t >= 1) exchanging = false;
        }

        public void RevealFinal(TimeTheftMatch match)
        {
            clocks[match.PlayerClock].Show(match.PlayerValue, true, "내 최종 시계", TableView.PlayerColor);
            clocks[match.AiClock].Show(match.AiValue, true, "상대 최종 시계", TableView.AiColor);
        }

        public void Settle(TimeTheftMatch match)
        {
            BeginNumberChange(match);
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
            playerLife.text = player + "h";
            aiLife.text = ai + "h";
            pot.text = stake + "h";
            playerLife.color = player <= 12 ? TableView.AiColor : TableView.Cream;
            aiLife.color = ai <= 12 ? TableView.AiColor : TableView.Cream;
        }

        private void NumberFeedback(TextMeshPro number, SpriteRenderer indicator, Vector3 scale, Color color, float pulse)
        {
            number.transform.localScale = scale * (1 + numberPulseScale * pulse);
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
            focusCamera.ReturnToOverview();
            exchanging = IsCountingTime = false;
            Inspectable(false);
            foreach (var clock in clocks) clock.ResetPresentation();
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
            foreach (var target in inspectionTargets) target.SetInputEnabled(enabled);
        }

        private void OnDestroy() { if (initialized && restart) restart.Clicked -= restartGame; }
    }
}
