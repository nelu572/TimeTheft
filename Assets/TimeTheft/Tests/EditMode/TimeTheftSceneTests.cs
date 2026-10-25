using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace TimeTheft.Tests
{
    public sealed class TimeTheftSceneTests
    {
        [Test]
        public void MainSceneHasOneNewEntryPointFiveReplaceableClocksAndOriginalArt()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
            try
            {
                var roots = scene.GetRootGameObjects();
                var controllers = roots.SelectMany(r => r.GetComponentsInChildren<TimeTheftGameController>(true)).ToArray();
                Assert.That(controllers, Has.Length.EqualTo(1));
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<TabletopGameController>(true)), Is.Empty);
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<TabletopGameView>(true)), Is.Empty);
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<TabletopDecisionUI>(true)), Is.Empty);
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<TabletopRoundFeedback>(true)), Is.Empty);
                var view = controllers[0].GetComponent<TimeTheftGameView>();
                Assert.That(controllers[0].Settings, Is.Not.Null);
                Assert.That(view.Clocks, Has.Length.EqualTo(5));
                foreach (var clock in view.Clocks)
                {
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(clock.gameObject), Is.EqualTo("Assets/TimeTheft/Prefabs/ClockToken.prefab"));
                    Assert.That(clock.SelectionTarget, Is.Not.Null);
                    Assert.That(clock.SelectionTarget.FocusOnClick, Is.False, "시계 클릭은 선택만 하고 기존 카메라 시점을 바꾸지 않는다");
                    Assert.That(clock.GetComponent<Collider>(), Is.Not.Null);
                }
                Assert.That(controllers[0].Settings.PreviewDuration, Is.EqualTo(5.5f).Within(.001f));
                foreach (var label in new[] { view.PlayerLife, view.AiLife, view.Bank })
                    Assert.That(label.GetComponentInParent<TabletopFocusTarget>().name, Is.EqualTo("IntegratedTimeCounter"));
                var serialized = new SerializedObject(view);
                var p = serialized.GetIterator();
                while (p.NextVisible(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference) Assert.That(p.objectReferenceValue, Is.Not.Null, p.propertyPath);
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).Count(), Is.EqualTo(1));
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Any(t =>
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == "Assets/TimeTheft/Table/Prefabs/TimeTheft_Table_T3.prefab"), Is.True);
                var counter = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "IntegratedTimeCounter");
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(counter.gameObject), Is.EqualTo("Assets/TimeTheft/Counter/IntegratedTimeCounter.prefab"));
                Assert.That(counter.localPosition.x, Is.EqualTo(-3.75f).Within(.001));
                Assert.That(counter.localScale, Is.EqualTo(Vector3.one * .6f));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void EveryPreservedSceneHasValidScriptsAndBuildStartsAtMainOnly()
        {
            foreach (string path in new[] { "TimeTheftGame", "TabletopGame", "TabletopStudy", "SampleScene", "CounterStructureStudy" })
            {
                var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/" + path + ".unity");
                try
                {
                    foreach (var go in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
                        Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go.gameObject), Is.Zero, path + "/" + go.name);
                    var legacy = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TabletopGameController>(true)).ToArray();
                    Assert.That(legacy.Length, Is.EqualTo(path == "TabletopGame" ? 1 : 0));
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            Assert.That(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                Is.EqualTo(new[] { "Assets/Scenes/TimeTheftGame.unity" }));
        }
    }
}
