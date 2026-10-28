using TMPro;
using UnityEngine;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView
    {
        private RectTransform clockHoverCard;
        private CanvasGroup clockHoverGroup;
        private TextMeshProUGUI clockHoverTitle, clockHoverHint;
        private bool clockHoverVisible;
        public TMP_Text ClockHoverHint => clockHoverHint;

        private void InitializeClockHover()
        {
            clockHoverCard = Panel("Clock Hover", new Vector2(280, 88), out clockHoverGroup);
            var frame = clockHoverCard.GetComponent<UiIndexGraphic>();
            frame.Layout = UiIndexGraphic.FrameLayout.Compact;
            frame.raycastTarget = false;
            frame.Accent = UiBrass;
            clockHoverTitle = Label(clockHoverCard, "Name", 28, UiInk, new Vector2(24, -13), new Vector2(232, 36));
            clockHoverTitle.fontStyle = FontStyles.Bold;
            clockHoverHint = Label(clockHoverCard, "Gesture", 19, UiBrass, new Vector2(24, -51), new Vector2(232, 24));
        }
        private void RefreshClockHover()
        {
            if (!clockHoverCard || itemMatch == null) return;
            bool busy = itemPresentation || IsHelpVisible || IsResultVisible || focusCamera.IsPresentationCameraOwned ||
                inputController && (inputController.IsPresentingClock || inputController.IsPresentingItem) ||
                worldItems && worldItems.ModelUses && worldItems.ModelUses.IsAcquiring;
            bool choosing = itemMatch.Stage == TurnStage.AwaitingItems || itemMatch.Stage == TurnStage.AwaitingClock ||
                itemMatch.Stage == TurnStage.AwaitingKeyDecision;
            bool visible = false;
            if (!busy && choosing && !SelectedItemId.HasValue && itemMatch.ActiveParticipant == Participant.Player)
                for (int i = 0; i < clocks.Length; i++)
                {
                    var clock = clocks[i];
                    if (!clock || !clock.SelectionTarget.InputEnabled || focusCamera.HoveredTarget != clock.SelectionTarget ||
                        itemMatch.GetClock(i).Visibility != ClockVisibility.Hidden) continue;
                    clockHoverTitle.text = "시계 선택";
                    clockHoverHint.text = itemMatch.Stage == TurnStage.AwaitingKeyDecision
                        ? (i == itemMatch.CurrentClock ? "클릭하여 이 시계 유지" : "클릭하여 다시 선택") : "클릭하여 선택";
                    Vector2 min, max;
                    if (ClockScreenBounds(i, out min, out max))
                        visible = PlaceClockCard(clockHoverCard, new Vector2((min.x + max.x) * .5f, min.y), false);
                    break;
                }
            Fade(clockHoverGroup, visible, ref clockHoverVisible, false);
            clockHoverGroup.blocksRaycasts = false;
        }
    }
}
