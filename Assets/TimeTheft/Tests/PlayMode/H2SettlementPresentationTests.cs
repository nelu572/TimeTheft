#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class H2SettlementPresentationTests
    {
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private H2TurnPresentation motion;
        private TabletopFocusCamera focus;
        private Camera camera;
        private InputTestFixture input;
        private float oldTime;
        private readonly Dictionary<Mesh, Vector3[]> meshVertices = new Dictionary<Mesh, Vector3[]>();
        [UnitySetUp] public IEnumerator Load()
        {
            oldTime = Time.timeScale; GameUiTestInput.DisableSceneModules(); input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var mouse=InputSystem.AddDevice<Mouse>(); InputSystem.AddDevice<Keyboard>();
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity",new LoadSceneParameters(LoadSceneMode.Single)); yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(controller.Settings); settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0; settings.Rules.EnableAiItemStrategy = false;
            settings.AiThinkDuration = 10;
            settings.PreviewDuration = settings.ShuffleDuration = .05f;
            settings.RevealDuration = .18f; settings.TransferDuration = .2f; settings.ResultHoldDuration = .18f;
            var so = new SerializedObject(controller); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller); motion = controller.View.TurnPresentation;
            motion.SelectDuration = .06f; motion.RevealEnterDuration = .08f; motion.SettlementTransitionDuration = .18f;
            motion.SettlementLeadDuration = .12f; motion.TurnCloseDuration = .18f; motion.TurnCameraReturnDuration = .12f;
            focus = controller.View.FocusCamera; camera = focus.GetComponent<Camera>();
            focus.SendMessage("OnApplicationFocus", true);
            camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position,camera.pixelRect.max+Vector2.one*20);
            Time.timeScale = 0;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false; if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules(); if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTime; yield return null; input.TearDown();
        }

        [UnityTest] public IEnumerator FourOutcomesUseRealCappedAndItemAdjustedAmountsExactlyOnceThroughClose()
        {
            var outcomes = new HashSet<string>();
            settings.Rules.InitialLifeHours = 11; settings.Rules.MinimumHours = settings.Rules.MaximumHours = 7;
            foreach (var action in new[] { TimeAction.Collect, TimeAction.Ration })
                for (int index = 0; index < 5; index++)
                {
                    controller.RestartGame();
                    int driver = TimeTheftItemTestSetup.Give(controller.Match,Participant.Player,ItemKind.Screwdriver);
                    int bypass = TimeTheftItemTestSetup.Give(controller.Match,Participant.Player,ItemKind.TimeBypass);
                    yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingItems);
                    controller.UseItem(driver); yield return Wait(() => !controller.IsPresentingItem);
                    controller.UseItem(bypass); yield return Wait(() => !controller.IsPresentingItem);
                    yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                    var model = controller.View.Clocks[index].H2Presentation;
                    var root = model.MotionRoot;
                    Quaternion flatRotation = root.rotation;
                    Vector3 visualScale = root.Find("VisualScale").localScale;
                    Assert.That(motion.IsOnPublicTable, Is.False, "이전 턴의 공용 공개 자세를 재시작에 남기지 않는다");
                    controller.SelectClock(index); yield return GameUiTestInput.ClockMotionReady(controller);
                    Assert.That(controller.Match.GetClock(index).Face, Is.Null);
                    controller.ChooseAction(action); var match=controller.Match; var face=match.GetClock(index).Face.Value;
                    outcomes.Add(action + "/" + face.Kind);
                    var expected=TimeTheftSettlement.Calculate(face,action,60,60,1200,true,true,50,50);
                    yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.ResultPose);
                    Assert.That(match.UsedCount, Is.Zero); Assert.That(match.PlayerLifeMinutes, Is.EqualTo(60));
                    var pose=root.position; var cam=camera.transform.position;
                    Quaternion openedLid = model.Watch.LidPivot.localRotation;
                    Assert.That(Quaternion.Angle(openedLid, Quaternion.identity), Is.EqualTo(109).Within(.1));
                    yield return new WaitForSecondsRealtime(.07f);
                    Assert.That(root.position, Is.EqualTo(pose)); Assert.That(camera.transform.position, Is.EqualTo(cam));
                    yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.SettlementTransition);
                    Assert.That(match.UsedCount, Is.Zero);
                    float transitionDeadline = Time.realtimeSinceStartup + 3;
                    while (motion.Phase == H2TurnPresentation.TurnPhase.SettlementTransition && Time.realtimeSinceStartup < transitionDeadline)
                    {
                        Assert.That(match.UsedCount, Is.Zero, "테이블에 내려놓는 도중에는 아직 실제 정산하지 않는다");
                        Assert.That(Quaternion.Angle(model.Watch.LidPivot.localRotation, openedLid), Is.LessThan(.01f), "열림 각도를 유지하며 내려놓는다");
                        Assert.That(WorldMeshMinimumY(model), Is.GreaterThanOrEqualTo(motion.PublicTableHeight - .001f), "내려놓는 도중에도 펠트를 관통하지 않는다");
                        yield return null;
                    }
                    Assert.That(motion.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.SettlementLead));
                    int closed=0, removals=0; Quaternion hand=Quaternion.identity;
                    Vector3 closedPosition = Vector3.zero; Quaternion closedRotation = Quaternion.identity;
                    Action<int> handler = i =>
                    {
                        closed++; Assert.That(i,Is.EqualTo(index)); hand=model.Watch.HourHand.localRotation;
                        closedPosition = root.position; closedRotation = root.rotation;
                    };
                    Action<int, bool> removalHandler = (i, discarded) =>
                    {
                        if (discarded) return;
                        removals++; Assert.That(i, Is.EqualTo(index)); Assert.That(closed, Is.EqualTo(1));
                        Assert.That(Vector3.Distance(root.position, closedPosition), Is.LessThan(.00001f), "닫힘 완료 자세에서 회수를 시작한다");
                        Assert.That(Quaternion.Angle(root.rotation, closedRotation), Is.LessThan(.01f), "회수 인계 때 몸체 회전이 튀지 않는다");
                    };
                    motion.CloseCompleted += handler;
                    controller.View.Lifecycle.RemoveStarted += removalHandler;
                    yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.SettlementLead);
                    Assert.That(match.UsedCount, Is.EqualTo(1));
                    AssertPublicTablePose(model, flatRotation, openedLid);
                    pose = root.position; Quaternion tableRotation = root.rotation;
                    Assert.That(model.IsRed, Is.EqualTo(face.Kind == ClockKind.Red));
                    Assert.That(model.DisplayedHours, Is.EqualTo(face.Hours));
                    Assert.That(root.Find("VisualScale").localScale, Is.EqualTo(visualScale));
                    Assert.That(model.Materials.C2Enabled, Is.True);
                    Assert.That(match.TransferredMinutes, Is.EqualTo(action == TimeAction.Ration ? 140 : face.Kind == ClockKind.Red ? 30 : 60));
                    Assert.That(match.PlayerLifeMinutes, Is.EqualTo(expected.OwnLifeMinutes));
                    Assert.That(match.AiLifeMinutes, Is.EqualTo(expected.OpponentLifeMinutes)); Assert.That(match.BankMinutes, Is.EqualTo(expected.BankMinutes));
                    Assert.That(controller.View.PlayerLife.text, Is.EqualTo("01:00"));
                    Assert.That(controller.View.PhaseLabel.text, Does.Contain("실제 " + GameTime.Format(expected.TransferredMinutes)));
                    controller.SelectClock((index+1)%5); controller.ChooseAction(TimeAction.Collect); controller.KeepPeekedClock();
                    Assert.That(match.CurrentClock, Is.EqualTo(index)); Assert.That(match.Action, Is.EqualTo(action));
                    float holdDeadline = Time.realtimeSinceStartup + 3;
                    while (motion.Phase != H2TurnPresentation.TurnPhase.SettlementHold && Time.realtimeSinceStartup < holdDeadline)
                    {
                        AssertFixedOpenTablePose(model, pose, tableRotation, openedLid);
                        Assert.That(match.UsedCount, Is.EqualTo(1));
                        yield return null;
                    }
                    Assert.That(motion.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.SettlementHold));
                    AssertFixedOpenTablePose(model, pose, tableRotation, openedLid);
                    AssertPublicResultLabels(index, face);
                    Assert.That(controller.View.PlayerLife.text, Is.EqualTo(GameTime.Format(expected.OwnLifeMinutes)));
                    Assert.That(controller.View.AiLife.text, Is.EqualTo(GameTime.Format(expected.OpponentLifeMinutes)));
                    Assert.That(controller.View.Bank.text, Is.EqualTo(GameTime.Format(expected.BankMinutes)));
                    if (expected.OwnLifeMinutes == 0 || expected.OpponentLifeMinutes == 0)
                    { Assert.That(match.Stage,Is.EqualTo(TurnStage.GameOver)); Assert.That(controller.View.IsResultVisible,Is.False); }
                    foreach (var label in new[] {controller.View.PlayerLife,controller.View.AiLife,controller.View.Bank})
                    {
                        var bounds=label.GetComponent<Renderer>().bounds;
                        for (int corner=0;corner<8;corner++)
                        {
                            var point=camera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                            Assert.That(point.z,Is.GreaterThan(0)); Assert.That(point.x,Is.InRange(.01f,.99f)); Assert.That(point.y,Is.InRange(.01f,.99f));
                        }
                    }
                    pose=root.position; cam=camera.transform.position; yield return new WaitForSecondsRealtime(.07f);
                    Assert.That(root.position,Is.EqualTo(pose)); Assert.That(camera.transform.position,Is.EqualTo(cam));
                    yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.Closing);
                    float closingDeadline = Time.realtimeSinceStartup + 3;
                    while (motion.Phase == H2TurnPresentation.TurnPhase.Closing && Time.realtimeSinceStartup < closingDeadline)
                    {
                        Assert.That(Vector3.Distance(root.position, pose), Is.LessThan(.00001f), "닫는 동안 공용 위치를 유지한다");
                        Assert.That(Quaternion.Angle(root.rotation, tableRotation), Is.LessThan(.01f), "닫는 동안 평평한 몸체 회전을 유지한다");
                        Assert.That(WorldMeshMinimumY(model), Is.GreaterThanOrEqualTo(motion.PublicTableHeight - .001f), "닫힘 중 펠트를 관통하지 않는다");
                        Assert.That(match.UsedCount, Is.EqualTo(1));
                        yield return null;
                    }
                    yield return Wait(() => closed==1); Assert.That(match.UsedCount,Is.EqualTo(1));
                    var boundsWatch=new Bounds(); bool first=true;
                    foreach(var renderer in controller.View.Clocks[index].H2Presentation.Watch.GetComponentsInChildren<Renderer>())
                    { if(first){boundsWatch=renderer.bounds;first=false;}else boundsWatch.Encapsulate(renderer.bounds); }
                    for(int corner=0;corner<8;corner++)
                    {
                        var point=camera.WorldToViewportPoint(boundsWatch.center+Vector3.Scale(boundsWatch.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                        Assert.That(point.x,Is.InRange(.01f,.99f)); Assert.That(point.y,Is.InRange(.01f,.99f));
                    }
                    Assert.That(Quaternion.Angle(hand,Quaternion.Euler(0,0,210)),Is.LessThan(.01f));
                    Assert.That(Quaternion.Angle(controller.View.Clocks[index].H2Presentation.Watch.LidPivot.localRotation,Quaternion.identity),Is.LessThan(.01f));
                    yield return Wait(() => !controller.View.Lifecycle.IsRunning && !controller.View.Clocks[index].gameObject.activeSelf && match.UsedCount == 1); motion.CloseCompleted -= handler;
                    controller.View.Lifecycle.RemoveStarted -= removalHandler;
                    Assert.That(closed,Is.EqualTo(1)); Assert.That(removals, Is.EqualTo(1)); Assert.That(match.UsedCount,Is.EqualTo(1));
                    Assert.That(camera.transform.position,Is.EqualTo(focus.OverviewPosition));
                    if (match.Stage == TurnStage.GameOver) Assert.That(focus.IsPresentationCameraOwned, Is.False);
                    else
                    {
                        Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
                        AssertAiOverviewCamera();
                    }
                }
            Assert.That(outcomes.Count,Is.EqualTo(4));
        }

        private void AssertPublicTablePose(H2ClockPresentation model, Quaternion flatRotation, Quaternion openedLid, bool opponent = false)
        {
            Assert.That(motion.IsOnPublicTable, Is.True);
            var tableFacing = opponent ? Quaternion.AngleAxis(180, Vector3.up) * flatRotation : flatRotation;
            Assert.That(Quaternion.Angle(model.MotionRoot.rotation, tableFacing), Is.LessThan(.01f), "각 참가자가 읽는 방향을 유지하며 평평하게 내려놓는다");
            Physics.SyncTransforms();
            var point = model.MotionRoot.position;
            var surfaces = Physics.RaycastAll(new Vector3(point.x, point.y + 5, point.z), Vector3.down, 10, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.normal.y > .9f && hit.collider.name.Contains("Tabletop")).ToArray();
            Assert.That(surfaces, Is.Not.Empty, "실제 테이블 Collider를 독립적으로 측정한다");
            float surface = surfaces.Max(hit => hit.point.y);
            Assert.That(motion.PublicTableHeight, Is.EqualTo(surface).Within(.001f));
            float gap = WorldMeshMinimumY(model) - surface;
            Assert.That(gap, Is.InRange(-.001f, .01f), "전체 실제 메시의 펠트 접촉 간격");
            Assert.That(gap, Is.EqualTo(motion.TableContactGap).Within(.001f));
            Vector3 center = Vector3.zero;
            foreach (var token in controller.View.Clocks) center += token.transform.position / controller.View.Clocks.Length;
            Assert.That(point.x, Is.EqualTo(center.x + motion.SettlementOffset.x).Within(.001f));
            Assert.That(point.z, Is.EqualTo(center.z + (opponent ? -motion.SettlementOffset.z : motion.SettlementOffset.z)).Within(.001f));
            Assert.That(model.IsOpen, Is.True);
            Assert.That(Quaternion.Angle(model.Watch.LidPivot.localRotation, openedLid), Is.LessThan(.01f));
        }

        private void AssertPublicResultLabels(int index, ClockFace face)
        {
            Canvas.ForceUpdateCanvases();
            var view = controller.View;
            var value = view.ClockValueLabels[index];
            Assert.That(value.isActiveAndEnabled, Is.True);
            Assert.That(value.text, Is.EqualTo(GameTime.Format(face.Hours * 60)));
            Assert.That(value.color, Is.EqualTo(face.Kind == ClockKind.Red ? new Color(.79f, .43f, .40f) : new Color(.91f, .88f, .78f)));
            Assert.That(value.raycastTarget, Is.False);
            var card = (RectTransform)value.transform.parent;
            Assert.That(card.Find("Kind"), Is.Null);
            Assert.That(card.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Length, Is.EqualTo(1));
            var frame = card.GetComponent<UiIndexGraphic>();
            Assert.That(frame.enabled, Is.True);
            Assert.That(frame.Layout, Is.EqualTo(UiIndexGraphic.FrameLayout.Compact));
            Assert.That(frame.raycastTarget, Is.False);
            Assert.That(card.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            Assert.That(view.PublicActionLabel.text, Is.Empty, "정산 중에는 확정 Cue를 반복하지 않는다");
            Assert.That(view.PublicActionLabel.gameObject.activeInHierarchy, Is.False);
            Assert.That(view.PublicActionLabel.raycastTarget, Is.False);
            Assert.That(view.PublicTransferLabel.text, Is.Empty, "계수기 위 변화량과 경로 카드를 중복 표시하지 않는다");
            Assert.That(view.PublicTransferLabel.gameObject.activeInHierarchy, Is.False);
            Assert.That(view.PublicTransferLabel.raycastTarget, Is.False);
            Assert.That(view.CounterDeltaLabels.Count, Is.EqualTo(3));
            Assert.That(view.CounterDeltaLabels[0], Is.SameAs(view.BankDeltaLabel));
            Assert.That(view.CounterDeltaLabels[1], Is.SameAs(view.PlayerDeltaLabel));
            Assert.That(view.CounterDeltaLabels[2], Is.SameAs(view.OpponentDeltaLabel));
            var serialized = new SerializedObject(view);
            var names = new[] { "bankDelta", "playerDelta", "aiDelta" };
            for (int i = 0; i < names.Length; i++)
            {
                var source = (TMPro.TextMeshPro)serialized.FindProperty(names[i]).objectReferenceValue;
                var shown = view.CounterDeltaLabels[i];
                Assert.That(source.GetComponent<Renderer>().enabled, Is.False, "기존 3D 변화량은 계산 API만 보존한다");
                Assert.That(source.GetComponentsInChildren<SpriteRenderer>(true).All(r => !r.enabled), Is.True, "기존 변화량의 빈 받침도 숨긴다");
                if (source.gameObject.activeSelf)
                {
                    Assert.That(shown.isActiveAndEnabled, Is.True);
                    Assert.That(shown.canvasRenderer.GetInheritedAlpha(), Is.GreaterThan(.99f));
                    Assert.That(shown.text, Is.EqualTo(source.text.Replace('−', '-')), "실제 부호와 HH:MM 변화량을 표시한다");
                }
                else Assert.That(string.IsNullOrEmpty(shown.text) || shown.canvasRenderer.GetInheritedAlpha() <= .01f, Is.True);
                Assert.That(shown.raycastTarget, Is.False);
            }
        }

        private void AssertFixedOpenTablePose(H2ClockPresentation model, Vector3 position, Quaternion rotation, Quaternion openedLid)
        {
            Assert.That(motion.IsOnPublicTable, Is.True);
            Assert.That(Vector3.Distance(model.MotionRoot.position, position), Is.LessThan(.00001f), "계수 중 시계는 움직이지 않는다");
            Assert.That(Quaternion.Angle(model.MotionRoot.rotation, rotation), Is.LessThan(.01f));
            Assert.That(model.IsOpen, Is.True);
            Assert.That(Quaternion.Angle(model.Watch.LidPivot.localRotation, openedLid), Is.LessThan(.01f));
        }

        private float WorldMeshMinimumY(H2ClockPresentation model)
        {
            float minimum = float.PositiveInfinity;
            foreach (var filter in model.Watch.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!mesh) continue;
                if (!meshVertices.TryGetValue(mesh, out var vertices))
                {
                    Assert.That(mesh.isReadable, Is.True, "실제 원본 정점을 검사한다: " + filter.name);
                    vertices = mesh.vertices; meshVertices.Add(mesh, vertices);
                }
                foreach (var vertex in vertices) minimum = Mathf.Min(minimum, filter.transform.TransformPoint(vertex).y);
            }
            Assert.That(float.IsInfinity(minimum), Is.False, "시계 전체의 실제 메시를 검사한다");
            return minimum;
        }

        [UnityTest] public IEnumerator AiLowersOntoItsOwnTableSideKeepsItsFacingThroughCloseAndRemovesOnce()
        {
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.InitialLifeHours = 100;
            settings.Rules.InitialItemCount = 1;
            settings.AiThinkDuration = .2f; settings.RevealDuration = .2f; settings.ResultHoldDuration = .25f;
            motion.SelectDuration = .12f; motion.RevealEnterDuration = .3f;
            controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.GetInventory(Participant.Ai).Count, Is.EqualTo(1));
            AssertAiOverviewCamera();
            var flatRotations = controller.View.Clocks.Select(token => token.H2Presentation.MotionRoot.rotation).ToArray();
            yield return Wait(() => motion.IsSelecting);
            int index = controller.Match.CurrentClock;
            var model = controller.View.Clocks[index].H2Presentation;
            yield return Wait(() => motion.Phase == H2TurnPresentation.TurnPhase.ResultPose);
            AssertAiOverviewCamera();
            Assert.That(controller.Match.UsedCount, Is.Zero);
            var face = controller.Match.GetClock(index).Face.Value;
            var openedLid = model.Watch.LidPivot.localRotation;
            var inspectPosition = model.MotionRoot.position; var inspectRotation = model.MotionRoot.rotation;
            float settlementDeadline = Time.realtimeSinceStartup + 3;
            while (motion.Phase != H2TurnPresentation.TurnPhase.SettlementHold && Time.realtimeSinceStartup < settlementDeadline)
            {
                AssertAiOverviewCamera();
                yield return null;
            }
            Assert.That(motion.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.SettlementHold));
            AssertAiOverviewCamera();
            AssertPublicTablePose(model, flatRotations[index], openedLid, true);
            Assert.That(model.MotionRoot.position.y, Is.LessThan(inspectPosition.y - .05f), "상대가 든 확인 자세와 자기 영역 테이블에 내려놓은 자세를 구분한다");
            Assert.That(Quaternion.Angle(model.MotionRoot.rotation, inspectRotation), Is.InRange(25f, 40f), "상대쪽 yaw를 유지하며 확인 tilt만 평평하게 푼다");
            var tablePosition = model.MotionRoot.position; var tableRotation = model.MotionRoot.rotation;
            AssertPublicResultLabels(index, face);
            Assert.That(controller.Match.UsedCount, Is.EqualTo(1));
            var balances = new[] { controller.Match.PlayerLifeMinutes, controller.Match.AiLifeMinutes, controller.Match.BankMinutes };
            int closes = 0, removals = 0;
            Action<int> onClose = closedIndex => { Assert.That(closedIndex, Is.EqualTo(index)); closes++; };
            Action<int, bool> onRemove = (removedIndex, discarded) =>
            {
                if (discarded) return;
                Assert.That(removedIndex, Is.EqualTo(index)); removals++;
                Assert.That(model.IsOpen, Is.False); Assert.That(model.DisplayedHours, Is.Null);
            };
            motion.CloseCompleted += onClose; controller.View.Lifecycle.RemoveStarted += onRemove;
            try
            {
                float finishDeadline = Time.realtimeSinceStartup + 10;
                while (controller.Match.ActiveParticipant == Participant.Ai && Time.realtimeSinceStartup < finishDeadline)
                {
                    AssertAiOverviewCamera();
                    if (motion.Phase == H2TurnPresentation.TurnPhase.Closing)
                    {
                        Assert.That(Vector3.Distance(model.MotionRoot.position, tablePosition), Is.LessThan(.00001f));
                        Assert.That(Quaternion.Angle(model.MotionRoot.rotation, tableRotation), Is.LessThan(.01f), "닫힘 중에도 상대쪽의 평평한 회전을 유지한다");
                    }
                    yield return null;
                }
                Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Player));
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
                controller.EndItemPhase();
                yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                Assert.That(closes, Is.EqualTo(1)); Assert.That(removals, Is.EqualTo(1));
                Assert.That(controller.Match.UsedCount, Is.EqualTo(1));
                Assert.That(new[] { controller.Match.PlayerLifeMinutes, controller.Match.AiLifeMinutes, controller.Match.BankMinutes }, Is.EqualTo(balances));
                Assert.That(controller.View.Clocks[index].gameObject.activeSelf, Is.False);
                Assert.That(focus.IsPresentationCameraOwned, Is.False);
            }
            finally
            {
                motion.CloseCompleted -= onClose; controller.View.Lifecycle.RemoveStarted -= onRemove;
            }
        }

        private void AssertAiOverviewCamera()
        {
            Assert.That(focus.IsPresentationCameraOwned, Is.True);
            Assert.That(Vector3.Distance(camera.transform.position, focus.OverviewPosition), Is.LessThan(.0001f), "AI 정산/닫힘/회수 중에도 전체 테이블 구도를 유지한다");
            Assert.That(Quaternion.Angle(camera.transform.rotation, focus.OverviewRotation), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(focus.OverviewFieldOfView).Within(.0001f));
        }

        [UnityTest] public IEnumerator BankCapKeepsFinalBalancesAndDoesNotRepeatSettlement()
        {
            settings.Rules.InitialLifeHours=1000; settings.Rules.MinimumHours=settings.Rules.MaximumHours=100;
            controller.RestartGame(); yield return Wait(() => controller.Match.Stage==TurnStage.AwaitingClock);
            controller.SelectClock(0); yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration);
            var match=controller.Match;
            yield return Wait(() => motion.Phase==H2TurnPresentation.TurnPhase.SettlementHold);
            Assert.That(match.TransferredMinutes,Is.EqualTo(1200)); Assert.That(match.BankMinutes,Is.Zero);
            Assert.That(controller.View.Bank.text,Is.EqualTo("00:00"));
            int player=match.PlayerLifeMinutes, ai=match.AiLifeMinutes;
            for(int i=0;i<3;i++) { controller.ChooseAction(TimeAction.Ration); controller.SkipAnimation(); yield return null; }
            Assert.That(match.UsedCount,Is.EqualTo(1)); Assert.That(match.PlayerLifeMinutes,Is.EqualTo(player)); Assert.That(match.AiLifeMinutes,Is.EqualTo(ai));
            yield return Wait(() => !controller.View.Lifecycle.IsRunning && !controller.View.Clocks[0].gameObject.activeSelf && match.UsedCount == 1);
            Assert.That(match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            AssertAiOverviewCamera();
            Assert.That(match.UsedCount, Is.EqualTo(1));
            Assert.That(match.PlayerLifeMinutes, Is.EqualTo(player)); Assert.That(match.AiLifeMinutes, Is.EqualTo(ai));
        }

        [UnityTest] public IEnumerator RestartAndSpaceCleanEveryPostRevealPhase()
        {
            foreach(var phase in new[]{H2TurnPresentation.TurnPhase.SettlementTransition,H2TurnPresentation.TurnPhase.SettlementLead,
                H2TurnPresentation.TurnPhase.CountingTime,H2TurnPresentation.TurnPhase.SettlementHold,H2TurnPresentation.TurnPhase.Closing,H2TurnPresentation.TurnPhase.Closed})
            {
                controller.RestartGame(); yield return Wait(() => controller.Match.Stage==TurnStage.AwaitingClock);
                controller.SelectClock(0); yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration);
                yield return Wait(() => motion.Phase==phase && (phase != H2TurnPresentation.TurnPhase.Closed ||
                    controller.View.Lifecycle.Phase == H2ClockLifecyclePresentation.LifecyclePhase.MovingToHatch)); controller.SkipAnimation(); yield return null;
                controller.RestartGame();
                Assert.That(controller.Match.UsedCount,Is.Zero); Assert.That(focus.IsPresentationCameraOwned,Is.True); // 새 4-A가 소유한다.
                yield return Wait(() => controller.Match.Stage==TurnStage.AwaitingClock);
                Assert.That(focus.IsPresentationCameraOwned,Is.False); Assert.That(camera.transform.position,Is.EqualTo(focus.OverviewPosition));
                foreach(var token in controller.View.Clocks) Assert.That(token.H2Presentation.MotionRoot.localPosition,Is.EqualTo(Vector3.zero));
            }
        }
        private IEnumerator Wait(Func<bool> condition)
        {
            float end=Time.realtimeSinceStartup+10;
            while(!condition()&&Time.realtimeSinceStartup<end) yield return null;
            Assert.That(condition(),Is.True,controller.Match.Stage+" / "+motion.Phase);
        }
    }
}
#endif
