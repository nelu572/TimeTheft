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

namespace TimeTheft.Tests
{
    public sealed class ItemModelIntegrationTests
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
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.EnableAiItemStrategy = false;
            settings.Rules.FirstParticipant = Participant.Player;
            settings.PreviewDuration = .1f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = .06f;
            settings.AiThinkDuration = 10;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller);
            Time.timeScale = 1; // 제작된 아이템 코루틴은 실제 deltaTime으로 재생한다.
            controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (controller) { controller.View.WorldItems.ModelUses?.ResetAll(); controller.enabled = false; }
            GameUiTestInput.DisableSceneModules();
            if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTimeScale;
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllSixHoldingModelsKeepTheirColliderAndFocusThenRealUseDetachesOnlyTheConsumedActor()
        {
            controller.RestartGame();
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind))) TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, kind);
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingItems);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(World.PlayerItems.Count, Is.EqualTo(6));
            yield return Wait(() => World.PlayerItems.All(item => World.ModelUses.CanUseVisual(Participant.Player, item.ItemId.Value)));
            foreach (var item in World.PlayerItems)
            {
                var model = Model(item);
                Assert.That(model, Is.Not.Null, item.Kind.ToString());
                SpeedUp(item);
                Assert.That(item.Label.enabled, Is.False);
                Assert.That(item.Target.FocusOnClick, Is.False);
                Assert.That(item.Target.FocusPoint, Is.Not.Null);
                var collider = item.GetComponent<Collider>();
                Assert.That(collider.enabled, Is.True);
                var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                Assert.That(renderers, Is.Not.Empty);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                Assert.That(bounds.size.magnitude, Is.GreaterThan(.05f));
                var profile = new SerializedObject(model).FindProperty("Profile").objectReferenceValue;
                float width = new SerializedObject(profile).FindProperty("InventoryWidth").floatValue;
                Assert.That(Mathf.Max(bounds.size.x, bounds.size.z), Is.EqualTo(width * item.transform.lossyScale.x).Within(.08f));
                var click = collider.bounds; click.Expand(.04f);
                Assert.That(click.Contains(bounds.min) && click.Contains(bounds.max), Is.True, item.Kind + " 실제 표시와 클릭 범위 / model=" + bounds + " / collider=" + click);
            }
            var used = World.PlayerItems.Single(item => item.Kind == ItemKind.Screwdriver);
            int id = used.ItemId.Value;
            yield return Wait(() => World.ModelUses.CanUseVisual(Participant.Player, id));
            controller.UseItem(id);
            Assert.That(controller.Match.GetInventory(Participant.Player).Any(item => item.Id == id), Is.False);
            Assert.That(World.ModelUses.ActiveCount, Is.EqualTo(1));
            Assert.That(used.transform.IsChildOf(World.PlayerAnchor), Is.False);
            Assert.That(used.IsRemoving, Is.True);
            Assert.That(used.GetComponent<Collider>().enabled, Is.False);
            Assert.That(World.PlayerItems.Count, Is.EqualTo(5));
            controller.RestartGame();
            yield return null;
            AssertCleanActors();
            Assert.That(used == null, Is.True, "재시작 후 사용 중인 모델도 제거한다");
        }

        [UnityTest]
        public IEnumerator ScissorsConsumeAndRemoveBothModelsThenResetLeavesNoActorsOrAnchors()
        {
            controller.RestartGame();
            int scissors = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.Scissors);
            TimeTheftItemTestSetup.Give(controller.Match, Participant.Ai, ItemKind.Screwdriver);
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingItems);
            var tool = World.PlayerItems.Single();
            var victim = World.OpponentItems.Single(); // 대상 하나로 규칙의 무작위 선택 결과를 추측하지 않는다.
            SpeedUp(tool); SpeedUp(victim);
            yield return Wait(() => World.ModelUses.CanUseVisual(Participant.Player, scissors));
            controller.UseItem(scissors);
            Assert.That(controller.Match.GetInventory(Participant.Player), Is.Empty);
            Assert.That(controller.Match.GetInventory(Participant.Ai), Is.Empty);
            Assert.That(World.PlayerItems, Is.Empty);
            Assert.That(World.OpponentItems, Is.Empty);
            Assert.That(World.ModelUses.ActiveCount, Is.EqualTo(1));
            Assert.That(tool.GetComponent<Collider>().enabled, Is.False);
            Assert.That(victim.GetComponent<Collider>().enabled, Is.False);
            Assert.That(tool.transform.IsChildOf(World.PlayerAnchor), Is.False);
            Assert.That(victim.transform.IsChildOf(World.OpponentAnchor), Is.False);
            yield return Wait(() => World.ModelUses.ActiveCount == 0, 15);
            yield return null;
            Assert.That(tool == null && victim == null, Is.True, "가위와 제거 대상 모두 정리한다");
            AssertCleanActors();
            World.ModelUses.ResetAll();
            controller.RestartGame();
            yield return null;
            AssertCleanActors();
        }

        private static MonoBehaviour Model(ItemWorldView item) => typeof(ItemWorldView).GetProperty("ModelPresentation").GetValue(item) as MonoBehaviour;
        private static void SpeedUp(ItemWorldView item)
        {
            foreach (var behaviour in Model(item).GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour) continue;
                var serialized = new SerializedObject(behaviour);
                var speed = serialized.FindProperty("PlaybackSpeed");
                if (speed == null) continue;
                speed.floatValue = 50;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        private void AssertCleanActors()
        {
            Assert.That(World.ModelUses.ActiveCount, Is.Zero);
            var actors = World.ModelUses.transform.Find("Used item models");
            Assert.That(actors, Is.Not.Null);
            Assert.That(actors.childCount, Is.Zero, "사용 모델과 임시 앵커가 남지 않는다");
        }
        private static IEnumerator Wait(Func<bool> condition, float timeout = 8)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "아이템 통합 상태 대기 시간 초과");
        }
    }
}
#endif
