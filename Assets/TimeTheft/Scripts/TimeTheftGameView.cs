using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView : MonoBehaviour
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
        private TextMeshProUGUI inventoryLabel;
        private string inventoryNotice = "";
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
        public int? SelectedTestItemId { get; private set; }
        public int? SelectedItemId { get; private set; }
        public ItemUseResult? LastItemUse { get; private set; }
        public TextMeshProUGUI InventoryLabel => inventoryLabel;

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
            CreateInventoryLabel();
        }

        public void ResetTable(TimeTheftMatch match)
        {
            CancelPresentation();
            if (worldItems) worldItems.SetVisible(true);
            SetItemContext(match, false, true);
            resultDisplay.SetActive(false);
            helpCard.SetActive(false);
            restart.SetInputEnabled(false);
            Numbers(match);
            RefreshInventory(match);
        }

        public void ShowBatch(TimeTheftMatch match)
        {
            SetItemContext(match, false, true);
            ClearDeltas();
            ApplyClocks(match);
            batchLabel.text = "B " + match.Batch.ToString("00");
            phaseLabel.text = "새 묶음 " + match.Batch + " · 숫자와 종류를 기억하세요";
            inventoryNotice = "지급";
            foreach (var grant in match.LastItemGrants)
                inventoryNotice += " · " + ActorName(grant.Owner) + " +" + grant.GrantedItems.Count
                    + (grant.SkippedCount > 0 ? " (보유 상한)" : "");
            RefreshInventory(match);
            Controls(false);
            Inspectable(true);
        }

        public void BeginShuffle(TimeTheftMatch match)
        {
            SetItemContext(match, true, true);
            Controls(false);
            Inspectable(false);
            focusCamera.ReturnToOverview();
            phaseLabel.text = "덮개를 닫고 섞는 중";
        }

        public void CloseFrame(float t) { foreach (var clock in clocks) clock.CloseFrame(t); }
        public void ShuffleFrame(float t) { foreach (var clock in clocks) clock.ShuffleFrame(t); }

        public void OpenTurn(TimeTheftMatch match)
        {
            SetItemContext(match, false, true);
            SelectedTestItemId = null;
            SelectedItemId = null;
            ClearDeltas();
            ApplyClocks(match);
            bool player = match.ActiveParticipant == Participant.Player;
            phaseLabel.text = (player ? "내 차례 · 닫힌 시계를 선택하세요" : "상대 차례 · 시계를 고르는 중")
                + "\n묶음 " + match.Batch + " · 사용 " + match.UsedCount + "/4";
            ShowItemReadiness(match);
            Controls(false);
            Inspectable(true);
            for (int i = 0; i < clocks.Length; i++)
                clocks[i].SetSelectable(match.CanSelectClock(Participant.Player, i));
            RefreshInventory(match);
        }

        public void ShowItemPhase(TimeTheftMatch match, int selectedIndex)
        {
            SetItemContext(match, false, true);
            ClearDeltas();
            ApplyClocks(match);
            Controls(false);
            Inspectable(true);
            if (match.ActiveParticipant != Participant.Player)
            {
                phaseLabel.text = "상대 차례 · 아이템 단계";
                RefreshInventory(match);
                return;
            }
            var inventory = match.GetInventory(Participant.Player);
            phaseLabel.text = "내 차례 · 아이템 단계 (" + inventory.Count + "개)\n아이템을 클릭해 선택하거나 시계 선택으로 진행하세요";
            ShowItemReadiness(match);
            RefreshInventory(match);
        }

        public void ShowItemUse(ItemUseResult result)
        {
            itemPresentation = true;
            if (worldItems) worldItems.ShowUse(result);
            inventoryNotice = ActorName(result.Actor) + " · " + ItemName(result.Effect.Value.Source.Kind) + " 소모";
            if (result.RemovedItem.HasValue)
                inventoryNotice += " / " + ActorName(result.RemovedItem.Value.Owner) + " " + ItemName(result.RemovedItem.Value.Kind) + " 제거";
            LastItemUse = result;
            SelectedTestItemId = null;
            SelectedItemId = null;
            RefreshItemControls();
            Controls(false);
            Inspectable(false);
            foreach (var clock in clocks) clock.SetSelectable(false);
            phaseLabel.text = result.Effect.Value.Source.IsFoundationTest
                ? "[공통 기반 테스트] #" + result.ItemId + " 소모\n실제 아이템 효과는 적용하지 않습니다"
                : ActorName(result.Actor) + " · " + ItemName(result.Effect.Value.Source.Kind) + " 사용\n소모 완료 · "
                    + ItemUseMessage(result);
        }

        public void ShowItemFailure(ItemUseResult result)
        {
            inventoryNotice = "사용 실패 · 소모 없음";
            phaseLabel.text += "\n사용 거부 · " + (result.Failure == ItemUseFailure.NoTarget ? "제거 대상 없음 · 소모 안 됨" : "현재 사용 불가 · 소모 안 됨");
            RefreshItemControls();
        }

        public void OpenKeyDecision(TimeTheftMatch match)
        {
            SetItemContext(match, false, true);
            Controls(false);
            Inspectable(true);
            ApplyClocks(match);
            phaseLabel.text = match.ActiveParticipant == Participant.Player
                ? "여벌 열쇠 · 나만 확인\n이 시계 유지 / 다른 미사용 시계 클릭으로 1회 재선택"
                : "상대 · 여벌 열쇠 확인 중";
            ShowPlayerClockKnowledge(match);
            for (int i = 0; i < clocks.Length; i++)
                clocks[i].SetSelectable(match.ActiveParticipant == Participant.Player && i != match.CurrentClock
                    && match.GetClock(i).Visibility == ClockVisibility.Hidden);
            clocks[match.CurrentClock].SetSelected(true);
            RefreshInventory(match);
        }

        public void OpenAction(TimeTheftMatch match)
        {
            SetItemContext(match, false, true);
            Controls(match.ActiveParticipant == Participant.Player);
            Inspectable(true);
            phaseLabel.text = ActorName(match.ActiveParticipant) + " · 시계 선택\n"
                + (match.ActiveParticipant == Participant.Player ? "징수 또는 배급을 결정하세요" : "상대가 행동을 고르는 중");
            ShowItemReadiness(match);
            ShowPlayerClockKnowledge(match);
            ApplyClocks(match);
            clocks[match.CurrentClock].SetSelected(true);
            RefreshInventory(match);
        }

        public void Reveal(TimeTheftMatch match)
        {
            SetItemContext(match, true, true);
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
            var transfers = match.LastTransfers;
            string paths = "";
            for (int i = 0; i < transfers.Count; i++)
            {
                var transfer = transfers[i];
                if (i > 0) paths += " / ";
                paths += AccountName(transfer.From) + " → " + AccountName(transfer.To);
                if (transfers.Count > 1) paths += " " + GameTime.Format(transfer.Minutes);
            }
            phaseLabel.text += "\n" + paths + " · 실제 " + GameTime.Format(match.TransferredMinutes);
            foreach (var result in match.LastItemEffects)
            {
                if (result.Source.Kind == ItemKind.TimeBypass)
                    phaseLabel.text += result.Triggered
                        ? "\n우회선 · " + ActorName(result.Recipient.Value) + "에게 " + GameTime.Format(result.BypassedMinutes)
                            + " / Bank " + GameTime.Format(result.BankReceivedMinutes)
                        : "\n우회선 · 미발동 (소모됨)";
                else if (result.Source.Kind == ItemKind.Screwdriver)
                    phaseLabel.text += result.Triggered
                        ? "\n드라이버 · 경감 " + GameTime.Format(result.PreventedMinutes) + " / 실제 손실 " + GameTime.Format(match.TransferredMinutes)
                        : "\n드라이버 · 미발동 (소모됨)";
                else if (result.Source.Kind == ItemKind.SpareKey) phaseLabel.text += "\n여벌 열쇠 · 확인 효과 종료 (소모됨)";
            }
            ShowTurnItemEvents(match);
        }

        private void ShowItemReadiness(TimeTheftMatch match)
        {
            if (match.HasScrewdriverProtection) phaseLabel.text += "\n드라이버 대기 중 · 빨간 징수에만 적용";
            if (match.HasTimeBypass) phaseLabel.text += "\n우회선 대기 중 · 징수에만 적용";
            if (match.HasSpareKeyReadiness) phaseLabel.text += "\n여벌 열쇠 대기 중 · 첫 시계 확인 / 재선택 1회";
            foreach (var effect in match.GetPendingItemEffects(match.ActiveParticipant))
            {
                if (effect.Source.IsFoundationTest) continue;
                if (effect.Source.Kind == ItemKind.RubberDuck) phaseLabel.text += "\n고무 오리 대기 · 상대의 다음 실제 행동에서 아이템 봉쇄";
                if (effect.Source.Kind == ItemKind.Handcuffs) phaseLabel.text += "\n수갑 대기 · 상대의 다음 차례 생략";
            }
            ShowTurnItemEvents(match);
        }

        private void ShowPlayerClockKnowledge(TimeTheftMatch match)
        {
            if (match.ActiveParticipant != Participant.Player) return;
            var face = match.GetPrivateClock(Participant.Player, match.CurrentClock);
            if (face.HasValue) phaseLabel.text += "\n나만 확인 · " + (face.Value.Kind == ClockKind.Red ? "빨강" : "일반") + " " + face.Value.Hours + "h";
        }

        private void ShowTurnItemEvents(TimeTheftMatch match)
        {
            foreach (var itemEvent in match.TurnItemEvents)
                phaseLabel.text += itemEvent.Kind == ItemTurnEventKind.TurnSkipped
                    ? "\n수갑 · " + ActorName(itemEvent.Target) + " 차례 생략 · 시계 미사용 / 해제"
                    : itemEvent.Kind == ItemTurnEventKind.ItemsBlocked
                        ? "\n고무 오리 · " + ActorName(itemEvent.Target) + " 아이템 사용 봉쇄"
                        : itemEvent.Kind == ItemTurnEventKind.RestrictionEnded ? "\n고무 오리 · 아이템 봉쇄 종료"
                        : "\n" + ItemName(itemEvent.Source.Kind) + " · 대기 종료 (게임 종료)";
        }

        private static string ItemName(ItemKind kind) => ItemWorldView.DisplayName(kind);

        private static string ItemUseMessage(ItemUseResult result)
        {
            if (result.RemovedItem.HasValue)
                return ActorName(result.RemovedItem.Value.Owner) + " " + ItemName(result.RemovedItem.Value.Kind) + " 제거 완료 · 효과 종료";
            switch (result.Effect.Value.Source.Kind)
            {
                case ItemKind.SpareKey: return "시계 선택에서 비공개 확인 대기";
                case ItemKind.RubberDuck: return "상대의 다음 실제 차례 아이템 봉쇄 대기";
                case ItemKind.Handcuffs: return "상대의 다음 차례 생략 대기";
                case ItemKind.TimeBypass: return "이번 차례 징수 시간을 분할합니다";
                default: return "이번 차례 빨간 징수에 대비합니다";
            }
        }
        private static string AccountName(TimeAccount account) => account == TimeAccount.Bank ? "Bank"
            : ActorName(account == TimeAccount.Player ? Participant.Player : Participant.Ai);

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
            SetItemContext(match, true, true);
            ClearDeltas();
            ApplyClocks(match);
            Controls(false);
            phaseLabel.text = "4개 사용 완료\n마지막 시계는 열지 않고 폐기합니다";
        }

        public void Result(TimeTheftMatch match)
        {
            SetItemContext(match, true, true);
            RefreshInventory(match);
            if (worldItems) worldItems.SetVisible(false);
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
            HideItemButtons();
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
        private void CreateInventoryLabel()
        {
            var labelObject = new GameObject("Item Inventory (temporary)", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(phaseLabel.canvas.transform, false);
            inventoryLabel = labelObject.GetComponent<TextMeshProUGUI>();
            inventoryLabel.font = phaseLabel.font;
            inventoryLabel.fontSharedMaterial = phaseLabel.fontSharedMaterial;
            inventoryLabel.fontSize = 20;
            inventoryLabel.color = phaseLabel.color;
            inventoryLabel.alignment = TextAlignmentOptions.BottomLeft;
            inventoryLabel.raycastTarget = false;
            var rect = inventoryLabel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(20, 20);
            rect.sizeDelta = new Vector2(440, 80);
        }

        public void RefreshInventory(TimeTheftMatch match)
        {
            itemMatch = match;
            if (inventoryLabel) inventoryLabel.text = inventoryNotice;
            ValidateItemSelection();
            RefreshWorldItems();
            RefreshItemControls();
        }
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
            SelectedTestItemId = null;
            LastItemUse = null;
            inventoryNotice = "";
            if (inventoryLabel) inventoryLabel.text = "";
            SelectedItemId = null;
            CancelItemControls();
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
            if (inventoryLabel) Destroy(inventoryLabel.gameObject);
            DestroyItemControls();
        }
    }
}
