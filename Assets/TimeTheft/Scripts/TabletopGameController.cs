using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeTheft
{
    [RequireComponent(typeof(TabletopGameView))]
    public sealed class TabletopGameController : MonoBehaviour
    {
        [SerializeField] private PrototypeSettings settings;
        [Header("결과 관찰 시간 · 월드 플레이 전용 (초)")]
        [SerializeField, Min(.05f)] private float actionReadDuration = 1.25f;
        [SerializeField, Min(.05f)] private float exchangeVerdictDuration = .5f;
        [SerializeField, Min(.05f)] private float exchangeDuration = .7f;
        [SerializeField, Min(.05f)] private float finalValueDuration = 1f;
        [SerializeField, Min(.05f)] private float timeTransferDuration = 1.1f;
        [SerializeField, Min(.05f)] private float resultHoldDuration = 1.5f;
        [SerializeField, Min(.05f)] private float roundCloseDuration = .22f;
        private TabletopGameView view;
        private bool skipRequested;
        public TimeTheftMatch Match { get; private set; }
        public TabletopGameView View => view;
        public PrototypeSettings Settings => settings;

        private void Start()
        {
            if (!settings) throw new System.InvalidOperationException("기존 PrototypeSettings를 연결하세요.");
            view = GetComponent<TabletopGameView>();
            view.Initialize(SelectClock, ChooseAction, RestartGame);
            RestartGame();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.hKey.wasPressedThisFrame) view.ToggleHelp();
            if (keyboard.rKey.wasPressedThisFrame) RestartGame();
            if (keyboard.spaceKey.wasPressedThisFrame) SkipAnimation();
        }

        public void RestartGame()
        {
            if (!isActiveAndEnabled || !view) return;
            StopAllCoroutines();
            skipRequested = false;
            Match = new TimeTheftMatch(settings.Rules, settings.RandomSeed == 0 ? new System.Random() : new System.Random(settings.RandomSeed));
            view.ResetTable(Match);
            StartCoroutine(Deal());
        }

        public void SelectClock(int index)
        {
            if (!isActiveAndEnabled || Match == null || !Match.SelectClock(index)) return;
            StartCoroutine(Allocate(index, true));
        }

        public void ChooseAction(ClockAction action)
        {
            if (!isActiveAndEnabled || Match == null || !Match.CommitAction(action)) return;
            StartCoroutine(Resolve());
        }

        public void SkipAnimation()
        {
            if (!isActiveAndEnabled || Match == null || Match.Stage == RoundStage.Selecting || Match.Stage == RoundStage.Deciding ||
                (Match.Stage == RoundStage.GameOver && view.IsResultVisible)) return;
            skipRequested = true;
        }

        private IEnumerator Deal()
        {
            if (!Match.BeginRound()) yield break;
            view.PrepareDeal(Match.Round);
            yield return Animate(settings.MoveDuration, view.DealFrame);
            Match.OpenSelection();
            view.OpenSelection(Match);
            if (Match.Stage == RoundStage.SelectingAi) yield return SelectAi();
        }

        private IEnumerator SelectAi()
        {
            view.OpenSelection(Match);
            yield return Pause(view.AiSelectionPause);
            if (!Match.SelectAiClock()) yield break;
            yield return Allocate(Match.AiClock, false);
        }

        private IEnumerator Allocate(int index, bool player)
        {
            // 소유권은 즉시 확정하되, 다른 선택은 물건이 도착한 뒤 연다. 카메라 완료와는 독립이다.
            view.BeginAllocation(index, player);
            yield return Animate(view.AllocationDuration, view.AllocationFrame);
            Match.CompleteAllocation();
            if (Match.Stage == RoundStage.SelectingAi)
            {
                yield return SelectAi();
                yield break;
            }
            if (Match.Stage == RoundStage.Selecting)
            {
                view.OpenSelection(Match);
                yield break;
            }
            view.BeginDiscard(Match.DiscardedClock);
            yield return Animate(view.AllocationDuration, view.AllocationFrame);
            view.RevealPlayer(Match);
            yield return Pause(settings.RevealDuration * .7f);
            Match.OpenDecision();
            view.OpenDecision(Match);
        }

        private IEnumerator Resolve()
        {
            int oldPlayer = Match.PlayerClock, oldAi = Match.AiClock;
            view.RevealActions(Match);
            yield return Pause(actionReadDuration);
            view.ShowExchangeVerdict(Match);
            yield return Pause(exchangeVerdictDuration);
            Match.ResolveExchange();
            view.Exchange(Match, oldPlayer, oldAi);
            if (Match.DidExchange) yield return Animate(exchangeDuration, view.ExchangeFrame);
            view.RevealFinal(Match);
            yield return Pause(finalValueDuration);
            Match.Settle();
            view.Settle(Match);
            yield return Animate(timeTransferDuration, view.NumbersFrame);
            // 기존 프로토타입과 같이 0h 및 차감액을 보여준 뒤 판돈을 지급한다.
            view.HoldRoundResult();
            yield return Pause(resultHoldDuration);
            Match.FinishRound();
            if (Match.Stage == RoundStage.GameOver)
            {
                if (Match.Winner != MatchWinner.Draw)
                {
                    view.ShowAward(Match);
                    view.BeginNumberChange(Match);
                    yield return Animate(timeTransferDuration, view.NumbersFrame);
                }
                view.Result(Match);
            }
            else
            {
                view.CloseRound();
                yield return Animate(roundCloseDuration, view.RoundCloseFrame);
                yield return Deal();
            }
        }

        private IEnumerator Pause(float duration) => Animate(duration, null);

        private IEnumerator Animate(float duration, Action<float> frame)
        {
            skipRequested = false;
            float elapsed = 0;
            while (elapsed < Mathf.Max(.05f, duration))
            {
                if (skipRequested && elapsed >= .06f) break;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(.05f, duration));
                frame?.Invoke(Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            skipRequested = false;
            frame?.Invoke(1);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (view)
            {
                view.CancelPresentation();
                if (Match != null) view.Numbers(Match);
            }
        }
    }
}
