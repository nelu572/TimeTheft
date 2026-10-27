using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class H2GameSceneTests
    {
        [Test]
        public void DisabledAdapterBeforePlayRestoresOriginalArtworkAtInitialization()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
            try
            {
                var clock = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TimeTheftGameView>(true)).Single().Clocks[0];
                clock.H2Presentation.enabled = false;
                clock.Initialize(0, null);
                Assert.That(clock.H2Presentation.Watch.gameObject.activeSelf, Is.False);
                Assert.That(clock.transform.Find("Visual/Case").GetComponent<Renderer>().enabled, Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCase(1)] [TestCase(12)] [TestCase(13)] [TestCase(24)]
        public void OpenHandsUseHoursAndExtendedValuesKeepTheExistingExactLabel(int hours)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
            try
            {
                var clock = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TimeTheftGameView>(true)).Single().Clocks[0];
                clock.Initialize(0, null);
                clock.Show(new ClockState(ClockVisibility.Open, new ClockFace(hours, ClockKind.Red)));
                Assert.That(clock.Number.text, Is.EqualTo(hours + "h"));
                Assert.That(clock.Number.GetComponent<Renderer>().enabled, Is.EqualTo(hours > 12));
                Assert.That(Quaternion.Angle(clock.H2Presentation.Watch.HourHand.localRotation, Quaternion.Euler(0, 0, 30 * (hours % 12))), Is.LessThan(.01f));
                clock.Show(new ClockState(ClockVisibility.Hidden, null));
                Assert.That(clock.H2Presentation.DisplayedHours, Is.Null);
                Assert.That(clock.Number.GetComponent<Renderer>().enabled, Is.False);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void MainKeepsFiveOriginalInputRootsWithSeparateH2Artwork()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
            try
            {
                var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TimeTheftGameView>(true)).Single();
                Assert.That(view.Clocks.Length, Is.EqualTo(5));
                for (int i = 0; i < 5; i++)
                {
                    var clock = view.Clocks[i];
                    Assert.That(clock.name, Is.EqualTo("Clock" + i));
                    Assert.That(clock.transform.localPosition, Is.EqualTo(new Vector3((i - 2) * .98f, 1.06f, 0)));
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(clock.gameObject), Is.EqualTo("Assets/TimeTheft/Prefabs/ClockToken.prefab"));
                    var hit = clock.GetComponent<BoxCollider>();
                    Assert.That(hit.size, Is.EqualTo(new Vector3(.82f, .82f, .1f)));
                    Assert.That(hit.center, Is.EqualTo(new Vector3(0, 0, -.02f)));
                    Assert.That(clock.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1), "덮개·고리에 별도 클릭 판정을 추가하지 않는다");
                    Assert.That(clock.SelectionTarget.FocusOnClick, Is.False);
                    var adapter = clock.H2Presentation;
                    Assert.That(adapter, Is.Not.Null);
                    var model = adapter.Watch.gameObject;
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model), Is.EqualTo("Assets/TimeTheft/PocketWatch/C2/Prefabs/H2_C2_PocketWatch.prefab"));
                    Assert.That(model.GetComponentsInChildren<MeshFilter>(true).Length, Is.EqualTo(14));
                    Assert.That(model.GetComponent<Animator>().enabled, Is.False, "이번 단계에는 Select/이동 클립을 재생하지 않는다");
                    Assert.That(model.transform.Find("H2_Watch/MotionRoot/VisualScale").localScale, Is.EqualTo(Vector3.one * .7f));
                    Assert.That(model.GetComponentsInChildren<Renderer>().Single(r => r.name == "Case").bounds.size.x, Is.EqualTo(.672f).Within(.00001));
                    Assert.That(clock.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(model.transform)).All(r => !r.enabled), Is.True);
                    Assert.That(adapter.Materials.C2Enabled, Is.True);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}

