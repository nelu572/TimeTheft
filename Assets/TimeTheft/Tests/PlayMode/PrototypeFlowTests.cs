using TimeTheft.Legacy;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace TimeTheft.Tests
{
    [Category("Legacy")]
    public sealed class PrototypeFlowTests
    {
        private PrototypeController controller;

        [UnitySetUp]
        public IEnumerator LoadPrototype()
        {
#if UNITY_EDITOR
            // 보존용 씬은 빌드에서 비활성이다. 메인 진입 씬을 바꾸지 않고 Editor에서 직접 연다.
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/SampleScene.unity");
#endif
            yield return null;
            controller = Object.FindFirstObjectByType<PrototypeController>();
            Assert.That(controller, Is.Not.Null);
            yield return WaitFor(RoundStage.Selecting);
        }

        [UnityTest]
        public IEnumerator TableRaycastsPrivacyAndThreeCompleteGames()
        {
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            for (int game = 0; game < 3; game++)
            {
                int rounds = 0;
                while (controller.Match.Stage != RoundStage.GameOver)
                {
                    Assert.That(++rounds, Is.LessThanOrEqualTo(30));
                    Assert.That(controller.View.Clocks, Has.Length.EqualTo(3));
                    for (int i = 0; i < controller.View.Clocks.Length; i++)
                    {
                        var watch = controller.View.Clocks[i];
                        Assert.That(watch.Button.interactable, Is.EqualTo(controller.Match.CanSelectClock(i)));
                        Assert.That(ValueText(watch), Is.EqualTo("?"));
                        Assert.That(watch.Group.alpha, Is.EqualTo(1).Within(.001));
                        Assert.That(Quaternion.Angle(watch.Rect.localRotation, Quaternion.identity), Is.LessThan(.01));
                    }
                    int selection = (rounds + game) % 3;
                    if (!controller.Match.CanSelectClock(selection)) selection = (selection + 1) % 3;
                    Click(controller.View.Clocks[selection].gameObject);
                    int selectedAi = controller.Match.AiClock;
                    // 더블클릭과 연타는 상태를 바꾸지 않는다.
                    controller.SelectClock((selection + 1) % 3);
                    Assert.That(controller.Match.PlayerClock, Is.EqualTo(selection));
                    Assert.That(controller.Match.AiClock, Is.EqualTo(selectedAi));
                    yield return WaitFor(RoundStage.Deciding);
                    Assert.That(ValueText(controller.View.Clocks[controller.Match.PlayerClock]), Is.EqualTo(controller.Match.PlayerValue.ToString()));
                    Assert.That(ValueText(controller.View.Clocks[controller.Match.AiClock]), Is.EqualTo("?"));
                    Assert.That(ValueText(controller.View.Clocks[controller.Match.DiscardedClock]), Is.EqualTo("?"));
                    Assert.That(controller.View.Clocks[controller.Match.DiscardedClock].Group.alpha, Is.Zero);
                    Assert.That(Vector2.Distance(controller.View.Clocks[controller.Match.PlayerClock].Rect.anchoredPosition,
                        controller.View.PlayerPosition), Is.LessThan(.01));
                    Assert.That(Vector2.Distance(controller.View.Clocks[controller.Match.AiClock].Rect.anchoredPosition,
                        controller.View.AiPosition), Is.LessThan(.01));
                    Assert.That(controller.View.Keep.gameObject.activeSelf, Is.True);
                    int beforePlayer = controller.Match.PlayerLife;
                    int beforeAi = controller.Match.AiLife;
                    int beforePot = controller.Match.Pot;
                    Click(rounds % 2 == 0 ? controller.View.Keep.gameObject : controller.View.Swap.gameObject);
                    ClockAction committed = controller.Match.PlayerAction;
                    controller.ChooseAction(committed == ClockAction.Keep ? ClockAction.Swap : ClockAction.Keep);
                    Assert.That(controller.Match.PlayerAction, Is.EqualTo(committed));
                    Assert.That(controller.View.Keep.gameObject.activeSelf, Is.False);
                    yield return WaitFor(RoundStage.Settling);
                    Assert.That(controller.Match.PlayerLife, Is.EqualTo(beforePlayer - controller.Match.PlayerLoss));
                    Assert.That(controller.Match.AiLife, Is.EqualTo(beforeAi - controller.Match.AiLoss));
                    Assert.That(controller.Match.Pot, Is.EqualTo(beforePot + controller.Match.PlayerLoss + controller.Match.AiLoss));
                    int discarded = controller.Match.DiscardedClock;
                    Assert.That(ValueText(controller.View.Clocks[discarded]), Is.EqualTo("?"));
                    Assert.That(Vector2.Distance(controller.View.Clocks[controller.Match.PlayerClock].Rect.anchoredPosition, controller.View.PlayerPosition), Is.LessThan(.01));
                    Assert.That(Vector2.Distance(controller.View.Clocks[controller.Match.AiClock].Rect.anchoredPosition, controller.View.AiPosition), Is.LessThan(.01));
                    yield return WaitForNextRoundOrGameOver();
                    if (controller.Match.Stage == RoundStage.GameOver)
                    {
                        // GameOver 상태를 먼저 확정하고 배당 연출이 끝난 뒤 결과를 연다.
                        float deadline = Time.realtimeSinceStartup + 5;
                        while (!controller.View.Replay.gameObject.activeInHierarchy && Time.realtimeSinceStartup < deadline)
                        {
                            controller.SkipAnimation();
                            yield return null;
                        }
                        Assert.That(controller.View.Replay.gameObject.activeInHierarchy, Is.True);
                        Assert.That(controller.Match.PlayerLife + controller.Match.AiLife + controller.Match.Pot, Is.EqualTo(60));
                        yield return null;
                    }
                }
                Click(controller.View.Replay.gameObject);
                yield return WaitFor(RoundStage.Selecting);
                Assert.That(controller.Match.Round, Is.EqualTo(1));
                Assert.That(controller.Match.PlayerLife, Is.EqualTo(30));
                Assert.That(controller.Match.AiLife, Is.EqualTo(30));
                Assert.That(controller.Match.Pot, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator EditedSlotsAndRepeatedInitializationPreserveSceneObjects()
        {
            var view = controller.View;
            var canvas = view.GetComponentInChildren<Canvas>();
            var watches = (ClockView[])view.Clocks.Clone();
            view.Initialize(controller.SelectClock, controller.ChooseAction, controller.RestartGame, controller.SkipAnimation);
            Assert.That(view.GetComponentInChildren<Canvas>(), Is.SameAs(canvas));
            Assert.That(view.GetComponentsInChildren<ClockView>(), Has.Length.EqualTo(3));

            var table = view.transform.Find("TimeTheftCanvas/Table");
            var slots = (RectTransform)table.Find("Slots");
            slots.anchoredPosition += new Vector2(12, -8);
            slots.localRotation = Quaternion.Euler(0, 0, 4);
            slots.localScale = new Vector3(.92f, .94f, 1);
            var clockRoot = (RectTransform)table.Find("ClockRoot");
            clockRoot.anchoredPosition = new Vector2(-9, 5);
            clockRoot.localRotation = Quaternion.Euler(0, 0, -3);
            clockRoot.localScale = new Vector3(.98f, 1.02f, 1);

            controller.RestartGame();
            yield return WaitFor(RoundStage.Selecting);
            CollectionAssert.AreEqual(watches, view.Clocks);
            for (int i = 0; i < 3; i++)
                Assert.That(Vector3.Distance(view.Clocks[i].transform.position, slots.Find("DealSlot" + i).position), Is.LessThan(.01));

            Click(view.Clocks[1].gameObject);
            yield return WaitFor(RoundStage.Deciding);
            AssertAllocatedToSlots(view, slots);
            Assert.That(Vector3.Distance(view.Clocks[controller.Match.DiscardedClock].transform.position,
                slots.Find("DiscardSlot").position), Is.LessThan(.01));

            Click(view.Swap.gameObject);
            yield return WaitFor(RoundStage.Settling);
            AssertAllocatedToSlots(view, slots);
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(view.GetComponentInChildren<Canvas>(), Is.SameAs(canvas));
        }

        private void AssertAllocatedToSlots(TableView view, RectTransform slots)
        {
            Assert.That(Vector3.Distance(view.Clocks[controller.Match.PlayerClock].transform.position,
                slots.Find("PlayerSlot").position), Is.LessThan(.01));
            Assert.That(Vector3.Distance(view.Clocks[controller.Match.AiClock].transform.position,
                slots.Find("AiSlot").position), Is.LessThan(.01));
        }

        [UnityTest]
        public IEnumerator RestartCancelsPendingAnimationsAndOldInputs()
        {
            for (int restart = 0; restart < 3; restart++)
            {
                Click(controller.View.Clocks[restart % 3].gameObject);
                if (restart == 1)
                {
                    yield return WaitFor(RoundStage.Deciding);
                    Click(controller.View.Swap.gameObject);
                }
                if (restart == 2)
                {
                    yield return WaitFor(RoundStage.Deciding);
                    Click(controller.View.Keep.gameObject);
                    yield return WaitFor(RoundStage.Settling);
                }
                Click(controller.View.Restart.gameObject);
                controller.ChooseAction(ClockAction.Swap);
                yield return WaitFor(RoundStage.Selecting);
                Assert.That(controller.Match.Round, Is.EqualTo(1));
                Assert.That(controller.Match.PlayerLife, Is.EqualTo(30));
                Assert.That(controller.Match.AiLife, Is.EqualTo(30));
                Assert.That(controller.Match.Pot, Is.Zero);
                Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator AiFirstRoundLeavesOnlyTwoPlayerChoicesAndKeepsTakenClockPrivate()
        {
            Click(controller.View.Clocks[0].gameObject);
            yield return WaitFor(RoundStage.Deciding);
            Click(controller.View.Keep.gameObject);
            yield return WaitFor(RoundStage.Selecting);
            Assert.That(controller.Match.Round, Is.EqualTo(2));
            Assert.That(controller.Match.PlayerSelectsFirst, Is.False);
            int ai = controller.Match.AiClock;
            Assert.That(ai, Is.InRange(0, 2));
            Assert.That(controller.Match.PlayerClock, Is.EqualTo(-1));
            Assert.That(controller.Match.DiscardedClock, Is.EqualTo(-1));
            int choices = 0;
            for (int i = 0; i < 3; i++)
            {
                Assert.That(ValueText(controller.View.Clocks[i]), Is.EqualTo("?"));
                Assert.That(controller.View.Clocks[i].Button.interactable, Is.EqualTo(i != ai));
                if (controller.View.Clocks[i].Button.interactable) choices++;
            }
            Assert.That(choices, Is.EqualTo(2));
            Assert.That(Vector2.Distance(controller.View.Clocks[ai].Rect.anchoredPosition,
                controller.View.AiPosition), Is.LessThan(.01));
            controller.SelectClock(ai);
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Selecting));
            Assert.That(controller.Match.PlayerClock, Is.EqualTo(-1));
            Click(controller.View.Clocks[(ai + 1) % 3].gameObject);
            yield return WaitFor(RoundStage.Deciding);
            Assert.That(controller.Match.AiClock, Is.EqualTo(ai));
        }

        private IEnumerator WaitFor(RoundStage target)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (controller.Match.Stage != target && Time.realtimeSinceStartup < deadline)
            {
                controller.SkipAnimation();
                yield return null;
            }
            Assert.That(controller.Match.Stage, Is.EqualTo(target), controller.Match.DebugSnapshot());
            // UI 활성화 후 Canvas/레이캐스트 등록이 완료된 프레임부터 클릭한다.
            yield return null;
        }

        private IEnumerator WaitForNextRoundOrGameOver()
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (controller.Match.Stage != RoundStage.Selecting && controller.Match.Stage != RoundStage.GameOver && Time.realtimeSinceStartup < deadline)
            {
                controller.SkipAnimation();
                yield return null;
            }
            Assert.That(controller.Match.Stage, Is.EqualTo(RoundStage.Selecting).Or.EqualTo(RoundStage.GameOver));
            yield return null;
        }

        private static string ValueText(ClockView watch) => watch.transform.Find("Watch/Value").GetComponent<TextMeshProUGUI>().text;

        private static void Click(GameObject target)
        {
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, target.transform.position),
                button = PointerEventData.InputButton.Left
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), "UI 레이캐스트: " + target.name);
            Assert.That(hits[0].gameObject.transform.IsChildOf(target.transform), Is.True,
                target.name + " 앞을 가리는 오브젝트: " + hits[0].gameObject.name);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
    }
}
