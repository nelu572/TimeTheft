using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TimeTheft.ItemPresentation;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView
    {
        private TextMeshProUGUI turnCue, blockedCue;
        private readonly Image[] readyMarks = new Image[TimeTheftMatch.ClocksPerBatch];
        private TimeTheftMatch cueMatch;
        private int cueTurn = -1;
        private Participant cueActor;
        private TurnStage cueStage = (TurnStage)(-1);
        private float turnCueUntil, blockedUntil, bypassStarted;
        private sealed class CueAccent
        {
            public Renderer Renderer;
            public MaterialPropertyBlock Original, Work;
            public int Property;
            public Color Color;
            public bool Destination;
        }
        private readonly List<CueAccent> bypassAccents = new List<CueAccent>();
        public TextMeshProUGUI BlockedItemCue => blockedCue;
        public TextMeshProUGUI TurnCue => turnCue;
        public bool IsBypassCueActive => bypassAccents.Count > 0;

        private void InitializeUxCues()
        {
            turnCue = Label(uiRoot, "Turn Cue", 36, UiInk, Vector2.zero, new Vector2(760, 110));
            turnCue.alignment = TextAlignmentOptions.Center; OutlineText(turnCue);
            blockedCue = Label(uiRoot, "Item Blocked Cue", 28, new Color(.83f, .40f, .34f), Vector2.zero, new Vector2(1000, 50));
            blockedCue.alignment = TextAlignmentOptions.Center; OutlineText(blockedCue);
            for (int i = 0; i < readyMarks.Length; i++)
            {
                var go = new GameObject("Clock " + i + " Ready", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(uiRoot, false);
                readyMarks[i] = go.GetComponent<Image>(); readyMarks[i].raycastTarget = false;
                go.SetActive(false);
            }
        }
        public void ShowItemBlockedFeedback()
        {
            if (itemMatch == null || itemMatch.ActiveParticipant != Participant.Player || !itemMatch.IsItemUseBlocked(Participant.Player)) return;
            blockedUntil = Time.unscaledTime + 2f;
            blockedCue.text = "러버덕이 아이템 사용을 막고 있습니다.";
        }
        private void RefreshUxCues()
        {
            if (!turnCue || itemMatch == null) return;
            bool busy = itemPresentation || inputController && (inputController.IsPresentingClock || inputController.IsPresentingItem) ||
                focusCamera.IsPresentationCameraOwned || IsHelpVisible || IsResultVisible || worldItems.ModelUses && worldItems.ModelUses.IsAcquiring;
            bool choosing = itemMatch.Stage == TurnStage.AwaitingItems || itemMatch.Stage == TurnStage.AwaitingClock;
            if (!IsHelpVisible && !IsResultVisible && !(worldItems.ModelUses && worldItems.ModelUses.IsAcquiring) && choosing && (cueMatch != itemMatch || cueTurn != itemMatch.Turn || cueActor != itemMatch.ActiveParticipant))
            {
                cueMatch = itemMatch; cueTurn = itemMatch.Turn; cueActor = itemMatch.ActiveParticipant;
                turnCue.text = cueActor == Participant.Player ? "내 차례" : "상대 차례";
                if (cueActor == Participant.Player) turnCue.text += itemMatch.Stage == TurnStage.AwaitingItems
                    ? "\n<size=24>아이템 사용 또는 닫힌 시계 선택</size>" : "\n<size=24>닫힌 회중시계를 선택하세요</size>";
                turnCueUntil = Time.unscaledTime + 2.1f;
            }
            if (!busy && itemMatch.ActiveParticipant == Participant.Player && cueStage != itemMatch.Stage)
            {
                if (itemMatch.Stage == TurnStage.AwaitingAction) ShowContextNotice("징수 또는 배급을 선택하세요");
                cueStage = itemMatch.Stage;
            }
            var rect = turnCue.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -32);
            var color = turnCue.color; color.a = !IsHelpVisible && !IsResultVisible && itemMatch.Stage != TurnStage.Revealing
                ? Mathf.Clamp01((turnCueUntil - Time.unscaledTime) / .3f) : 0; turnCue.color = color;
            rect = blockedCue.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 30);
            color = blockedCue.color; color.a = !IsHelpVisible && !IsResultVisible && itemMatch.IsItemUseBlocked(Participant.Player)
                ? Mathf.Clamp01((blockedUntil - Time.unscaledTime) / .3f) : 0; blockedCue.color = color;
            for (int i = 0; i < readyMarks.Length; i++)
            {
                bool show = !busy && choosing && itemMatch.ActiveParticipant == Participant.Player && (itemMatch.CanSelectClock(Participant.Player, i) || itemMatch.Stage == TurnStage.AwaitingItems && itemMatch.GetClock(i).Visibility == ClockVisibility.Hidden);
                Vector2 min, max;
                show = show && ClockScreenBounds(i, out min, out max);
                if (show)
                {
                    ClockScreenBounds(i, out min, out max);
                    Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, new Vector2((min.x+max.x)*.5f,min.y),null,out local);
                    var mark = readyMarks[i].rectTransform; mark.anchorMin=mark.anchorMax=mark.pivot=new Vector2(.5f,.5f);
                    mark.anchoredPosition = local + Vector2.down * 12; mark.sizeDelta = new Vector2(52, 3);
                    readyMarks[i].color = new Color(UiBrass.r, UiBrass.g, UiBrass.b, .48f + .14f*Mathf.Sin(Time.unscaledTime*2));
                }
                readyMarks[i].gameObject.SetActive(show);
            }
            if (!busy && choosing && itemMatch.ActiveParticipant == Participant.Player && itemMatch.IsItemUseBlocked(Participant.Player) &&
                Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                bool overUi = false;
                if (EventSystem.current)
                {
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position=Mouse.current.position.ReadValue() },hits);
                    overUi = hits.Count > 0;
                }
                RaycastHit hit;
                if (!overUi && Physics.Raycast(uiCamera.ScreenPointToRay(Mouse.current.position.ReadValue()),out hit,100))
                {
                    var item = hit.collider.GetComponentInParent<ItemWorldView>();
                    if (item && item.Owner == Participant.Player && !item.IsRemoving) ShowItemBlockedFeedback();
                }
            }
            RefreshBypassCue();
        }
        public void BeginBypassCue(ItemEffectResult effect, BypassCablePresentation cables)
        {
            EndBypassCue();
            if (!effect.Triggered || effect.BypassedMinutes <= 0 || !effect.Recipient.HasValue) return;
            bypassStarted = Time.unscaledTime;
            AddBypassAccent(bank.GetComponent<Renderer>(), false);
            AddBypassAccent((effect.Recipient.Value == Participant.Player ? playerLife : aiLife).GetComponent<Renderer>(), true);
            if (cables) foreach (var mesh in cables.CableMeshes) if (mesh) AddBypassAccent(mesh.GetComponent<Renderer>(), false);
        }
        private void AddBypassAccent(Renderer renderer, bool destination)
        {
            if (!renderer || !renderer.sharedMaterial) return;
            int property = Shader.PropertyToID(renderer.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
            if (!renderer.sharedMaterial.HasProperty(property)) return;
            var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original);
            var work = new MaterialPropertyBlock(); renderer.GetPropertyBlock(work);
            var c = work.GetColor(property); if (c == Color.clear) c = renderer.sharedMaterial.GetColor(property);
            bypassAccents.Add(new CueAccent { Renderer=renderer, Original=original, Work=work, Property=property, Color=c, Destination=destination });
        }
        private void RefreshBypassCue()
        {
            foreach (var accent in bypassAccents)
            {
                if (!accent.Renderer) continue;
                float time = Time.unscaledTime - bypassStarted - (accent.Destination ? .18f : 0);
                float pulse = time < 0 ? 0 : Mathf.Pow(Mathf.Max(0,Mathf.Sin(time*9)),2);
                var c=accent.Color*(1+pulse*.7f); c.a=accent.Color.a;
                accent.Work.SetColor(accent.Property,c); accent.Renderer.SetPropertyBlock(accent.Work);
            }
        }
        public void EndBypassCue()
        {
            foreach (var accent in bypassAccents) if (accent.Renderer) accent.Renderer.SetPropertyBlock(accent.Original);
            bypassAccents.Clear();
        }
    }
}
