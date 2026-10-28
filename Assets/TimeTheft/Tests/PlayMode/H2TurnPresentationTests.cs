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
    public sealed class H2TurnPresentationTests
    {
        private TimeTheftGameController controller;
        private TimeTheftSettings settings;
        private H2TurnPresentation turn;
        private Camera camera;
        private TabletopFocusCamera focus;
        private InputTestFixture input;
        private Mouse mouse;
        private float oldTime;
        [UnitySetUp] public IEnumerator Load()
        {
            oldTime = Time.timeScale; GameUiTestInput.DisableSceneModules(); input = new InputTestFixture(); input.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("H2TurnMouse"); InputSystem.AddDevice<Keyboard>("H2TurnKeyboard");
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TimeTheftGame.unity", new LoadSceneParameters(LoadSceneMode.Single)); yield return null;
            controller = UnityEngine.Object.FindFirstObjectByType<TimeTheftGameController>();
            settings = UnityEngine.Object.Instantiate(controller.Settings); settings.RandomSeed = 635;
            settings.Rules.InitialItemCount = settings.Rules.ItemsPerBatch = 0; settings.Rules.InitialLifeHours = 100;
            settings.Rules.EnableAiItemStrategy = false;
            settings.PreviewDuration = settings.ShuffleDuration = settings.TransferDuration = settings.ResultHoldDuration = settings.DiscardDuration = .06f;
            settings.RevealDuration = 1; settings.AiThinkDuration = .2f;
            var so = new SerializedObject(controller); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            GameUiTestInput.ShortenBatchReveal(controller); turn = controller.View.TurnPresentation;
            turn.SelectDuration = .25f; turn.RevealEnterDuration = .3f; turn.LegacyReturnDuration = .12f;
            focus = controller.View.FocusCamera; camera = focus.GetComponent<Camera>();
            focus.SendMessage("OnApplicationFocus", true); camera.GetComponent<TabletopCameraLook>().SendMessage("OnApplicationFocus", true);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20); Time.timeScale = 0; controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (controller) controller.enabled = false; if (focus) focus.enabled = false;
            GameUiTestInput.DisableSceneModules(); if (settings) UnityEngine.Object.Destroy(settings);
            Time.timeScale = oldTime; yield return null; input?.TearDown();
        }

        [UnityTest] public IEnumerator EverySlotRetargetsToOneClosedHoldAndRevealsTheActualFaceWithoutChangingOtherWatches()
        {
            var kinds = new System.Collections.Generic.HashSet<ClockKind>();
            for (int index = 0; index < 5; index++)
            {
                if (index > 0) { controller.RestartGame(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock); }
                var token = controller.View.Clocks[index]; var model = token.H2Presentation;
                var scale = model.MotionRoot.Find("VisualScale").localScale;
                var body = model.Watch.GetComponentsInChildren<Renderer>().Single(r => r.name == "Case");
                controller.SelectClock(index); var match = controller.Match;
                Assert.That(match.CurrentClock, Is.EqualTo(index)); Assert.That(controller.IsPresentingClock, Is.True);
                controller.SelectClock((index + 1) % 5); controller.ChooseAction(TimeAction.Collect);
                Assert.That(match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
                while (controller.IsPresentingClock)
                {
                    AssertHidden();
                    for (int j = 0; j < 5; j++) if (j != index)
                    {
                        Assert.That(controller.View.Clocks[j].H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
                        var other = controller.View.Clocks[j].H2Presentation.Watch.GetComponentsInChildren<Renderer>().Single(r => r.name == "Case");
                        Assert.That(body.bounds.Intersects(other.bounds), Is.False, "Select body clearance: " + index + " / " + j);
                    }
                    yield return null;
                }
                Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.DecisionHold));
                Assert.That(turn.IsOpponentTurn, Is.False);
                Assert.That(model.MotionRoot.position.x, Is.EqualTo(.1f).Within(.002));
                Assert.That(model.MotionRoot.position.z, Is.EqualTo(-.9f).Within(.002));
                var hold = model.MotionRoot.localPosition; var rotation = model.MotionRoot.localRotation;
                yield return new WaitForSecondsRealtime(.4f);
                Assert.That(model.MotionRoot.localPosition, Is.EqualTo(hold)); Assert.That(model.MotionRoot.localRotation, Is.EqualTo(rotation)); AssertHidden();
                AssertNoPublicClockValues();
                Assert.That(controller.View.PublicActionLabel.text, Is.Empty, "플레이어의 비공개 결정 대기에는 확정 행동을 표시하지 않는다");
                Assert.That(controller.View.PublicTransferLabel.text, Is.Empty);
                controller.ChooseAction(TimeAction.Ration); var face = match.GetClock(index).Face.Value; kinds.Add(face.Kind);
                controller.ChooseAction(TimeAction.Collect); controller.SelectClock((index + 1) % 5);
                Assert.That(match.Action, Is.EqualTo(TimeAction.Ration)); Assert.That(match.CurrentClock, Is.EqualTo(index));
                yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.ResultPose);
                Assert.That(match.Stage, Is.EqualTo(TurnStage.Revealing));
                Assert.That(model.DisplayedHours, Is.EqualTo(face.Hours)); Assert.That(model.IsRed, Is.EqualTo(face.Kind == ClockKind.Red));
                Assert.That(model.IsOpen, Is.True); Assert.That(Quaternion.Angle(model.Watch.LidPivot.localRotation, Quaternion.identity), Is.EqualTo(109).Within(.1));
                Assert.That(Quaternion.Angle(model.Watch.HourHand.localRotation, Quaternion.Euler(0, 0, (face.Hours % 12) * 30)), Is.LessThan(.01));
                Assert.That(Quaternion.Angle(model.Watch.MinuteHand.localRotation, Quaternion.identity), Is.LessThan(.01));
                Assert.That(model.MotionRoot.Find("VisualScale").localScale, Is.EqualTo(scale)); Assert.That(model.Materials.C2Enabled, Is.True);
                var center = model.Watch.HourHand.position;
                float width = Vector3.Distance(camera.WorldToViewportPoint(center - Vector3.right * .28f), camera.WorldToViewportPoint(center + Vector3.right * .28f)) * 1280;
                Assert.That(width, Is.GreaterThan(170));
                Assert.That(focus.IsPresentationCameraOwned, Is.True);
                yield return null; // 상황형 라벨의 LateUpdate까지 실제 프레임을 진행한다.
                AssertPublicClockValue(index, face);
                Assert.That(controller.View.PublicActionLabel.text, Is.Empty, "플레이어가 이미 누른 행동을 공개 화면에 중복하지 않는다");
                Assert.That(controller.View.PublicTransferLabel.text, Is.Empty, "정산 전에는 이전 턴 이동량을 노출하지 않는다");
            }
            Assert.That(kinds.Count, Is.EqualTo(2), "같은 공유 모션으로 일반/빨강을 모두 검증한다");
        }

        [UnityTest] public IEnumerator ActualColliderAndActionButtonInputWaitsForSelectAndFocusCanInspectCountersDuringHold()
        {
            var token = controller.View.Clocks[4];
            for (int i = 0; i < 10; i++) { input.Set(mouse.position, (Vector2)camera.WorldToScreenPoint(token.GetComponent<Collider>().bounds.center)); yield return null; }
            Assert.That(focus.HoveredTarget, Is.SameAs(token.SelectionTarget));
            input.Press(mouse.leftButton); yield return null; input.Release(mouse.leftButton); yield return null;
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(4)); Assert.That(controller.IsPresentingClock, Is.True);
            Assert.That(controller.View.CollectButton.gameObject.activeSelf, Is.False);
            controller.ChooseAction(TimeAction.Collect); AssertHidden();
            yield return Wait(() => !controller.IsPresentingClock);
            var p = token.H2Presentation.MotionRoot.localPosition;
            yield return new WaitForSecondsRealtime(1.2f); AssertHidden(); Assert.That(token.H2Presentation.MotionRoot.localPosition, Is.EqualTo(p));
            var counter = controller.View.PlayerLife.GetComponentInParent<TabletopFocusTarget>();
            Assert.That(focus.Focus(counter), Is.True); yield return Wait(() => !focus.IsTransitioning);
            Assert.That(camera.fieldOfView, Is.EqualTo(counter.FieldOfView).Within(.001));
            // Focus한 실제 현재 카메라에서 Reveal을 시작하며 먼저 overview로 순간이동하지 않는다.
            var before = camera.transform.position;
            float beforeFov = camera.fieldOfView;
            controller.ChooseAction(TimeAction.Ration);
            Assert.That(camera.transform.position, Is.EqualTo(before)); Assert.That(focus.IsPresentationCameraOwned, Is.True);
            Assert.That(camera.fieldOfView, Is.EqualTo(beforeFov));
            controller.ChooseAction(TimeAction.Collect);
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.ResultPose);
            Assert.That(camera.fieldOfView, Is.EqualTo(focus.OverviewFieldOfView));
            Assert.That(controller.Match.Action, Is.EqualTo(TimeAction.Ration));
            yield return Wait(() => controller.Match.UsedCount > 0);
            yield return Wait(() => !focus.IsPresentationCameraOwned);
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
            yield return Wait(() => controller.Match.ActiveParticipant == Participant.Player && controller.Match.Stage == TurnStage.AwaitingClock);
            input.Set(mouse.position, camera.pixelRect.max + Vector2.one * 20);
            controller.SelectClock(controller.Match.GetAvailableClockIndices().First()); yield return GameUiTestInput.ClockMotionReady(controller);
            yield return GameUiTestInput.Click(input, mouse, controller.View.CollectButton);
            Assert.That(controller.Match.Action, Is.EqualTo(TimeAction.Collect));
        }

        [UnityTest] public IEnumerator AiOpensAndObservesOnlyOnItsOwnSideWithAnUnmovingOverviewCamera()
        {
            settings.AiThinkDuration = .6f; turn.RevealEnterDuration = .84f;
            settings.Rules.InitialItemCount = 1;
            settings.Rules.FirstParticipant = Participant.Ai; controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.GetInventory(Participant.Ai).Count, Is.EqualTo(1), "AI 아이템 단계도 거친 뒤 첫 Think부터 카메라를 소유한다");
            float thinkDeadline = Time.realtimeSinceStartup + 3;
            while (controller.Match.Stage == TurnStage.AwaitingClock && Time.realtimeSinceStartup < thinkDeadline)
            {
                AssertOverviewCamera(); AssertHidden(); AssertNoPublicClockValues();
                input.Set(mouse.position, camera.pixelRect.min + Vector2.one * 24);
                yield return null;
            }
            yield return Wait(() => turn.IsSelecting); int index = controller.Match.CurrentClock;
            var model = controller.View.Clocks[index].H2Presentation;
            Assert.That(turn.IsOpponentTurn, Is.True, "Controller가 AI 선택 표현을 명시적으로 전달한다");
            Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Ai)); AssertHidden();
            float selectionDeadline = Time.realtimeSinceStartup + 3;
            while (turn.IsSelecting && Time.realtimeSinceStartup < selectionDeadline)
            {
                AssertOverviewCamera(); AssertHidden(); AssertNoPublicClockValues(); AssertNoUncommittedAiAction();
                input.Set(mouse.position, camera.pixelRect.max - Vector2.one * 24);
                yield return null;
            }
            Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.DecisionHold)); AssertHidden();
            Vector3 center = Vector3.zero;
            foreach (var clock in controller.View.Clocks) center += clock.transform.position / controller.View.Clocks.Length;
            Assert.That(model.MotionRoot.position.x, Is.EqualTo(center.x + .1f).Within(.002f));
            Assert.That(model.MotionRoot.position.z, Is.EqualTo(center.z + .9f).Within(.002f), "AI는 상대 영역에서 닫힌 시계를 고른다");
            var holdPosition = model.MotionRoot.position;
            float decisionDeadline = Time.realtimeSinceStartup + 3;
            while (turn.Phase == H2TurnPresentation.TurnPhase.DecisionHold && Time.realtimeSinceStartup < decisionDeadline)
            {
                AssertOverviewCamera();
                AssertHidden(); AssertNoPublicClockValues(); AssertNoUncommittedAiAction();
                Assert.That(model.MotionRoot.position, Is.EqualTo(holdPosition));
                yield return null;
            }
            Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.RevealEnter));
            int opponentSamples = 0; bool sawCommittedCue = false, sawOpponentOpen = false;
            Quaternion opponentOpenRotation = Quaternion.identity;
            var dial = model.Watch.GetComponentsInChildren<MeshFilter>().Single(mesh => mesh.name == "Dial").transform;
            float opponentRevealDuration = turn.RevealEnterDuration + .35f;
            float revealDeadline = Time.realtimeSinceStartup + 3;
            while (turn.Phase == H2TurnPresentation.TurnPhase.RevealEnter && Time.realtimeSinceStartup < revealDeadline)
            {
                AssertOverviewCamera();
                Canvas.ForceUpdateCanvases();
                sawCommittedCue |= controller.View.PublicActionLabel.text.Contains("상대") &&
                    controller.View.PublicActionLabel.text.Contains(controller.Match.Action == TimeAction.Collect ? "징수" : "배급");
                Assert.That(turn.IsResultPublic, Is.False);
                AssertNoPublicClockValues();
                Assert.That(model.MotionRoot.position.x, Is.EqualTo(holdPosition.x).Within(.002f));
                Assert.That(model.MotionRoot.position.z, Is.EqualTo(holdPosition.z).Within(.002f), "개봉과 확인 중에는 중앙이나 플레이어 영역으로 이동하지 않는다");
                if (turn.PhaseElapsed / opponentRevealDuration < .6f)
                {
                    opponentSamples++;
                    if (Quaternion.Angle(model.Watch.LidPivot.localRotation, Quaternion.identity) >= 85)
                    {
                        sawOpponentOpen = true; opponentOpenRotation = model.MotionRoot.rotation;
                        Assert.That(dial.forward.z, Is.GreaterThan(.25f), "상대가 읽는 문자판 앞면은 +z 상대 방향을 향한다");
                    }
                }
                input.Set(mouse.position, opponentSamples % 2 == 0 ? camera.pixelRect.min + Vector2.one * 24 : camera.pixelRect.max - Vector2.one * 24);
                yield return null;
            }
            Assert.That(opponentSamples, Is.GreaterThan(0)); Assert.That(sawOpponentOpen, Is.True, "자기 영역에서 상대 방향으로 실제 덮개를 연다");
            Assert.That(sawCommittedCue, Is.False, "기존 상대 행동 문구를 중앙 안내와 중복하지 않는다");
            Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.ResultPose));
            Assert.That(turn.IsResultPublic, Is.True);
            AssertOverviewCamera();
            Assert.That(model.MotionRoot.position.x, Is.EqualTo(holdPosition.x).Within(.002f));
            Assert.That(model.MotionRoot.position.z, Is.EqualTo(holdPosition.z).Within(.002f));
            Assert.That(model.MotionRoot.position.y, Is.EqualTo(holdPosition.y + .18f).Within(.002f), "확인 자세에서 공개 관찰을 마친 뒤 별도 정산 단계에서 내려놓는다");
            Assert.That(Quaternion.Angle(opponentOpenRotation, model.MotionRoot.rotation), Is.LessThan(.1f), "확인 결과를 공개해도 상대가 읽는 방향을 유지한다");
            Assert.That(dial.forward.z, Is.GreaterThan(.25f), "공개 관찰 때도 문자판 앞면은 +z 상대 방향을 향한다");
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(index)); Assert.That(controller.View.Clocks[index].H2Presentation.IsOpen, Is.True);
            var face = controller.Match.GetClock(index).Face.Value;
            Assert.That(model.DisplayedHours, Is.EqualTo(face.Hours));
            Assert.That(model.IsRed, Is.EqualTo(face.Kind == ClockKind.Red));
            Assert.That(Quaternion.Angle(model.Watch.LidPivot.localRotation, Quaternion.identity), Is.EqualTo(109).Within(.1f));
            Assert.That(Quaternion.Angle(model.Watch.HourHand.localRotation, Quaternion.Euler(0, 0, (face.Hours % 12) * 30)), Is.LessThan(.01f));
            Assert.That(model.Materials.C2Enabled, Is.True);
            yield return null;
            AssertOverviewCamera();
            AssertPublicClockValue(index, face);
            yield return new WaitForSecondsRealtime(.4f);
            Canvas.ForceUpdateCanvases();
            Assert.That(controller.View.PublicActionLabel.text, Is.Empty, "확정 Cue는 약 1.1초 뒤 자동으로 사라진다");
            Assert.That(controller.View.PublicActionLabel.gameObject.activeInHierarchy, Is.False);
            float finishDeadline = Time.realtimeSinceStartup + 7;
            while (controller.Match.ActiveParticipant == Participant.Ai && Time.realtimeSinceStartup < finishDeadline)
            {
                AssertOverviewCamera();
                yield return null;
            }
            Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Player));
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingItems));
            controller.EndItemPhase();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.UsedCount, Is.EqualTo(1)); Assert.That(controller.View.Clocks[index].gameObject.activeSelf, Is.False);
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
        }

        [UnityTest] public IEnumerator AiSkipAndRestartReleaseOwnedOverviewAndDoNotLeaveAnOldPublicFace()
        {
            settings.AiThinkDuration = .2f; settings.RevealDuration = .3f;
            settings.Rules.FirstParticipant = Participant.Ai;
            settings.Rules.InitialItemCount = 1;
            turn.SelectDuration = turn.RevealEnterDuration = 5;
            controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(controller.Match.GetInventory(Participant.Ai).Count, Is.EqualTo(1));
            AssertOverviewCamera();
            yield return Wait(() => turn.IsSelecting);
            AssertOverviewCamera(); PressSpace();
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.DecisionHold);
            AssertOverviewCamera(); AssertHidden(); AssertNoPublicClockValues();
            Assert.That(turn.IsResultPublic, Is.False);
            Assert.That(controller.Match.UsedCount, Is.Zero);
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.RevealEnter);
            int index = controller.Match.CurrentClock;
            AssertOverviewCamera(); PressSpace();
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.ResultPose);
            AssertOverviewCamera();
            Assert.That(turn.IsResultPublic, Is.True);
            Assert.That(controller.View.Clocks[index].H2Presentation.IsOpen, Is.True);
            Assert.That(controller.Match.UsedCount, Is.Zero, "표현 단축이 실제 정산을 중복 실행하지 않는다");
            settings.Rules.FirstParticipant = Participant.Player;
            settings.Rules.InitialItemCount = 0;
            controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            Assert.That(turn.IsOpponentTurn, Is.False);
            Assert.That(turn.IsResultPublic, Is.False);
            Assert.That(focus.IsPresentationCameraOwned, Is.False);
            Assert.That(controller.Match.UsedCount, Is.Zero);
            Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
            Assert.That(Quaternion.Angle(camera.transform.rotation, focus.OverviewRotation), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(focus.OverviewFieldOfView));
            AssertHidden(); AssertNoPublicClockValues();
        }

        [UnityTest] public IEnumerator KeyboardHelpShowsOnlyItsMouseCloseControlAndOptionalHudStaysHidden()
        {
            var match = controller.Match;
            Assert.That(controller.View.HelpButton.gameObject.activeInHierarchy, Is.False);
            Assert.That(controller.View.SkipButton.gameObject.activeInHierarchy, Is.False);
            var batch = (TMPro.TextMeshPro)new SerializedObject(controller.View).FindProperty("batchLabel").objectReferenceValue;
            Assert.That(batch.text, Does.Contain("01"));
            Assert.That(batch.GetComponent<Renderer>().enabled, Is.False, "묶음 텍스트 API는 남기고 화면 표시를 숨긴다");
            Assert.That(batch.transform.parent.GetComponentsInChildren<SpriteRenderer>(true).All(r => !r.enabled), Is.True, "빈 임시 묶음 받침도 숨긴다");
            var counter = controller.View.PlayerLife.GetComponentInParent<TabletopFocusTarget>();
            Assert.That(focus.Focus(counter), Is.True); yield return Wait(() => !focus.IsTransitioning);
            input.Press(Keyboard.current.hKey); InputSystem.Update(); controller.SendMessage("Update");
            input.Release(Keyboard.current.hKey); InputSystem.Update(); yield return null;
            Assert.That(controller.View.IsHelpVisible, Is.True);
            yield return GameUiTestInput.Click(input, mouse, controller.View.HelpButton);
            Assert.That(controller.View.IsHelpVisible, Is.False);
            Assert.That(controller.View.HelpButton.gameObject.activeInHierarchy, Is.False);
            Assert.That(controller.Match, Is.SameAs(match)); Assert.That(match.CurrentClock, Is.EqualTo(-1));
        }

        [UnityTest] public IEnumerator PublicClockValuesUseTheActualPreviewFacesAndClearWhenTheBatchCloses()
        {
            settings.PreviewDuration = 20;
            controller.RestartGame();
            yield return Wait(() => controller.View.BatchReveal.Phase == H2BatchRevealPresentation.RevealPhase.Observing);
            yield return null;
            Assert.That(controller.View.ClockValueLabels.Count, Is.EqualTo(5));
            for (int i = 0; i < 5; i++)
            {
                var face = controller.Match.GetClock(i).Face;
                Assert.That(face.HasValue, Is.True);
                AssertPublicValueLabel(i, face.Value);
            }
            for (int i = 0; i < 5; i++)
                for (int j = i + 1; j < 5; j++)
                    Assert.That(PublicCardScreenRect(i).Overlaps(PublicCardScreenRect(j)), Is.False,
                        "최초 공개의 5개 시간 패널을 서로 겹치지 않는다: " + i + " / " + j);
            Assert.That(controller.View.PublicActionLabel.text, Is.Empty);
            Assert.That(controller.View.PublicTransferLabel.text, Is.Empty);
            settings.PreviewDuration = .12f;
            controller.RestartGame();
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            yield return null;
            AssertHidden(); AssertNoPublicClockValues();
        }

        [UnityTest] public IEnumerator RestartAndDisableReleaseEveryMotionStageWithoutStaleWrites()
        {
            foreach (var phase in new[] { H2TurnPresentation.TurnPhase.Selecting, H2TurnPresentation.TurnPhase.DecisionHold,
                H2TurnPresentation.TurnPhase.RevealEnter, H2TurnPresentation.TurnPhase.ResultPose, H2TurnPresentation.TurnPhase.SettlementTransition })
            {
                controller.RestartGame(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                controller.SelectClock(0);
                if (phase != H2TurnPresentation.TurnPhase.Selecting)
                {
                    yield return Wait(() => !controller.IsPresentingClock);
                    if (phase != H2TurnPresentation.TurnPhase.DecisionHold) controller.ChooseAction(TimeAction.Ration);
                }
                yield return Wait(() => turn.Phase == phase);
                if (phase == H2TurnPresentation.TurnPhase.DecisionHold)
                {
                    Assert.That(focus.Focus(controller.View.PlayerLife.GetComponentInParent<TabletopFocusTarget>()), Is.True);
                    yield return Wait(() => !focus.IsTransitioning);
                }
                controller.enabled = false;
                Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.Idle)); Assert.That(focus.IsPresentationCameraOwned, Is.False);
                Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
                Assert.That(camera.fieldOfView, Is.EqualTo(focus.OverviewFieldOfView));
                foreach (var t in controller.View.Clocks) Assert.That(t.H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(camera.transform.position, Is.EqualTo(focus.OverviewPosition));
                controller.enabled = true; Assert.That(controller.Match.Batch, Is.EqualTo(1)); Assert.That(controller.Match.UsedCount, Is.Zero);
            }
        }

        [UnityTest] public IEnumerator DisablingTheOptionalPresentationFallsBackWithoutLeavingInputOrCameraLocked()
        {
            foreach (var phase in new[] { H2TurnPresentation.TurnPhase.Selecting, H2TurnPresentation.TurnPhase.RevealEnter,
                H2TurnPresentation.TurnPhase.ResultPose, H2TurnPresentation.TurnPhase.SettlementTransition })
            {
                turn.enabled = true; controller.RestartGame(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
                controller.SelectClock(3);
                if (phase != H2TurnPresentation.TurnPhase.Selecting)
                { yield return GameUiTestInput.ClockMotionReady(controller); controller.ChooseAction(TimeAction.Ration); }
                yield return Wait(() => turn.Phase == phase); turn.enabled = false;
                if (phase == H2TurnPresentation.TurnPhase.Selecting)
                    yield return Wait(() => !controller.IsPresentingClock);
                Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.Idle));
                if (phase == H2TurnPresentation.TurnPhase.Selecting) controller.ChooseAction(TimeAction.Ration);
                Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.Revealing));
                var face = controller.Match.GetClock(3).Face.Value; var model = controller.View.Clocks[3].H2Presentation;
                Assert.That(model.IsOpen, Is.True); Assert.That(model.DisplayedHours, Is.EqualTo(face.Hours));
                Assert.That(model.IsRed, Is.EqualTo(face.Kind == ClockKind.Red));
                yield return Wait(() => controller.Match.UsedCount > 0);
                Assert.That(focus.IsPresentationCameraOwned, Is.False);
            }
        }

        [UnityTest] public IEnumerator SpaceCompletesOnlyTheActiveMotionAndNeverCommitsTheClosedDecision()
        {
            turn.SelectDuration = turn.RevealEnterDuration = turn.SettlementTransitionDuration = 10;
            controller.SelectClock(4);
            Assert.That(controller.View.SkipButton.gameObject.activeInHierarchy, Is.False, "모션 중에도 단축 버튼을 상시 노출하지 않는다");
            PressSpace();
            yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(turn.Phase, Is.EqualTo(H2TurnPresentation.TurnPhase.DecisionHold)); AssertHidden();
            PressSpace(); yield return null;
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction)); AssertHidden();
            controller.ChooseAction(TimeAction.Ration); PressSpace();
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.ResultPose);
            Assert.That(controller.View.Clocks[4].H2Presentation.IsOpen, Is.True);
            yield return Wait(() => turn.Phase == H2TurnPresentation.TurnPhase.SettlementTransition); PressSpace();
            yield return Wait(() => controller.Match.UsedCount > 0);
            yield return Wait(() => !controller.View.Lifecycle.IsRunning && !controller.View.Clocks[4].gameObject.activeSelf && controller.Match.UsedCount == 1);
            Assert.That(controller.Match.ActiveParticipant, Is.EqualTo(Participant.Ai));
            Assert.That(controller.Match.UsedCount, Is.EqualTo(1));
            AssertOverviewCamera();
            controller.RestartGame();
            int key = TimeTheftItemTestSetup.Give(controller.Match, Participant.Player, ItemKind.SpareKey);
            yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingItems);
            controller.UseItem(key); yield return Wait(() => !controller.IsPresentingItem);
            controller.EndItemPhase(); yield return Wait(() => controller.Match.Stage == TurnStage.AwaitingClock);
            controller.SelectClock(0); PressSpace(); yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingKeyDecision));
            yield return null;
            AssertNoPublicClockValues();
            Assert.That(controller.View.PublicActionLabel.text, Is.Empty, "플레이어 열쇠의 개인 정보는 공용 행동 라벨에 쓰지 않는다");
            controller.SelectClock(1); PressSpace(); yield return GameUiTestInput.ClockMotionReady(controller);
            Assert.That(controller.Match.CurrentClock, Is.EqualTo(1)); Assert.That(controller.Match.Stage, Is.EqualTo(TurnStage.AwaitingAction));
            AssertHidden(); Assert.That(controller.View.Clocks[0].H2Presentation.MotionRoot.localPosition, Is.EqualTo(Vector3.zero));
        }

        private void AssertNoPublicClockValues()
        {
            Canvas.ForceUpdateCanvases();
            Assert.That(controller.View.ClockValueLabels.Count, Is.EqualTo(5));
            foreach (var label in controller.View.ClockValueLabels)
            {
                Assert.That(label, Is.Not.Null);
                Assert.That(label.text, Is.Empty, "닫힌 시계에는 이전 공개 시간도 남기지 않는다");
                Assert.That(label.gameObject.activeInHierarchy, Is.False);
                Assert.That(label.raycastTarget, Is.False);
                Assert.That(label.transform.parent.Find("Kind"), Is.Null, "시계 종류용 별도 UI 오브젝트를 남기지 않는다");
                Assert.That(label.transform.parent.GetComponent<UiIndexGraphic>().raycastTarget, Is.False);
            }
        }

        private void AssertNoUncommittedAiAction()
        {
            Canvas.ForceUpdateCanvases();
            Assert.That(controller.View.PublicActionLabel.text, Does.Not.Contain("징수"));
            Assert.That(controller.View.PublicActionLabel.text, Does.Not.Contain("배급"));
            Assert.That(controller.View.PublicTransferLabel.text, Is.Empty, "AI가 결정하기 전에는 정산 단서가 없어야 한다");
        }

        private void AssertPublicClockValue(int index, ClockFace face)
        {
            Assert.That(controller.View.ClockValueLabels.Count, Is.EqualTo(5));
            AssertPublicValueLabel(index, face);
            for (int i = 0; i < 5; i++) if (i != index)
            {
                Assert.That(controller.Match.GetClock(i).Face, Is.Null);
                Assert.That(controller.View.ClockValueLabels[i].text, Is.Empty);
                Assert.That(controller.View.ClockValueLabels[i].gameObject.activeInHierarchy, Is.False);
            }
        }

        private void AssertPublicValueLabel(int index, ClockFace face)
        {
            Canvas.ForceUpdateCanvases();
            var label = controller.View.ClockValueLabels[index];
            Assert.That(label, Is.Not.Null); Assert.That(label.isActiveAndEnabled, Is.True);
            Assert.That(label.text, Is.EqualTo(GameTime.Format(face.Hours * 60)), "공개 시간은 HH:00 숫자 하나로 표시한다");
            Assert.That(label.color, Is.EqualTo(face.Kind == ClockKind.Red ? new Color(.79f, .43f, .40f) : new Color(.91f, .88f, .78f)));
            Assert.That(label.GetPreferredValues(label.text, label.rectTransform.rect.width, 0).y,
                Is.LessThanOrEqualTo(label.rectTransform.rect.height + 1), "시간 숫자가 한 줄로 프레임 안에 들어온다");
            Assert.That(label.raycastTarget, Is.False, "공개 값 표시가 월드 클릭을 가리지 않는다");
            var card = (RectTransform)label.transform.parent;
            Assert.That(card.Find("Kind"), Is.Null, "숫자와 별도 종류 라벨을 중복 표시하지 않는다");
            Assert.That(card.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Length, Is.EqualTo(1));
            var frame = card.GetComponent<UiIndexGraphic>();
            Assert.That(frame.enabled, Is.True);
            Assert.That(frame.Layout, Is.EqualTo(UiIndexGraphic.FrameLayout.Compact));
            Assert.That(frame.raycastTarget, Is.False);
            Assert.That(card.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            float lidTop = float.NegativeInfinity;
            foreach (var filter in controller.View.Clocks[index].H2Presentation.Watch.LidPivot.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh) continue;
                // 회전한 덮개의 AABB 모서리는 실제 모델 밖으로 돌출될 수 있다.
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var screen = camera.WorldToScreenPoint(filter.transform.TransformPoint(vertex));
                    lidTop = Mathf.Max(lidTop, screen.y);
                }
            }
            Assert.That(float.IsInfinity(lidTop), Is.False, "실제 덮개 메시를 기준으로 간격을 검사한다");
            Assert.That(PublicCardScreenRect(index).yMin - lidTop, Is.GreaterThanOrEqualTo(1f),
                "공개 시간 패널은 열린 덮개 위에 여백을 둔다: slot=" + index + " screen=" + Screen.width + "x" + Screen.height +
                " rect=" + PublicCardScreenRect(index) + " lidTop=" + lidTop);
        }

        private Rect PublicCardScreenRect(int index)
        {
            var card = (RectTransform)controller.View.ClockValueLabels[index].transform.parent;
            var corners = new Vector3[4]; card.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(null, corner);
                min = Vector2.Min(min, screen); max = Vector2.Max(max, screen);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private void AssertOverviewCamera()
        {
            Assert.That(focus.IsPresentationCameraOwned, Is.True, "AI 대기부터 공개까지 자유 시차와 Focus 입력을 소유한다");
            Assert.That(Vector3.Distance(camera.transform.position, focus.OverviewPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, focus.OverviewRotation), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(focus.OverviewFieldOfView).Within(.0001f));
        }

        private void PressSpace()
        {
            input.Press(Keyboard.current.spaceKey); InputSystem.Update(); controller.SendMessage("Update");
            input.Release(Keyboard.current.spaceKey); InputSystem.Update();
        }

        private void AssertHidden()
        {
            Assert.That(Enumerable.Range(0, 5).All(i => !controller.Match.GetClock(i).Face.HasValue), Is.True);
            foreach (var c in controller.View.Clocks)
            {
                Assert.That(c.H2Presentation.IsOpen || c.H2Presentation.IsRed, Is.False);
                Assert.That(c.H2Presentation.DisplayedHours, Is.Null);
                Assert.That(Quaternion.Angle(c.H2Presentation.Watch.LidPivot.localRotation, Quaternion.identity), Is.LessThan(.001));
            }
        }
        private IEnumerator Wait(Func<bool> condition)
        {
            float end = Time.realtimeSinceStartup + 7;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(condition(), Is.True, controller.Match.Stage + " / " + turn.Phase);
        }
    }
}
#endif
