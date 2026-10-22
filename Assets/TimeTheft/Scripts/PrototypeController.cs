using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeTheft
{
    [RequireComponent(typeof(TableView))]
    public sealed class PrototypeController : MonoBehaviour
    {
        [SerializeField] private PrototypeSettings settings;
        [SerializeField, TextArea(5, 9)] private string debugState;
        private TableView view;
        private bool skipRequested;
        private bool firstDecision;
        public TimeTheftMatch Match { get; private set; }
        public TableView View => view;

        private void Start()
        {
            view = GetComponent<TableView>();
            view.Initialize(SelectClock, ChooseAction, RestartGame, SkipAnimation);
            RestartGame();
        }

        private void Update()
        {
            if (Keyboard.current == null) return;
            if (Keyboard.current.rKey.wasPressedThisFrame) RestartGame();
            if (Keyboard.current.spaceKey.wasPressedThisFrame) SkipAnimation();
        }

        public void RestartGame()
        {
            if (!view || view.Clocks == null) return;
            StopAllCoroutines();
            skipRequested = false;
            firstDecision = true;
            Match = new TimeTheftMatch(settings.Rules, settings.RandomSeed == 0 ? new System.Random() : new System.Random(settings.RandomSeed));
            view.Actions(false);
            view.ResetTable(settings.Rules.InitialLife);
            StartCoroutine(Deal());
        }

        public void SelectClock(int index)
        {
            if (Match == null || !Match.SelectClock(index)) return;
            view.CloseSelection();
            StartCoroutine(Allocate());
        }

        public void ChooseAction(ClockAction action)
        {
            if (Match == null || !Match.CommitAction(action)) return;
            view.Actions(false);
            StartCoroutine(Resolve());
        }

        public void SkipAnimation()
        {
            if (Match == null || Match.Stage == RoundStage.Selecting || Match.Stage == RoundStage.Deciding || (Match.Stage == RoundStage.GameOver && view.IsResultVisible)) return;
            skipRequested = true;
        }

        private IEnumerator Deal()
        {
            if (!Match.BeginRound()) yield break;
            Snapshot();
            view.PrepareDeal(Match.Round);
            yield return Animate(settings.MoveDuration, view.DealFrame);
            Match.OpenSelection();
            view.OpenSelection(Match.Round);
            Snapshot();
        }

        private IEnumerator Allocate()
        {
            Snapshot();
            int player = Match.PlayerClock, ai = Match.AiClock, discarded = Match.DiscardedClock;
            view.PrepareAllocation(player, ai, discarded);
            yield return Animate(settings.MoveDuration, t => view.AllocationFrame(player, ai, discarded, t));
            view.RevealPlayerClock(player, Match.PlayerValue);
            yield return Animate(settings.RevealDuration * .7f, t => view.PulseClock(player, t, .07f));
            Match.OpenDecision();
            view.OpenDecision(firstDecision);
            firstDecision = false;
            Snapshot();
        }

        private IEnumerator Resolve()
        {
            Snapshot();
            int oldPlayer = Match.PlayerClock, oldAi = Match.AiClock;
            view.RevealActions(Match.PlayerAction, Match.AiAction);
            view.Cue("양측 행동 공개", "나 " + TableView.ActionName(Match.PlayerAction) + "  ·  상대 " + TableView.ActionName(Match.AiAction));
            yield return Animate(settings.RevealDuration, _ => { });
            Match.ResolveExchange();
            Snapshot();
            if (Match.DidExchange)
            {
                view.PrepareExchange(oldPlayer, oldAi);
                yield return Animate(settings.MoveDuration * 1.5f, t => view.ExchangeFrame(oldPlayer, oldAi, t));
                view.ExchangeCue(false);
            }
            else
            {
                view.Cue("교환 없음", "양측 선택이 같아 시계가 제자리에 남습니다");
                yield return Animate(settings.MoveDuration, t =>
                {
                    view.PulseClock(oldPlayer, t, .06f);
                    view.PulseClock(oldAi, t, .06f);
                });
            }
            view.RevealFinalClocks(Match);
            yield return Animate(settings.RevealDuration * .65f, _ => { });
            int previousPlayer = Match.PlayerLife, previousAi = Match.AiLife, previousPot = Match.Pot;
            Match.Settle();
            Snapshot();
            view.Cue("시간 정산", "잃은 수명은 중앙 판돈에 누적됩니다");
            view.ShowLosses(Match.PlayerLoss, Match.AiLoss);
            yield return Animate(settings.SettlementDuration, t =>
            {
                view.Numbers(Mathf.RoundToInt(Mathf.Lerp(previousPlayer, Match.PlayerLife, t)),
                    Mathf.RoundToInt(Mathf.Lerp(previousAi, Match.AiLife, t)),
                    Mathf.RoundToInt(Mathf.Lerp(previousPot, Match.Pot, t)), settings.Rules.InitialLife);
                view.SettlementFeedback(t);
            });
            view.RememberRound(Match);
            // 0h와 차감액을 읽을 시간을 보장한 뒤 보상을 지급한다.
            view.Cue(Match.Winner == MatchWinner.None ? "다음 라운드" : "승부 결정", "");
            yield return Animate(settings.RoundPause, _ => { });
            int settledPlayer = Match.PlayerLife, settledAi = Match.AiLife, settledPot = Match.Pot;
            Match.FinishRound();
            Snapshot();
            if (Match.Stage == RoundStage.GameOver)
            {
                view.ClearDeltas();
                if (Match.Winner != MatchWinner.Draw)
                {
                    view.Cue("판돈 " + Match.Award + "h 지급", Match.Winner == MatchWinner.Player ? "살아남은 당신에게" : "살아남은 상대에게");
                    yield return Animate(settings.SettlementDuration, t =>
                    {
                        view.Numbers(Mathf.RoundToInt(Mathf.Lerp(settledPlayer, Match.PlayerLife, t)),
                            Mathf.RoundToInt(Mathf.Lerp(settledAi, Match.AiLife, t)),
                            Mathf.RoundToInt(Mathf.Lerp(settledPot, Match.Pot, t)), settings.Rules.InitialLife);
                        view.SettlementFeedback(t);
                    });
                }
                view.Result(Match);
            }
            else yield return Deal();
        }

        private IEnumerator Animate(float duration, Action<float> frame)
        {
            skipRequested = false;
            float elapsed = 0;
            while (elapsed < Mathf.Max(.05f, duration))
            {
                if (skipRequested && elapsed >= .06f) break;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(.05f, duration));
                frame(t * t * (3 - 2 * t));
                yield return null;
            }
            skipRequested = false;
            frame(1);
        }

        private void Snapshot() { debugState = Match.DebugSnapshot(); }

        [ContextMenu("디버그/현재 상태와 숨겨진 값 기록")]
        private void LogDebugState() { if (Match != null) Debug.Log(Match.DebugSnapshot(), this); }
    }
}
