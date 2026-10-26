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
            foreach (var item in World.PlayerItems.Concat(World.OpponentItems))
            {
                Assert.That(AssetDatabase.GetAssetPath(item.Label.font), Is.EqualTo("Assets/TimeTheft/Fonts/NanumGothic SDF.asset"));
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
            Assert.That(controller.View.ItemDetailLabel.text, Does.Contain("드라이버").And.Contain("37"));
            Assert.That(controller.View.SelectedItemId, Is.Null);
            yield return ClickItem(first);
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(driver));
            Assert.That(first.IsSelected, Is.True);
            Assert.That(first.GetComponent<Collider>().bounds.center, Is.EqualTo(colliderPosition));
            Assert.That(controller.Match.GetInventory(Participant.Player).Count, Is.EqualTo(2), "선택 클릭만으로 소모하지 않는다");
            yield return ClickButton(controller.View.UseItemButton);
            Assert.That(controller.Match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(bypass));
            Assert.That(World.PlayerItems.Single().ItemId, Is.EqualTo(bypass));
            yield return WaitFor(() => !controller.IsPresentingItem);
            Assert.That(controller.View.SelectedItemId, Is.Null, "소모 뒤 다른 물건이 자동 선택되지 않는다");
            Assert.That(controller.View.UseItemButton.interactable, Is.False);
            yield return ClickItem(World.PlayerItems.Single());
            Assert.That(controller.View.ItemDetailLabel.text, Does.Contain("우회선").And.Contain("29"));
            yield return ClickButton(controller.View.UseItemButton);
            yield return WaitFor(() => !controller.IsPresentingItem);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(World.PlayerItems, Is.Empty);
            Assert.That(controller.Match.HasScrewdriverProtection && controller.Match.HasTimeBypass, Is.True);
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1), "사용 UI의 같은 클릭은 시계로 관통하지 않는다");
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
            yield return ClickItem(World.PlayerItems.Single());
            yield return ClickButton(controller.View.EndItemsButton);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingClock));
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(-1));
            Assert.That(controller.View.SelectedItemId, Is.Null);
            controller.SelectItem(id); controller.UseSelectedItem();
            Assert.That(controller.Match.GetInventory(Participant.Player).Single().Id, Is.EqualTo(id));
            Assert.That(controller.View.UseItemButton.interactable, Is.False);
            yield return ClickClock(4, TurnStage.AwaitingAction);
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
                yield return ClickButton(controller.View.UseItemButton);
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
            yield return ClickButton(controller.View.UseItemButton);
            Assert.That(World.PlayerItems.Single().ItemId, Is.EqualTo(second));
            Assert.That(World.OpponentItems, Is.Empty);
            Assert.That(controller.Match.GetInventory(Participant.Ai).Any(i => i.Id == removed), Is.False);
            yield return WaitFor(() => !controller.IsPresentingItem);
            yield return ClickItem(World.PlayerItems.Single());
            Assert.That(controller.View.SelectedItemId, Is.EqualTo(second));
            Assert.That(controller.View.UseItemButton.interactable, Is.False);
            Assert.That(controller.View.ItemDetailLabel.text, Does.Contain("없음"));
            controller.UseItem(second);
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
            Assert.That(controller.View.ItemDetailLabel.text, Does.Contain("비공개").And.Not.Contain("드라이버").And.Not.Contain("수갑"));
            yield return ClickItem(first);
            Assert.That(controller.View.SelectedItemId, Is.Null, "상대품은 클릭해도 사용 선택할 수 없다");
            Assert.That(controller.Match.GetInventory(Participant.Ai).Count, Is.EqualTo(2));
            Assert.That(controller.View.UseItemButton.interactable, Is.False);
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
            Assert.That(controller.View.UseItemButton.interactable, Is.False);
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
            input.Press(mouse.leftButton); InputSystem.Update(); focus.SendMessage("Update");
            yield return null;
            input.Release(mouse.leftButton); InputSystem.Update();
            yield return null;
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
            input.Press(mouse.leftButton); InputSystem.Update(); focus.SendMessage("Update");
            yield return null;
            input.Release(mouse.leftButton); InputSystem.Update();
            yield return null;
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(index));
            Assert.That(controller.Match.Stage, Is.EqualTo(expected));
        }

        private void PointAt(Collider collider)
        {
            // 기존 메인/Focus 하네스처럼 비활성 Editor에서도 테스트 포인터가 유효한 창으로 취급되게 한다.
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            Physics.SyncTransforms();
            mouse.MakeCurrent();
            input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(collider.bounds.center));
            InputSystem.Update();
            focus.SendMessage("Update");
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
