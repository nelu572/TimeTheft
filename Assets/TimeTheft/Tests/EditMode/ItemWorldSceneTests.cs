using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class ItemWorldSceneTests
    {
        [Test]
        public void SharedItemPrefabKeepsReplaceableVisualKoreanLabelAndStableClickRoot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TimeTheft/Prefabs/ItemWorld.prefab");
            Assert.That(prefab, Is.Not.Null);
            var view = prefab.GetComponent<ItemWorldView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.Target, Is.SameAs(prefab.GetComponent<TabletopFocusTarget>()));
            Assert.That(view.Target.FocusOnClick, Is.False);
            Assert.That(view.Target.FocusPoint, Is.Not.Null, "클릭 전용 대상도 기존 FocusTarget 가용성 계약을 만족한다");
            Assert.That(prefab.GetComponent<Collider>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Collider>().isTrigger, Is.False);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1));
            var visual = prefab.transform.Find("Visual");
            Assert.That(visual, Is.Not.Null);
            Assert.That(view.Label, Is.Not.Null);
            Assert.That(view.Label.transform.IsChildOf(visual), Is.True, "모델 교체는 표시 하위 계층에서 끝난다");
            Assert.That(view.Label.raycastTarget, Is.False);
            Assert.That(AssetDatabase.GetAssetPath(view.Label.font), Is.EqualTo("Assets/TimeTheft/Fonts/NanumGothic SDF.asset"));
            Assert.That(view.Label.font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic));
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject), Is.Zero, t.name);
        }

        [Test]
        public void MainSceneBindsIndependentWorldInventoryAndPreservesHiddenLayoutSamples()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
            try
            {
                var roots = scene.GetRootGameObjects();
                var world = roots.SelectMany(r => r.GetComponentsInChildren<ItemWorldInventoryView>(true)).Single();
                var view = roots.SelectMany(r => r.GetComponentsInChildren<TimeTheftGameView>(true)).Single();
                Assert.That(view.WorldItems, Is.SameAs(world));
                Assert.That(AssetDatabase.GetAssetPath(world.ItemPrefab), Is.EqualTo("Assets/TimeTheft/Prefabs/ItemWorld.prefab"));
                Assert.That(world.PlayerAnchor, Is.Not.Null);
                Assert.That(world.OpponentAnchor, Is.Not.Null);
                Assert.That(world.PlayerAnchor.position.z, Is.LessThan(-1));
                Assert.That(world.OpponentAnchor.position.z, Is.GreaterThan(1));
                Assert.That(world.PlayerAnchor.position.y, Is.EqualTo(1.0075f).Within(.01));
                Assert.That(world.OpponentAnchor.position.y, Is.EqualTo(1.0075f).Within(.01));
                var samples = roots.Single(r => r.name == "ItemSlots");
                Assert.That(samples.activeSelf, Is.False);
                Assert.That(world.transform.IsChildOf(samples.transform), Is.False);
                Assert.That(samples.GetComponentsInChildren<Collider>(true), Is.Empty);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
