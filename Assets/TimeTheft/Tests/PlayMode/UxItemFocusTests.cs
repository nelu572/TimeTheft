#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TimeTheft.ItemPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class UxItemFocusTests
    {
        private TimeTheftGameController game;
        private TimeTheftSettings settings;
        private InputTestFixture input;
        private float oldTimeScale;
        private ItemWorldInventoryView World => game.View.WorldItems;

        [UnitySetUp]
        public IEnumerator Load()
        {
            oldTimeScale = Time.timeScale;
            GameUiTestInput.DisableSceneModules();
            input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.AddDevice<Mouse>("UxItemFocusMouse");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(game.Settings);
            settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0;
            settings.Rules.EnableAiItemStrategy = false;
            settings.Rules.AiDecisionNoise = settings.Rules.AiItemConservation = 0;
            settings.Rules.MinimumHours = settings.Rules.MaximumHours = 8;
            settings.Rules.ExtraRedProbability = 1;
            settings.Rules.InitialLifeHours = 100;
            settings.PreviewDuration = .1f;
            settings.ShuffleDuration = settings.RevealDuration = settings.TransferDuration = settings.ResultHoldDuration = .08f;
            settings.AiThinkDuration = .4f;
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
        }

        [UnityTest] public IEnumerator PlayerKeyUsesItsTipOnTheCrownAboveTheTable() => KeyContact(Participant.Player);
        [UnityTest] public IEnumerator AiKeyUsesItsTipOnTheCrownAboveTheTable() => KeyContact(Participant.Ai);
        [UnityTest] public IEnumerator PlayerDuckKeepsItsDirectionAfterSupplyAndUse() => DuckPose(Participant.Player);
        [UnityTest] public IEnumerator AiDuckFacesThePlayerAfterSupplyAndUse() => DuckPose(Participant.Ai);

        private IEnumerator KeyContact(Participant actor)
        {
            settings.Rules.FirstParticipant = actor;
            settings.Rules.EnableAiItemStrategy = actor == Participant.Ai;
            game.RestartGame();
            int id = TimeTheftItemTestSetup.Give(game.Match, actor, ItemKind.SpareKey);
            ItemPresentationController model = null;
            bool requestedUse = false, requestedClock = false, sawDeploy = false, sawContact = false;
            float deadline = Time.realtimeSinceStartup + 25;
            while (Time.realtimeSinceStartup < deadline)
            {
                Accelerate();
                if (!model)
                {
                    var item = (actor == Participant.Player ? World.PlayerItems : World.OpponentItems)
                        .FirstOrDefault(value => value.Kind == ItemKind.SpareKey);
                    if (item) model = item.ModelPresentation;
                }
                if (actor == Participant.Player && !requestedUse && Ready() && PlayerItemReady(id))
                {
                    game.UseItem(id);
                    requestedUse = !game.Match.GetInventory(Participant.Player).Any(value => value.Id == id);
                }
                if (actor == Participant.Player && requestedUse && !requestedClock && !game.IsPresentingItem &&
                    !game.IsPresentingClock && game.Match.Stage == TurnStage.AwaitingClock && game.View.Clocks[0].SelectionTarget.InputEnabled)
                {
                    game.SelectClock(0);
                    requestedClock = game.Match.Stage == TurnStage.AwaitingKeyDecision && game.Match.CurrentClock == 0;
                }
                if (model && model.Anchors != null && model.Anchors.TargetClock)
                {
                    if (model.State == ItemPresentationState.Deploying)
                    {
                        sawDeploy = true;
                        Assert.That(model.Contact.position.y, Is.GreaterThanOrEqualTo(TableHeight() - .015f), "접근 중 실제 열쇠 끝이 상판 아래로 들어가지 않는다");
                    }
                    if ((model.State == ItemPresentationState.Activating || model.State == ItemPresentationState.Holding) &&
                        model.SourceSeconds >= 74f / 30f && game.Match.Stage == TurnStage.AwaitingKeyDecision)
                    {
                        sawContact = true;
                        Assert.That(Vector3.Distance(model.Contact.position, model.Anchors.TargetClock.position), Is.LessThan(.008f), "원본 회전 중 실제 Contact_Tip은 Crown 접촉점에 유지된다");
                        Assert.That(Vector3.Dot(-model.Contact.right, Vector3.up), Is.GreaterThan(.55f), "손잡이가 태엽보다 위쪽에 놓이는 경사 자세를 유지한다");
                        Assert.That(VisibleBounds(model.Visual).min.y, Is.GreaterThanOrEqualTo(TableHeight() - .03f), "접촉 중 열쇠 몸체가 상판 아래로 파고들지 않는다");
                    }
                }
                if (sawContact && model && (model.State == ItemPresentationState.Complete || model.State == ItemPresentationState.Failed)) break;
                if (sawContact && !model) break;
                yield return null;
            }
            Assert.That(sawDeploy, Is.True, actor + " 실제 Deploy 경로를 관찰한다 / " + Diagnostic(id, model) + ", requestedUse=" + requestedUse + ", requestedClock=" + requestedClock);
            Assert.That(sawContact, Is.True, actor + " 실제 개인 확인 접촉/회전 구간을 관찰한다 / " + Diagnostic(id, model));
            Assert.That(World.ModelUses.HasFailedPresentation, Is.False);
        }

        private IEnumerator DuckPose(Participant actor)
        {
            settings.Rules.FirstParticipant = actor;
            settings.Rules.EnableAiItemStrategy = actor == Participant.Ai;
            game.RestartGame();
            int id = TimeTheftItemTestSetup.Give(game.Match, actor, ItemKind.RubberDuck);
            if (actor == Participant.Ai)
            {
                TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.Screwdriver);
                TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.TimeBypass);
                TimeTheftItemTestSetup.Give(game.Match, Participant.Player, ItemKind.SpareKey);
            }
            ItemPresentationController model = null;
            bool requested = false, sawInventoryPose = false;
            float deadline = Time.realtimeSinceStartup + 25;
            while (Time.realtimeSinceStartup < deadline)
            {
                Accelerate();
                if (!model)
                {
                    var item = (actor == Participant.Player ? World.PlayerItems : World.OpponentItems)
                        .FirstOrDefault(value => value.Kind == ItemKind.RubberDuck);
                    if (item) model = item.ModelPresentation;
                }
                if (model && model.Anchors == null && model.Visual.activeInHierarchy)
                {
                    AssertDuckFacing(model, actor);
                    sawInventoryPose = true;
                }
                if (actor == Participant.Player && !requested && Ready() && PlayerItemReady(id))
                {
                    game.UseItem(id);
                    requested = !game.Match.GetInventory(Participant.Player).Any(value => value.Id == id);
                }
                if (model && model.Anchors != null && model.State == ItemPresentationState.Holding)
                {
                    Assert.That(sawInventoryPose, Is.True, "해치 등장 중 보유 방향을 관찰한다");
                    AssertDuckFacing(model, actor);
                    var target = model.Anchors.TargetItem;
                    Assert.That(target.position.x, Is.EqualTo(0).Within(.01f));
                    Assert.That(target.position.z, Is.EqualTo(actor == Participant.Player ? 2.5f : -2.5f).Within(.01f), "양측 러버덕은 시계 뒤쪽 상대 영역 중앙에 놓인다");
                    Assert.That(Quaternion.Angle(model.MotionRoot.rotation, target.rotation), Is.LessThan(1), "Deploy가 명시한 사용 주체별 목표 회전을 반영한다");
                    var bounds = VisibleBounds(model.Visual);
                    Assert.That(bounds.center.x, Is.EqualTo(0).Within(.01f), "실제 모델이 받는 쪽 정면 중앙에 안착한다");
                    Assert.That(bounds.min.y, Is.EqualTo(TableHeight()).Within(.025f));
                    Assert.That(actor == Participant.Player ? bounds.min.z : -bounds.max.z, Is.GreaterThan(1.5f), "중앙 5개 시계 영역과 분리된다");
                    Assert.That(model.Anchors.Opponent, Is.EqualTo(actor == Participant.Ai));
                    yield break;
                }
                yield return null;
            }
            Assert.Fail(actor + " 러버덕 실제 사용/배치가 제한 시간 내 끝나지 않았다 / " + Diagnostic(id, model) + ", requestedUse=" + requested);
        }

        private static void AssertDuckFacing(ItemPresentationController model, Participant actor)
        {
            var renderers = model.Visual.GetComponentsInChildren<Renderer>();
            var bill = renderers.Single(value => value.name.Contains("Bill"));
            var body = renderers.Single(value => value.name.Contains("Body_Head"));
            var facing = Vector3.ProjectOnPlane(bill.bounds.center - body.bounds.center, Vector3.up).normalized;
            Assert.That(Vector3.Dot(facing, actor == Participant.Ai ? Vector3.back : Vector3.forward), Is.GreaterThan(.9f), "AI 부리는 플레이어를 향하고 플레이어 부리는 기존 방향을 유지한다");
        }

        private void Accelerate()
        {
            World.ModelUses.Hatch.PlaybackSpeed = 20;
            foreach (var item in UnityEngine.Object.FindObjectsByType<ItemWorldView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (item.ModelPresentation) item.ModelPresentation.PlaybackSpeed = 12;
        }
        private bool Ready() => game.Match.ActiveParticipant == Participant.Player &&
            game.Match.Stage == TurnStage.AwaitingItems && !game.IsPresentingItem && !game.IsPresentingClock &&
            !World.ModelUses.IsAcquiring && !World.ModelUses.Hatch.Busy;
        private bool PlayerItemReady(int id)
        {
            var item = World.PlayerItems.FirstOrDefault(value => value.ItemId == id);
            return item && item.Target.InputEnabled && World.ModelUses.CanUseVisual(Participant.Player, id);
        }
        private string Diagnostic(int id, ItemPresentationController model)
        {
            var item = World.PlayerItems.FirstOrDefault(value => value.ItemId == id);
            return "stage=" + game.Match.Stage + ", actor=" + game.Match.ActiveParticipant +
                ", presentingItem=" + game.IsPresentingItem + ", presentingClock=" + game.IsPresentingClock +
                ", acquiring=" + World.ModelUses.IsAcquiring + ", hatchBusy=" + World.ModelUses.Hatch.Busy +
                ", targetInput=" + (item ? item.Target.InputEnabled.ToString() : "missing") +
                ", canUseVisual=" + World.ModelUses.CanUseVisual(Participant.Player, id) +
                ", checkItemUse=" + game.Match.CheckItemUse(Participant.Player, id) +
                ", settingsFirst=" + settings.Rules.FirstParticipant +
                ", remainingInventory=" + string.Join(";", game.Match.GetInventory(Participant.Player).Select(value => value.Id + ":" + value.Kind)) +
                ", model=" + (model ? model.State + "/" + model.SourceSeconds : "missing") +
                ", viewItemPresentation=" + typeof(TimeTheftGameView).GetField("itemPresentation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(game.View);
        }
        private float TableHeight() => World.ModelUses.Hatch.Surface.Surface.GetComponent<Renderer>().bounds.max.y;
        private static Bounds VisibleBounds(GameObject visual)
        {
            var values = visual.GetComponentsInChildren<Renderer>().Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            Assert.That(values, Is.Not.Empty);
            var bounds = values[0].bounds; foreach (var value in values.Skip(1)) bounds.Encapsulate(value.bounds); return bounds;
        }
    }
}
#endif
