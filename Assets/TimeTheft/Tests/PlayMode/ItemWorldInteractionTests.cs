#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class ItemWorldInteractionTests
    {
        private InputTestFixture input;
        private Mouse mouse;
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private Camera camera;
        private TabletopFocusCamera focus;
        private float oldTimeScale;
        private double lastWorldPressTime;
        private ItemWorldInventoryView World => controller.View.WorldItems;

        [UnitySetUp]
        public IEnumerator LoadMainScene()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("ItemWorldMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            GameUiTestInput.ShortenBatchReveal(controller);
            settings = UnityEngine.Object.Instantiate(controller.Settings);
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.EnableAiItemStrategy = false;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.RandomSeed = 635;
            settings.PreviewDuration = .2f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = settings.DiscardDuration = .08f;
            settings.AiThinkDuration = .4f;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            camera = Camera.main;
            focus = controller.View.FocusCamera;
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            Time.timeScale = 0;
            controller.RestartGame();
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
        public IEnumerator AllSixKoreanNamesAndDuplicateIdsUseWorldObjectsBeyondThreeSlots()
        {
            // UnitySetUp 이후 첫 프레임 길이에 의존하지 않고 관찰 시작과 주입을 같은 프레임에 수행한다.
            controller.RestartGame();
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
                TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, kind);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Screwdriver);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Screwdriver);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            Assert.That(World.PlayerItems.Count, Is.EqualTo(7), "잠정 상한 3개를 표시 코드에 고정하지 않는다");
            Assert.That(World.OpponentItems.Count, Is.EqualTo(2));
            Assert.That(World.PlayerItems.Select(i => i.ItemId.Value), Is.EquivalentTo(controller.Match.GetInventory(Participant.Player).Select(i => i.Id)));
            Assert.That(World.PlayerItems.Select(i => i.Label.text).Distinct(), Is.EquivalentTo(new[] { "여벌 열쇠", "드라이버", "우회선", "가위", "고무 오리", "수갑" }));
            Assert.That(World.PlayerItems.Select(i => i.transform.localPosition.x).Distinct().Count(), Is.EqualTo(7));
            Assert.That(World.PlayerItems.Average(i => i.transform.localPosition.x), Is.EqualTo(0).Within(.001));
            var interfaceFont = Resources.Load<TMPro.TMP_FontAsset>("TimeTheftUi");
            Assert.That(interfaceFont, Is.Not.Null, "현재 UI 전용 폰트를 사용한다");
            Assert.That(interfaceFont.atlasPopulationMode, Is.EqualTo(TMPro.AtlasPopulationMode.Static), "플레이 도중 기존 폰트 캐시를 쓰지 않는다");
            foreach (var item in World.PlayerItems.Concat(World.OpponentItems))
            {
                Assert.That(item.Label.font, Is.SameAs(interfaceFont));
                Assert.That(item.Label.raycastTarget, Is.False);
                Assert.That(item.Target.FocusOnClick, Is.False);
                Assert.That(item.GetComponent<Collider>().enabled, Is.True);
            }
            Assert.That(World.OpponentItems.All(i => i.Kind == ItemKind.Screwdriver && i.ItemId == null), Is.True);
            Assert.That(controller.View.InventoryLabel.text, Does.Not.Contain("나 보유").And.Not.Contain("상대 보유"));

            // 새 설명의 동적 글리프가 추가돼도 이미 보이던 상단 '내'의 GPU atlas 내용이 지워지지 않아야 한다.
            // CPU 문자표/메시가 정상인 채 화면 글자만 사라졌던 회귀를 실제 GPU 텍스처 읽기로 검사한다.
            AssertPhaseGlyphStillExistsOnGpu("초기 아이템 단계");
            foreach (var item in World.PlayerItems.Take(6))
            {
                controller.SelectItem(item.ItemId.Value);
                yield return null;
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("내 차례"));
                AssertPhaseGlyphStillExistsOnGpu(item.Label.text + " 설명 선택 후");
                Canvas.ForceUpdateCanvases();
                var card = (RectTransform)controller.View.ItemDetailLabel.transform.parent;
                foreach (var text in card.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
                {
                    if (!text.gameObject.activeInHierarchy) continue;
                    var preferred = text.GetPreferredValues(text.text, text.rectTransform.rect.width, 0);
                    Assert.That(preferred.y, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1), item.Label.text + " / " + text.name + " 설명이 프레임 안에 들어온다");
                    Assert.That(text.raycastTarget, Is.False);
                }
                var corners = new Vector3[4]; card.GetWorldCorners(corners);
                Assert.That(corners.All(p => camera.pixelRect.Contains(RectTransformUtility.WorldToScreenPoint(null, p))), Is.True, "긴 설명 카드도 화면 경계를 벗어나지 않는다");
            }
        }

        [UnityTest]
        public IEnumerator ActualMouseHoverSelectConfirmAndSequentialUseReachClockAndActionWithoutKeyboard()
        {
            settings.Rules.ScrewdriverReductionPercent = 37;
            settings.Rules.TimeBypassPercent = 29;
            controller.RestartGame();
            int driver = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
            int bypass = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.TimeBypass);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            Assert.That(controller.View.SelectedItemId, Is.Null);
            var first = World.PlayerItems.Single(i => i.ItemId == driver);
            Physics.SyncTransforms();
            Vector3 colliderPosition = first.GetComponent<Collider>().bounds.center;
            yield return Hover(first);
            Assert.That(first.Target.IsHighlighted, Is.True);
            Assert.That(ItemCardText("Name"), Is.EqualTo("드라이버"));
            Assert.That(controller.View.ItemDetailLabel.text, Is.Empty, "Hover는 이름과 짧은 상태만 표시한다");
            Assert.That(controller.View.ItemDetailLabel.gameObject.activeSelf, Is.False);
            Assert.That(ItemCardText("State"), Does.Contain("클릭해 선택"));
            Assert.That(controller.View.SelectedItemId, Is.Null);
            yield return ClickItem(first);
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(driver));
            Assert.That(first.IsSelected, Is.True);
            Assert.That(controller.View.ItemDetailLabel.gameObject.activeSelf, Is.True);
            Assert.That(controller.View.ItemDetailLabel.text, Does.Contain("37"), "첫 선택 뒤 상세 효과를 표시한다");
            Assert.That(first.GetComponent<Collider>().bounds.center, Is.EqualTo(colliderPosition));
            Assert.That(controller.Match.GetInventory(Participant.Player).Count, Is.EqualTo(2), "선택 클릭만으로 소모하지 않는다");
            AssertLegacyItemButtonsHidden();
            yield return ConfirmSelectedItem(first);
            Assert.That(controller.Match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(bypass));
            Assert.That(World.PlayerItems.Single().ItemId, Is.EqualTo(bypass));
            yield return WaitFor(() => !controller.IsPresentingItem);
            Assert.That(controller.View.SelectedItemId, Is.Null, "소모 뒤 다른 물건이 자동 선택되지 않는다");
            AssertLegacyItemButtonsHidden();
            yield return ClickItem(World.PlayerItems.Single());
            Assert.That(ItemCardText("Name"), Is.EqualTo("우회선"));
            Assert.That(controller.View.ItemDetailLabel.text, Does.Contain("29"));
            yield return ConfirmSelectedItem(World.PlayerItems.Single());
            yield return WaitFor(() => !controller.IsPresentingItem);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(World.PlayerItems, Is.Empty);
            Assert.That(controller.Match.HasScrewdriverProtection && controller.Match.HasTimeBypass, Is.True);
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1), "아이템 확정 클릭으로 시계를 함께 선택하지 않는다");
            yield return ClickClock(0, TurnStage.AwaitingAction);
            yield return ClickButton(controller.View.RationButton,
                () => Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.Revealing)));
        }

        [UnityTest]
        public IEnumerator MouseEndPhaseLeavesInventoryAndBlocksItemUseAfterB()
        {
            controller.RestartGame();
            int id = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            Assert.That(controller.View.SelectedItemId, Is.Null);
            AssertLegacyItemButtonsHidden();
            yield return ClickClock(4, TurnStage.AwaitingAction);
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(4), "아이템 미선택 상태의 시계 클릭 하나로 A 종료와 선택을 함께 처리한다");
            Assert.That(controller.View.SelectedItemId, Is.Null);
            controller.SelectItem(id); controller.UseSelectedItem();
            Assert.That(controller.Match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(id));
            yield return ClickCollider(World.PlayerItems.Single().GetComponent<Collider>());
            Assert.That(controller.View.SelectedItemId, Is.Null, "B 이후 보유품 클릭으로 A에 복귀하지 않는다");
            Assert.That(controller.Match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(id));
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(4));
            yield return ClickButton(controller.View.CollectButton,
                () => Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.Revealing)));
        }

        [UnityTest]
        public IEnumerator SpareKeyKeepAndReselectAreBothAvailableThroughMouse()
        {
            foreach (bool reselect in new[] { false, true })
            {
                controller.RestartGame();
                TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
                yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
                yield return ClickItem(World.PlayerItems.Single());
                yield return ConfirmSelectedItem(World.PlayerItems.Single());
                yield return WaitFor(() => !controller.IsPresentingItem);
                yield return ClickClock(0, TurnStage.AwaitingKeyDecision);
                Assert.That(controller.View.PhaseLabel.text, Does.Contain("나만 확인"));
                Assert.That(controller.Match.GetPrivateClock(Participant.Player, 0).HasValue, Is.True);
                Assert.That(controller.Match.GetPrivateClock(Participant.Ai, 0).HasValue, Is.False);
                if (reselect) yield return ClickClock(1, TurnStage.AwaitingAction);
                else yield return ClickButton(controller.View.KeepClockButton);
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
                Assert.That(controller.Match.CurrentClock, Is.EqualTo(reselect ? 1 : 0));
                Assert.That(controller.View.Clocks.All(i => !i.IsOpen), Is.True);
                Assert.That(controller.View.KeepClockButton.gameObject.activeInHierarchy, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator MouseScissorsRemovalRefreshesBothSidesAndNoTargetFailureDoesNotConsume()
        {
            controller.RestartGame();
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Scissors);
            int second = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Scissors);
            int removed = TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Handcuffs);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            yield return ClickItem(World.PlayerItems.First());
            yield return ConfirmSelectedItem(World.PlayerItems.First());
            Assert.That(World.PlayerItems.Single().ItemId, Is.EqualTo(second));
            Assert.That(World.OpponentItems, Is.Empty);
            Assert.That(controller.Match.GetInventory(Participant.Ai).Any(i => i.Id == removed), Is.False);
            yield return WaitFor(() => !controller.IsPresentingItem);
            yield return ClickItem(World.PlayerItems.Single());
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(second));
            AssertLegacyItemButtonsHidden();
            Assert.That(ItemCardText("State"), Does.Contain("없음"));
            yield return ConfirmSelectedItem(World.PlayerItems.Single());
            Assert.That(controller.Match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(second));
            Assert.That(World.PlayerItems.Single().ItemId, Is.EqualTo(second));
            Assert.That(controller.View.InventoryLabel.text, Does.Contain("실패"));
        }

        [UnityTest]
        public IEnumerator HiddenOpponentObjectsAndHoverExposeOnlyCount()
        {
            settings.Rules.RevealOpponentItems = false;
            controller.RestartGame();
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Screwdriver);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Handcuffs);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            Assert.That(World.OpponentItems.Count, Is.EqualTo(2));
            Assert.That(World.OpponentItems.All(i => !i.Kind.HasValue && !i.ItemId.HasValue && i.Label.text == "아이템"), Is.True);
            var first = World.OpponentItems[0];
            var second = World.OpponentItems[1];
            Assert.That(first.transform.localScale, Is.EqualTo(second.transform.localScale));
            yield return Hover(first);
            Assert.That(ItemCardText("State"), Does.Contain("비공개").And.Not.Contain("드라이버").And.Not.Contain("수갑"));
            Assert.That(controller.View.ItemDetailLabel.text, Is.Empty);
            Assert.That(controller.View.ItemDetailLabel.gameObject.activeSelf, Is.False);
            yield return ClickItem(first);
            Assert.That(controller.View.SelectedItemId, Is.Null, "상대품은 클릭해도 사용 선택할 수 없다");
            Assert.That(controller.Match.GetInventory(Participant.Ai).Count, Is.EqualTo(2));
            AssertLegacyItemButtonsHidden();
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            InputSystem.Update(); focus.SendMessage("Update");
            yield return null;
            var a = new MaterialPropertyBlock();
            var b = new MaterialPropertyBlock();
            first.GetComponentInChildren<Renderer>().GetPropertyBlock(a);
            second.GetComponentInChildren<Renderer>().GetPropertyBlock(b);
            Assert.That(a.GetColor("_BaseColor"), Is.EqualTo(b.GetColor("_BaseColor")), "비공개품은 종류별 색도 숨긴다");
        }

        [UnityTest]
        public IEnumerator RestartClearsSelectionAndOldBindingsEvenWhenIdsAreReused()
        {
            controller.RestartGame();
            int oldId = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            var old = World.PlayerItems.Single();
            yield return ClickItem(old);
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(oldId));
            settings.Rules.InitialItemCount = 1;
            settings.Rules.ItemWeights = new[] { 0, 1, 0, 0, 0, 0 };
            controller.RestartGame();
            Assert.That(controller.View.SelectedItemId, Is.Null);
            Assert.That(old.gameObject.activeSelf, Is.False);
            old.Target.Activate();
            Assert.That(controller.View.SelectedItemId, Is.Null);
            Assert.That(World.PlayerItems.Single().ItemId, Is.EqualTo(oldId), "새 Match가 ID를 재사용해도 선택은 연결되지 않는다");
            Assert.That(World.PlayerItems.Single(), Is.Not.SameAs(old));
            Assert.That(World.PlayerItems.Single().Kind, Is.EqualTo(ItemKind.Screwdriver));
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            Assert.That(controller.Match.GetInventory(Participant.Player).Count, Is.EqualTo(1));
            AssertLegacyItemButtonsHidden();
            yield return ClickItem(World.PlayerItems.Single());
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(oldId));
            Assert.That(controller.Match.GetInventory(Participant.Player).Count, Is.EqualTo(1), "이전 ID와 클릭 시간이 남아 새 아이템의 첫 클릭을 확정하지 않는다");
            Assert.That(controller.Match.HasScrewdriverProtection, Is.False);
        }

        [UnityTest]
        public IEnumerator CameraParallaxKeepsActualVisibleItemClickAccurateAndRootColliderStable()
        {
            controller.RestartGame();
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            int driver = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            Vector3 original = camera.transform.position;
            input.Set(mouse.position, camera.pixelRect.max - Vector2.one * 5);
            InputSystem.Update();
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(Vector3.Distance(camera.transform.position, original), Is.GreaterThan(.001f));
            var target = World.PlayerItems.Single(i => i.ItemId == driver);
            Physics.SyncTransforms();
            Vector3 root = target.transform.position;
            Vector3 collider = target.GetComponent<Collider>().bounds.center;
            yield return ClickItem(target);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(driver));
            Assert.That(target.transform.position, Is.EqualTo(root));
            Assert.That(target.GetComponent<Collider>().bounds.center, Is.EqualTo(collider));
            Assert.That(target.transform.Find("Visual").localPosition.y, Is.GreaterThan(0));
            Assert.That(focus.HasCameraControl, Is.False, "아이템 클릭으로 카메라 Focus에 들어가지 않는다");
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator MouseCancelClearsOnlyUnconsumedSelectionAndCannotRefundUse()
        {
            controller.RestartGame();
            int driver = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
            int key = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            var match = controller.Match;
            yield return ClickItem(World.PlayerItems.Single(i => i.ItemId == driver));
            yield return RightClick();
            Assert.That(controller.View.SelectedItemId, Is.Null);
            Assert.That(World.PlayerItems.All(i => !i.IsSelected), Is.True);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            AssertLegacyItemButtonsHidden();
            yield return ClickItem(World.PlayerItems.Single(i => i.ItemId == driver));
            yield return ClickEmptyTable();
            Assert.That(controller.View.SelectedItemId, Is.Null, "실제 빈 테이블 클릭으로 미사용 선택을 취소한다");
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2));
            yield return ClickItem(World.PlayerItems.Single(i => i.ItemId == driver));
            yield return ConfirmSelectedItem(World.PlayerItems.Single(i => i.ItemId == driver));
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(key));
            yield return WaitFor(() => !controller.IsPresentingItem);
            yield return RightClick();
            yield return ClickEmptyTable();
            Assert.That(match.HasScrewdriverProtection, Is.True);
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(key));
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            yield return ClickClock(0, TurnStage.AwaitingAction);
            AssertLegacyItemButtonsHidden();
        }

        [UnityTest]
        public IEnumerator SelectedItemBlocksClockEntryUntilCancelledAndAnotherItemOnlySwitchesSelection()
        {
            controller.RestartGame();
            int driver = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
            int key = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            var match = controller.Match;
            yield return ClickItem(World.PlayerItems.Single(i => i.ItemId == driver));
            yield return ClickCollider(controller.View.Clocks[0].GetComponent<Collider>());
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(driver), "선택 중 시계 클릭은 A와 선택을 보존한다");
            Assert.That(World.PlayerItems.Single(i => i.ItemId == driver).IsSelected, Is.True);
            yield return ClickItem(World.PlayerItems.Single(i => i.ItemId == key));
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(key));
            Assert.That(World.PlayerItems.Single(i => i.ItemId == driver).IsSelected, Is.False);
            Assert.That(World.PlayerItems.Single(i => i.ItemId == key).IsSelected, Is.True);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2), "다른 아이템 클릭은 사용이 아닌 선택 전환이다");
            yield return ClickCollider(controller.View.Clocks[1].GetComponent<Collider>());
            Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(key));
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2));
            yield return RightClick();
            Assert.That(controller.View.SelectedItemId, Is.Null);
            yield return ClickClock(1, TurnStage.AwaitingAction);
            Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2));
            Assert.That(match.GetClock(1).Face, Is.Null, "미사용 열쇠를 보유했을 뿐인 시계 선택은 정보를 공개하지 않는다");
        }

        [UnityTest]
        public IEnumerator RapidItemClicksRefreshTheConfirmDelayAndOnlyASeparatedClickConsumesOnce()
        {
            controller.RestartGame();
            int driver = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
            int key = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            var match = controller.Match;
            yield return Hover(World.PlayerItems.Single(i => i.ItemId == driver));
            yield return ClickCurrentPointer();
            double first = lastWorldPressTime;
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(driver));
            for (int click = 0; click < 2; click++)
            {
                double previous = lastWorldPressTime;
                yield return new WaitForSecondsRealtime(.18f);
                yield return ClickCurrentPointer();
                if (lastWorldPressTime - previous >= .32)
                    Assert.Inconclusive("Editor 프레임 지연으로 실제 클릭 간격이 0.32초 이상이 되어 연타 검증을 재실행해야 합니다.");
                Assert.That(match.GetInventory(Participant.Player).Count, Is.EqualTo(2), "빠른 클릭을 사용으로 처리하지 않는다");
                Assert.That(controller.View.SelectedItemId, Is.EqualTo(driver));
                Assert.That(match.HasScrewdriverProtection, Is.False);
            }
            Assert.That(lastWorldPressTime - first, Is.GreaterThanOrEqualTo(.32), "첫 클릭부터는 0.32초가 지나도 마지막 연타 기준으로 계속 막아야 한다");
            yield return new WaitForSecondsRealtime(.36f);
            yield return ClickCurrentPointer();
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(key));
            Assert.That(match.HasScrewdriverProtection, Is.True);
            yield return ClickCurrentPointer();
            yield return WaitFor(() => !controller.IsPresentingItem);
            Assert.That(match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(key));
            Assert.That(controller.View.SelectedItemId, Is.Null, "소모 중/직후 클릭이 다음 보유품 선택으로 예약되지 않는다");
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator HoldingTheSelectionClickNeverConfirmsWithoutANewMousePress()
        {
            controller.RestartGame();
            int driver = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            yield return Hover(World.PlayerItems.Single(i => i.ItemId == driver));
            input.Press(mouse.leftButton); ProcessPointer();
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(driver));
            yield return new WaitForSecondsRealtime(.38f);
            ProcessPointer();
            Assert.That(controller.Match.GetInventory(Participant.Player).Count, Is.EqualTo(2), "누른 상태를 유지해도 두 번째 클릭이 생성되지 않는다");
            Assert.That(controller.Match.HasScrewdriverProtection, Is.False);
            input.Release(mouse.leftButton); ProcessPointer(); yield return null;
            yield return ClickCurrentPointer();
            Assert.That(controller.Match.GetInventory(Participant.Player).Count, Is.EqualTo(1));
            Assert.That(controller.Match.HasScrewdriverProtection, Is.True);
        }

        [UnityTest]
        public IEnumerator MouseOverviewWorksWhileOptionalHudIsHiddenWithoutChangingTheTurn()
        {
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingClock);
            var match = controller.Match;
            var counter = GameObject.Find("Devices/IntegratedTimeCounter").GetComponent<TabletopFocusTarget>();
            Assert.That(focus.Focus(counter), Is.True);
            yield return WaitFor(() => !focus.IsTransitioning);
            yield return ClickButton(controller.View.OverviewButton);
            yield return WaitFor(() => !focus.HasCameraControl);
            Assert.That(controller.View.OverviewButton.gameObject.activeSelf, Is.False);
            Assert.That(focus.Focus(counter), Is.True);
            yield return WaitFor(() => !focus.IsTransitioning);
            Assert.That(controller.View.HelpButton.gameObject.activeInHierarchy, Is.False, "기본 화면에 도움말 버튼을 상시 표시하지 않는다");
            Assert.That(controller.View.IsHelpVisible, Is.False);
            Assert.That(controller.Match, Is.SameAs(match));
            Assert.That(match.CurrentClock, Is.EqualTo(-1));
            Assert.That(controller.View.SkipButton.gameObject.activeSelf, Is.False);
            controller.RequestRestart();
            Assert.That(controller.Match, Is.SameAs(match), "게임 중 사용자 재시작은 거부한다");
        }

        [UnityTest]
        public IEnumerator MouseOnlyCycleUsesItemsDiscardsPrivatelyDealsAgainAndRestartsFromReceipt()
        {
            Assert.That(Keyboard.current, Is.Null, "이 fixture는 마우스만 생성한다");
            controller.RestartGame();
            int driver = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Screwdriver);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return WaitFor(() => controller.Match.Stage == TurnStage.AwaitingItems);
            yield return ClickItem(World.PlayerItems.Single(i => i.ItemId == driver));
            yield return RightClick();
            yield return ClickItem(World.PlayerItems.Single(i => i.ItemId == driver));
            yield return ConfirmSelectedItem(World.PlayerItems.Single(i => i.ItemId == driver));
            yield return WaitFor(() => !controller.IsPresentingItem);
            yield return ClickItem(World.PlayerItems.Single());
            yield return ConfirmSelectedItem(World.PlayerItems.Single());
            yield return WaitFor(() => !controller.IsPresentingItem);
            yield return ClickClock(4, TurnStage.AwaitingKeyDecision);
            yield return ClickButton(controller.View.KeepClockButton);
            yield return ClickButton(controller.View.RationButton);
            bool sawAi = false, sawDiscard = false, sawNextBatch = false;
            int turns = 1;
            float deadline = Time.realtimeSinceStartup + 120;
            var match = controller.Match;
            while (!controller.View.IsResultVisible && Time.realtimeSinceStartup < deadline)
            {
                sawAi |= match.ActiveParticipant == Participant.Ai;
                sawNextBatch |= match.Batch > 1;
                for (int i = 0; i < 5; i++)
                    if (match.GetClock(i).Visibility == ClockVisibility.Discarded)
                    {
                        sawDiscard = true;
                        Assert.That(match.GetClock(i).Face, Is.Null);
                        Assert.That(controller.View.Clocks[i].IsOpen, Is.False);
                    }
                if (match.Stage != TurnStage.GameOver)
                {
                    controller.RequestRestart();
                    Assert.That(controller.Match, Is.SameAs(match));
                }
                if (match.ActiveParticipant == Participant.Player && match.Stage == TurnStage.AwaitingClock)
                {
                    yield return ClickClock(match.GetAvailableClockIndices().Last(), TurnStage.AwaitingAction);
                    yield return ClickButton(controller.View.CollectButton);
                    turns++;
                }
                yield return null;
            }
            Assert.That(controller.View.IsResultVisible, Is.True);
            Assert.That(sawAi && sawDiscard && sawNextBatch, Is.True);
            Assert.That(turns, Is.GreaterThan(2));
            Assert.That(match.Winner, Is.Not.EqualTo(GameWinner.None));
            Assert.That(controller.View.SelectedItemId, Is.Null);
            AssertLegacyItemButtonsHidden();
            var replay = GameObject.Find("ReplaySeal").GetComponent<Collider>();
            for (int frame = 0; frame < 10; frame++) { PointAt(replay); yield return null; }
            Assert.That(focus.HoveredTarget, Is.SameAs(replay.GetComponent<TabletopFocusTarget>()));
            input.Press(mouse.leftButton); InputSystem.Update(); focus.SendMessage("Update");
            var restarted = controller.Match;
            input.Release(mouse.leftButton); InputSystem.Update();
            Assert.That(restarted, Is.Not.SameAs(match));
            Assert.That(controller.View.IsResultVisible, Is.False);
            Assert.That(restarted.Batch, Is.EqualTo(1));
            controller.RequestRestart();
            Assert.That(controller.Match, Is.SameAs(restarted), "다시하기 연타는 새 게임을 중복 시작하지 않는다");
        }

        private IEnumerator Hover(ItemWorldView item)
        {
            for (int frame = 0; frame < 10; frame++)
            {
                PointAt(item.GetComponent<Collider>());
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(item.Target),
                "실제 Collider와 현재 카메라의 Raycast / " + DescribePointer(item.GetComponent<Collider>(), item.Target));
        }

        private IEnumerator ClickItem(ItemWorldView item)
        {
            yield return Hover(item);
            yield return ClickCurrentPointer();
        }

        private IEnumerator ConfirmSelectedItem(ItemWorldView item)
        {
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(item.ItemId));
            // Hover 대기 프레임 수가 아니라 실제 unscaled 시간으로 별도 클릭을 만든다.
            yield return new WaitForSecondsRealtime(.36f);
            yield return ClickItem(item);
        }

        private IEnumerator ClickCurrentPointer()
        {
            lastWorldPressTime = Time.unscaledTimeAsDouble;
            input.Press(mouse.leftButton); ProcessPointer();
            yield return null;
            input.Release(mouse.leftButton); ProcessPointer();
            yield return null;
        }

        private IEnumerator ClickCollider(Collider collider)
        {
            // 거부돼야 하는 입력도 실제 Collider에 도달했는지 확인한다. InputEnabled는 전제하지 않는다.
            for (int frame = 0; frame < 10; frame++) { PointAt(collider); yield return null; }
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(camera.ScreenPointToRay(mouse.position.ReadValue()), out var hit, 30, ~0,
                QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(hit.collider, Is.SameAs(collider));
            yield return ClickCurrentPointer();
        }

        private IEnumerator RightClick()
        {
            input.Press(mouse.rightButton); ProcessPointer(); yield return null;
            input.Release(mouse.rightButton); ProcessPointer(); yield return null;
        }

        private IEnumerator ClickEmptyTable()
        {
            Vector2 point = EmptyTablePoint();
            mouse.MakeCurrent(); input.Set(mouse.position, point); ProcessPointer();
            yield return null;
            yield return ClickCurrentPointer();
        }

        private Vector2 EmptyTablePoint()
        {
            Physics.SyncTransforms();
            foreach (float y in new[] { .2f, .3f, .4f, .5f, .6f, .7f, .8f })
                foreach (float x in new[] { .5f, .35f, .65f, .2f, .8f })
                {
                    var point = new Vector2(camera.pixelRect.xMin + camera.pixelWidth * x,
                        camera.pixelRect.yMin + camera.pixelHeight * y);
                    if (!Physics.Raycast(camera.ScreenPointToRay(point), out var hit, 30, ~0, QueryTriggerInteraction.Ignore) ||
                        hit.normal.y < .5f || hit.collider.GetComponentInParent<TabletopFocusTarget>()) continue;
                    bool table = false;
                    for (var parent = hit.collider.transform; parent; parent = parent.parent)
                        table |= parent.name.IndexOf("Table", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!table) continue;
                    var events = UnityEngine.EventSystems.EventSystem.current;
                    var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                    events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = point }, hits);
                    if (hits.Any(h => h.module is UnityEngine.UI.GraphicRaycaster)) continue;
                    return point;
                }
            Assert.Fail("UI나 다른 조작 물체가 없는 실제 테이블 Collider 지점을 찾지 못했습니다.");
            return Vector2.zero;
        }

        private void ProcessPointer()
        {
            GameUiTestInput.Process();
            focus.SendMessage("Update");
        }

        private void AssertLegacyItemButtonsHidden()
        {
            foreach (var button in new[] { controller.View.UseItemButton, controller.View.CancelItemButton, controller.View.EndItemsButton })
                Assert.That(!button || !button.gameObject.activeInHierarchy, Is.True, "사용/취소/단계 진행은 월드 입력으로 처리한다");
        }

        private string ItemCardText(string childName)
        {
            var child = controller.View.ItemDetailLabel.transform.parent.Find(childName);
            Assert.That(child, Is.Not.Null, "아이템 카드의 " + childName + " 텍스트가 있다");
            var text = child.GetComponent<TMPro.TMP_Text>();
            Assert.That(text, Is.Not.Null);
            return text.text;
        }

        private IEnumerator ClickClock(int index, TurnStage expected)
        {
            var token = controller.View.Clocks[index];
            Assert.That(token.SelectionTarget.InputEnabled, Is.True);
            for (int frame = 0; frame < 10; frame++)
            {
                PointAt(token.GetComponent<Collider>());
                yield return null;
            }
            Assert.That(focus.HoveredTarget, Is.SameAs(token.SelectionTarget),
                DescribePointer(token.GetComponent<Collider>(), token.SelectionTarget));
            yield return ClickCurrentPointer();
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(index));
            Assert.That(controller.Match.Stage, Is.EqualTo(expected));
            yield return GameUiTestInput.ClockMotionReady(controller);
        }

        private void PointAt(Collider collider)
        {
            // 기존 메인/Focus 하네스처럼 비활성 Editor에서도 테스트 포인터가 유효한 창으로 취급되게 한다.
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            Physics.SyncTransforms();
            mouse.MakeCurrent();
            input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(collider.bounds.center));
            ProcessPointer();
        }

        private string DescribePointer(Collider collider, TabletopFocusTarget target)
        {
            Vector2 pointer = mouse.position.ReadValue();
            RaycastHit hit;
            bool physicsHit = Physics.Raycast(camera.ScreenPointToRay(pointer), out hit, 30, ~0, QueryTriggerInteraction.Ignore);
            var uiHits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            if (UnityEngine.EventSystems.EventSystem.current)
                UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(
                    UnityEngine.EventSystems.EventSystem.current) { position = pointer }, uiHits);
            return "stage=" + controller.Match.Stage + " pointer=" + pointer + " viewport=" + camera.pixelRect
                + " camera=" + camera.transform.position + "/" + camera.transform.eulerAngles
                + " transitioning=" + focus.IsTransitioning + " cameraControl=" + focus.HasCameraControl
                + " targetActive=" + target.gameObject.activeInHierarchy + " targetEnabled=" + target.enabled
                + " available=" + target.IsAvailable + " input=" + target.InputEnabled
                + " colliderEnabled=" + collider.enabled + " collider=" + collider.bounds
                + " left=" + mouse.leftButton.isPressed + " pressed=" + mouse.leftButton.wasPressedThisFrame
                + " physicsHit=" + (physicsHit ? hit.collider.name : "none")
                + " uiHits=[" + string.Join(",", uiHits.Select(h => h.gameObject.name + ":" + h.module.GetType().Name)) + "]";
        }

        private void AssertPhaseGlyphStillExistsOnGpu(string context)
        {
            var font = controller.View.PhaseLabel.font;
            var character = font.characterTable.Single(c => c.unicode == '내');
            var glyph = character.glyph;
            var rect = glyph.glyphRect;
            var atlas = font.atlasTextures[glyph.atlasIndex];
            var cpuPixels = atlas.GetPixels32();
            long cpuAlpha = 0;
            for (int y = rect.y; y < rect.y + rect.height; y++)
                for (int x = rect.x; x < rect.x + rect.width; x++)
                    cpuAlpha += cpuPixels[y * atlas.width + x].a;
            Assert.That(cpuAlpha, Is.GreaterThan(0), context + " · 기존 '내' CPU 글리프가 있어야 한다");

            RenderTexture previous = RenderTexture.active;
            RenderTexture copy = null;
            Texture2D readback = null;
            try
            {
                copy = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear);
                Graphics.Blit(atlas, copy);
                RenderTexture.active = copy;
                readback = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false, true);
                readback.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0, false);
                long gpuAlpha = readback.GetPixels32().Sum(p => (long)p.a);
                // 동일 크기 복사의 포맷 양자화는 픽셀당 alpha 1단계까지만 허용한다.
                Assert.That(gpuAlpha, Is.EqualTo(cpuAlpha).Within(rect.width * rect.height),
                    context + " · CPU/GPU atlas의 기존 글리프 alpha가 일치해야 한다 (CPU=" + cpuAlpha + ", GPU=" + gpuAlpha + ")");
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback) UnityEngine.Object.Destroy(readback);
                if (copy) RenderTexture.ReleaseTemporary(copy);
            }
        }

        private IEnumerator ClickButton(UnityEngine.UI.Button button, Action afterRelease = null)
        {
            yield return GameUiTestInput.Click(input, mouse, button, null, afterRelease);
        }

        private IEnumerator WaitFor(Func<bool> predicate)
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "진행 시간 초과: " + controller.Match.Stage);
        }
    }
}
#endif
