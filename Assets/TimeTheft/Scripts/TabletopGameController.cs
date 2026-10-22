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
            // 규칙은 즉시 확정하며 카메라 완료를 게임 진행 조건으로 삼지 않는다.
            view.FocusCamera.Focus(view.Clocks[index].FocusTarget);
            StartCoroutine(Allocate());
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
            yield return Pause(settings.MoveDuration);
            Match.OpenSelection();
            view.OpenSelection();
        }

        private IEnumerator Allocate()
        {
            view.Allocate(Match);
            yield return Pause(settings.MoveDuration);
            view.RevealPlayer(Match);
            yield return Pause(settings.RevealDuration * .7f);
            Match.OpenDecision();
            view.OpenDecision(Match);
        }

        private IEnumerator Resolve()
        {
            int oldPlayer = Match.PlayerClock, oldAi = Match.AiClock;
            view.RevealActions(Match);
            yield return Pause(settings.RevealDuration);
            Match.ResolveExchange();
            view.Exchange(Match, oldPlayer, oldAi);
            yield return Animate(settings.MoveDuration * (Match.DidExchange ? 1.5f : 1), view.ExchangeFrame);
            view.RevealFinal(Match);
            yield return Pause(settings.RevealDuration * .65f);
            Match.Settle();
            view.Settle(Match);
            yield return Animate(settings.SettlementDuration, view.NumbersFrame);
            // 기존 프로토타입과 같이 0h 및 차감액을 보여준 뒤 판돈을 지급한다.
            yield return Pause(settings.RoundPause);
            Match.FinishRound();
            if (Match.Stage == RoundStage.GameOver)
            {
                if (Match.Winner != MatchWinner.Draw)
                {
                    view.BeginNumberChange(Match);
                    yield return Animate(settings.SettlementDuration, view.NumbersFrame);
                }
                view.Result(Match);
            }
            else yield return Deal();
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
