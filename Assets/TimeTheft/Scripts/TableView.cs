using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TimeTheft
{
    public sealed class TableView : MonoBehaviour
    {
        public static readonly Color Ink = new Color(.035f, .065f, .075f);
        public static readonly Color Cream = new Color(.94f, .9f, .77f);
        public static readonly Color Muted = new Color(.57f, .66f, .65f);
        public static readonly Color Gold = new Color(.78f, .63f, .35f);
        public static readonly Color PlayerColor = new Color(.33f, .85f, .75f);
        public static readonly Color AiColor = new Color(.88f, .48f, .4f);
        [Header("시계 프리팹과 씬 배치 기준")]
        [SerializeField] private ClockView clockPrefab;
        [SerializeField] private RectTransform clockRoot;
        [SerializeField] private RectTransform[] dealSlots;
        [SerializeField] private RectTransform playerSlot, aiSlot, discardSlot;
        [Header("고정 UI 참조")]
        [SerializeField] private UnityEngine.UI.Button keep, swap, restart, skip, replay;
        [SerializeField] private TextMeshProUGUI prompt, detail, roundLabel;
        [SerializeField] private TextMeshProUGUI playerLife, aiLife, pot, playerAction, aiAction, playerDelta, aiDelta, potDelta;
        [SerializeField] private RectTransform playerBar, aiBar, potPanel, resultPanel, exchangeCue;
        [SerializeField] private TextMeshProUGUI resultTitle, resultDetail, lastRound;
        private Vector2 playerBarSize, aiBarSize;
        private Vector3 potScale;
        public ClockView[] Clocks { get; private set; }
        public UnityEngine.UI.Button Keep => keep;
        public UnityEngine.UI.Button Swap => swap;
        public UnityEngine.UI.Button Restart => restart;
        public UnityEngine.UI.Button Skip => skip;
        public UnityEngine.UI.Button Replay => replay;
        public TextMeshProUGUI Prompt => prompt;
        public TextMeshProUGUI Detail => detail;
        public TextMeshProUGUI RoundLabel => roundLabel;
        public Vector2 PlayerPosition => SlotPosition(playerSlot);
        public Vector2 AiPosition => SlotPosition(aiSlot);
        public Vector2 DealPosition(int index) => SlotPosition(dealSlots[index]);
        public bool IsResultVisible => resultPanel.gameObject.activeInHierarchy;

        // 고정 화면은 씬 소유, 교체 가능한 시계만 프리팹에서 한 번 생성한다.
        public void Initialize(Action<int> select, Action<ClockAction> choose, Action restartGame, Action skipAnimation)
        {
            if (Clocks != null) return;
            if (!clockPrefab || !clockRoot || dealSlots == null || dealSlots.Length != 3 ||
                !playerSlot || !aiSlot || !discardSlot)
                throw new InvalidOperationException("TableView의 시계 프리팹과 배치 기준을 연결하세요.");
            foreach (var slot in dealSlots)
                if (!slot) throw new InvalidOperationException("TableView의 분배 위치 3개를 연결하세요.");
            playerBarSize = playerBar.sizeDelta;
            aiBarSize = aiBar.sizeDelta;
            potScale = potPanel.localScale;
            Clocks = new ClockView[3];
            for (int i = 0; i < Clocks.Length; i++)
            {
                Clocks[i] = Instantiate(clockPrefab, clockRoot, false);
                Clocks[i].name = "Clock" + i;
                Clocks[i].Initialize(i, select);
                Clocks[i].Rect.anchoredPosition = DealPosition(i);
            }
            Keep.onClick.AddListener(() => choose(ClockAction.Keep));
            Swap.onClick.AddListener(() => choose(ClockAction.Swap));
            Restart.onClick.AddListener(() => restartGame());
            Replay.onClick.AddListener(() => restartGame());
            Skip.onClick.AddListener(() => skipAnimation());
            AddPreview(Keep, false);
            AddPreview(Swap, true);
            Actions(false);
        }

        private Vector2 SlotPosition(RectTransform slot)
        {
            // 기준점의 부모가 달라도 시계 컨테이너의 좌표로 변환한다.
            return clockRoot.InverseTransformPoint(slot.position);
        }

        public void PrepareDeal(int round)
        {
            ClearDeltas();
            ClearActions();
            RoundLabel.text = "ROUND " + round.ToString("00");
            Cue("새로운 시간", "", false);
            for (int i = 0; i < Clocks.Length; i++)
            {
                var clock = Clocks[i];
                clock.Show(0, false, "", Gold);
                clock.SetSelectable(false);
                clock.Group.alpha = 0;
                clock.SetRotation(0);
                clock.SetScale(.85f);
                clock.Rect.anchoredPosition = DealPosition(i) + new Vector2(0, 35);
            }
        }

        public void DealFrame(float t)
        {
            for (int i = 0; i < Clocks.Length; i++)
            {
                float stagger = Mathf.Clamp01(t * 1.4f - i * .2f);
                var clock = Clocks[i];
                clock.Group.alpha = stagger;
                clock.SetScale(Mathf.Lerp(.85f, 1, stagger));
                clock.Rect.anchoredPosition = Vector2.Lerp(DealPosition(i) + new Vector2(0, 35), DealPosition(i), stagger);
            }
        }

        public void OpenSelection(TimeTheftMatch match)
        {
            for (int i = 0; i < Clocks.Length; i++)
            {
                if (i == match.PlayerClock || i == match.AiClock) continue;
                Clocks[i].Show(0, false, match.CanSelectClock(i) ? "선택 가능" : "", Gold);
                Clocks[i].SetSelectable(match.CanSelectClock(i));
            }
            Cue(match.Stage == RoundStage.Selecting ? "시계를 선택하세요" : "상대가 시계를 선택합니다",
                match.PlayerSelectsFirst ? "이번 라운드는 내가 먼저 선택합니다" : "이번 라운드는 상대가 먼저 선택합니다", false);
        }

        public void CloseSelection()
        {
            foreach (var clock in Clocks) clock.SetSelectable(false);
        }

        public void PrepareAllocation(int index, bool player)
        {
            CloseSelection();
            Clocks[index].Show(0, false, player ? "나의 시계" : "상대의 시계", player ? PlayerColor : AiColor);
            Cue(player ? "내 시계를 가져옵니다" : "상대가 시계를 가져갑니다", "");
        }

        public void AllocationFrame(int index, bool player, float t)
        {
            Clocks[index].Rect.anchoredPosition = Vector2.Lerp(DealPosition(index), player ? PlayerPosition : AiPosition, t);
        }

        public void DiscardFrame(int index, float t)
        {
            Clocks[index].Rect.anchoredPosition = Vector2.Lerp(DealPosition(index), SlotPosition(discardSlot), t);
            Clocks[index].SetRotation(Mathf.Lerp(0, -30, t));
            Clocks[index].Group.alpha = 1 - t;
        }

        public void RevealPlayerClock(int player, int value)
        {
            Clocks[player].Show(value, true, "나만 확인", PlayerColor);
            Cue("자신의 시계를 확인하세요", "이 숫자가 최종적으로 내 수명에서 빠집니다");
        }

        public void PulseClock(int index, float t, float amount) => Clocks[index].SetScale(1 + amount * Mathf.Sin(t * Mathf.PI));

        public void OpenDecision(bool firstDecision)
        {
            Actions(true);
            Skip.gameObject.SetActive(false);
            Detail.text = firstDecision ? "유지할까요, 교환할까요?  ·  한쪽만 교환을 고르면 자리가 바뀝니다" : "서로의 선택은 확정 후 함께 공개됩니다";
        }

        public void PrepareExchange(int oldPlayer, int oldAi)
        {
            Cue("시계 교환", "한쪽만 교환을 선택했습니다");
            ExchangeCue(true);
            // 이동 중에는 두 시계를 뒤집어 상대의 비공개 값을 노출하지 않는다.
            Clocks[oldPlayer].Show(0, false, "상대에게 이동", AiColor);
            Clocks[oldAi].Show(0, false, "나에게 이동", PlayerColor);
        }

        public void ExchangeFrame(int oldPlayer, int oldAi, float t)
        {
            float arc = Mathf.Sin(t * Mathf.PI) * 160;
            Clocks[oldPlayer].Rect.anchoredPosition = Vector2.Lerp(PlayerPosition, AiPosition, t) + Vector2.left * arc;
            Clocks[oldAi].Rect.anchoredPosition = Vector2.Lerp(AiPosition, PlayerPosition, t) + Vector2.right * arc;
        }

        public void RevealFinalClocks(TimeTheftMatch match)
        {
            Clocks[match.PlayerClock].Show(match.PlayerValue, true, "나의 최종 시계", PlayerColor);
            Clocks[match.AiClock].Show(match.AiValue, true, "상대의 최종 시계", AiColor);
            Cue("최종 숫자 공개", "각자 가진 시계만큼 자신의 시간을 잃습니다");
        }

        public void ResetTable(int initialLife)
        {
            resultPanel.gameObject.SetActive(false);
            exchangeCue.gameObject.SetActive(false);
            lastRound.text = "";
            ClearDeltas();
            playerAction.text = aiAction.text = "";
            Numbers(initialLife, initialLife, 0, initialLife);
            foreach (var c in Clocks) { c.Group.alpha = 1; c.SetScale(1); c.SetSelectable(false); }
        }

        public void Numbers(int player, int ai, int stake, int initial)
        {
            playerLife.text = player + "h";
            aiLife.text = ai + "h";
            pot.text = stake + "h";
            playerBar.sizeDelta = new Vector2(playerBarSize.x * Mathf.Clamp01((float)player / initial), playerBarSize.y);
            aiBar.sizeDelta = new Vector2(aiBarSize.x * Mathf.Clamp01((float)ai / initial), aiBarSize.y);
            playerLife.color = player <= 12 ? AiColor : Cream;
            aiLife.color = ai <= 12 ? AiColor : Cream;
        }

        public void Actions(bool visible)
        {
            Keep.gameObject.SetActive(visible);
            Swap.gameObject.SetActive(visible);
            Prompt.gameObject.SetActive(!visible);
            exchangeCue.gameObject.SetActive(false);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        public void Cue(string prompt, string hint, bool canSkip = true)
        {
            Prompt.text = prompt;
            Detail.text = hint;
            Skip.gameObject.SetActive(canSkip);
        }

        public void RevealActions(ClockAction player, ClockAction ai)
        {
            playerAction.text = "나  ·  " + ActionName(player);
            aiAction.text = "상대  ·  " + ActionName(ai);
        }

        public void ClearActions() { playerAction.text = aiAction.text = ""; }
        public void ClearDeltas() { playerDelta.text = aiDelta.text = potDelta.text = ""; potPanel.localScale = potScale; }
        public void ShowLosses(int player, int ai)
        {
            playerDelta.text = "-" + player + "h";
            aiDelta.text = "-" + ai + "h";
            potDelta.text = "+" + (player + ai) + "h";
        }

        public void SettlementFeedback(float t)
        {
            potPanel.localScale = potScale * (1 + .08f * Mathf.Sin(t * Mathf.PI));
            var color = Gold; color.a = 1 - t * .5f; potDelta.color = color;
        }

        public void ExchangeCue(bool show) => exchangeCue.gameObject.SetActive(show);
        public void RememberRound(TimeTheftMatch match)
        {
            lastRound.text = $"지난 라운드\n나 -{match.PlayerLoss}h  /  상대 -{match.AiLoss}h\n{(match.DidExchange ? "시계 교환" : "제자리 유지")}";
        }

        public void Result(TimeTheftMatch match)
        {
            Actions(false);
            Skip.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(true);
            resultTitle.text = match.Winner == MatchWinner.Player ? "당신이 살아남았습니다" : match.Winner == MatchWinner.Ai ? "당신의 시간이 다했습니다" : "무승부  ·  두 사람 모두 0h";
            resultTitle.color = match.Winner == MatchWinner.Player ? PlayerColor : AiColor;
            resultDetail.text = match.Winner == MatchWinner.Draw ? $"남겨진 판돈 {match.Pot}h · 획득자 없음" : $"{(match.Winner == MatchWinner.Player ? "나" : "상대")}에게 판돈 {match.Award}h 지급";
            Cue("게임 종료", "새 게임으로 다시 겨뤄보세요", false);
        }

        private void AddPreview(UnityEngine.UI.Button button, bool swap)
        {
            var trigger = button.GetComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { exchangeCue.gameObject.SetActive(swap); Detail.text = swap ? "한쪽만 교환을 선택하면 두 시계가 자리를 바꿉니다" : "양쪽 선택이 같으면 시계는 현재 자리에 남습니다"; });
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { exchangeCue.gameObject.SetActive(false); });
            trigger.triggers.Add(exit);
        }

        public static string ActionName(ClockAction action) => action == ClockAction.Keep ? "유지" : "교환";

    }
}
