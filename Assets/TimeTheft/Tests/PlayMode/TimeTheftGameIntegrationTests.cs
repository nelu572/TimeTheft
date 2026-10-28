#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class TimeTheftGameIntegrationTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private Keyboard keyboard;
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private Camera camera;
        private TabletopFocusCamera focus;
        private float oldTimeScale;
        private Color playerColor, aiColor;
        // 선택 설명은 새 화면 고정 아이템 패널, 턴/정산 결과는 기존 상단 문구에 표시한다.
        private string ItemFeedback => controller.View.PhaseLabel.text + "\n" + controller.View.ItemDetailLabel.text;

        [UnitySetUp]
        public IEnumerator LoadMainScene()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("TimeTheftV03Mouse");
            keyboard = InputSystem.AddDevice<Keyboard>("TimeTheftV03Keyboard");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            GameUiTestInput.ShortenBatchReveal(controller);
            Assert.That(controller, Is.Not.Null);
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            // 기존 규칙/입력 회귀는 지급과 분리한다. 지급 통합 검사는 명시적으로 다시 켠다.
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.EnableAiItemStrategy = false;
            settings.RandomSeed = 635;
            settings.PreviewDuration = .2f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.AiThinkDuration =
                settings.ResultHoldDuration = settings.DiscardDuration = .08f;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            playerColor = controller.View.PlayerLife.color; aiColor = controller.View.AiLife.color;
            camera = Camera.main;
            focus = controller.View.FocusCamera;
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
            controller.RestartGame();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false;
            if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
            input?.TearDown();
        }

        [UnityTest]
        public IEnumerator MainSceneCompletesThroughActionButtonsAndActualClockClicks()
        {
            Assert.That(UnityEngine.Object.FindObjectsByType<TimeTheftGameController>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(GameObject.Find("Action UI/Controls/Ready"), Is.Null);
            var counter = GameObject.Find("Devices/IntegratedTimeCounter").transform;
            foreach (var label in new[] { controller.View.PlayerLife, controller.View.AiLife, controller.View.Bank })
                Assert.That(label.transform.IsChildOf(counter), Is.True);
            Assert.That(controller.View.Clocks, Has.Length.EqualTo(5));
            AssertNumbers();
            int initialClockId = controller.View.Clocks[0].GetInstanceID();
            int acted = 0, batches = 0, lastBatch = 0;
            bool sawRation = false, sawCollect = false, sawTransfer = false, sawNonSequential = false;
            float deadline = Time.realtimeSinceStartup + 120;
            while (!controller.View.IsResultVisible && Time.realtimeSinceStartup < deadline)
            {
                var match = controller.Match;
                if (match.Batch != lastBatch) { batches++; lastBatch = match.Batch; }
                if (match.Stage == TurnStage.AwaitingClock && match.ActiveParticipant == Participant.Player)
                {
                    bool ration = acted++ == 0;
                    sawRation |= ration; sawCollect |= !ration;
                    var available = match.GetAvailableClockIndices();
                    int index = available[(acted * 3) % available.Length];
                    sawNonSequential |= index != match.UsedCount;
                    yield return ClickClock(index);
                    yield return ChooseAction(ration ? TimeAction.Ration : TimeAction.Collect);
                }
                if (controller.View.IsCountingTime)
                {
                    sawTransfer = true;
                    Assert.That(Read(controller.View.PlayerLife) + Read(controller.View.AiLife) + Read(controller.View.Bank), Is.EqualTo(3600));
                    Assert.That(Read(controller.View.Bank), Is.GreaterThanOrEqualTo(0));
                }
                else if (match.Stage == TurnStage.AwaitingClock || match.Stage == TurnStage.BatchPreview || controller.View.IsResultVisible) AssertNumbers();
                yield return null;
            }
            Assert.That(controller.View.IsResultVisible, Is.True, "고정 시드 게임 완주");
            Assert.That(controller.Match.Winner, Is.Not.EqualTo(GameWinner.None));
            Assert.That(sawCollect && sawRation && sawTransfer && sawNonSequential, Is.True);
            Assert.That(batches, Is.GreaterThanOrEqualTo(2), "다음 묶음 자동 진행");
            Assert.That(Math.Min(controller.Match.PlayerLifeMinutes, controller.Match.AiLifeMinutes), Is.Zero);
            AssertNumbers();
            controller.RestartGame();
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview));
            Assert.That(controller.View.IsResultVisible, Is.False);
            Assert.That(controller.View.Clocks[0].GetInstanceID(), Is.EqualTo(initialClockId));
            Assert.That(controller.Match.BankMinutes, Is.EqualTo(1200));
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator PreviewDurationAutomaticallyClosesAndShufflesForEitherFirstParticipant()
        {
            foreach (var first in new[] { Participant.Player, Participant.Ai })
            {
                settings.Rules.FirstParticipant = first;
                settings.PreviewDuration = first == Participant.Player ? .45f : .7f;
                settings.AiThinkDuration = .3f;
                controller.RestartGame();
                float started = Time.realtimeSinceStartup;
                controller.SelectClock(4);
                controller.ChooseAction(TimeAction.Collect);
                yield return new WaitForSecondsRealtime(settings.PreviewDuration * .4f);
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview));
                Assert.That(controller.View.Clocks.All(c => c.IsOpen && c.Number.text != "" && !c.SelectionTarget.InputEnabled), Is.True);
                Assert.That(controller.View.CollectButton.gameObject.activeSelf, Is.False);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.Shuffling);
                Assert.That(Time.realtimeSinceStartup - started, Is.GreaterThanOrEqualTo(settings.PreviewDuration - .06f));
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                Assert.That(controller.View.Clocks.All(c => !c.IsOpen && c.Number.text == "" && c.Kind.text == ""), Is.True);
                Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
                if (first == Participant.Ai)
                {
                    yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingAction);
                    Assert.That(controller.Match.CurrentClock, Is.InRange(0, 4));
                    Assert.That(controller.View.Clocks.All(c => !c.IsOpen), Is.True, "AI도 먼저 고른 닫힌 시계를 유지한다");
                    Assert.That(Enumerable.Range(0, 5).All(i => !controller.Match.GetClock(i).Face.HasValue), Is.True);
                    yield return WaitFor(() => controller.Match.UsedCount > 0);
                    Assert.That(controller.Match.CurrentClock, Is.InRange(0, 4));
                    Assert.That(controller.View.CollectButton.gameObject.activeSelf, Is.False);
                }
            }
        }

        [UnityTest]
        public IEnumerator NonSequentialSelectionStaysPrivateUntilActionAndCannotBeReused()
        {
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            controller.ChooseAction(TimeAction.Ration);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock), "시계 선택 전 행동 확정 거부");
            Assert.That(controller.View.CollectButton.interactable || controller.View.RationButton.interactable, Is.False);
            yield return ClickClock(4);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen && !c.SelectionTarget.InputEnabled), Is.True);
            Assert.That(controller.View.Clocks[4].IsSelected, Is.True);
            controller.SelectClock(0);
            controller.View.Clocks[0].SelectionTarget.Activate();
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(4), "선택 후 연타로 다른 시계를 고르지 않는다");
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.Match.Turn == 3 && controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.GetClock(4).Visibility, Is.EqualTo(ClockVisibility.Used));
            Assert.That(controller.View.Clocks[4].gameObject.activeSelf, Is.False);
            controller.SelectClock(4);
            controller.View.Clocks[4].SelectionTarget.Activate();
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator LastTwoRemainSelectableThenUnchosenClockIsDiscardedUnopened()
        {
            settings.Rules.InitialLifeHours = 100;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.DiscardDuration = .3f;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Turn == 2 && controller.Match.Stage == TurnStage.AwaitingClock);
            yield return ClickClock(controller.Match.GetAvailableClockIndices().Last());
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.Match.Turn == 4 && controller.Match.Stage == TurnStage.AwaitingClock);
            var remaining = controller.Match.GetAvailableClockIndices();
            Assert.That(remaining, Has.Length.EqualTo(2));
            Assert.That(remaining.All(i => controller.View.Clocks[i].SelectionTarget.InputEnabled), Is.True);
            int chosen = remaining.Last(), discarded = remaining.First();
            var discardedToken = controller.View.Clocks[discarded]; bool discardedRemoved = false;
            Action<int, bool> removed = (i, isDiscard) =>
            {
                if (i != discarded || !isDiscard) return;
                Assert.That(discardedToken.gameObject.activeSelf, Is.False);
                Assert.That(discardedToken.H2Presentation.DisplayedHours, Is.Null);
                discardedRemoved = true;
            };
            controller.View.Lifecycle.RemoveCompleted += removed;
            yield return ClickClock(chosen);
            Assert.That(controller.Match.GetClock(chosen).Face.HasValue, Is.False);
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.BatchComplete);
            Assert.That(controller.Match.UsedCount, Is.EqualTo(4));
            Assert.That(controller.Match.GetClock(discarded).Visibility, Is.EqualTo(ClockVisibility.Discarded));
            Assert.That(controller.Match.GetClock(discarded).Face.HasValue, Is.False);
            Assert.That(discardedToken.H2Presentation.IsOpen || discardedToken.H2Presentation.IsRed, Is.False);
            Assert.That(discardedToken.SelectionTarget.InputEnabled || discardedToken.GetComponent<Collider>().enabled, Is.False);
            Assert.That(controller.View.Clocks[discarded].Number.text, Is.Empty);
            yield return WaitFor(() => discardedRemoved);
            controller.View.Lifecycle.RemoveCompleted -= removed;
            yield return WaitFor(() => controller.Match.Batch == 2 && controller.View.BatchReveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing);
            Assert.That(controller.View.Clocks.All(c => c.H2Presentation.IsOpen), Is.True);
            for (int i = 0; i < 5; i++) Assert.That(controller.View.Clocks[i].H2Presentation.DisplayedHours, Is.EqualTo(controller.Match.GetClock(i).Face.Value.Hours));
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen), Is.True);
            yield return WaitFor(() => controller.Match.Turn == 6 && controller.Match.Stage == TurnStage.AwaitingClock);
        }

        [UnityTest]
        public IEnumerator RestartCancelsPreviewSelectionAndPresentationWithoutStaleInput()
        {
            settings.PreviewDuration = .4f;
            foreach (var stage in new[] { TurnStage.BatchPreview, TurnStage.Shuffling, TurnStage.AwaitingClock,
                TurnStage.AwaitingAction, TurnStage.Revealing, TurnStage.Settling })
            {
                settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = .2f;
                controller.RestartGame();
                if (stage == TurnStage.Shuffling) yield return WaitFor(() => controller.Match.Stage == stage);
                else if (stage != TurnStage.BatchPreview)
                {
                    yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                    if (stage != TurnStage.AwaitingClock)
                    {
                        controller.SelectClock(4);
                        if (stage != TurnStage.AwaitingAction)
                        {
                            yield return GameUiTestInput.ClockMotionReady(controller);
                            controller.ChooseAction(TimeAction.Collect);
                        }
                        yield return WaitFor(() => controller.Match.Stage == stage);
                    }
                }
                controller.RestartGame();
                controller.SelectClock(0);
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview));
                Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
                Assert.That(controller.Match.UsedCount, Is.Zero);
                Assert.That(controller.View.Clocks.All(c => c.IsOpen && !c.SelectionTarget.InputEnabled), Is.True);
                AssertNumbers();
            }
            controller.enabled = false;
            controller.SelectClock(4);
            Assert.That(controller.View.Clocks.All(c => !c.SelectionTarget.InputEnabled), Is.True);
            controller.enabled = true;
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.BatchPreview));
            input.Press(keyboard.rKey); InputSystem.Update(); controller.SendMessage("Update"); input.Release(keyboard.rKey); InputSystem.Update();
            Assert.That(controller.Match.Batch, Is.EqualTo(1));
            // 씬 종료 시 UI가 Controller보다 먼저 파괴되는 순서도 안전해야 한다.
            UnityEngine.Object.Destroy(controller.View.CollectButton.GetComponentInParent<Canvas>().gameObject);
            yield return null;
            controller.enabled = false;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BothRationFacesShowActualRecipientAndExactMinutesBeyondTwentyFourHours()
        {
            settings.Rules.InitialLifeHours = 40;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 7;
            settings.TransferDuration = .3f;
            settings.ResultHoldDuration = .35f;
            foreach (var kind in new[] { ClockKind.Normal, ClockKind.Red })
            {
                controller.RestartGame();
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                Assert.That(controller.View.PlayerLife.text, Is.EqualTo("30:00"));
                int index = -1;
                for (int i = 0; i < 5; i++)
                {
                    // 같은 시드의 독립 모델을 오라클로 사용한다. 런타임 비공개 API는 확장하지 않는다.
                    var reference = new TimeTheftMatch(settings.Rules, new System.Random(settings.RandomSeed));
                    reference.BeginBatch(); reference.CloseAndShuffle(); reference.CompleteShuffle();
                    reference.SelectClock(Participant.Player, i); reference.CommitAction(Participant.Player, TimeAction.Ration);
                    if (reference.GetClock(i).Face.Value.Kind == kind) { index = i; break; }
                }
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                yield return ClickClock(index);
                yield return ChooseAction(TimeAction.Ration);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.Settling);
                var match = controller.Match;
                bool playerReceives = kind == ClockKind.Normal;
                Assert.That(match.TransferParticipant, Is.EqualTo(playerReceives ? Participant.Player : Participant.Ai));
                Assert.That(match.TransferredMinutes, Is.EqualTo(140));
                Assert.That(match.BankMinutes, Is.EqualTo(1060));
                Assert.That(match.PlayerLifeMinutes, Is.EqualTo(playerReceives ? 1940 : 1800));
                Assert.That(match.AiLifeMinutes, Is.EqualTo(playerReceives ? 1800 : 1940));
                Assert.That(controller.View.PhaseLabel.text, Does.Contain(playerReceives ? "Bank → 나" : "Bank → 상대"));
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("02:20"));
                var serialized = new SerializedObject(controller.View);
                var targetDelta = (TextMeshPro)serialized.FindProperty(playerReceives ? "playerDelta" : "aiDelta").objectReferenceValue;
                var bankDelta = (TextMeshPro)serialized.FindProperty("bankDelta").objectReferenceValue;
                Assert.That(targetDelta.text, Is.EqualTo("+02:20"));
                Assert.That(bankDelta.text, Is.EqualTo("−02:20"));
                yield return WaitFor(() => !controller.View.IsCountingTime);
                AssertNumbers();
                Assert.That(playerReceives ? controller.View.PlayerLife.text : controller.View.AiLife.text, Is.EqualTo("32:20"));
                Assert.That(controller.View.Bank.text, Is.EqualTo("17:40"));
            }
        }

        [UnityTest]
        public IEnumerator LargeMinuteBalancesKeepUnchangedLifeExactDuringTransfer()
        {
            settings.Rules.InitialLifeHours = int.MaxValue / (2 * GameTime.MinutesPerHour);
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 7;
            settings.TransferDuration = .4f;
            settings.ResultHoldDuration = .3f;
            controller.RestartGame();
            int initialLife = controller.Match.PlayerLifeMinutes;
            int total = initialLife + controller.Match.AiLifeMinutes + controller.Match.BankMinutes;
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            yield return ClickClock(4);
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.View.IsCountingTime);
            while (controller.View.IsCountingTime)
            {
                var unchanged = controller.Match.TransferParticipant == Participant.Player ? controller.View.AiLife : controller.View.PlayerLife;
                Assert.That(Read(unchanged), Is.EqualTo(initialLife), "수치가 큰 경우에도 변하지 않는 수명을 float로 반올림하지 않는다");
                Assert.That(Read(controller.View.PlayerLife) + Read(controller.View.AiLife) + Read(controller.View.Bank), Is.EqualTo(total));
                Assert.That(Read(controller.View.Bank), Is.GreaterThanOrEqualTo(0));
                yield return null;
            }
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator CollectionStillCapsAtLifeAndCounterInspectionReturnsToClockSelection()
        {
            settings.Rules.InitialLifeHours = 11;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 12;
            controller.RestartGame();
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            yield return ClickClock(4);
            var target = GameObject.Find("Devices/IntegratedTimeCounter").GetComponent<TabletopFocusTarget>();
            Assert.That(focus.Focus(target), Is.True);
            yield return ChooseAction(TimeAction.Collect);
            yield return WaitFor(() => controller.View.IsResultVisible);
            Assert.That(controller.Match.BankMinutes, Is.EqualTo(1260));
            Assert.That(controller.Match.TransferredMinutes, Is.EqualTo(60));
            Assert.That(Math.Min(controller.Match.PlayerLifeMinutes, controller.Match.AiLifeMinutes), Is.Zero);
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator FoundationItemsUseSequentiallyThroughKeysBeforeActualClockInput()
        {
            // Setup의 마지막 프레임이 길어도 첫 관찰 중에 동기적으로 주입한다.
            controller.RestartGame();
            int first = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver, ItemTestUse.RecordCurrentEffect);
            int second = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver, ItemTestUse.RecordFutureEffect);
            int unavailable = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Scissors);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            var match = controller.Match;
            yield return PressItemKey(keyboard.tabKey);
            Assert.That(ItemFeedback, Does.Contain("공통 기반 테스트"));
            Assert.That(controller.View.Clocks.All(c => !c.SelectionTarget.InputEnabled && !c.IsOpen), Is.True);
            controller.SelectClock(0); controller.ChooseAction(TimeAction.Collect);
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            yield return PressItemKey(keyboard.iKey, () =>
            {
                // 입력 직후의 보호를 검사한다. 느린 Editor의 다음 프레임에는 짧은 피드백이 끝날 수 있다.
                Assert.That(controller.IsPresentingItem, Is.True);
                controller.UseItem(second); controller.EndItemPhase();
                Assert.That(match.GetInventory(Participant.Player).Select(i => i.Id), Is.EqualTo(new[] { second, unavailable }));
                Assert.That(controller.View.LastItemUse.Value.ItemId, Is.EqualTo(first));
            });
            yield return WaitFor(() => !controller.IsPresentingItem);
            yield return PressItemKey(keyboard.iKey);
            yield return WaitFor(() => !controller.IsPresentingItem);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Has.Count.EqualTo(1));
            Assert.That(match.GetPendingItemEffects(Participant.Player), Has.Count.EqualTo(1));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.BankMinutes, Is.EqualTo(1200));
            Assert.That(controller.View.SelectedTestItemId, Is.Null);
            yield return PressItemKey(keyboard.tabKey);
            Assert.That(ItemFeedback, Does.Contain("제거할 상대 보유품 없음"));
            yield return PressItemKey(keyboard.enterKey);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            controller.UseItem(unavailable);
            Assert.That(match.GetInventory(Participant.Player), Has.Count.EqualTo(1));
            yield return ClickClock(4);
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.Turn == 3 && match.Stage == TurnStage.AwaitingItems);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(match.GetPendingItemEffects(Participant.Player), Has.Count.EqualTo(1));
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.UsedCount, Is.EqualTo(2));
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator AiKeepsInventoryUntilStrategyIntegrationAndEndsAWithoutExposingItsItems()
        {
            settings.Rules.FirstParticipant = Participant.Ai;
            controller.RestartGame();
            int aiItem = TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Handcuffs);
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)).Cast<ItemKind>().Where(kind => kind != ItemKind.Screwdriver && kind != ItemKind.TimeBypass))
                TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, kind);
            yield return WaitFor(() => controller.Match.Turn == 2 && controller.Match.Stage == TurnStage.AwaitingItems);
            var match = controller.Match;
            Assert.That(match.GetInventory(Participant.Ai).Single().Id, Is.EqualTo(aiItem));
            Assert.That(match.GetInventory(Participant.Player), Has.Count.EqualTo(4));
            Assert.That(controller.View.SelectedTestItemId, Is.Null);
            Assert.That(controller.View.LastItemUse, Is.Null);
            Assert.That(controller.View.SelectedItemId, Is.Null, "A 진입은 자동 선택하지 않는다");
            yield return PressItemKey(keyboard.tabKey);
            Assert.That(ItemFeedback, Does.Contain("여벌 열쇠"));
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(2));
            Assert.That(match.GetInventory(Participant.Player), Has.Count.EqualTo(4));
            yield return PressItemKey(keyboard.tabKey);
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(3));
            yield return PressItemKey(keyboard.enterKey);
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.GetInventory(Participant.Ai), Has.Count.EqualTo(1));
            Assert.That(match.GetCurrentItemEffects(Participant.Ai), Is.Empty);
            yield return ClickClock(4);
            Assert.That(match.GetClock(4).Face, Is.Null);
        }

        [UnityTest]
        public IEnumerator RestartAndDisableCancelItemFeedbackAndClearAllItemState()
        {
            foreach (bool disable in new[] { false, true })
            {
                controller.RestartGame();
                int id = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.TimeBypass, ItemTestUse.RecordFutureEffect);
                TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver, ItemTestUse.RecordCurrentEffect);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
                var oldMatch = controller.Match;
                controller.UseItem(id);
                Assert.That(controller.IsPresentingItem, Is.True);
                Assert.That(oldMatch.GetPendingItemEffects(Participant.Player), Has.Count.EqualTo(1));
                if (disable)
                {
                    controller.enabled = false;
                    controller.UseItem(id); controller.EndItemPhase();
                    Assert.That(controller.View.Clocks.All(c => !c.SelectionTarget.InputEnabled), Is.True);
                    Assert.That(controller.IsPresentingItem, Is.False);
                    controller.enabled = true;
                }
                else controller.RestartGame();
                Assert.That(controller.Match, Is.Not.SameAs(oldMatch));
                Assert.That(controller.View.LastItemUse, Is.Null);
                Assert.That(controller.Match.GetInventory(Participant.Player), Is.Empty);
                Assert.That(controller.Match.GetCurrentItemEffects(Participant.Player), Is.Empty);
                Assert.That(controller.Match.GetPendingItemEffects(Participant.Player), Is.Empty);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                yield return new WaitForSecondsRealtime(.2f);
                controller.UseItem(id);
                Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
                Assert.That(controller.Match.UsedCount, Is.Zero);
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
                Assert.That(controller.View.Clocks.All(c => c.SelectionTarget.InputEnabled), Is.True);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator LastTestItemAutoEndsAButClockInputWaitsForFeedback()
        {
            controller.RestartGame();
            int id = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.RubberDuck, ItemTestUse.RecordCurrentEffect);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            controller.UseItem(id);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(controller.IsPresentingItem, Is.True);
            controller.SelectClock(4); controller.EndItemPhase(); controller.UseItem(id);
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
            Assert.That(controller.View.Clocks.All(c => !c.SelectionTarget.InputEnabled), Is.True);
            controller.SkipAnimation();
            yield return WaitFor(() => !controller.IsPresentingItem);
            Assert.That(controller.Match.GetCurrentItemEffects(Participant.Player), Has.Count.EqualTo(1));
            Assert.That(controller.View.Clocks.All(c => c.SelectionTarget.InputEnabled), Is.True);
            yield return ClickClock(4);
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => controller.Match.Turn == 3 && controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            Assert.That(controller.Match.GetInventory(Participant.Player), Is.Empty);
        }

        [UnityTest]
        public IEnumerator RealScrewdriverUsesKeysBlocksDuplicatesAndShowsReducedCounters()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.TransferDuration = .3f;
            settings.ResultHoldDuration = .4f;
            controller.RestartGame();
            var match = controller.Match;
            int driver = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            int spare = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            int other = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Scissors, ItemTestUse.RecordCurrentEffect);
            int red = ReferenceClockIndex(ClockKind.Red);
            yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
            yield return PressItemKey(keyboard.tabKey);
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(driver));
            Assert.That(controller.View.SelectedTestItemId, Is.Null);
            Assert.That(ItemFeedback, Does.Contain("드라이버").And.Not.Contain("실제 효과 없음"));
            yield return PressItemKey(keyboard.iKey);
            Assert.That(match.HasScrewdriverProtection, Is.True);
            Assert.That(match.GetInventory(Participant.Player).Select(i => i.Id), Is.EqualTo(new[] { spare, other }));
            controller.UseItem(spare); controller.EndItemPhase(); controller.SelectClock(red);
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            yield return WaitFor(() => !controller.IsPresentingItem);
            controller.SelectItem(spare);
            controller.UseItem(spare);
            Assert.That(match.CheckItemUse(Participant.Player, spare), Is.EqualTo(ItemUseFailure.AlreadyActive));
            Assert.That(ItemFeedback, Does.Contain("이미 효과가 준비"));
            Assert.That(match.GetInventory(Participant.Player), Has.Count.EqualTo(2));
            yield return PressItemKey(keyboard.tabKey);
            yield return PressItemKey(keyboard.iKey);
            yield return WaitFor(() => !controller.IsPresentingItem);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Has.Count.EqualTo(2));
            yield return PressItemKey(keyboard.enterKey);
            Assert.That(controller.View.PhaseLabel.text, Does.Contain("드라이버 대기 중"));
            yield return ClickClock(red);
            Assert.That(match.GetClock(red).Face, Is.Null);
            yield return ChooseAction(TimeAction.Collect);
            yield return WaitFor(() => controller.View.IsCountingTime);
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(960));
            Assert.That(match.AiLifeMinutes, Is.EqualTo(1200));
            Assert.That(match.BankMinutes, Is.EqualTo(1440));
            Assert.That(match.TransferredMinutes, Is.EqualTo(240));
            Assert.That(match.LastItemEffects.Single().PreventedMinutes, Is.EqualTo(240));
            Assert.That(controller.View.PhaseLabel.text, Does.Contain("경감 04:00 / 실제 손실 04:00"));
            Assert.That(match.HasScrewdriverProtection, Is.False);
            Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            while (controller.View.IsCountingTime)
            {
                Assert.That(Read(controller.View.PlayerLife) + Read(controller.View.AiLife) + Read(controller.View.Bank), Is.EqualTo(3600));
                yield return null;
            }
            AssertNumbers();
            Assert.That(controller.View.PlayerLife.text, Is.EqualTo("16:00"));
            Assert.That(controller.View.Bank.text, Is.EqualTo("24:00"));
        }

        [UnityTest]
        public IEnumerator RealScrewdriverNonTriggersConsumeAndDisplayFeedbackWithoutChangingTransfer()
        {
            settings.Rules.InitialLifeHours = 100;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.TransferDuration = .25f;
            settings.ResultHoldDuration = .4f;
            foreach (var scenario in new[] { 0, 1, 2 })
            {
                controller.RestartGame();
                var match = controller.Match;
                int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
                ClockKind kind = scenario == 2 ? ClockKind.Red : ClockKind.Normal;
                TimeAction action = scenario == 0 ? TimeAction.Collect : TimeAction.Ration;
                int index = ReferenceClockIndex(kind);
                yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
                yield return PressItemKey(keyboard.iKey);
                yield return WaitFor(() => !controller.IsPresentingItem);
                Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
                yield return ClickClock(index);
                yield return ChooseAction(action);
                yield return WaitFor(() => controller.View.IsCountingTime);
                Assert.That(match.GetInventory(Participant.Player), Is.Empty);
                Assert.That(match.HasScrewdriverProtection, Is.False);
                Assert.That(match.LastItemEffects.Single().Source.Id, Is.EqualTo(id));
                Assert.That(match.LastItemEffects.Single().Triggered, Is.False);
                Assert.That(match.LastItemEffects.Single().PreventedMinutes, Is.Zero);
                Assert.That(match.TransferredMinutes, Is.EqualTo(action == TimeAction.Collect ? 480 : 160));
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("드라이버 · 미발동 (소모됨)"));
                yield return WaitFor(() => !controller.View.IsCountingTime);
                AssertNumbers();
                Assert.That(Read(controller.View.PlayerLife) + Read(controller.View.AiLife) + Read(controller.View.Bank), Is.EqualTo(12000));
            }
        }

        [UnityTest]
        public IEnumerator RealScrewdriverSurvivesExcessLossAndRestartOrDisableClearsProtection()
        {
            settings.Rules.InitialLifeHours = 13;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 10;
            settings.TransferDuration = .3f;
            settings.ResultHoldDuration = .4f;
            controller.RestartGame();
            int id = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            controller.UseItem(id);
            yield return WaitFor(() => !controller.IsPresentingItem);
            yield return ClickClock(ReferenceClockIndex(ClockKind.Red));
            yield return ChooseAction(TimeAction.Collect);
            yield return WaitFor(() => controller.View.IsCountingTime);
            Assert.That(controller.Match.PlayerLifeMinutes, Is.EqualTo(90));
            Assert.That(controller.Match.BankMinutes, Is.EqualTo(1290));
            Assert.That(controller.Match.Winner, Is.EqualTo(GameWinner.None));
            Assert.That(controller.Match.LastItemEffects.Single().PreventedMinutes, Is.EqualTo(90));
            Assert.That(controller.View.PhaseLabel.text, Does.Contain("경감 01:30 / 실제 손실 01:30"));
            foreach (bool disable in new[] { false, true })
            {
                controller.RestartGame();
                id = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
                controller.UseItem(id);
                Assert.That(controller.Match.HasScrewdriverProtection, Is.True);
                if (disable) { controller.enabled = false; controller.enabled = true; }
                else controller.RestartGame();
                Assert.That(controller.Match.HasScrewdriverProtection, Is.False);
                Assert.That(controller.Match.LastItemEffects, Is.Empty);
                Assert.That(controller.Match.GetInventory(Participant.Player), Is.Empty);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                Assert.That(controller.Match.UsedCount, Is.Zero);
                Assert.That(controller.IsPresentingItem, Is.False);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator TimeBypassSplitsBothCollectionFacesAndShowsRecipientAndBank()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.TransferDuration = .3f;
            settings.ResultHoldDuration = .4f;
            foreach (var kind in new[] { ClockKind.Normal, ClockKind.Red })
            {
                controller.RestartGame();
                var match = controller.Match;
                int id = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
                int duplicate = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
                yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
                yield return PressItemKey(keyboard.tabKey);
                Assert.That(controller.View.SelectedItemId, Is.EqualTo(id));
                Assert.That(ItemFeedback, Does.Contain("우회선").And.Not.Contain("드라이버"));
                yield return PressItemKey(keyboard.iKey);
                controller.UseItem(duplicate); controller.SelectClock(0); controller.EndItemPhase();
                Assert.That(match.CurrentClock, Is.EqualTo(-1));
                yield return WaitFor(() => !controller.IsPresentingItem);
                controller.SelectItem(duplicate);
                controller.UseItem(duplicate);
                Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(duplicate));
                Assert.That(match.CheckItemUse(Participant.Player, duplicate), Is.EqualTo(ItemUseFailure.AlreadyActive));
                Assert.That(ItemFeedback, Does.Contain("우회선 대기 중").And.Contain("이미 효과가 준비"));
                yield return PressItemKey(keyboard.enterKey);
                yield return ClickClock(ReferenceClockIndex(kind));
                yield return ChooseAction(TimeAction.Collect);
                yield return WaitFor(() => controller.View.IsCountingTime);
                bool normal = kind == ClockKind.Normal;
                Assert.That(match.PlayerLifeMinutes, Is.EqualTo(normal ? 1440 : 720));
                Assert.That(match.AiLifeMinutes, Is.EqualTo(normal ? 720 : 1440));
                Assert.That(match.BankMinutes, Is.EqualTo(1440));
                Assert.That(match.TransferredMinutes, Is.EqualTo(480));
                Assert.That(match.LastTransfers.Count, Is.EqualTo(2));
                Assert.That(match.LastTransfers.Sum(t => t.Minutes), Is.EqualTo(480));
                Assert.That(match.LastItemEffects.Single().BypassedMinutes, Is.EqualTo(240));
                Assert.That(match.HasTimeBypass, Is.False);
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("우회선 · " + (normal ? "나" : "상대") + "에게 04:00 / Bank 04:00"));
                yield return CheckCountingTotal(3600);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator TimeBypassCombinesWithScrewdriverInEitherUseOrder()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.TransferDuration = .3f;
            settings.ResultHoldDuration = .4f;
            foreach (bool bypassFirst in new[] { false, true })
            foreach (var kind in new[] { ClockKind.Normal, ClockKind.Red })
            {
                controller.RestartGame();
                var match = controller.Match;
                TimeTheftItemTestSetup.Give(match, Participant.Player, bypassFirst ? ItemKind.TimeBypass : ItemKind.Screwdriver);
                TimeTheftItemTestSetup.Give(match, Participant.Player, bypassFirst ? ItemKind.Screwdriver : ItemKind.TimeBypass);
                yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
                yield return PressItemKey(keyboard.iKey);
                yield return WaitFor(() => !controller.IsPresentingItem);
                yield return PressItemKey(keyboard.iKey);
                yield return WaitFor(() => !controller.IsPresentingItem);
                Assert.That(match.HasScrewdriverProtection && match.HasTimeBypass, Is.True);
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("드라이버 대기 중").And.Contain("우회선 대기 중"));
                yield return ClickClock(ReferenceClockIndex(kind));
                yield return ChooseAction(TimeAction.Collect);
                yield return WaitFor(() => controller.View.IsCountingTime);
                bool normal = kind == ClockKind.Normal;
                Assert.That(match.PlayerLifeMinutes, Is.EqualTo(normal ? 1440 : 960));
                Assert.That(match.AiLifeMinutes, Is.EqualTo(normal ? 720 : 1320));
                Assert.That(match.BankMinutes, Is.EqualTo(normal ? 1440 : 1320));
                var driver = match.LastItemEffects.Single(e => e.Source.Kind == ItemKind.Screwdriver);
                var bypass = match.LastItemEffects.Single(e => e.Source.Kind == ItemKind.TimeBypass);
                Assert.That(driver.Triggered, Is.EqualTo(!normal));
                Assert.That(driver.PreventedMinutes, Is.EqualTo(normal ? 0 : 240));
                Assert.That(bypass.BypassedMinutes, Is.EqualTo(normal ? 240 : 120));
                Assert.That(bypass.BankReceivedMinutes, Is.EqualTo(normal ? 240 : 120));
                if (!normal) Assert.That(controller.View.PhaseLabel.text,
                    Does.Contain("경감 04:00 / 실제 손실 04:00").And.Contain("상대에게 02:00 / Bank 02:00"));
                Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
                yield return CheckCountingTotal(3600);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator TimeBypassAndScrewdriverRationNonTriggersKeepOriginalPayment()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.TransferDuration = .3f;
            settings.ResultHoldDuration = .4f;
            foreach (var kind in new[] { ClockKind.Normal, ClockKind.Red })
            {
                controller.RestartGame();
                var match = controller.Match;
                TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.TimeBypass);
                TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
                yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
                yield return PressItemKey(keyboard.iKey);
                yield return WaitFor(() => !controller.IsPresentingItem);
                yield return PressItemKey(keyboard.iKey);
                yield return WaitFor(() => !controller.IsPresentingItem);
                yield return ClickClock(ReferenceClockIndex(kind));
                yield return ChooseAction(TimeAction.Ration);
                yield return WaitFor(() => controller.View.IsCountingTime);
                Assert.That(match.LastTransfers.Count, Is.EqualTo(1));
                Assert.That(match.LastTransfers.Single().From, Is.EqualTo(TimeAccount.Bank));
                Assert.That(match.TransferredMinutes, Is.EqualTo(160));
                Assert.That(match.BankMinutes, Is.EqualTo(1040));
                Assert.That(match.PlayerLifeMinutes, Is.EqualTo(kind == ClockKind.Normal ? 1360 : 1200));
                Assert.That(match.AiLifeMinutes, Is.EqualTo(kind == ClockKind.Normal ? 1200 : 1360));
                Assert.That(match.LastItemEffects.All(e => !e.Triggered && e.PreventedMinutes == 0 && e.BypassedMinutes == 0), Is.True);
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("우회선 · 미발동 (소모됨)").And.Contain("드라이버 · 미발동 (소모됨)"));
                Assert.That(match.GetInventory(Participant.Player), Is.Empty);
                yield return CheckCountingTotal(3600);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator TimeBypassRestartAndDisableCancelFeedbackAndClearBothEffects()
        {
            foreach (bool disable in new[] { false, true })
            {
                controller.RestartGame();
                var oldMatch = controller.Match;
                int id = TimeTheftItemTestSetup.Give(oldMatch, Participant.Player, ItemKind.TimeBypass);
                TimeTheftItemTestSetup.Give(oldMatch, Participant.Player, ItemKind.Screwdriver);
                yield return WaitFor(() => oldMatch.Stage == TurnStage.AwaitingItems);
                controller.UseItem(id);
                Assert.That(controller.IsPresentingItem, Is.True);
                Assert.That(oldMatch.HasTimeBypass, Is.True);
                if (disable) { controller.enabled = false; controller.enabled = true; }
                else controller.RestartGame();
                Assert.That(controller.Match, Is.Not.SameAs(oldMatch));
                Assert.That(controller.Match.HasTimeBypass || controller.Match.HasScrewdriverProtection, Is.False);
                Assert.That(controller.Match.LastTransfers, Is.Empty);
                Assert.That(controller.Match.LastItemEffects, Is.Empty);
                Assert.That(controller.Match.GetInventory(Participant.Player), Is.Empty);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                controller.UseItem(id);
                Assert.That(controller.IsPresentingItem, Is.False);
                Assert.That(controller.Match.UsedCount, Is.Zero);
                Assert.That(controller.View.SelectedItemId, Is.Null);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator TimeBypassLargeMinuteSplitKeepsTotalAndNonnegativeCountersEveryFrame()
        {
            settings.Rules.InitialLifeHours = int.MaxValue / (2 * GameTime.MinutesPerHour);
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 7;
            settings.TransferDuration = .4f;
            settings.ResultHoldDuration = .4f;
            foreach (int percent in new[] { 50, 100 })
            {
                settings.Rules.TimeBypassPercent = percent;
                controller.RestartGame();
                int total = controller.Match.PlayerLifeMinutes + controller.Match.AiLifeMinutes + controller.Match.BankMinutes;
                int id = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.TimeBypass);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
                controller.UseItem(id);
                yield return WaitFor(() => !controller.IsPresentingItem);
                yield return ClickClock(ReferenceClockIndex(ClockKind.Red));
                yield return ChooseAction(TimeAction.Collect);
                yield return WaitFor(() => controller.View.IsCountingTime);
                Assert.That(controller.Match.BankMinutes, Is.EqualTo(percent == 50 ? 1410 : 1200));
                yield return CheckCountingTotal(total);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator TimeBypassTerminalCollectionShowsFinalSplitWithoutAwardingBank()
        {
            settings.Rules.InitialLifeHours = 11;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            controller.RestartGame();
            int id = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.TimeBypass);
            // 모델 A 진입 뒤에도 공급 연출이 입력을 잠글 수 있다. 실제 사용 가능 시점에 제출한다.
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems &&
                controller.View.WorldItems.ModelUses.CanUseVisual(Participant.Player, id) && controller.View.Clocks[0].SelectionTarget.InputEnabled);
            controller.UseItem(id);
            yield return WaitFor(() => !controller.IsPresentingItem);
            yield return ClickClock(ReferenceClockIndex(ClockKind.Normal));
            yield return ChooseAction(TimeAction.Collect);
            yield return WaitFor(() => controller.View.IsResultVisible);
            Assert.That(controller.Match.Winner, Is.EqualTo(GameWinner.Player));
            Assert.That(controller.Match.PlayerLifeMinutes, Is.EqualTo(90));
            Assert.That(controller.Match.AiLifeMinutes, Is.Zero);
            Assert.That(controller.Match.BankMinutes, Is.EqualTo(1230));
            Assert.That(controller.Match.HasTimeBypass, Is.False);
            Assert.That(controller.Match.GetCurrentItemEffects(Participant.Player), Is.Empty);
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator SpareKeyPrivateChoiceUsesRealClockInputAndKeepsSettlementItems()
        {
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.TransferDuration = .3f; settings.ResultHoldDuration = .4f;
            foreach (bool reselect in new[] { false, true })
            {
                controller.RestartGame();
                var match = controller.Match;
                foreach (var kind in new[] { ItemKind.SpareKey, ItemKind.Screwdriver, ItemKind.TimeBypass })
                    TimeTheftItemTestSetup.Give(match, Participant.Player, kind);
                yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
                for (int i = 0; i < 3; i++)
                {
                    yield return PressItemKey(keyboard.iKey);
                    yield return WaitFor(() => !controller.IsPresentingItem);
                }
                int red = ReferenceClockIndex(ClockKind.Red);
                yield return ClickClock(red, keyDecision: true);
                Assert.That(match.GetPrivateClock(Participant.Player, red).Value.Kind, Is.EqualTo(ClockKind.Red));
                Assert.That(match.GetPrivateClock(Participant.Ai, red), Is.Null);
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("나만 확인").And.Contain("빨강 8h"));
                controller.ChooseAction(TimeAction.Collect);
                Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
                if (reselect)
                {
                    yield return ClickClock(ReferenceClockIndex(ClockKind.Normal));
                    Assert.That(controller.View.PhaseLabel.text, Does.Not.Contain("나만 확인"));
                    Assert.That(match.GetClock(red).Visibility, Is.EqualTo(ClockVisibility.Hidden));
                }
                else yield return PressItemKey(keyboard.enterKey);
                Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
                Assert.That(match.ReselectPeekedClock(Participant.Player, 4), Is.False);
                yield return ChooseAction(TimeAction.Collect);
                yield return WaitFor(() => controller.View.IsCountingTime);
                Assert.That(match.TransferredMinutes, Is.EqualTo(reselect ? 480 : 240));
                Assert.That(match.LastItemEffects.Single(e => e.Source.Kind == ItemKind.TimeBypass).BypassedMinutes, Is.EqualTo(reselect ? 240 : 120));
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("여벌 열쇠 · 확인 효과 종료"));
                yield return CheckCountingTotal(3600);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator AiSpareKeyKnowledgeNeverAppearsInPlayerView()
        {
            controller.enabled = false;
            var match = new TimeTheftMatch(new GameRules { InitialItemCount = 0, ItemsPerBatch = 0, FirstParticipant = Participant.Ai, MinimumHours = 8, MaximumHours = 8 }, new System.Random(63));
            int key = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.SpareKey);
            match.BeginBatch(); match.CloseAndShuffle(); match.CompleteShuffle();
            Assert.That(match.UseItem(Participant.Ai, key).Succeeded, Is.True);
            Assert.That(match.SelectClock(Participant.Ai, 0), Is.True);
            controller.View.OpenKeyDecision(match);
            Assert.That(controller.View.PhaseLabel.text, Does.Not.Contain("8h").And.Not.Contain("나만 확인"));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen && !c.SelectionTarget.InputEnabled), Is.True);
            Assert.That(match.GetPrivateClock(Participant.Player, 0), Is.Null);
            Assert.That(match.KeepPeekedClock(Participant.Ai), Is.True);
            controller.View.OpenAction(match);
            Assert.That(controller.View.PhaseLabel.text, Does.Not.Contain("8h").And.Not.Contain("나만 확인"));
            Assert.That(controller.View.CollectButton.interactable, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScissorsUiShowsOnlyRemovedItemAndNoTargetDoesNotConsume()
        {
            foreach (bool withTarget in new[] { false, true })
            {
                controller.RestartGame();
                var match = controller.Match;
                int scissors = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Scissors);
                int target = withTarget ? TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Handcuffs) : -1;
                yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
                if (!withTarget)
                {
                    yield return PressItemKey(keyboard.tabKey);
                    Assert.That(controller.View.SelectedItemId, Is.EqualTo(scissors));
                    Assert.That(controller.View.UseItemButton.interactable, Is.False);
                    Assert.That(ItemFeedback, Does.Contain("제거할 상대 보유품 없음"));
                    controller.UseItem(scissors);
                    Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(scissors));
                    Assert.That(controller.View.PhaseLabel.text, Does.Contain("소모 안 됨"));
                    controller.EndItemPhase();
                }
                else
                {
                    yield return PressItemKey(keyboard.iKey);
                    Assert.That(controller.View.LastItemUse.Value.RemovedItem.Value.Id, Is.Zero, "상대 실 ID는 표시 결과에 전달하지 않는다");
                    Assert.That(controller.View.PhaseLabel.text, Does.Contain("수갑").And.Contain("제거 완료"));
                    Assert.That(match.GetInventory(Participant.Ai).Any(item => item.Id == target), Is.False);
                    Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
                    Assert.That(match.GetCurrentItemEffects(Participant.Player), Is.Empty);
                    yield return WaitFor(() => !controller.IsPresentingItem);
                }
                Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
                yield return ClickClock(0);
                yield return ChooseAction(TimeAction.Ration);
                yield return WaitFor(() => match.Turn >= 3);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator DuckWaitsAcrossHandcuffSkipAndBlocksOnlyTheNextActualAiAction()
        {
            settings.AiThinkDuration = .4f;
            controller.RestartGame();
            var match = controller.Match;
            TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs);
            int aiDriver = TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.SpareKey);
            yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
            for (int i = 0; i < 2; i++)
            {
                yield return PressItemKey(keyboard.iKey);
                yield return WaitFor(() => !controller.IsPresentingItem);
            }
            yield return ClickClock(0);
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.Turn == 3 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            Assert.That(match.GetPendingItemEffects(Participant.Player).Single().Source.Kind, Is.EqualTo(ItemKind.RubberDuck));
            Assert.That(controller.View.PhaseLabel.text, Does.Contain("상대 차례 생략"));
            yield return ClickClock(1);
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.IsItemUseBlocked(Participant.Ai));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(controller.View.PhaseLabel.text, Does.Contain("아이템 사용 봉쇄"));
            Assert.That(match.UseItem(Participant.Ai, aiDriver).Failure, Is.EqualTo(ItemUseFailure.UseNotAllowed));
            Assert.That(match.GetInventory(Participant.Ai).Count, Is.EqualTo(2));
            yield return WaitFor(() => match.Turn == 5 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.UsedCount, Is.EqualTo(3));
            Assert.That(match.IsItemUseBlocked(Participant.Ai), Is.False);
            Assert.That(match.GetPendingItemEffects(Participant.Player), Is.Empty);
            AssertNumbers();
            yield return ClickClock(match.GetAvailableClockIndices().First());
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.Stage == TurnStage.BatchPreview && match.Batch == 2);
            Assert.That(match.UsedCount, Is.Zero);
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator AllFourNewItemStatesClearOnRestartAndDisableDuringPrivateChoice()
        {
            foreach (bool disable in new[] { false, true })
            {
                controller.RestartGame();
                var old = controller.Match;
                foreach (var kind in new[] { ItemKind.Scissors, ItemKind.SpareKey, ItemKind.RubberDuck, ItemKind.Handcuffs })
                    TimeTheftItemTestSetup.Give(old, Participant.Player, kind);
                TimeTheftItemTestSetup.Give(old, Participant.Ai, ItemKind.Screwdriver);
                yield return WaitFor(() => old.Stage == TurnStage.AwaitingItems);
                for (int i = 0; i < 4; i++)
                {
                    yield return PressItemKey(keyboard.iKey);
                    yield return WaitFor(() => !controller.IsPresentingItem);
                }
                yield return ClickClock(0, keyDecision: true);
                Assert.That(old.GetPendingItemEffects(Participant.Player).Count, Is.EqualTo(2));
                if (disable) { controller.enabled = false; controller.enabled = true; }
                else controller.RestartGame();
                Assert.That(controller.Match, Is.Not.SameAs(old));
                Assert.That(controller.Match.GetInventory(Participant.Player), Is.Empty);
                Assert.That(controller.Match.GetPendingItemEffects(Participant.Player), Is.Empty);
                Assert.That(controller.Match.GetCurrentItemEffects(Participant.Player), Is.Empty);
                Assert.That(controller.Match.GetPrivateClock(Participant.Player, 0), Is.Null);
                Assert.That(controller.Match.TurnItemEvents, Is.Empty);
                Assert.That(controller.View.PhaseLabel.text, Does.Not.Contain("나만 확인"));
                controller.KeepPeekedClock();
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
                Assert.That(controller.Match.UsedCount, Is.Zero);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator AiPreparedItemsFinishPrivateChoiceAndSkipBeforePlayerCommands()
        {
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.AiThinkDuration = .4f;
            controller.RestartGame();
            var match = controller.Match;
            var ids = new[] { ItemKind.SpareKey, ItemKind.RubberDuck, ItemKind.Handcuffs, ItemKind.Screwdriver, ItemKind.TimeBypass }
                .Select(kind => TimeTheftItemTestSetup.Give(match, Participant.Ai, kind)).ToArray();
            int playerItem = TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Scissors);
            // 전용 fixture가 AI의 A 사용만 준비한다. 제품 ChooseItem 전략은 그대로 null이다.
            controller.StopAllCoroutines();
            controller.View.CancelPresentation(); // 최초 공개를 건너뛰는 fixture도 카메라 소유권을 반납한다.
            Assert.That(match.CloseAndShuffle(), Is.True);
            Assert.That(match.CompleteShuffle(), Is.True);
            foreach (int id in ids) Assert.That(match.UseItem(Participant.Ai, id).Succeeded, Is.True);
            controller.StartCoroutine("OpenTurn");
            yield return WaitFor(() => match.Stage == TurnStage.AwaitingAction);
            Assert.That(match.GetPrivateClock(Participant.Ai, match.CurrentClock).HasValue, Is.True);
            Assert.That(match.GetPrivateClock(Participant.Player, match.CurrentClock), Is.Null);
            Assert.That(controller.View.PhaseLabel.text, Does.Not.Contain("나만 확인").And.Not.Contain("8h"));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen), Is.True);
            yield return WaitFor(() => match.Turn == 3 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.TurnItemEvents.Single().Target, Is.EqualTo(Participant.Player));
            Assert.That(match.GetPendingItemEffects(Participant.Ai).Single().Source.Kind, Is.EqualTo(ItemKind.RubberDuck));
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.False);
            yield return WaitFor(() => match.IsItemUseBlocked(Participant.Player));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(match.UsedCount, Is.EqualTo(2));
            Assert.That(match.CheckItemUse(Participant.Player, playerItem), Is.EqualTo(ItemUseFailure.UseNotAllowed));
            Assert.That(controller.View.PhaseLabel.text, Does.Contain("나 아이템 사용 봉쇄").And.Not.Contain("나만 확인"));
            yield return ClickClock(match.GetAvailableClockIndices().First());
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.Turn == 5 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.False);
            Assert.That(match.GetPendingItemEffects(Participant.Ai), Is.Empty);
            AssertNumbers();
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
        }

        [UnityTest]
        public IEnumerator AutomaticGrantsCarryToNewBatchesRespectCapAndReplayOnRestart()
        {
            EnableAutomaticGrants();
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            foreach (var first in new[] { Participant.Player, Participant.Ai })
            {
                settings.Rules.FirstParticipant = first;
                controller.RestartGame();
                var match = controller.Match;
                var initial = match.GetInventory(Participant.Player).Concat(match.GetInventory(Participant.Ai)).ToArray();
                Assert.That(initial.Length, Is.EqualTo(4));
                Assert.That(initial.All(item => !item.IsFoundationTest && !item.IsConsumed), Is.True);
                Assert.That(initial.Select(item => item.Id).Distinct().Count(), Is.EqualTo(4));
                Assert.That(match.LastItemGrants.Sum(g => g.GrantedItems.Count), Is.EqualTo(4));
                AssertWorldInventory(2, 2);
                Assert.That(controller.View.InventoryLabel.text, Does.Contain("나 +2").And.Not.Contain("나 보유"));
                Assert.That(controller.View.InventoryLabel.raycastTarget, Is.False);
                Assert.That(match.BeginBatch(), Is.False);
                Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2));
                yield return ReachGrantedBatch(2);
                Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(3));
                Assert.That(match.GetInventory(Participant.Ai).Count, Is.EqualTo(3));
                Assert.That(match.LastItemGrants.All(g => g.GrantedItems.Count == 1 && g.Batch == 2), Is.True);
                Assert.That(controller.View.InventoryLabel.text, Does.Contain("나 +1").And.Contain("상대 +1"));
                var cappedIds = match.GetInventory(Participant.Player).Concat(match.GetInventory(Participant.Ai)).Select(item => item.Id).ToArray();
                yield return ReachGrantedBatch(3);
                Assert.That(match.LastItemGrants.All(g => g.GrantedItems.Count == 0 && g.SkippedCount == 1), Is.True);
                Assert.That(match.GetInventory(Participant.Player).Concat(match.GetInventory(Participant.Ai)).Select(item => item.Id), Is.EqualTo(cappedIds));
                Assert.That(controller.View.InventoryLabel.text, Does.Contain("보유 상한"));
                controller.RestartGame();
                Assert.That(controller.Match.Batch, Is.EqualTo(1));
                Assert.That(controller.Match.GetInventory(Participant.Player).Concat(controller.Match.GetInventory(Participant.Ai)), Is.EqualTo(initial));
                Assert.That(controller.Match.LastItemGrants.All(g => g.Batch == 1 && g.GrantedItems.Count == 2), Is.True);
                Assert.That(controller.View.LastItemUse, Is.Null);
                AssertNumbers();
            }
        }

        [UnityTest]
        public IEnumerator AutomaticallyGrantedItemsUseSequentiallyThenRefillWithoutTestInjection()
        {
            EnableAutomaticGrants();
            settings.RandomSeed = 13;
            settings.Rules.InitialLifeHours = 100;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.TransferDuration = .3f; settings.ResultHoldDuration = .4f;
            controller.RestartGame();
            var match = controller.Match;
            Assert.That(match.GetInventory(Participant.Player).Select(item => item.Kind), Is.EquivalentTo(new[] { ItemKind.Screwdriver, ItemKind.TimeBypass }));
            var aiItems = match.GetInventory(Participant.Ai).ToArray();
            yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
            for (int i = 0; i < 2; i++)
            {
                yield return PressItemKey(keyboard.iKey);
                Assert.That(controller.View.InventoryLabel.text, Does.Contain("소모"));
                yield return WaitFor(() => !controller.IsPresentingItem);
                Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(1 - i));
            }
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            AssertWorldInventory(0, 2);
            yield return ClickClock(ReferenceClockIndex(ClockKind.Red));
            yield return ChooseAction(TimeAction.Collect);
            yield return WaitFor(() => controller.View.IsCountingTime);
            Assert.That(match.TransferredMinutes, Is.EqualTo(240));
            Assert.That(match.LastItemEffects.Single(e => e.Source.Kind == ItemKind.TimeBypass).BypassedMinutes, Is.EqualTo(120));
            yield return CheckCountingTotal(12000);
            yield return WaitFor(() => match.Turn == 3 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.GetInventory(Participant.Ai), Is.EqualTo(aiItems));
            Assert.That(match.GetInventory(Participant.Player), Is.Empty);
            yield return ReachGrantedBatch(2);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(1));
            Assert.That(match.GetInventory(Participant.Ai).Count, Is.EqualTo(3));
            AssertWorldInventory(1, 3);
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator AutomaticScissorsRemovalUpdatesPublicInventoryAndRefillCapacity()
        {
            EnableAutomaticGrants();
            settings.RandomSeed = 4;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            controller.RestartGame();
            var match = controller.Match;
            var targetItems = match.GetInventory(Participant.Ai).ToArray();
            var targets = targetItems.Select(item => item.Id).ToArray();
            yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
            Assert.That(match.GetInventory(Participant.Player).First().Kind, Is.EqualTo(ItemKind.Scissors));
            yield return PressItemKey(keyboard.iKey);
            var removed = controller.View.LastItemUse.Value.RemovedItem.Value;
            int removedId = targets.Single(id => match.GetInventory(Participant.Ai).All(item => item.Id != id));
            Assert.That(removed.Id, Is.Zero, "표시 결과에는 상대 실 ID 대신 공개 종류만 전달한다");
            Assert.That(removed.Kind, Is.EqualTo(targetItems.Single(item => item.Id == removedId).Kind));
            AssertWorldInventory(1, 1);
            Assert.That(controller.View.InventoryLabel.text, Does.Contain("제거"));
            yield return WaitFor(() => !controller.IsPresentingItem);
            var remaining = match.GetInventory(Participant.Player).Single();
            Assert.That(match.UseItem(Participant.Player, controller.View.LastItemUse.Value.ItemId).Failure, Is.EqualTo(ItemUseFailure.AlreadyConsumed));
            Assert.That(match.GetInventory(Participant.Player).Single(), Is.EqualTo(remaining));
            yield return ReachGrantedBatch(2);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2));
            Assert.That(match.GetInventory(Participant.Ai).Count, Is.EqualTo(2));
            Assert.That(match.GetInventory(Participant.Ai).Any(item => item.Id == removedId), Is.False);
            AssertWorldInventory(2, 2);
            AssertNumbers();
        }

        [UnityTest]
        public IEnumerator InventoryVisibilitySettingHidesOnlyOpponentKindsAndAiKeepsItsGrant()
        {
            EnableAutomaticGrants();
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.RevealOpponentItems = false;
            settings.Rules.ItemWeights = new[] { 0, 0, 0, 0, 0, 1 };
            controller.RestartGame();
            var match = controller.Match;
            AssertWorldInventory(2, 2);
            Assert.That(controller.View.WorldItems.OpponentItems.All(item => item.ItemId == null && item.Kind == null && item.Label.text == "아이템"), Is.True);
            Assert.That(controller.View.WorldItems.PlayerItems.All(item => item.Kind == ItemKind.Handcuffs && item.Label.text == "수갑"), Is.True);
            var observation = match.ObserveItems(Participant.Ai);
            Assert.That(observation.OpponentInventory.Count, Is.EqualTo(2));
            Assert.That(observation.OpponentInventory.Kinds, Is.Empty);
            Assert.That(observation.OwnItems.All(item => item.Owner == Participant.Ai), Is.True);
            yield return WaitFor(() => match.Turn == 2 && match.Stage == TurnStage.AwaitingItems);
            Assert.That(match.GetInventory(Participant.Ai).Count, Is.EqualTo(2));
            Assert.That(match.GetPendingItemEffects(Participant.Ai), Is.Empty);
            Assert.That(Enumerable.Range(0, 5).All(i => !match.GetClock(i).Face.HasValue), Is.True);
            Assert.That(match.ObserveItems(Participant.Ai).PublicGame, Is.EqualTo(match.Observe(Participant.Ai)));
            AssertNumbers();
        }

        private void EnableAutomaticGrants()
        {
            settings.Rules.InitialItemCount = 2;
            settings.Rules.ItemsPerBatch = 1;
            settings.Rules.MaxHeldItems = 3;
            settings.Rules.RevealOpponentItems = true;
            settings.Rules.ItemWeights = new int[0];
        }

        private IEnumerator ReachGrantedBatch(int targetBatch)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (controller.Match.Batch < targetBatch && Time.realtimeSinceStartup < deadline)
            {
                var match = controller.Match;
                Assert.That(match.Winner, Is.EqualTo(GameWinner.None));
                if (match.Stage == TurnStage.AwaitingItems && match.ActiveParticipant == Participant.Player) controller.EndItemPhase();
                if (match.Stage == TurnStage.AwaitingClock && match.ActiveParticipant == Participant.Player)
                {
                    yield return ClickClock(match.GetAvailableClockIndices().Last());
                    yield return ChooseAction(TimeAction.Ration);
                }
                yield return null;
            }
            Assert.That(controller.Match.Batch, Is.EqualTo(targetBatch));
        }

        [UnityTest]
        public IEnumerator AiStrategyUsesDriverAndBypassThenAppliesTheirRealSettlement()
        {
            EnableItemStrategy();
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.Rules.ExtraRedProbability = 0;
            settings.AiThinkDuration = .4f;
            controller.RestartGame();
            var match = controller.Match;
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Screwdriver);
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.TimeBypass);
            yield return WaitFor(() => match.Stage == TurnStage.AwaitingClock && match.GetInventory(Participant.Ai).Count == 0);
            Assert.That(match.HasScrewdriverProtection && match.HasTimeBypass, Is.True);
            Assert.That(controller.View.LastItemUse.Value.Actor, Is.EqualTo(Participant.Ai));
            Assert.That(controller.View.WorldItems.OpponentItems, Is.Empty);
            Assert.That(controller.View.LastItemUse.Value.ItemId, Is.Zero, "AI 실 ID는 View에 전달하지 않는다");
            // 0.08초 표시 구간 대신 실제 정산 결과를 기다린다. 느린 Editor 프레임에서도 같은 효과를 검증한다.
            yield return WaitFor(() => match.LastItemEffects.Count == 2 && match.LastTransfers.Count == 2);
            Assert.That(match.Action, Is.EqualTo(TimeAction.Collect));
            Assert.That(match.LastItemEffects.Count, Is.EqualTo(2));
            Assert.That(match.LastTransfers.Count, Is.EqualTo(2));
            Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
            Assert.That(match.GetCurrentItemEffects(Participant.Ai), Is.Empty);
        }

        [UnityTest]
        public IEnumerator AiStrategyResolvesItsKeyChoiceWithoutDisplayingPrivateInformation()
        {
            EnableItemStrategy();
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.Rules.ExtraRedProbability = 1;
            settings.AiThinkDuration = .4f;
            controller.RestartGame();
            var match = controller.Match;
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.SpareKey);
            yield return WaitFor(() => match.Stage == TurnStage.AwaitingAction);
            var info = match.ObserveItems(Participant.Ai);
            Assert.That(info.KnownClocks.Count, Is.EqualTo(1));
            Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
            Assert.That(match.ObserveItems(Participant.Player).KnownClocks, Is.Empty);
            Assert.That(controller.View.PhaseLabel.text, Does.Not.Contain("나만 확인").And.Not.Contain("8h"));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen), Is.True);
            if (info.CurrentClock != info.KnownClocks[0].Index) Assert.That(match.GetPrivateClock(Participant.Ai, info.CurrentClock), Is.Null);
            Assert.That(match.ReselectPeekedClock(Participant.Ai, 4), Is.False);
            yield return WaitFor(() => match.UsedCount == 1);
            Assert.That(match.GetCurrentItemEffects(Participant.Ai), Is.Empty);
        }

        [UnityTest]
        public IEnumerator AiStrategyCombinesDuckAndHandcuffsWithoutSpendingDuckOnSkip()
        {
            EnableItemStrategy();
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            settings.Rules.ExtraRedProbability = 0;
            settings.AiThinkDuration = .3f;
            controller.RestartGame();
            var match = controller.Match;
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.RubberDuck);
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.Handcuffs);
            foreach (var kind in new[] { ItemKind.Handcuffs, ItemKind.Screwdriver, ItemKind.TimeBypass })
                TimeTheftItemTestSetup.Give(match, Participant.Player, kind);
            yield return WaitFor(() => match.Turn == 3 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.GetInventory(Participant.Ai), Is.Empty);
            Assert.That(match.GetPendingItemEffects(Participant.Ai).Single().Source.Kind, Is.EqualTo(ItemKind.RubberDuck));
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.False);
            yield return WaitFor(() => match.IsItemUseBlocked(Participant.Player));
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(3));
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Player), new System.Random(1)), Is.Null);
            yield return ClickClock(match.GetAvailableClockIndices().First());
            yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.Turn == 5 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.GetPendingItemEffects(Participant.Ai), Is.Empty);
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.False);
        }

        [UnityTest]
        public IEnumerator ActiveAiStrategyDoesNotUseItemsOnBlockedOrSkippedTurns()
        {
            EnableItemStrategy();
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            settings.AiThinkDuration = .4f;
            controller.RestartGame();
            var match = controller.Match;
            TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.RubberDuck);
            TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs);
            foreach (var kind in new[] { ItemKind.SpareKey, ItemKind.Screwdriver, ItemKind.TimeBypass })
                TimeTheftItemTestSetup.Give(match, Participant.Ai, kind);
            var held = match.GetInventory(Participant.Ai).ToArray();
            yield return WaitFor(() => match.Stage == TurnStage.AwaitingItems);
            for (int i = 0; i < 2; i++)
            {
                yield return PressItemKey(keyboard.iKey);
                yield return WaitFor(() => !controller.IsPresentingItem);
            }
            yield return ClickClock(0); yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.Turn == 3 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.GetInventory(Participant.Ai), Is.EqualTo(held));
            yield return ClickClock(1); yield return ChooseAction(TimeAction.Ration);
            yield return WaitFor(() => match.IsItemUseBlocked(Participant.Ai));
            Assert.That(match.ObserveItems(Participant.Ai).UsableItemIds, Is.Empty);
            Assert.That(TimeTheftAi.ChooseItem(match.ObserveItems(Participant.Ai), new System.Random(1)), Is.Null);
            Assert.That(match.GetInventory(Participant.Ai), Is.EqualTo(held));
            yield return WaitFor(() => match.Turn == 5 && match.Stage == TurnStage.AwaitingClock);
            Assert.That(match.GetInventory(Participant.Ai), Is.EqualTo(held));
        }

        [UnityTest]
        public IEnumerator RestartAndDisableCancelAiStrategyDuringItemPresentation()
        {
            EnableItemStrategy(); EnableAutomaticGrants();
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.ItemWeights = new[] { 0, 0, 0, 0, 0, 1 };
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            settings.Rules.ExtraRedProbability = 0;
            foreach (bool disable in new[] { false, true })
            {
                controller.enabled = true;
                controller.RestartGame();
                var old = controller.Match;
                yield return WaitFor(() => controller.IsPresentingItem && controller.View.LastItemUse.HasValue);
                Assert.That(controller.View.LastItemUse.Value.Actor, Is.EqualTo(Participant.Ai));
                int oldCount = old.GetInventory(Participant.Ai).Count;
                Assert.That(oldCount, Is.EqualTo(1));
                if (disable) { controller.enabled = false; controller.enabled = true; }
                else controller.RestartGame();
                Assert.That(controller.Match, Is.Not.SameAs(old));
                Assert.That(controller.IsPresentingItem, Is.False);
                Assert.That(controller.Match.GetInventory(Participant.Ai).Count, Is.EqualTo(2));
                Assert.That(controller.Match.GetPendingItemEffects(Participant.Ai), Is.Empty);
                controller.enabled = false;
                yield return null;
                Assert.That(old.GetInventory(Participant.Ai).Count, Is.EqualTo(oldCount));
            }
        }

        [UnityTest]
        public IEnumerator MainGameWithAutomaticGrantsAndActiveAiStrategyFinishes()
        {
            EnableAutomaticGrants();
            settings.Rules.EnableAiItemStrategy = true;
            settings.RandomSeed = 7;
            controller.RestartGame();
            bool sawAiUse = false;
            float deadline = Time.realtimeSinceStartup + 90;
            while (!controller.View.IsResultVisible && Time.realtimeSinceStartup < deadline)
            {
                var match = controller.Match;
                if (controller.View.LastItemUse.HasValue && controller.View.LastItemUse.Value.Actor == Participant.Ai)
                    sawAiUse = true;
                if (match.ActiveParticipant == Participant.Player && match.Stage == TurnStage.AwaitingItems) controller.EndItemPhase();
                if (match.ActiveParticipant == Participant.Player && match.Stage == TurnStage.AwaitingClock)
                {
                    yield return ClickClock(match.GetAvailableClockIndices().Last());
                    yield return ChooseAction(TimeAction.Collect);
                }
                Assert.That(match.PlayerLifeMinutes + match.AiLifeMinutes + match.BankMinutes, Is.EqualTo(3600));
                yield return null;
            }
            Assert.That(controller.View.IsResultVisible, Is.True);
            Assert.That(sawAiUse, Is.True);
            Assert.That(controller.Match.GetPendingItemEffects(Participant.Ai), Is.Empty);
            AssertNumbers();
        }

        private void EnableItemStrategy()
        {
            settings.Rules.EnableAiItemStrategy = true;
            settings.Rules.AiDecisionNoise = 0;
            settings.Rules.AiItemConservation = 0;
        }

        private void AssertWorldInventory(int player, int opponent)
        {
            Assert.That(controller.View.WorldItems.PlayerItems.Count, Is.EqualTo(player));
            Assert.That(controller.View.WorldItems.OpponentItems.Count, Is.EqualTo(opponent));
            Assert.That(controller.View.WorldItems.PlayerItems.Select(item => item.ItemId.Value),
                Is.EquivalentTo(controller.Match.GetInventory(Participant.Player).Select(item => item.Id)));
            Assert.That(controller.View.WorldItems.OpponentItems.All(item => !item.ItemId.HasValue), Is.True);
        }

        private IEnumerator CheckCountingTotal(int total)
        {
            while (controller.View.IsCountingTime)
            {
                int player = Read(controller.View.PlayerLife), ai = Read(controller.View.AiLife), bank = Read(controller.View.Bank);
                Assert.That(player + ai + bank, Is.EqualTo(total));
                Assert.That(player, Is.GreaterThanOrEqualTo(0));
                Assert.That(ai, Is.GreaterThanOrEqualTo(0));
                Assert.That(bank, Is.GreaterThanOrEqualTo(0));
                yield return null;
            }
        }

        private int ReferenceClockIndex(ClockKind kind)
        {
            // 재현용 독립 모델은 테스트 오라클이다. 게임/AI의 비공개 API를 넓히지 않는다.
            for (int i = 0; i < 5; i++)
            {
                var reference = new TimeTheftMatch(settings.Rules, new System.Random(settings.RandomSeed));
                reference.BeginBatch(); reference.CloseAndShuffle(); reference.CompleteShuffle();
                if (reference.Stage == TurnStage.AwaitingItems) reference.EndItemPhase(settings.Rules.FirstParticipant);
                reference.SelectClock(settings.Rules.FirstParticipant, i);
                reference.CommitAction(settings.Rules.FirstParticipant, TimeAction.Collect);
                if (reference.GetClock(i).Face.Value.Kind == kind) return i;
            }
            throw new InvalidOperationException("종류별 시계가 보장되어야 합니다.");
        }

        private IEnumerator PressItemKey(UnityEngine.InputSystem.Controls.KeyControl key, Action afterSubmit = null)
        {
            // 마우스 UX는 사용 후 선택을 비운다. 보조 I 경로도 실제 Tab으로 다시 선택한다.
            if (key == keyboard.iKey && !controller.View.SelectedItemId.HasValue)
            {
                input.Press(keyboard.tabKey); InputSystem.Update(); controller.SendMessage("Update");
                input.Release(keyboard.tabKey); InputSystem.Update();
                yield return null;
            }
            input.Press(key); InputSystem.Update(); controller.SendMessage("Update");
            afterSubmit?.Invoke();
            input.Release(key); InputSystem.Update();
            yield return null;
        }

        private IEnumerator ChooseAction(TimeAction action)
        {
            var match = controller.Match;
            int selected = match.CurrentClock;
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            Assert.That(selected, Is.InRange(0, 4));
            yield return GameUiTestInput.Click(input, mouse,
                action == TimeAction.Ration ? controller.View.RationButton : controller.View.CollectButton, null, () =>
                {
                    Assert.That(match.Stage, Is.EqualTo(TurnStage.Revealing));
                    Assert.That(match.CurrentClock, Is.EqualTo(selected));
                    AssertRevealed(selected);
                    controller.View.RationButton.onClick.Invoke();
                    controller.View.CollectButton.onClick.Invoke();
                    Assert.That(match.Action, Is.EqualTo(action));
                });
        }

        private IEnumerator ClickClock(int index, Action afterPress = null, bool keyDecision = false)
        {
            yield return WaitFor(() => !focus.IsTransitioning);
            var token = controller.View.Clocks[index];
            var target = token.SelectionTarget;
            Assert.That(target.IsAvailable && target.InputEnabled, Is.True, "선택 가능한 시계 " + index);
            var collider = token.GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            for (int frame = 0; frame < 10; frame++)
            {
                mouse.MakeCurrent();
                input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(collider.bounds.center));
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(target), "실제 Collider 레이캐스트로 클릭한 시계를 선택");
            input.Press(mouse.leftButton);
            yield return null;
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(index));
            Assert.That(controller.Match.Stage, Is.EqualTo(keyDecision ? TurnStage.AwaitingKeyDecision : TurnStage.AwaitingAction));
            Assert.That(controller.View.Clocks.All(c => !c.IsOpen && c.Number.text == "" && c.Kind.text == ""), Is.True);
            Assert.That(Enumerable.Range(0, 5).All(i => !controller.Match.GetClock(i).Face.HasValue), Is.True);
            Assert.That(controller.View.Clocks.Count(c => c.IsSelected), Is.EqualTo(1));
            Assert.That(token.IsSelected, Is.True);
            Assert.That(token.transform.Find("Visual").localPosition.z, Is.LessThan(0));
            yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(controller.View.CollectButton.interactable && controller.View.RationButton.interactable, Is.EqualTo(!keyDecision));
            afterPress?.Invoke();
            input.Release(mouse.leftButton);
            yield return null;
        }

        private void AssertRevealed(int index)
        {
            var match = controller.Match;
            var face = match.GetClock(index).Face;
            Assert.That(face.HasValue, Is.True);
            Assert.That(controller.View.Clocks[index].IsOpen, Is.True);
            Assert.That(controller.View.Clocks[index].Number.text, Is.EqualTo(face.Value.Hours + "h"));
            Assert.That(controller.View.Clocks[index].Kind.text, Is.EqualTo(face.Value.Kind == ClockKind.Red ? "빨강" : "일반"));
            Assert.That(Enumerable.Range(0, 5).Where(i => i != index).All(i => !match.GetClock(i).Face.HasValue && !controller.View.Clocks[i].IsOpen), Is.True);
        }
        private void AssertNumbers()
        {
            Assert.That(Read(controller.View.PlayerLife), Is.EqualTo(controller.Match.PlayerLifeMinutes));
            Assert.That(Read(controller.View.AiLife), Is.EqualTo(controller.Match.AiLifeMinutes));
            Assert.That(Read(controller.View.Bank), Is.EqualTo(controller.Match.BankMinutes));
            Assert.That(controller.View.PlayerLife.color, Is.EqualTo(playerColor));
            Assert.That(controller.View.AiLife.color, Is.EqualTo(aiColor));
        }
        private static int Read(TextMeshPro label)
        {
            var parts = label.text.Split(':');
            return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
        }
        private IEnumerator WaitFor(Func<bool> predicate)
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "진행 제한시간 · " + (controller && controller.Match != null
                ? controller.Match.Stage + " / turn " + controller.Match.Turn + " / " + controller.Match.ActiveParticipant : "Controller 없음"));
        }
    }
}
#endif
