using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeTheft
{
    [RequireComponent(typeof(TimeTheftGameView))]
    public sealed class TimeTheftGameController : MonoBehaviour
    {
        [SerializeField] private TimeTheftSettings settings;
        private TimeTheftGameView view;
        private System.Random aiRandom;
        private bool skipRequested;
        private int selectedItemIndex;
        public bool IsPresentingItem { get; private set; }
        public TimeTheftMatch Match { get; private set; }
        public TimeTheftSettings Settings => settings;
        public TimeTheftGameView View => view;

        private void Start()
        {
            if (!settings) throw new InvalidOperationException("TimeTheftSettings를 연결하세요.");
            view = GetComponent<TimeTheftGameView>();
            view.Initialize(SelectClock, ChooseAction, RestartGame);
            RestartGame();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !view) return;
            if (keyboard.hKey.wasPressedThisFrame) view.ToggleHelp();
            if (keyboard.rKey.wasPressedThisFrame) RestartGame();
            if (keyboard.spaceKey.wasPressedThisFrame) SkipAnimation();
            if (Match != null && Match.Stage == TurnStage.AwaitingKeyDecision && Match.ActiveParticipant == Participant.Player)
            {
                if (keyboard.enterKey.wasPressedThisFrame) KeepPeekedClock();
                return;
            }
            if (Match == null || Match.Stage != TurnStage.AwaitingItems || Match.ActiveParticipant != Participant.Player || IsPresentingItem) return;
            if (keyboard.tabKey.wasPressedThisFrame) view.ShowItemPhase(Match, ++selectedItemIndex);
            if (keyboard.iKey.wasPressedThisFrame && view.SelectedItemId.HasValue) UseItem(view.SelectedItemId.Value);
            if (keyboard.enterKey.wasPressedThisFrame) EndItemPhase();
        }

        public void RestartGame()
        {
            if (!isActiveAndEnabled || !view) return;
            StopAllCoroutines();
            skipRequested = false;
            IsPresentingItem = false;
            selectedItemIndex = 0;
            int seed = settings.RandomSeed == 0 ? Environment.TickCount : settings.RandomSeed;
            Match = new TimeTheftMatch(settings.Rules, new System.Random(seed), new System.Random(seed ^ 0x2471));
            aiRandom = new System.Random(seed ^ 0x51a7);
            view.ResetTable(Match);
            StartCoroutine(DealBatch());
        }

        private IEnumerator DealBatch()
        {
            if (!Match.BeginBatch()) yield break;
            view.ShowBatch(Match);
            yield return Animate(settings.PreviewDuration, null, false);
            Match.CloseAndShuffle();
            view.BeginShuffle(Match);
            yield return Animate(Mathf.Min(.3f, settings.ShuffleDuration * .3f), view.CloseFrame);
            yield return Animate(settings.ShuffleDuration, view.ShuffleFrame);
            Match.CompleteShuffle();
            yield return OpenTurn();
        }

        public void ChooseAction(TimeAction action)
        {
            if (!isActiveAndEnabled || Match == null || !Match.CommitAction(Participant.Player, action)) return;
            StartCoroutine(Resolve());
        }

        public void SelectClock(int index)
        {
            if (!isActiveAndEnabled || IsPresentingItem || Match == null) return;
            if (Match.Stage == TurnStage.AwaitingKeyDecision)
            {
                if (Match.ReselectPeekedClock(Participant.Player, index)) view.OpenAction(Match);
                return;
            }
            if (!Match.SelectClock(Participant.Player, index)) return;
            if (Match.Stage == TurnStage.AwaitingKeyDecision) view.OpenKeyDecision(Match);
            else view.OpenAction(Match);
        }

        public void KeepPeekedClock()
        {
            if (!isActiveAndEnabled || IsPresentingItem || Match == null || !Match.KeepPeekedClock(Participant.Player)) return;
            view.OpenAction(Match);
        }

        public void UseItem(int itemId)
        {
            if (!isActiveAndEnabled || IsPresentingItem || Match == null) return;
            var result = Match.UseItem(Participant.Player, itemId);
            if (!result.Succeeded)
            {
                if (Match.Stage == TurnStage.AwaitingItems) { view.ShowItemFailure(result); view.RefreshInventory(Match); }
                return;
            }
            IsPresentingItem = true;
            StartCoroutine(PresentPlayerItem(result));
        }

        private IEnumerator PresentPlayerItem(ItemUseResult result)
        {
            yield return PresentItem(result);
            if (Match.Stage == TurnStage.AwaitingItems) view.ShowItemPhase(Match, selectedItemIndex);
            else yield return OpenTurn();
        }

        private IEnumerator PresentItem(ItemUseResult result)
        {
            IsPresentingItem = true;
            view.ShowItemUse(result);
            view.RefreshInventory(Match);
            yield return Animate(.15f, null);
            IsPresentingItem = false;
        }

        public void EndItemPhase()
        {
            if (!isActiveAndEnabled || IsPresentingItem || Match == null || !Match.EndItemPhase(Participant.Player)) return;
            StartCoroutine(OpenTurn());
        }

        private IEnumerator OpenTurn()
        {
            selectedItemIndex = 0;
            if (Match.Stage == TurnStage.AwaitingItems)
            {
                view.ShowItemPhase(Match, selectedItemIndex);
                if (Match.ActiveParticipant == Participant.Player) yield break;
                // 매 성공 뒤 새 관찰로 재평가한다. 시작 보유량과 시도 ID로 반복을 유한하게 제한한다.
                int itemBudget = Match.GetInventory(Participant.Ai).Count;
                var attempted = new HashSet<int>();
                while (Match.Stage == TurnStage.AwaitingItems)
                {
                    var info = Match.ObserveItems(Participant.Ai);
                    int? itemId = TimeTheftAi.ChooseItem(info, aiRandom);
                    if (!itemId.HasValue || attempted.Count >= itemBudget || !Contains(info.UsableItemIds, itemId.Value)
                        || !attempted.Add(itemId.Value)) { Match.EndItemPhase(Participant.Ai); break; }
                    var result = Match.UseItem(Participant.Ai, itemId.Value);
                    if (!result.Succeeded) { Match.EndItemPhase(Participant.Ai); break; }
                    yield return PresentItem(result);
                }
            }
            view.OpenTurn(Match);
            if (Match.ActiveParticipant != Participant.Ai) yield break;
            yield return Animate(settings.AiThinkDuration, null);
            var clockInfo = Match.ObserveItems(Participant.Ai);
            int selected = clockInfo.Strategy.Enabled ? TimeTheftAi.SelectClock(clockInfo, aiRandom)
                : TimeTheftAi.SelectClock(Match.GetAvailableClockIndices(), aiRandom);
            if (!Contains(clockInfo.AvailableClockIndices, selected)) selected = clockInfo.AvailableClockIndices[0];
            if (!Match.SelectClock(Participant.Ai, selected)) yield break;
            if (Match.Stage == TurnStage.AwaitingKeyDecision)
            {
                var keyInfo = Match.ObserveItems(Participant.Ai);
                int choice = keyInfo.Strategy.Enabled ? TimeTheftAi.ChooseKeyClock(keyInfo, aiRandom) : Match.CurrentClock;
                if (choice == Match.CurrentClock || !Contains(keyInfo.AvailableClockIndices, choice)) Match.KeepPeekedClock(Participant.Ai);
                else if (!Match.ReselectPeekedClock(Participant.Ai, choice)) Match.KeepPeekedClock(Participant.Ai);
            }
            view.OpenAction(Match);
            yield return Animate(settings.AiThinkDuration * .5f, null);
            // 합법적인 자기 지식과 공개 관찰만 사용한다. 게임 RNG/숨은 순서에는 접근하지 않는다.
            var actionInfo = Match.ObserveItems(Participant.Ai);
            var action = actionInfo.Strategy.Enabled ? TimeTheftAi.Decide(actionInfo, aiRandom)
                : TimeTheftAi.Decide(actionInfo.PublicGame, actionInfo.Strategy.RiskTolerance, aiRandom);
            if (!Match.CommitAction(Participant.Ai, action)) yield break;
            yield return Resolve();
        }

        private IEnumerator Resolve()
        {
            view.Reveal(Match);
            yield return Animate(settings.RevealDuration, null);
            Match.Settle();
            view.BeginTransfer(Match);
            yield return Animate(settings.TransferDuration, view.NumbersFrame);
            yield return Animate(settings.ResultHoldDuration, null);
            if (Match.Stage == TurnStage.GameOver) { view.Result(Match); yield break; }
            Match.CompleteTurn();
            if (Match.Stage == TurnStage.BatchComplete)
            {
                view.DiscardLast(Match);
                yield return Animate(settings.DiscardDuration, null);
                yield return DealBatch();
            }
            else yield return OpenTurn();
        }

        private static bool Contains(IReadOnlyList<int> values, int value)
        {
            foreach (int candidate in values) if (candidate == value) return true;
            return false;
        }

        public void SkipAnimation()
        {
            if (!isActiveAndEnabled || Match == null) return;
            if (IsPresentingItem) { skipRequested = true; return; }
            if (Match.Stage == TurnStage.AwaitingAction ||
                Match.Stage == TurnStage.AwaitingClock || Match.Stage == TurnStage.AwaitingItems ||
                Match.Stage == TurnStage.AwaitingKeyDecision ||
                Match.Stage == TurnStage.BatchPreview || view.IsResultVisible) return;
            skipRequested = true;
        }

        private IEnumerator Animate(float duration, Action<float> frame, bool allowSkip = true)
        {
            skipRequested = false;
            float elapsed = 0;
            duration = Mathf.Max(.05f, duration);
            while (elapsed < duration)
            {
                if (allowSkip && skipRequested && elapsed >= .06f) break;
                elapsed += Time.unscaledDeltaTime;
                frame?.Invoke(Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            skipRequested = false;
            frame?.Invoke(1);
        }

        private void OnEnable() { if (view && Match != null) RestartGame(); }
        private void OnDisable()
        {
            StopAllCoroutines();
            IsPresentingItem = false;
            skipRequested = false;
            if (!view) return;
            view.CancelPresentation();
            if (Match != null) view.Numbers(Match);
        }
    }
}
