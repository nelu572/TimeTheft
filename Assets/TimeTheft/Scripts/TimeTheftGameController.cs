using System;
using System.Collections;
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
        }

        public void RestartGame()
        {
            if (!isActiveAndEnabled || !view) return;
            StopAllCoroutines();
            skipRequested = false;
            int seed = settings.RandomSeed == 0 ? Environment.TickCount : settings.RandomSeed;
            Match = new TimeTheftMatch(settings.Rules, new System.Random(seed));
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
            if (!isActiveAndEnabled || Match == null || !Match.SelectClock(Participant.Player, index)) return;
            view.OpenAction(Match);
        }

        private IEnumerator OpenTurn()
        {
            view.OpenTurn(Match);
            if (Match.ActiveParticipant != Participant.Ai) yield break;
            yield return Animate(settings.AiThinkDuration, null);
            int selected = TimeTheftAi.SelectClock(Match.GetAvailableClockIndices(), aiRandom);
            if (!Match.SelectClock(Participant.Ai, selected)) yield break;
            view.OpenAction(Match);
            yield return Animate(settings.AiThinkDuration * .5f, null);
            // 공개 구성/수명/Bank만 받는 AI. 게임 RNG와 분리해 숨은 순서에 접근하지 않는다.
            var action = TimeTheftAi.Decide(Match.Observe(Participant.Ai), settings.Rules.AiRiskTolerance, aiRandom);
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

        public void SkipAnimation()
        {
            if (!isActiveAndEnabled || Match == null || Match.Stage == TurnStage.AwaitingAction ||
                Match.Stage == TurnStage.AwaitingClock || Match.Stage == TurnStage.BatchPreview || view.IsResultVisible) return;
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
            if (!view) return;
            view.CancelPresentation();
            if (Match != null) view.Numbers(Match);
        }
    }
}
