using TimeTheft.Legacy;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    public enum RoundPresentationPhase { None, Actions, Exchange, Values, Settlement, ResultHold, Preparing, Award }

    // 공개된 행동과 실제 정산액만 전달받는다. 규칙 계산이나 입력을 소유하지 않는다.
    public sealed class TabletopRoundFeedback : MonoBehaviour
    {
        [Header("소유 위치에 남는 행동 표식")]
        [SerializeField] private GameObject actionDisplay;
        [SerializeField] private TextMeshPro playerAction, aiAction, verdict;
        [SerializeField] private SpriteRenderer playerActionIcon, aiActionIcon;
        [SerializeField] private Sprite keepIcon, swapIcon;
        [Header("각 계수기 옆의 실제 변화량")]
        [SerializeField] private TextMeshPro playerDelta, aiDelta, potDelta;
        [SerializeField] private Transform playerSource, aiSource, bankDestination;
        [Header("수명 계수기에서 Time Bank로 이동하는 시간 표시")]
        [SerializeField] private SpriteRenderer playerTransfer, aiTransfer;
        [SerializeField, Min(0)] private float transferArc = .3f;
        private Vector3 transferStart, transferEnd;
        private bool awarding, transferPlayer, transferAi;

        public RoundPresentationPhase Phase { get; private set; }
        public TextMeshPro PlayerAction => playerAction;
        public TextMeshPro AiAction => aiAction;
        public TextMeshPro Verdict => verdict;
        public TextMeshPro PlayerDelta => playerDelta;
        public TextMeshPro AiDelta => aiDelta;
        public TextMeshPro PotDelta => potDelta;
        public SpriteRenderer PlayerTransfer => playerTransfer;
        public SpriteRenderer AiTransfer => aiTransfer;
        public bool IsVisible => Phase != RoundPresentationPhase.None;

        public void Clear()
        {
            Phase = RoundPresentationPhase.None;
            if (actionDisplay) actionDisplay.SetActive(false);
            foreach (var label in new[] { verdict, playerDelta, aiDelta, potDelta })
                if (label) { label.text = ""; label.gameObject.SetActive(false); }
            HideTransfers();
        }

        public void ShowActions(ClockAction player, ClockAction ai)
        {
            Clear();
            Phase = RoundPresentationPhase.Actions;
            actionDisplay.SetActive(true);
            playerAction.text = "나 · " + TableView.ActionName(player);
            aiAction.text = "상대 · " + TableView.ActionName(ai);
            playerActionIcon.sprite = player == ClockAction.Keep ? keepIcon : swapIcon;
            aiActionIcon.sprite = ai == ClockAction.Keep ? keepIcon : swapIcon;
        }

        public void ShowExchange(bool exchange)
        {
            Phase = RoundPresentationPhase.Exchange;
            ShowLabel(verdict, exchange ? "다른 선택 · 시계 교환" : "같은 선택 · 교환 없음");
        }

        public void ShowValues() => Phase = RoundPresentationPhase.Values;

        public void ShowSettlement(int playerLoss, int aiLoss)
        {
            Phase = RoundPresentationPhase.Settlement;
            awarding = false;
            ShowLabel(playerDelta, "−" + playerLoss + "h");
            ShowLabel(aiDelta, "−" + aiLoss + "h");
            ShowLabel(potDelta, "+" + (playerLoss + aiLoss) + "h");
            playerDelta.color = aiDelta.color = TableView.AiColor;
            potDelta.color = TableView.Gold;
            transferPlayer = playerLoss > 0;
            transferAi = aiLoss > 0;
        }

        public void TransferFrame(float t)
        {
            if (Phase != RoundPresentationPhase.Settlement && Phase != RoundPresentationPhase.Award) return;
            t = Mathf.Clamp01(t);
            if (awarding)
                MoveTransfer(playerTransfer, transferStart, transferEnd, t, true);
            else
            {
                MoveTransfer(playerTransfer, playerSource.position, bankDestination.position, t, transferPlayer);
                MoveTransfer(aiTransfer, aiSource.position, bankDestination.position, t, transferAi);
            }
        }

        public void HoldResult()
        {
            Phase = RoundPresentationPhase.ResultHold;
            HideTransfers();
        }

        public void PrepareNextRound()
        {
            Clear();
            Phase = RoundPresentationPhase.Preparing;
            ShowLabel(verdict, "다음 라운드 준비");
        }

        public void ShowAward(bool playerWins, int award)
        {
            Phase = RoundPresentationPhase.Award;
            awarding = true;
            HideTransfers();
            transferStart = bankDestination.position;
            transferEnd = playerWins ? playerSource.position : aiSource.position;
            ShowLabel(verdict, playerWins ? "Time Bank → 나" : "Time Bank → 상대");
            ShowLabel(playerDelta, playerWins ? "+" + award + "h" : "0h");
            ShowLabel(aiDelta, playerWins ? "0h" : "+" + award + "h");
            ShowLabel(potDelta, "−" + award + "h");
            playerDelta.color = playerWins ? TableView.PlayerColor : TableView.Muted;
            aiDelta.color = playerWins ? TableView.Muted : TableView.PlayerColor;
            potDelta.color = TableView.Gold;
        }

        private void MoveTransfer(SpriteRenderer marker, Vector3 start, Vector3 end, float t, bool active)
        {
            marker.gameObject.SetActive(active && t > 0 && t < 1);
            marker.transform.position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * transferArc);
        }

        private void HideTransfers()
        {
            if (playerTransfer) playerTransfer.gameObject.SetActive(false);
            if (aiTransfer) aiTransfer.gameObject.SetActive(false);
        }

        private static void ShowLabel(TextMeshPro label, string text)
        {
            label.text = text;
            label.gameObject.SetActive(true);
        }
    }
}
