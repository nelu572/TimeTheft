#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    // 실제 Controller와 View 연결 검사. 포인터/UI 버튼 회귀는 기존 ItemWorldInteractionTests가 담당한다.
    public sealed class AiDenialIntegrationTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private float oldTimeScale;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("AiDenialMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.Rules.InitialLifeHours = 30;
            settings.Rules.ScrewdriverReductionPercent = settings.Rules.TimeBypassPercent = 50;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.EnableAiItemStrategy = true;
            settings.Rules.AiDecisionNoise = settings.Rules.AiItemConservation = 0;
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.RandomSeed = 635;
            settings.PreviewDuration = .5f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = settings.DiscardDuration = .08f;
            settings.AiThinkDuration = .4f;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            input.Set(mouse.position, Camera.main.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false;
            var focus = UnityEngine.Object.FindFirstObjectByType<TabletopFocusCamera>();
            if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator EmptyTargetDuckWaitsForRealRefillThenWorldRemovalAndCuffedBlockAgree()
        {
            settings.Rules.ItemsPerBatch = 1;
            settings.Rules.ItemWeights = new[] { 0, 0, 0, 0, 0, 1 };
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            settings.Rules.ExtraRedProbability = 0;
            controller.RestartGame();
            var match = controller.Match;
            int duck = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.RubberDuck);
            for (int playerActions = 0; playerActions < 2; playerActions++)
            {
                yield return WaitFor(() => match.ActiveParticipant == Participant.Player && match.Stage == TurnStage.AwaitingClock);
                Assert.That(match.Batch, Is.EqualTo(1));
                Assert.That(match.GetInventory(Participant.Player), Is.Empty);
                Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(duck), "아직 다음 실제 A 전 지급이 없으므로 오리를 보존한다");
                AssertWorldInventory();
                controller.SelectClock(match.GetAvailableClockIndices()[0]);
                controller.ChooseAction(TimeAction.Ration);
            }
            yield return WaitFor(() => match.Batch == 2 && match.Stage == TurnStage.BatchPreview);
            Assert.That(match.GetInventory(Participant.Player).Single().Kind, Is.EqualTo(ItemKind.Handcuffs));
            Assert.That(match.LastItemGrants.All(g => g.GrantedItems.Count == 1), Is.True);
            AssertWorldInventory();
            yield return WaitFor(() => match.IsItemUseBlocked(Participant.Player));
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.Turn, Is.GreaterThanOrEqualTo(8), "수갑의 후보 생략과 AI 추가 행동 뒤 실제 상대 A에서 봉쇄한다");
            Assert.That(match.GetInventory(Participant.Ai).Any(i => i.Id == duck), Is.False);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(1));
            Assert.That(controller.View.PhaseLabel.text, Does.Contain("고무 오리").And.Contain("봉쇄"));
            Assert.That(controller.View.UseItemButton.gameObject.activeInHierarchy, Is.False);
            AssertWorldInventory();
            Assert.That(controller.View.LastItemUse.Value.ItemId, Is.Zero, "상대 실 ID는 View에 전달하지 않는다");
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
        }

        [UnityTest]
        public IEnumerator SettlementCombinationWorldSyncHiddenStateAndRestartStayConsistent()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.Rules.ExtraRedProbability = 0;
            settings.Rules.RevealOpponentItems = false;
            controller.RestartGame();
            var old = controller.Match;
            TimeTheftItemTestSetup.Give(old, Participant.Ai, ItemKind.Screwdriver);
            TimeTheftItemTestSetup.Give(old, Participant.Ai, ItemKind.TimeBypass);
            controller.View.RefreshInventory(old);
            Assert.That(controller.View.WorldItems.OpponentItems.Count, Is.EqualTo(2));
            Assert.That(controller.View.WorldItems.OpponentItems.All(i => i.Kind == null && i.ItemId == null && i.Label.text == "아이템"), Is.True);
            yield return WaitFor(() => old.Stage == TurnStage.AwaitingClock && old.ActiveParticipant == Participant.Ai);
            Assert.That(old.HasScrewdriverProtection && old.HasTimeBypass, Is.True);
            Assert.That(controller.View.WorldItems.OpponentItems, Is.Empty);
            Assert.That(controller.View.LastItemUse.Value.ItemId, Is.Zero);
            yield return WaitFor(() => controller.View.IsCountingTime);
            Assert.That(old.LastItemEffects.Count, Is.EqualTo(2));
            Assert.That(old.LastTransfers.Count, Is.EqualTo(2));
            while (controller.View.IsCountingTime)
            {
                Assert.That(Read(controller.View.PlayerLife) + Read(controller.View.AiLife) + Read(controller.View.Bank), Is.EqualTo(3600));
                yield return null;
            }
            Assert.That(Read(controller.View.PlayerLife), Is.EqualTo(old.PlayerLifeMinutes));
            Assert.That(Read(controller.View.AiLife), Is.EqualTo(old.AiLifeMinutes));
            Assert.That(Read(controller.View.Bank), Is.EqualTo(old.BankMinutes));
            controller.RestartGame();
            Assert.That(controller.Match, Is.Not.SameAs(old));
            Assert.That(controller.View.SelectedItemId, Is.Null);
            Assert.That(controller.View.LastItemUse, Is.Null);
            Assert.That(controller.View.WorldItems.PlayerItems, Is.Empty);
            Assert.That(controller.View.WorldItems.OpponentItems, Is.Empty);
            Assert.That(controller.Match.GetPendingItemEffects(Participant.Ai), Is.Empty);
        }

        [UnityTest]
        public IEnumerator CertainNormalClockPlanSpendsBypassButKeepsUselessDriverInTheWorld()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.Rules.ExtraRedProbability = 0;
            settings.Rules.AiDecisionNoise = 1;
            settings.Rules.RevealOpponentItems = true;
            settings.RandomSeed = SeedForFirstAiRedClock();
            controller.RestartGame();
            var match = controller.Match;
            int driver = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            int wire = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.TimeBypass);
            // 첫 A만 검증용 허용 스위치로 통과시켜 두 품목을 보존한다. 모델 상태/시계 배열은 수정하지 않는다.
            TimeTheftItemTestSetup.SetUseAllowed(match, Participant.Ai, false);
            yield return WaitFor(() => match.ActiveParticipant == Participant.Player && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.ObserveItems(Participant.Player).RemainingFaces.All(f => f.Kind == ClockKind.Normal), Is.True,
                "실제 첫 AI 정산에서 유일한 빨강을 사용한 재현 시드다");
            Assert.That(match.GetInventory(Participant.Ai).Count, Is.EqualTo(2));
            TimeTheftItemTestSetup.SetUseAllowed(match, Participant.Ai, true);
            controller.SelectClock(match.GetAvailableClockIndices()[0]);
            controller.ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.Turn == 3 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.ObserveItems(Participant.Ai).RemainingFaces.Count, Is.EqualTo(3));
            Assert.That(match.ObserveItems(Participant.Ai).RemainingFaces.All(f => f.Kind == ClockKind.Normal && f.Hours == 8), Is.True);
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(driver));
            Assert.That(match.GetInventory(Participant.Ai).Any(i => i.Id == wire), Is.False);
            Assert.That(match.HasScrewdriverProtection, Is.False);
            Assert.That(match.HasTimeBypass, Is.True);
            Assert.That(controller.View.WorldItems.OpponentItems.Single().Kind, Is.EqualTo(ItemKind.Screwdriver));
            Assert.That(controller.View.WorldItems.OpponentItems.Single().ItemId, Is.Null);
            AssertWorldInventory();
            yield return WaitFor(() => controller.View.IsCountingTime);
            Assert.That(match.Action, Is.EqualTo(TimeAction.Collect));
            Assert.That(match.LastItemEffects.Single().Source.Kind, Is.EqualTo(ItemKind.TimeBypass));
            Assert.That(match.LastTransfers.Count, Is.EqualTo(2));
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(driver));
        }

        private int SeedForFirstAiRedClock()
        {
            // 테이블의 실제 비공개 값을 AI에 주지 않는 독립 테스트 오라클이다.
            for (int seed = 1; seed <= 100; seed++)
            {
                var reference = new TimeTheftMatch(settings.Rules, new System.Random(seed), new System.Random(seed ^ 0x2471));
                TimeTheftItemTestSetup.Give(reference, Participant.Ai, ItemKind.Screwdriver);
                TimeTheftItemTestSetup.Give(reference, Participant.Ai, ItemKind.TimeBypass);
                TimeTheftItemTestSetup.SetUseAllowed(reference, Participant.Ai, false);
                Assert.That(reference.BeginBatch() && reference.CloseAndShuffle() && reference.CompleteShuffle(), Is.True);
                int index = TimeTheftAi.SelectClock(reference.ObserveItems(Participant.Ai), new System.Random(seed ^ 0x51a7));
                Assert.That(reference.SelectClock(Participant.Ai, index) && reference.CommitAction(Participant.Ai, TimeAction.Collect), Is.True);
                if (reference.GetClock(index).Face.Value.Kind == ClockKind.Red) return seed;
            }
            throw new InvalidOperationException("첫 AI가 유일한 빨강을 사용하는 고정 시드를 찾지 못했습니다.");
        }

        private void AssertWorldInventory()
        {
            Assert.That(controller.View.WorldItems.PlayerItems.Select(i => i.ItemId.Value),
                Is.EquivalentTo(controller.Match.GetInventory(Participant.Player).Select(i => i.Id)));
            Assert.That(controller.View.WorldItems.OpponentItems.Count, Is.EqualTo(controller.Match.GetInventory(Participant.Ai).Count));
            Assert.That(controller.View.WorldItems.OpponentItems.All(i => !i.ItemId.HasValue), Is.True);
        }

        private static int Read(TMP_Text label)
        { var parts = label.text.Split(':'); return int.Parse(parts[0]) * 60 + int.Parse(parts[1]); }

        private IEnumerator WaitFor(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 12;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "단계 진행 시간 초과: " + controller.Match.Stage + " / turn=" + controller.Match.Turn);
        }
    }
}
#endif
