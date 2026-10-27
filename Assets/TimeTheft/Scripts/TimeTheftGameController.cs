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
        private bool selectingClock;
        public bool IsPresentingClock => selectingClock;
        public bool IsPresentingItem { get; private set; }
        public TimeTheftMatch Match { get; private set; }
        public TimeTheftSettings Settings => settings;
        public TimeTheftGameView View => view;

        private void Start()
        {
            if (!settings) throw new InvalidOperationException("TimeTheftSettings를 연결하세요.");
            view = GetComponent<TimeTheftGameView>();
            view.Initialize(SelectClock, ChooseAction, RestartGame);
            view.InitializeItemControls(SelectItem, UseSelectedItem, EndItemPhase, KeepPeekedClock);
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
            if (keyboard.tabKey.wasPressedThisFrame) view.CycleItem(Match);
            if (keyboard.iKey.wasPressedThisFrame && view.SelectedItemId.HasValue) UseItem(view.SelectedItemId.Value);
            if (keyboard.enterKey.wasPressedThisFrame) EndItemPhase();
        }

        public void RestartGame()
        {
            if (!isActiveAndEnabled || !view) return;
            StopAllCoroutines();
            selectingClock = false;
            skipRequested = false;
            IsPresentingItem = false;
            int seed = settings.RandomSeed == 0 ? Environment.TickCount : settings.RandomSeed;
            Match = new TimeTheftMatch(settings.Rules, new System.Random(seed), new System.Random(seed ^ 0x2471));
            aiRandom = new System.Random(seed ^ 0x51a7);
            view.ConfigureItemRules(settings.Rules);
            view.ResetTable(Match);
            StartCoroutine(DealBatch());
        }

        private IEnumerator DealBatch()
        {
            if (!Match.BeginBatch()) yield break;
            view.ShowBatch(Match);
            var lifecycle = view.Lifecycle;
            if (lifecycle && lifecycle.CanPlay(view))
            {
                yield return lifecycle.Supply(view, Match.Batch > 1);
                view.RestoreBatchFaces(Match);
            }
            var batchReveal = view.BatchReveal;
            bool cinematic = batchReveal && batchReveal.CanPlay(view);
            if (cinematic)
            {
                batchReveal.Prepare(view);
                yield return batchReveal.Reveal();
            }
            // 읽기 가능 신호 뒤부터 실제 관찰 시간을 센다. 진입 프레임의 이전 delta를 포함하지 않는다.
            double observeUntil = Time.realtimeSinceStartupAsDouble + Mathf.Max(.05f, settings.PreviewDuration);
            while (Time.realtimeSinceStartupAsDouble < observeUntil) yield return null;
            if (cinematic) yield return batchReveal.CloseAndReturn();
            Match.CloseAndShuffle();
            view.BeginShuffle(Match);
            if (!cinematic) yield return Animate(Mathf.Min(.3f, settings.ShuffleDuration * .3f), view.CloseFrame);
            var shuffle = view.ShufflePresentation;
            if (shuffle && shuffle.CanPlay(view))
            {
                shuffle.Prepare(view, Match.LastShuffle);
                yield return Animate(shuffle.Duration(settings.ShuffleDuration), shuffle.Frame, true, () => shuffle.IsRunning, false);
                shuffle.Complete();
            }
            else yield return Animate(settings.ShuffleDuration, view.ShuffleFrame);
            Match.CompleteShuffle();
            yield return OpenTurn();
        }

        public void ChooseAction(TimeAction action)
        {
            if (!isActiveAndEnabled || selectingClock || Match == null || !Match.CommitAction(Participant.Player, action)) return;
            StartCoroutine(Resolve());
        }

        public void SelectClock(int index)
        {
            if (!isActiveAndEnabled || IsPresentingItem || selectingClock || Match == null) return;
            if (Match.Stage == TurnStage.AwaitingKeyDecision)
            {
                if (Match.ReselectPeekedClock(Participant.Player, index))
                { view.OpenAction(Match); selectingClock = true; StartCoroutine(PresentClockSelection()); }
                return;
            }
            if (!Match.SelectClock(Participant.Player, index)) return;
            if (Match.Stage == TurnStage.AwaitingKeyDecision) view.OpenKeyDecision(Match);
            else view.OpenAction(Match);
            selectingClock = true; StartCoroutine(PresentClockSelection());
        }

        public void KeepPeekedClock()
        {
            if (!isActiveAndEnabled || IsPresentingItem || selectingClock || Match == null || !Match.KeepPeekedClock(Participant.Player)) return;
            view.OpenAction(Match);
        }

        private IEnumerator PresentClockSelection()
        {
            var motion = view.TurnPresentation;
            if (!motion || !motion.CanPlay(view, Match.CurrentClock)) { selectingClock = false; yield break; }
            selectingClock = true; view.LockClockMotionInput();
            yield return motion.Select(view, Match.CurrentClock);
            selectingClock = false;
            if (!isActiveAndEnabled) yield break;
            if (Match.Stage == TurnStage.AwaitingKeyDecision) view.OpenKeyDecision(Match);
            else if (Match.Stage == TurnStage.AwaitingAction) view.OpenAction(Match);
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

        public void SelectItem(int itemId)
        {
            if (!isActiveAndEnabled || IsPresentingItem || Match == null || !view ||
                Match.Stage != TurnStage.AwaitingItems || Match.ActiveParticipant != Participant.Player) return;
            view.SelectItem(Match, itemId);
        }

        public void UseSelectedItem()
        {
            if (view && view.SelectedItemId.HasValue) UseItem(view.SelectedItemId.Value);
        }

        private IEnumerator PresentPlayerItem(ItemUseResult result)
        {
            yield return PresentItem(result);
            if (Match.Stage == TurnStage.AwaitingItems) view.ShowItemPhase(Match, 0);
            else yield return OpenTurn();
        }

        private IEnumerator PresentItem(ItemUseResult result)
        {
            IsPresentingItem = true;
            view.ShowItemUse(PublicItemResult(result));
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
            if (Match.Stage == TurnStage.AwaitingItems)
            {
                view.ShowItemPhase(Match, 0);
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
            yield return PresentClockSelection();
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
            var motion = view.TurnPresentation;
            bool cinematic = motion && motion.CanPlay(view, Match.CurrentClock);
            bool settlementMotion = cinematic && motion.CanSettle(view, Match.CurrentClock);
            view.Reveal(Match, cinematic);
            if (cinematic)
            {
                yield return motion.RevealEnter(view, Match.CurrentClock);
                view.ShowRevealDetails(Match);
            }
            yield return Animate(settings.RevealDuration, null);
            if (settlementMotion) yield return motion.SettlementTransition();
            else if (cinematic) yield return motion.ReturnToLegacy();
            if (!Match.Settle()) yield break;
            view.BeginTransfer(Match);
            if (settlementMotion)
            {
                motion.SetSettlementPhase(H2TurnPresentation.TurnPhase.SettlementLead);
                yield return Animate(motion.SettlementLeadDuration, null);
                motion.SetSettlementPhase(H2TurnPresentation.TurnPhase.CountingTime);
            }
            yield return Animate(settings.TransferDuration, view.NumbersFrame);
            if (settlementMotion) motion.SetSettlementPhase(H2TurnPresentation.TurnPhase.SettlementHold);
            yield return Animate(settings.ResultHoldDuration, null);
            var lifecycle = view.Lifecycle;
            bool removal = settlementMotion && lifecycle && lifecycle.CanPlay(view);
            if (settlementMotion) yield return motion.CloseAndReturn(removal);
            if (removal)
            {
                if (lifecycle && lifecycle.CanPlay(view)) yield return lifecycle.Remove(view, Match.CurrentClock, false);
                else yield return motion.ReturnToLegacy();
            }
            if (Match.Stage == TurnStage.GameOver) { view.Result(Match); yield break; }
            Match.CompleteTurn();
            if (motion) motion.Cancel();
            if (Match.Stage == TurnStage.BatchComplete)
            {
                bool discardMotion = lifecycle && lifecycle.CanPlay(view);
                view.DiscardLast(Match, discardMotion);
                if (discardMotion)
                {
                    for (int i = 0; i < TimeTheftMatch.ClocksPerBatch; i++)
                        if (Match.GetClock(i).Visibility == ClockVisibility.Discarded)
                        { yield return lifecycle.Remove(view, i, true); break; }
                }
                else yield return Animate(settings.DiscardDuration, null);
                yield return DealBatch();
            }
            else yield return OpenTurn();
        }

        private static bool Contains(IReadOnlyList<int> values, int value)
        {
            foreach (int candidate in values) if (candidate == value) return true;
            return false;
        }

        // 상대 보유품은 종류와 수량으로만 표시한다. 공개 사용 결과에도 상대 ID를 넘기지 않는다.
        private static ItemUseResult PublicItemResult(ItemUseResult result)
        {
            ItemEffectState? effect = result.Effect;
            if (effect.HasValue && effect.Value.Source.Owner == Participant.Ai)
            {
                var value = effect.Value;
                var source = value.Source;
                effect = new ItemEffectState(new ItemState(0, source.Kind, source.Owner, source.IsConsumed,
                    source.IsFoundationTest), value.Timing, value.RegisteredTurn, value.Target);
            }
            ItemState? removed = result.RemovedItem;
            if (removed.HasValue && removed.Value.Owner == Participant.Ai)
            {
                var source = removed.Value;
                removed = new ItemState(0, source.Kind, source.Owner, source.IsConsumed, source.IsFoundationTest);
            }
            return new ItemUseResult(result.Actor, result.Actor == Participant.Ai ? 0 : result.ItemId,
                result.Failure, effect, removed);
        }

        public void SkipAnimation()
        {
            if (!isActiveAndEnabled || Match == null) return;
            var lifecycle = view.Lifecycle;
            if (lifecycle && lifecycle.IsRunning) { lifecycle.SkipMotion(); return; }
            var motion = view.TurnPresentation;
            if (motion && motion.IsBusy) { motion.SkipMotion(); return; }
            if (IsPresentingItem) { skipRequested = true; return; }
            if (Match.Stage == TurnStage.AwaitingAction ||
                Match.Stage == TurnStage.AwaitingClock || Match.Stage == TurnStage.AwaitingItems ||
                Match.Stage == TurnStage.AwaitingKeyDecision ||
                Match.Stage == TurnStage.BatchPreview || view.IsResultVisible) return;
            skipRequested = true;
        }

        internal void RestoreClockPresentationAfterDisable(int index)
        {
            if (!isActiveAndEnabled || !view || Match == null || index < 0 || index != Match.CurrentClock) return;
            if (!Match.GetClock(index).Face.HasValue) return;
            view.RestoreRevealedClock(Match);
            if (Match.Stage == TurnStage.Revealing) view.ShowRevealDetails(Match);
        }

        private IEnumerator Animate(float duration, Action<float> frame, bool allowSkip = true, Func<bool> continueWhile = null, bool ease = true)
        {
            skipRequested = false;
            float elapsed = 0;
            duration = Mathf.Max(.05f, duration);
            while (elapsed < duration)
            {
                if (continueWhile != null && !continueWhile()) break;
                if (allowSkip && skipRequested && elapsed >= .06f) break;
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                frame?.Invoke(ease ? Mathf.SmoothStep(0, 1, progress) : progress);
                yield return null;
            }
            skipRequested = false;
            frame?.Invoke(1);
        }

        private void OnEnable() { if (view && Match != null) RestartGame(); }
        private void OnDisable()
        {
            StopAllCoroutines();
            selectingClock = false;
            IsPresentingItem = false;
            skipRequested = false;
            if (!view) return;
            view.CancelPresentation();
            if (Match != null) view.Numbers(Match);
        }
    }
}
