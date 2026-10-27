using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView
    {
        private readonly TextMeshProUGUI[] clockValues = new TextMeshProUGUI[TimeTheftMatch.ClocksPerBatch];
        private readonly RectTransform[] clockValueCards = new RectTransform[TimeTheftMatch.ClocksPerBatch];
        private readonly MeshFilter[] clockDials = new MeshFilter[TimeTheftMatch.ClocksPerBatch];
        private readonly MeshFilter[][] clockLids = new MeshFilter[TimeTheftMatch.ClocksPerBatch][];
        private readonly ClockTokenView[] dialOwners = new ClockTokenView[TimeTheftMatch.ClocksPerBatch];
        private RectTransform publicActionCard;
        private TextMeshProUGUI publicActionLabel, publicTransferLabel;
        private float publicActionUntil;
        private string committedActionCue = "";
        public IReadOnlyList<TextMeshProUGUI> ClockValueLabels => System.Array.AsReadOnly(clockValues);
        public TextMeshProUGUI PublicActionLabel => publicActionLabel;
        public TextMeshProUGUI PublicTransferLabel => publicTransferLabel;

        private void InitializeClockContext()
        {
            for (int i = 0; i < clocks.Length; i++)
            {
                CanvasGroup group;
                var card = Panel("Clock " + i + " Public Value", new Vector2(172, 64), out group);
                group.alpha = 1; group.blocksRaycasts = false;
                var frame = card.GetComponent<UiIndexGraphic>();
                frame.Layout = UiIndexGraphic.FrameLayout.Compact;
                frame.raycastTarget = false;
                clockValueCards[i] = card;
                clockValues[i] = Label(card, "Hours", 36, UiInk, new Vector2(22, -10), new Vector2(132, 44));
                clockValues[i].alignment = TextAlignmentOptions.Midline;
                clockValues[i].fontStyle = FontStyles.Bold;
                OutlineText(clockValues[i]);
                card.gameObject.SetActive(false);
                FindClockDial(i);
            }
            CanvasGroup cueGroup;
            publicActionCard = Panel("Public Turn Result", new Vector2(350, 76), out cueGroup);
            cueGroup.alpha = 1; cueGroup.blocksRaycasts = false;
            publicActionCard.GetComponent<UiIndexGraphic>().raycastTarget = false;
            publicActionCard.GetComponent<UiIndexGraphic>().enabled = false;
            publicActionLabel = Label(publicActionCard, "Committed Action", 27, UiBrass, new Vector2(20, -18), new Vector2(310, 38));
            publicTransferLabel = Label(publicActionCard, "Actual Transfers", 23, UiInk, new Vector2(20, -64), new Vector2(310, 88));
            OutlineText(publicActionLabel);
            publicTransferLabel.gameObject.SetActive(false);
            publicActionCard.gameObject.SetActive(false);
        }

        private void ShowCommittedAction(TimeTheftMatch match)
        {
            committedActionCue = match.ActiveParticipant == Participant.Ai ? "상대 · " + ActionName(match.Action) : "";
            publicActionUntil = Time.unscaledTime + 1.1f;
        }

        private void RefreshClockContext()
        {
            if (!publicActionCard) return;
            var motion = TurnPresentation;
            var life = Lifecycle;
            bool removing = life && life.IsRunning;
            bool closing = motion && (motion.Phase == H2TurnPresentation.TurnPhase.Closing ||
                motion.Phase == H2TurnPresentation.TurnPhase.Closed || motion.Phase == H2TurnPresentation.TurnPhase.Returning);
            bool preview = itemMatch.Stage == TurnStage.BatchPreview;
            int selected = itemMatch.CurrentClock;
            for (int i = 0; i < clocks.Length; i++)
            {
                if (dialOwners[i] != clocks[i]) FindClockDial(i);
                // public GetClock만 사용한다. 열쇠의 개인 지식/AI 내부 관찰을 화면에 전달하지 않는다.
                var clock = itemMatch.GetClock(i);
                var h2 = clocks[i].H2Presentation;
                bool open = h2 && h2.isActiveAndEnabled
                    ? Quaternion.Angle(Quaternion.identity, h2.Watch.LidPivot.localRotation) >= 85 : clocks[i].IsOpen;
                bool show = !IsHelpVisible && !IsResultVisible && !removing && !closing &&
                    (!motion || !motion.IsOpponentTurn || motion.IsResultPublic) && clock.Face.HasValue && open &&
                    (preview || i == selected);
                clockValues[i].text = show ? clock.Face.Value.Hours.ToString("00") + ":00" : "";
                if (show)
                {
                    clockValues[i].color = clock.Face.Value.Kind == ClockKind.Red ? new Color(.79f, .43f, .40f) : UiInk;
                    clockValueCards[i].GetComponent<UiIndexGraphic>().Accent = clockValues[i].color;
                    show = PlacePublicValue(i);
                }
                clockValueCards[i].gameObject.SetActive(show);
            }

            bool showAction = !IsHelpVisible && !IsResultVisible && !removing && !closing &&
                itemMatch.Stage == TurnStage.Revealing && Time.unscaledTime < publicActionUntil &&
                committedActionCue.Length > 0 && (!motion || !motion.IsOnPublicTable && motion.Phase != H2TurnPresentation.TurnPhase.SettlementTransition);
            publicActionLabel.text = showAction ? committedActionCue : "";
            publicTransferLabel.text = "";
            publicTransferLabel.gameObject.SetActive(false);
            publicActionCard.anchorMin = publicActionCard.anchorMax = publicActionCard.pivot = new Vector2(.5f, 1);
            publicActionCard.anchoredPosition = new Vector2(0, -44);
            publicActionCard.sizeDelta = new Vector2(350, 60);
            publicActionCard.gameObject.SetActive(showAction);
        }

        private bool PlacePublicValue(int index)
        {
            Vector2 min, max;
            if (!ClockScreenBounds(index, out min, out max)) return false;
            return PlaceClockCard(clockValueCards[index], new Vector2((min.x + max.x) * .5f, max.y), true);
        }

        private bool ClockScreenBounds(int index, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            if (clockDials[index])
            {
                var dial = clockDials[index]; var bounds = dial.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var point = uiCamera.WorldToScreenPoint(dial.transform.TransformPoint(bounds.center +
                        Vector3.Scale(bounds.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                    if (point.z <= uiCamera.nearClipPlane) return false;
                    min = Vector2.Min(min, point); max = Vector2.Max(max, point);
                }
            }
            else min = max = uiCamera.WorldToScreenPoint(clocks[index].transform.position);
            // 문자판뿐 아니라 열린 덮개 위로도 여백을 확보한다. 가로 기준은 문자판 중심을 유지한다.
            if (clockLids[index] != null)
                foreach (var mesh in clockLids[index])
                {
                    var bounds = mesh.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var point = uiCamera.WorldToScreenPoint(mesh.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                        if (point.z <= uiCamera.nearClipPlane) return false;
                        max.y = Mathf.Max(max.y, point.y);
                    }
                }
            return true;
        }

        private bool PlaceClockCard(RectTransform card, Vector2 screen, bool above)
        {
            if (!uiCamera.pixelRect.Contains(screen)) return false;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, screen, null, out local);
            var half = card.sizeDelta * .5f; var limits = uiRoot.rect;
            card.anchoredPosition = new Vector2(Mathf.Clamp(local.x, limits.xMin + half.x + 16, limits.xMax - half.x - 16),
                Mathf.Clamp(local.y + (above ? 1 : -1) * (half.y + 20), limits.yMin + half.y + 16, limits.yMax - half.y - 16));
            return true;
        }

        private void FindClockDial(int index)
        {
            dialOwners[index] = clocks[index]; clockDials[index] = null;
            clockLids[index] = null;
            if (!clocks[index].H2Presentation) return;
            clockLids[index] = clocks[index].H2Presentation.Watch.LidPivot.GetComponentsInChildren<MeshFilter>();
            foreach (var mesh in clocks[index].H2Presentation.Watch.GetComponentsInChildren<MeshFilter>())
                if (mesh.name == "Dial") { clockDials[index] = mesh; break; }
        }

        private void HideClockContext()
        {
            foreach (var value in clockValues) if (value) { value.text = ""; value.transform.parent.gameObject.SetActive(false); }
            if (publicActionCard) publicActionCard.gameObject.SetActive(false);
            if (publicActionLabel) publicActionLabel.text = "";
            if (publicTransferLabel) publicTransferLabel.text = "";
            committedActionCue = ""; publicActionUntil = 0;
        }
    }
}
