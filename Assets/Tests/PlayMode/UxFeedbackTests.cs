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
    public sealed class UxFeedbackTests
    {
        private TimeTheftGameController game;
        private TimeTheftSettings settings;
        private InputTestFixture input;
        private Mouse mouse;
        private Camera camera;
        private IDisposable resolution;
        private float oldTimeScale;
        private ItemWorldInventoryView World => game.View.WorldItems;

        [UnitySetUp]
        public IEnumerator Load()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("UxFeedbackMouse");
            var scope = Type.GetType("TimeTheft.LightingStudy.Editor.FocusedLightingGameView+Scope, TimeTheft.FocusedLighting.Editor", true);
            resolution = (IDisposable)Activator.CreateInstance(scope, new object[] { 1280, 720 });
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            camera = game.View.FocusCamera.GetComponent<Camera>();
            game.View.FocusCamera.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            settings = UnityEngine.Object.Instantiate(game.Settings);
            settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.InitialLifeHours = 100;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.Rules.EnableAiItemStrategy = false;
            settings.Rules.AiDecisionNoise = settings.Rules.AiItemConservation = 0;
            settings.PreviewDuration = .1f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = .08f;
            settings.AiThinkDuration = .3f;
            var serialized = new SerializedObject(game);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(game);
            Time.timeScale = 0;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game) { World.ModelUses.ResetAll(); game.enabled = false; }
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
            input?.TearDown();
            resolution?.Dispose(); resolution = null;
        }

        [UnityTest]
        public IEnumerator ActualAiDuckBlockClickShowsOneWarningAndFadesAfterTwoSeconds()
        {
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.EnableAiItemStrategy = true;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 1;
            settings.Rules.ExtraRedProbability = 0;
            game.RestartGame();
            var match = game.Match;
            TimeTheftItemTestSetup.Give(match, Participant.Ai, ItemKind.RubberDuck);
            TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Handcuffs);
            TimeTheftItemTestSetup.Give(match, Participant.Player, ItemKind.Screwdriver);
            bool sawAiTurnCue = false;
            yield return Wait(() => {
                sawAiTurnCue |= game.View.TurnCue.text == "상대 차례" && game.View.TurnCue.color.a > .5f;
                return match.ActiveParticipant == Participant.Player && match.Stage == TurnStage.AwaitingClock &&
                    match.IsItemUseBlocked(Participant.Player) && Ready();
            });
            Assert.That(sawAiTurnCue, Is.True, "AI 연출/기본 카메라 소유 중에도 짧은 턴 안내를 표시한다");
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.True, "실제 AI 사용과 다음 플레이어 차례의 봉쇄를 기다린다");
            Assert.That(game.View.LastItemUse.HasValue, Is.True);
            Assert.That(game.View.LastItemUse.Value.Actor, Is.EqualTo(Participant.Ai));
            Assert.That(match.GetInventory(Participant.Ai), Is.Empty, "오리는 실제로 소모되었다");
            var item = World.PlayerItems.First();
            Physics.SyncTransforms();
            Vector2 point = camera.WorldToScreenPoint(item.GetComponent<Collider>().bounds.center);
            Assert.That(camera.pixelRect.Contains(point), Is.True);
            input.Set(mouse.position, point);
            for (int i = 0; i < 3; i++)
            {
                input.Press(mouse.leftButton);
                game.View.SendMessage("LateUpdate");
                input.Release(mouse.leftButton);
                yield return null;
            }
            game.View.SendMessage("LateUpdate");
            Assert.That(game.View.BlockedItemCue.text, Is.EqualTo("러버덕이 아이템 사용을 막고 있습니다."));
            Assert.That(game.View.BlockedItemCue.color.a, Is.GreaterThan(.8f));
            Assert.That(UnityEngine.Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Count(text => text.name == "Item Blocked Cue"), Is.EqualTo(1), "연타해도 경고 오브젝트는 하나다");
            Assert.That(match.IsItemUseBlocked(Participant.Player), Is.True);
            yield return new WaitForSecondsRealtime(2.2f);
            game.View.SendMessage("LateUpdate");
            Assert.That(game.View.BlockedItemCue.color.a, Is.LessThan(.01f));
        }

        [UnityTest]
        public IEnumerator SelectedDriverShowsActualReductionAndReadable720pCopyWithRubberduckGlyph()
        {
            settings.Rules.ScrewdriverReductionPercent = 37;
            game.RestartGame();
            int id = TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.Screwdriver);
            yield return Wait(() => game.Match.Stage == TurnStage.AwaitingItems && Ready());
            game.View.SelectItem(game.Match, id);
            yield return null;
            game.View.SendMessage("LateUpdate");
            Canvas.ForceUpdateCanvases();
            var text = game.View.ItemDetailLabel;
            text.ForceMeshUpdate();
            Assert.That(text.gameObject.activeInHierarchy, Is.True);
            Assert.That(text.text, Does.Contain("37% 경감"));
            Assert.That(text.text, Does.Contain("빨간 시계 징수"));
            Assert.That(text.text, Does.Contain("<color="));
            Assert.That(text.text, Does.Not.Contain("사용하면 취소할 수 없습니다"));
            Assert.That(text.isTextOverflowing, Is.False, "720p Selected 본문이 설명 영역에 맞는다");
            Assert.That(ItemWorldView.DisplayName(ItemKind.RubberDuck), Is.EqualTo("러버덕"));
            Assert.That(text.font.HasCharacter('덕', true, true), Is.True, "런타임 fallback이 새 명칭의 글리프를 지원한다");
            Assert.That(game.View.BlockedItemCue.color.a, Is.LessThan(.01f), "봉쇄가 없는 상태에 경고는 표시되지 않는다");
            game.View.ShowItemBlockedFeedback();
            game.View.SendMessage("LateUpdate");
            Assert.That(game.View.BlockedItemCue.color.a, Is.LessThan(.01f));
        }

        private bool Ready() => !World.ModelUses.IsAcquiring && !World.ModelUses.Hatch.Busy &&
            !game.IsPresentingClock && !game.IsPresentingItem && !game.View.FocusCamera.IsPresentationCameraOwned;
        private IEnumerator Wait(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                World.ModelUses.Hatch.PlaybackSpeed = 40;
                foreach (var item in UnityEngine.Object.FindObjectsByType<ItemWorldView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (item.ModelPresentation) item.ModelPresentation.PlaybackSpeed = 40;
                yield return null;
            }
            Assert.That(condition(), Is.True, game.Match.Stage + " / " + game.Match.ActiveParticipant);
        }
    }
}
#endif
