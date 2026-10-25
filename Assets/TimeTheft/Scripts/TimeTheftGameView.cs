using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace TimeTheft
{
    public sealed class TimeTheftGameView : MonoBehaviour
    {
        [SerializeField] private ClockTokenView[] clocks;
        [SerializeField] private TabletopFocusCamera focusCamera;
        [SerializeField] private TabletopFocusTarget[] inspectionTargets;
        [Header("기존 통합 계수기 · 수치와 기본 색상만 재사용")]
        [SerializeField] private TextMeshPro playerLife, aiLife, bank, batchLabel;
        [SerializeField] private TextMeshPro playerDelta, aiDelta, bankDelta;
        [Header("화면 고정 행동 입력")]
        [SerializeField] private UnityEngine.UI.Button collectButton;
        [FormerlySerializedAs("withdrawButton")]
        [SerializeField] private UnityEngine.UI.Button rationButton;
        [SerializeField] private TextMeshProUGUI phaseLabel;
        [Header("기존 도움말과 결과 전표")]
        [SerializeField] private GameObject helpCard, resultDisplay;
        [SerializeField] private TextMeshPro resultTitle, resultDetail;
        [SerializeField] private TabletopFocusTarget restart;
        private Action restartGame;
        private Action<TimeAction> choose;
        private bool initialized;
        private int fromPlayer, fromAi, fromBank, toPlayer, toAi, toBank;
        private Vector3 playerScale, aiScale, bankScale;
        public ClockTokenView[] Clocks => (ClockTokenView[])clocks.Clone();
        public UnityEngine.UI.Button CollectButton => collectButton;
        public UnityEngine.UI.Button RationButton => rationButton;
        public TextMeshProUGUI PhaseLabel => phaseLabel;
        public TextMeshPro PlayerLife => playerLife;
        public TextMeshPro AiLife => aiLife;
        public TextMeshPro Bank => bank;
        public TabletopFocusCamera FocusCamera => focusCamera;
        public bool IsResultVisible => resultDisplay.activeSelf;
        public bool IsCountingTime { get; private set; }

        public void Initialize(Action<int> selectClock, Action<TimeAction> action, Action newGame)
        {
            if (clocks == null || clocks.Length != TimeTheftMatch.ClocksPerBatch || !focusCamera ||
                !playerLife || !aiLife || !bank || !batchLabel || !collectButton || !rationButton ||
                !phaseLabel || !helpCard || !resultDisplay || !resultTitle || !resultDetail || !restart ||
                !playerDelta || !aiDelta || !bankDelta)
                throw new InvalidOperationException("TimeTheftGameView의 시계 5개·계수기·행동 UI·결과 전표를 연결하세요.");
            for (int i = 0; i < clocks.Length; i++)
            {
                if (!clocks[i]) throw new InvalidOperationException("임시 시계 참조가 비었습니다.");
                clocks[i].Initialize(i, selectClock);
            }
            choose = action; restartGame = newGame;
            if (initialized) return;
            collectButton.onClick.AddListener(Collect);
            rationButton.onClick.AddListener(Ration);
            restart.Clicked += Restart;
            playerScale = playerLife.transform.localScale;
            aiScale = aiLife.transform.localScale;
            bankScale = bank.transform.localScale;
            initialized = true;
        }

        public void ResetTable(TimeTheftMatch match)
        {
            CancelPresentation();
            resultDisplay.SetActive(false);
            helpCard.SetActive(false);
            restart.SetInputEnabled(false);
            Numbers(match);
        }

        public void ShowBatch(TimeTheftMatch match)
        {
            ClearDeltas();
            ApplyClocks(match);
            batchLabel.text = "B " + match.Batch.ToString("00");
            phaseLabel.text = "새 묶음 " + match.Batch + " · 숫자와 종류를 기억하세요";
            Controls(false);
            Inspectable(true);
        }

        public void BeginShuffle(TimeTheftMatch match)
        {
            Controls(false);
            Inspectable(false);
            focusCamera.ReturnToOverview();
            phaseLabel.text = "덮개를 닫고 섞는 중";
        }

        public void CloseFrame(float t) { foreach (var clock in clocks) clock.CloseFrame(t); }
        public void ShuffleFrame(float t) { foreach (var clock in clocks) clock.ShuffleFrame(t); }

        public void OpenTurn(TimeTheftMatch match)
        {
            ClearDeltas();
            ApplyClocks(match);
            bool player = match.ActiveParticipant == Participant.Player;
            phaseLabel.text = (player ? "내 차례 · 닫힌 시계를 선택하세요" : "상대 차례 · 시계를 고르는 중")
                + "\n묶음 " + match.Batch + " · 사용 " + match.UsedCount + "/4";
            Controls(false);
            Inspectable(true);
            for (int i = 0; i < clocks.Length; i++)
                clocks[i].SetSelectable(match.CanSelectClock(Participant.Player, i));
        }

        public void OpenAction(TimeTheftMatch match)
        {
            Controls(match.ActiveParticipant == Participant.Player);
            Inspectable(true);
            phaseLabel.text = ActorName(match.ActiveParticipant) + " · 시계 선택\n"
                + (match.ActiveParticipant == Participant.Player ? "징수 또는 배급을 결정하세요" : "상대가 행동을 고르는 중");
            ApplyClocks(match);
            clocks[match.CurrentClock].SetSelected(true);
        }

        public void Reveal(TimeTheftMatch match)
        {
            Controls(false);
            Inspectable(false);
            focusCamera.ReturnToOverview();
            ApplyClocks(match);
            clocks[match.CurrentClock].SetSelected(true);
            ClockFace face = match.GetClock(match.CurrentClock).Face.Value;
            phaseLabel.text = ActorName(match.ActiveParticipant) + " · " + ActionName(match.Action) + "\n"
                + (face.Kind == ClockKind.Red ? "빨강" : "일반") + " 시계 " + face.Hours + "h";
        }

        public void BeginTransfer(TimeTheftMatch match)
        {
            toPlayer = match.PlayerLifeMinutes; toAi = match.AiLifeMinutes; toBank = match.BankMinutes;
            IsCountingTime = true;
            ShowDelta(playerDelta, toPlayer - fromPlayer, playerLife.color);
            ShowDelta(aiDelta, toAi - fromAi, aiLife.color);
            ShowDelta(bankDelta, toBank - fromBank, bank.color);
            phaseLabel.text += "\n" + (match.Action == TimeAction.Ration ? "Bank → " + ActorName(match.TransferParticipant)
                : ActorName(match.TransferParticipant) + " → Bank") + " · 실제 " + GameTime.Format(match.TransferredMinutes);
        }

        public void NumbersFrame(float t)
        {
            if (!IsCountingTime) return;
            t = Mathf.Clamp01(t);
            // 보간은 표시용이다. 완료 시 모델의 정확한 분 정수를 그대로 적용한다.
            int player = t >= 1 ? toPlayer : fromPlayer + (int)Math.Round((toPlayer - (double)fromPlayer) * t);
            int ai = t >= 1 ? toAi : fromAi + (int)Math.Round((toAi - (double)fromAi) * t);
            int balance = fromBank + fromPlayer - player + fromAi - ai;
            SetNumbers(player, ai, t >= 1 ? toBank : balance);
            float pulse = Mathf.Sin(t * Mathf.PI) * .065f;
            playerLife.transform.localScale = playerScale * (1 + (fromPlayer != toPlayer ? pulse : 0));
            aiLife.transform.localScale = aiScale * (1 + (fromAi != toAi ? pulse : 0));
            bank.transform.localScale = bankScale * (1 + (fromBank != toBank ? pulse : 0));
            if (t < 1) return;
            IsCountingTime = false;
            fromPlayer = toPlayer; fromAi = toAi; fromBank = toBank;
        }

        public void DiscardLast(TimeTheftMatch match)
        {
            ClearDeltas();
            ApplyClocks(match);
            Controls(false);
            phaseLabel.text = "4개 사용 완료\n마지막 시계는 열지 않고 폐기합니다";
        }

        public void Result(TimeTheftMatch match)
        {
            Numbers(match);
            resultDisplay.SetActive(true);
            restart.SetInputEnabled(true);
            Inspectable(true);
            Controls(false);
            resultTitle.text = match.Winner == GameWinner.Player ? "당신이 살아남았습니다" : "당신의 시간이 다했습니다";
            resultDetail.text = "Time Bank 잔액 " + GameTime.Format(match.BankMinutes);
            phaseLabel.text = resultTitle.text + "\nR 또는 전표의 다시하기";
        }

        public void Numbers(TimeTheftMatch match)
        {
            fromPlayer = toPlayer = match.PlayerLifeMinutes;
            fromAi = toAi = match.AiLifeMinutes;
            fromBank = toBank = match.BankMinutes;
            SetNumbers(fromPlayer, fromAi, fromBank);
            IsCountingTime = false;
            RestoreNumberScale();
        }

        private void SetNumbers(int player, int ai, int balance)
        {
            if (!playerLife || !aiLife || !bank) return;
            playerLife.text = GameTime.Format(player);
            aiLife.text = GameTime.Format(ai);
            bank.text = GameTime.Format(balance);
        }

        private void ApplyClocks(TimeTheftMatch match)
        {
            for (int i = 0; i < clocks.Length; i++) { clocks[i].ResetPose(); clocks[i].Show(match.GetClock(i)); }
        }

        private void Controls(bool acting)
        {
            SetButton(collectButton, acting);
            SetButton(rationButton, acting);
        }

        private static void SetButton(UnityEngine.UI.Button button, bool value)
        {
            if (!button) return;
            button.interactable = value;
            button.gameObject.SetActive(value);
        }

        private static void ShowDelta(TextMeshPro label, int delta, Color color)
        {
            label.text = (delta < 0 ? "−" : "+") + GameTime.Format(Math.Abs(delta));
            label.color = color;
            label.gameObject.SetActive(delta != 0);
        }

        private void ClearDeltas()
        {
            foreach (var label in new[] { playerDelta, aiDelta, bankDelta })
                if (label) { label.text = ""; label.gameObject.SetActive(false); }
        }

        private void Inspectable(bool value) { foreach (var target in inspectionTargets) if (target) target.SetInputEnabled(value); }
        private void Collect() => Submit(TimeAction.Collect);
        private void Ration() => Submit(TimeAction.Ration);
        private void Submit(TimeAction action)
        {
            if (!collectButton.interactable || !rationButton.interactable) return;
            Controls(false);
            focusCamera.SuppressPointerForCurrentFrame();
            choose?.Invoke(action);
        }
        private void Restart() => restartGame?.Invoke();
        public void ToggleHelp() { helpCard.SetActive(!helpCard.activeSelf); if (helpCard.activeSelf) focusCamera.ReturnToOverview(); }
        private static string ActorName(Participant participant) => participant == Participant.Player ? "나" : "상대";
        private static string ActionName(TimeAction action) => action == TimeAction.Collect ? "징수" : "배급";
        private void RestoreNumberScale()
        {
            if (playerLife) playerLife.transform.localScale = playerScale;
            if (aiLife) aiLife.transform.localScale = aiScale;
            if (bank) bank.transform.localScale = bankScale;
        }
        public void CancelPresentation()
        {
            if (!initialized) return;
            Controls(false);
            Inspectable(false);
            IsCountingTime = false;
            ClearDeltas();
            RestoreNumberScale();
            if (focusCamera) focusCamera.ReturnToOverview();
            foreach (var clock in clocks)
                if (clock) { clock.SetSelectable(false); clock.ResetPose(); }
        }
        private void OnDestroy()
        {
            if (!initialized) return;
            if (collectButton) collectButton.onClick.RemoveListener(Collect);
            if (rationButton) rationButton.onClick.RemoveListener(Ration);
            if (restart) restart.Clicked -= Restart;
        }
    }
}
