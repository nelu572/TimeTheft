using System;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView
    {
        [Header("테이블 위 아이템 · 표시와 마우스 입력")]
        [SerializeField] private ItemWorldInventoryView worldItems;
        private UnityEngine.UI.Button useItemButton, endItemsButton, keepClockButton, cancelItemButton;
        private TextMeshProUGUI itemDetailLabel;
        private Action useSelectedItem, endItems, keepClock;
        private Action<int> selectItem;
        private double lastItemClick = double.NegativeInfinity;
        private const double ItemConfirmQuietSeconds = .32;
        private TimeTheftMatch itemMatch;
        private TabletopFocusTarget lastItemHoverTarget;
        private ItemWorldView hoveredItem;
        private bool itemPresentation;
        private int? screwdriverPercent, bypassPercent;

        public ItemWorldInventoryView WorldItems => worldItems;
        public UnityEngine.UI.Button UseItemButton => useItemButton;
        public UnityEngine.UI.Button EndItemsButton => endItemsButton;
        public UnityEngine.UI.Button KeepClockButton => keepClockButton;
        public UnityEngine.UI.Button CancelItemButton => cancelItemButton;
        public TextMeshProUGUI ItemDetailLabel => itemDetailLabel;

        public void InitializeItemControls(Action<int> select, Action use, Action end, Action keep)
        {
            if (!worldItems) throw new InvalidOperationException("월드 아이템 표시부를 연결하세요.");
            selectItem = select;
            worldItems.Initialize(ClickWorldItem);
            worldItems.BindModels(this);
            useSelectedItem = use;
            endItems = end;
            keepClock = keep;
            if (itemDetailLabel) return;
            itemDetailLabel = CreateItemText("Item Description", new Vector2(780, 110), new Vector2(0, 120));
            itemDetailLabel.alignment = TextAlignmentOptions.Bottom;
            itemDetailLabel.fontSize = 21;
            useItemButton = CreateItemButton("Use Item", "사용", -135, UseSelectedItem);
            endItemsButton = CreateItemButton("End Items", "시계 선택", 135, EndItems);
            cancelItemButton = CreateItemButton("Cancel Item Selection", "선택 취소", -270, CancelItemSelection);
            HideItemButtons();
            InitializeContextUi();
        }

        private void ClickWorldItem(int id)
        {
            if (itemPresentation || itemMatch == null || !itemMatch.CanUseItems(Participant.Player)) return;
            double now = Time.unscaledTimeAsDouble;
            if (SelectedItemId != id)
            {
                selectItem?.Invoke(id);
                lastItemClick = now;
                return;
            }
            double quiet = now - lastItemClick;
            // 빠른 두 번째/세 번째 클릭을 예약하지 않는다. 연타가 끝난 뒤 새 클릭이 필요하다.
            lastItemClick = now;
            if (quiet < ItemConfirmQuietSeconds) return;
            focusCamera.SuppressPointerForCurrentFrame();
            useSelectedItem?.Invoke();
        }

        public void ConfigureItemRules(GameRules rules)
        {
            screwdriverPercent = rules == null ? (int?)null : rules.ScrewdriverReductionPercent;
            bypassPercent = rules == null ? (int?)null : rules.TimeBypassPercent;
            PrepareItemFont();
        }

        private void PrepareItemFont()
        {
            var font = phaseLabel.font;
            if (!font || font.atlasPopulationMode == AtlasPopulationMode.Static) return;
            var text = new System.Text.StringBuilder("0123456789%#()+- · / → "
                + "사용 시계 선택 이 시계 유지 선택 취소 도움말 닫기 테이블로 연출 단축 확대 복귀 종료 전표 다시하기 상대 상대의 보유품 사용할 수 없습니다 "
                + "아이템 클릭으로 선택 사용 버튼으로 확정 아이템 사용을 마치려면 시계 선택 "
                + "클릭으로 선택 선택만으로 소모되지 않습니다 자기 차례 아이템 단계에서 사용 가능 "
                + "[공통 기반 테스트] 내 차례 아이템 단계 개 아이템을 클릭해 선택하거나 시계 선택으로 진행하세요");
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
                text.Append(ItemWorldView.DisplayName(kind)).Append(DescribeItem(kind));
            text.Append(ItemWorldView.DisplayName(null)).Append(DescribeItem(null));
            foreach (ItemUseFailure failure in Enum.GetValues(typeof(ItemUseFailure)))
                text.Append(ItemFailureText(failure));
            // Hover 중 새 글리프를 추가하지 않도록 시작 때 준비하고 GPU에도 한 번 반영한다.
            font.TryAddCharacters(text.ToString());
            foreach (var atlas in font.atlasTextures)
                if (atlas && atlas.isReadable) atlas.Apply(false, false);
        }

        public void SelectItem(TimeTheftMatch match, int id)
        {
            if (itemPresentation || !match.CanUseItems(Participant.Player)) return;
            foreach (var item in match.GetInventory(Participant.Player))
            {
                if (item.Id != id) continue;
                itemMatch = match;
                SelectedItemId = id;
                lastItemClick = Time.unscaledTimeAsDouble;
                SelectedTestItemId = item.IsFoundationTest ? id : (int?)null;
                RefreshWorldItems();
                RefreshItemControls();
                return;
            }
        }

        public void CycleItem(TimeTheftMatch match)
        {
            if (itemPresentation || !match.CanUseItems(Participant.Player)) return;
            var inventory = match.GetInventory(Participant.Player);
            if (inventory.Count == 0) return;
            int next = 0;
            for (int i = 0; i < inventory.Count; i++)
                if (inventory[i].Id == SelectedItemId) { next = (i + 1) % inventory.Count; break; }
            SelectItem(match, inventory[next].Id);
        }

        private void SetItemContext(TimeTheftMatch match, bool presenting, bool clearSelection)
        {
            itemMatch = match;
            itemPresentation = presenting;
            if (clearSelection) { SelectedItemId = SelectedTestItemId = null; lastItemClick = double.NegativeInfinity; }
            // 연출/AI 단계는 입력만 막는다. 보유품 전체를 비활성화하면 매 턴 사라졌다 다시 나타난다.
            if (worldItems) worldItems.SetVisible(true);
            RefreshWorldItems();
            RefreshItemControls();
        }

        private void ValidateItemSelection()
        {
            if (!SelectedItemId.HasValue) return;
            if (itemPresentation || itemMatch == null || !itemMatch.CanUseItems(Participant.Player))
            {
                SelectedItemId = SelectedTestItemId = null;
                return;
            }
            foreach (var item in itemMatch.GetInventory(Participant.Player))
                if (item.Id == SelectedItemId) return;
            SelectedItemId = SelectedTestItemId = null;
        }

        private bool CanHoverItems => !itemPresentation && itemMatch != null &&
            itemMatch.ActiveParticipant == Participant.Player && itemMatch.Stage == TurnStage.AwaitingItems &&
            !IsHelpVisible && (!inputController || !inputController.IsPresentingClock && !inputController.IsPresentingItem) &&
            (!worldItems.ModelUses || !worldItems.ModelUses.IsAcquiring);

        private void RefreshWorldItems()
        {
            if (!worldItems || itemMatch == null) return;
            worldItems.Refresh(itemMatch.GetInventory(Participant.Player),
                itemMatch.ObserveInventory(Participant.Player, Participant.Ai), SelectedItemId, CanHoverItems);
        }

        private void Update()
        {
            RefreshMouseControls();
            if (!initialized || !itemDetailLabel || !focusCamera) return;
            var target = CanHoverItems ? focusCamera.HoveredTarget : null;
            if (!target && PointerOverItemCard()) return;
            if (lastItemHoverTarget == target) return;
            lastItemHoverTarget = target;
            hoveredItem = target ? target.GetComponent<ItemWorldView>() : null;
            if (hoveredItem && !hoveredItem.IsRemoving && SelectedItemId.HasValue &&
                hoveredItem.ItemId != SelectedItemId && itemMatch.CanUseItems(Participant.Player))
            {
                // 다른 물건을 살펴보면 미확정 선택을 끝낸다. Hover만으로 새 선택/사용을 예약하지 않는다.
                SelectedItemId = SelectedTestItemId = null;
                lastItemClick = double.NegativeInfinity;
                RefreshWorldItems();
                RefreshItemControls();
            }
            RefreshItemDetails();
        }

        private void RefreshItemControls()
        {
            if (!useItemButton) return;
            bool items = !itemPresentation && itemMatch != null && itemMatch.CanUseItems(Participant.Player);
            SetButton(useItemButton, false);
            SetButton(endItemsButton, false);
            SetButton(cancelItemButton, false);
            RefreshItemDetails();
        }

        private void RefreshItemDetails()
        {
            if (!itemDetailLabel) return;
            if (itemPresentation || itemMatch == null || itemMatch.Stage == TurnStage.GameOver)
            {
                itemDetailLabel.text = "";
                return;
            }
            ItemState? selected = null;
            if (SelectedItemId.HasValue)
                foreach (var item in itemMatch.GetInventory(Participant.Player))
                    if (item.Id == SelectedItemId) { selected = item; break; }
            bool hover = CanHoverItems && hoveredItem && !hoveredItem.IsRemoving && hoveredItem.gameObject.activeInHierarchy;
            if (hover)
            {
                itemDetailLabel.text = (hoveredItem.Owner == Participant.Ai ? "상대 · " : "")
                    + ItemWorldView.DisplayName(hoveredItem.Kind) + " · " + DescribeItem(hoveredItem.Kind);
                if (hoveredItem.Owner == Participant.Ai) itemDetailLabel.text += "\n상대의 보유품 · 사용할 수 없습니다";
            }
            else if (selected.HasValue)
                itemDetailLabel.text = ItemName(selected.Value.Kind) + " · " + DescribeItem(selected.Value.Kind);
            else itemDetailLabel.text = "";

            if (selected.HasValue)
            {
                var item = selected.Value;
                string failure = ItemFailureText(itemMatch.CheckItemUse(Participant.Player, item.Id));
                if (!string.IsNullOrEmpty(failure)) itemDetailLabel.text += "\n" + failure;
            }
            else if (hover && hoveredItem.Owner == Participant.Player)
                itemDetailLabel.text += itemMatch.CanUseItems(Participant.Player) ? "\n클릭으로 선택 · 같은 아이템을 다시 클릭해 사용" : "\n자기 차례 아이템 단계에서 사용 가능";
        }

        private string DescribeItem(ItemKind? kind)
        {
            if (!kind.HasValue) return "종류 비공개";
            switch (kind.Value)
            {
                case ItemKind.SpareKey: return "닫힌 시계 1개의 시간·종류를\n나만 확인합니다.\n확인 후 같은 시계 또는\n다른 시계를 선택합니다.";
                case ItemKind.Screwdriver: return "이번 차례 <nobr><color=#C88F7D>빨간 시계 징수</color></nobr>에서\n내 시간 손실을 "
                    + (screwdriverPercent.HasValue ? "<nobr><color=#E4C889>" + screwdriverPercent.Value + "% 경감</color></nobr>합니다." : "줄입니다.")
                    + "\n일반 징수·배급에는\n효과가 없습니다.";
                case ItemKind.TimeBypass: return "이번 차례 징수 손실의 "
                    + (bypassPercent.HasValue ? "<nobr><color=#E4C889>" + bypassPercent.Value + "%</color></nobr>" : "일부")
                    + "를\n손실을 입은 쪽의 상대에게\n지급합니다.\n빨간 시계라면\n상대가 받습니다.";
                case ItemKind.Scissors: return "상대의 미사용 아이템 1개를\n무작위로 제거합니다.\n대상이 없으면\n소모되지 않습니다.";
                case ItemKind.RubberDuck: return "상대의 다음 행동 차례에\n아이템 사용을 막습니다.\n시계 선택과 징수·배급은\n가능합니다.";
                default: return "상대의 다음 차례를\n건너뜁니다.\n그 차례에는\n시계를 사용하지 않습니다.";
            }
        }

        private static string ItemFailureText(ItemUseFailure failure)
        {
            switch (failure)
            {
                case ItemUseFailure.None: return "";
                case ItemUseFailure.AlreadyActive: return "이미 효과가 준비되어 있습니다.";
                case ItemUseFailure.NoTarget: return "제거할 상대 아이템이 없습니다.";
                case ItemUseFailure.UseNotAllowed: return "아이템 사용이 봉쇄되어 있습니다.";
                default: return "지금은 사용할 수 없습니다.";
            }
        }

        private void HideItemButtons()
        {
            foreach (var button in new[] { useItemButton, endItemsButton, keepClockButton, cancelItemButton })
                if (button) { button.interactable = false; button.gameObject.SetActive(false); }
        }

        private void UseSelectedItem()
        {
            if (!useItemButton.interactable) return;
            focusCamera.SuppressPointerForCurrentFrame();
            HideItemButtons();
            useSelectedItem?.Invoke();
        }

        private void EndItems()
        {
            if (!endItemsButton.interactable) return;
            focusCamera.SuppressPointerForCurrentFrame();
            HideItemButtons();
            endItems?.Invoke();
        }

        private void KeepClock()
        {
            if (!keepClockButton || !keepClockButton.interactable) return;
            focusCamera.SuppressPointerForCurrentFrame();
            HideItemButtons();
            keepClock?.Invoke();
        }

        public void CancelItemSelection()
        {
            if (itemPresentation || itemMatch == null || !itemMatch.CanUseItems(Participant.Player) ||
                !SelectedItemId.HasValue) return;
            focusCamera.SuppressPointerForCurrentFrame();
            SelectedItemId = SelectedTestItemId = null;
            lastItemClick = double.NegativeInfinity;
            RefreshWorldItems();
            RefreshItemControls();
        }

        private TextMeshProUGUI CreateItemText(string name, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(phaseLabel.canvas.transform, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = phaseLabel.font;
            text.fontSharedMaterial = phaseLabel.fontSharedMaterial;
            text.color = phaseLabel.color;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(.5f, 0);
            text.rectTransform.pivot = new Vector2(.5f, 0);
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = size;
            return text;
        }

        private UnityEngine.UI.Button CreateItemButton(string name, string title, float x, UnityEngine.Events.UnityAction click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(phaseLabel.canvas.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(x, 70);
            rect.sizeDelta = new Vector2(240, 56);
            var image = go.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(.12f, .15f, .15f, .98f);
            var button = go.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(.75f, .88f, .85f);
            colors.pressedColor = new Color(.45f, .62f, .6f);
            colors.disabledColor = new Color(.4f, .4f, .4f);
            button.colors = colors;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            button.onClick.AddListener(click);
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(go.transform, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = phaseLabel.font;
            label.fontSharedMaterial = phaseLabel.fontSharedMaterial;
            label.text = title;
            label.fontSize = 23;
            label.color = phaseLabel.color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private void CancelItemControls()
        {
            HideClockContext();
            itemMatch = null;
            itemPresentation = true;
            lastItemClick = double.NegativeInfinity;
            lastItemHoverTarget = null;
            hoveredItem = null;
            HideItemButtons();
            if (itemDetailLabel) itemDetailLabel.text = "";
            if (worldItems) { worldItems.ResetItems(); worldItems.SetVisible(false); }
            if (itemGroup) { itemGroup.alpha = 0; itemGroup.blocksRaycasts = false; }
            if (actionGroup) { actionGroup.alpha = 0; actionGroup.blocksRaycasts = false; }
            if (noticeGroup) noticeGroup.alpha = 0;
        }

        private void DestroyItemControls()
        {
            if (useItemButton) { useItemButton.onClick.RemoveListener(UseSelectedItem); Destroy(useItemButton.gameObject); }
            if (endItemsButton) { endItemsButton.onClick.RemoveListener(EndItems); Destroy(endItemsButton.gameObject); }
            if (keepClockButton) { keepClockButton.onClick.RemoveListener(KeepClock); Destroy(keepClockButton.gameObject); }
            if (cancelItemButton) { cancelItemButton.onClick.RemoveListener(CancelItemSelection); Destroy(cancelItemButton.gameObject); }
            if (itemDetailLabel) Destroy(itemDetailLabel.gameObject);
        }
    }
}
