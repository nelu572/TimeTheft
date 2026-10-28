#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TimeTheft.ItemPresentation;

namespace TimeTheft.Tests
{
    public sealed class GameFlowTempoTests
    {
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private float oldTimeScale;
        private ItemWorldInventoryView World => controller.View.WorldItems;

        [UnitySetUp]
        public IEnumerator Load()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.Rules.InitialLifeHours = 50;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.EnableAiItemStrategy = false;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.PreviewDuration = .06f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = settings.DiscardDuration = .05f;
            settings.AiThinkDuration = .05f;
            settings.RandomSeed = 635;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller);
            controller.FlowNotice.PlaybackSpeed = 8;
            Time.timeScale = 0;
            controller.RestartGame();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false;
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
        }

        private static IEnumerator Wait(Func<bool> condition, float seconds = 30)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "진행 완료 신호 시간 초과");
        }
        private bool PlayerReady => !controller.IsFlowBlocked && !controller.IsPresentingItem &&
            controller.Match.ActiveParticipant == Participant.Player &&
            (controller.Match.Stage == TurnStage.AwaitingClock || controller.Match.Stage == TurnStage.AwaitingItems);
        private IEnumerator Act()
        {
            if (controller.Match.Stage == TurnStage.AwaitingItems) controller.EndItemPhase();
            yield return Wait(() => !controller.IsFlowBlocked);
            controller.SelectClock(controller.Match.GetAvailableClockIndices()[0]);
            yield return GameUiTestInput.ClockMotionReady(controller);
            controller.ChooseAction(TimeAction.Ration);
        }
        private void SpeedItems(float speed = 8)
        {
            foreach (var item in World.PlayerItems.Concat(World.OpponentItems))
                if (item.ModelPresentation) item.ModelPresentation.PlaybackSpeed = speed;
        }

        [UnityTest]
        public IEnumerator StartAndRestartBlockInputAndPreserveFiftyFifteenThirtyHourSetup()
        {
            Assert.That(controller.FlowNotice.IsVisible, Is.True);
            Assert.That(controller.FlowNotice.Message, Is.EqualTo("첫 번째 시계 묶음"));
            Assert.That(controller.Match.PlayerLifeMinutes, Is.EqualTo(2100));
            Assert.That(controller.Match.AiLifeMinutes, Is.EqualTo(2100));
            Assert.That(controller.Match.BankMinutes, Is.EqualTo(1800));
            Assert.That(GameTime.Format(controller.Match.BankMinutes), Is.EqualTo("30:00"));
            controller.SelectClock(0); controller.EndItemPhase(); controller.ChooseAction(TimeAction.Collect);
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
            bool sawTurn = false;
            yield return Wait(() => {
                sawTurn |= controller.FlowNotice.IsVisible && controller.FlowNotice.Message == "당신의 차례";
                return PlayerReady;
            });
            Assert.That(sawTurn, Is.True);
            var observation = controller.Match.Observe(Participant.Ai);
            Assert.That(observation.OwnLifeMinutes, Is.EqualTo(2100));
            Assert.That(observation.OpponentLifeMinutes, Is.EqualTo(2100));
            Assert.That(observation.BankMinutes, Is.EqualTo(1800));
            controller.RestartGame();
            Assert.That(controller.Match.PlayerLifeMinutes, Is.EqualTo(2100));
            Assert.That(controller.Match.AiLifeMinutes, Is.EqualTo(2100));
            Assert.That(controller.Match.BankMinutes, Is.EqualTo(1800));
            yield return Wait(() => PlayerReady);
        }

        [UnityTest]
        public IEnumerator TurnsAndNextBatchWaitForNoticesAndAiActionStaysClosedUntilNoticeEnds()
        {
            yield return Wait(() => PlayerReady);
            bool sawAiTurn = false, sawPlayerTurn = false, sawAction = false, sawSecondBatch = false;
            float deadline = Time.realtimeSinceStartup + 45;
            int actionTurn = -1;
            while (controller.Match.Batch < 2 && Time.realtimeSinceStartup < deadline)
            {
                var notice = controller.FlowNotice;
                if (notice.IsVisible)
                {
                    sawAiTurn |= notice.Message == "상대의 차례";
                    sawPlayerTurn |= controller.Match.Turn > 1 && notice.Message == "당신의 차례";
                    sawSecondBatch |= notice.Message == "두 번째 시계 묶음";
                    if (notice.Message == "징수" || notice.Message == "배급")
                    {
                        sawAction = true; actionTurn = controller.Match.Turn;
                        Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Ai));
                        Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.Revealing));
                        var clock = controller.View.Clocks[controller.Match.CurrentClock];
                        Assert.That(clock.IsSelected, Is.True);
                        Assert.That(clock.IsOpen, Is.False, "AI 안내가 끝나기 전 시계를 열지 않는다");
                        int used = controller.Match.UsedCount;
                        controller.SelectClock(0); controller.ChooseAction(TimeAction.Collect); controller.EndItemPhase();
                        Assert.That(controller.Match.UsedCount, Is.EqualTo(used));
                        Assert.That(controller.Match.Turn, Is.EqualTo(actionTurn));
                    }
                }
                if (PlayerReady) yield return Act();
                yield return null;
            }
            sawSecondBatch |= controller.FlowNotice.IsVisible && controller.FlowNotice.Message == "두 번째 시계 묶음";
            Assert.That(controller.Match.Batch, Is.EqualTo(2));
            Assert.That(sawAiTurn && sawPlayerTurn && sawAction && sawSecondBatch, Is.True);
        }

        [UnityTest]
        public IEnumerator ScissorsKeepVictimAndRemainingPositionsUntilImpactAndFinishBeforeNextInput()
        {
            int scissors = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Scissors);
            foreach (var kind in new[] { ItemKind.Screwdriver, ItemKind.SpareKey, ItemKind.TimeBypass })
                TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, kind);
            yield return Wait(() => PlayerReady && !World.ModelUses.IsAcquiring);
            SpeedItems(4);
            var positions = World.OpponentItems.ToDictionary(item => item, item => item.transform.position);
            var tool = World.PlayerItems.Single();
            var model = tool.ModelPresentation;
            bool impact = false;
            model.Presented += (token, signal) => { if (signal == ItemPresentationEvent.CutImpact) impact = true; };
            controller.UseItem(scissors);
            var stageAfterUse = controller.Match.Stage;
            Assert.That(controller.IsPresentingItem, Is.True);
            foreach (var entry in positions)
                Assert.That(Vector3.Distance(entry.Key.transform.position, entry.Value), Is.LessThan(.001f));
            while (!impact && controller.IsPresentingItem)
            {
                foreach (var entry in positions)
                    Assert.That(Vector3.Distance(entry.Key.transform.position, entry.Value), Is.LessThan(.001f));
                controller.EndItemPhase(); controller.SelectClock(0); controller.ChooseAction(TimeAction.Ration);
                Assert.That(controller.Match.Stage, Is.EqualTo(stageAfterUse));
                yield return null;
            }
            Assert.That(impact, Is.True);
            Assert.That(controller.IsPresentingItem, Is.True, "절단 뒤 대상 회수와 가위 퇴장까지 대기");
            yield return Wait(() => !controller.IsPresentingItem);
            Assert.That(World.ModelUses.ActiveCount, Is.Zero);
            Assert.That(World.OpponentItems.Count, Is.EqualTo(2));
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator AiWaitsForEachItemPresentationBeforeSelectingItsClock()
        {
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.EnableAiItemStrategy = true;
            settings.Rules.AiItemConservation = settings.Rules.AiDecisionNoise = 0;
            controller.RestartGame();
            foreach (var kind in new[] { ItemKind.Screwdriver, ItemKind.SpareKey, ItemKind.TimeBypass })
                TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, kind);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Scissors);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Screwdriver);
            bool usedScissors = false;
            ItemKind? previousKind = null;
            ItemPresentationController previousModel = null;
            float deadline = Time.realtimeSinceStartup + 35;
            while (controller.Match.CurrentClock < 0 && Time.realtimeSinceStartup < deadline)
            {
                SpeedItems(8);
                var result = controller.View.LastItemUse;
                if (result.HasValue && result.Value.Actor == Participant.Ai)
                {
                    var kind = result.Value.Effect.Value.Source.Kind;
                    if (previousKind != kind)
                    {
                        Assert.That(!previousModel || previousModel.IsUseReady, Is.True,
                            "이전 아이템의 사용 모션이 끝난 뒤 다음 아이템을 사용한다");
                        previousKind = kind;
                        previousModel = World.ModelUses.GetComponentsInChildren<ItemPresentationController>(true)
                            .FirstOrDefault(model => model.Profile.Item == (PresentedItem)kind && model.State != ItemPresentationState.Idle);
                    }
                    usedScissors |= kind == ItemKind.Scissors;
                    if (World.ModelUses.IsTransitioning)
                        Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
                }
                yield return null;
            }
            Assert.That(usedScissors, Is.True, "실제 AI 전략이 가위를 사용한 경로를 검사한다");
            Assert.That(controller.Match.CurrentClock, Is.GreaterThanOrEqualTo(0));
            Assert.That(World.ModelUses.IsTransitioning, Is.False);
        }

        [UnityTest]
        public IEnumerator HandcuffReleaseFinishesBeforeNextTurnNoticeAndPlayerAction()
        {
            int cuffs = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Handcuffs);
            yield return Wait(() => PlayerReady && !World.ModelUses.IsAcquiring);
            SpeedItems(4);
            var model = World.PlayerItems.Single().ModelPresentation;
            controller.UseItem(cuffs);
            yield return Wait(() => !controller.IsPresentingItem);
            Assert.That(model.State, Is.EqualTo(ItemPresentationState.Holding));
            yield return Act();
            yield return Wait(() => controller.Match.Turn == 3);
            Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Player));
            bool sawRelease = false;
            while (model && model.State != ItemPresentationState.Complete)
            {
                sawRelease |= model.State == ItemPresentationState.Releasing || model.State == ItemPresentationState.Exiting || model.State == ItemPresentationState.WaitingForHatch;
                Assert.That(controller.IsFlowBlocked, Is.True);
                Assert.That(controller.FlowNotice.IsVisible, Is.False, "수갑 해제·퇴장 뒤 턴 안내");
                controller.SelectClock(controller.Match.GetAvailableClockIndices()[0]);
                Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
                yield return null;
            }
            Assert.That(sawRelease, Is.True);
            yield return Wait(() => PlayerReady);
            Assert.That(World.ModelUses.ActiveCount, Is.Zero);
        }
    }
}
#endif
