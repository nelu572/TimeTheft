using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class FinalAssetIntegrationTests
    {
        private const string ItemRoot = "Assets/TimeTheft/Art/ItemsM410/Runtime/Prefabs/";
        private const string Character = "Assets/TimeTheft/OpponentCharacter/Final/TimeTheft_Opponent.prefab";

        [Test]
        public void MainSceneBindsFinalCharacterAndSixItemModelsWithoutMissingReferences()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
            try
            {
                var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
                var character = objects.Single(t => PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == Character);
                Assert.That(Vector3.Distance(character.position, new Vector3(0, 3.70f, 3.77f)), Is.LessThan(.001f), "사용자가 조정한 상대 위치를 보존한다");
                Assert.That(Quaternion.Angle(character.rotation, Quaternion.identity), Is.LessThan(.001f));
                Assert.That(character.localScale, Is.EqualTo(Vector3.one));
                var inventory = objects.Select(t => t.GetComponent<ItemWorldInventoryView>()).Single(value => value);
                var bindings = new SerializedObject(inventory).FindProperty("modelPrefabs");
                Assert.That(bindings, Is.Not.Null);
                Assert.That(bindings.arraySize, Is.EqualTo(6));
                foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
                {
                    string path = ItemRoot + kind + "Runtime.prefab";
                    var prefab = bindings.GetArrayElementAtIndex((int)kind).objectReferenceValue as GameObject;
                    Assert.That(prefab, Is.Not.Null, kind.ToString());
                    Assert.That(AssetDatabase.GetAssetPath(prefab), Is.EqualTo(path));
                    Assert.That(File.Exists(path + ".meta"), Is.True);
                    Assert.That(prefab.GetComponents<MonoBehaviour>().Any(c => c && c.GetType().Name == "ItemPresentationController"), Is.True);
                    CheckObjects(prefab.GetComponentsInChildren<Transform>(true));
                }
                CheckObjects(objects);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void CheckObjects(Transform[] objects)
        {
            foreach (var transform in objects)
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
                foreach (var component in transform.GetComponents<Component>())
                {
                    if (!component) continue;
                    var property = new SerializedObject(component).GetIterator();
                    while (property.NextVisible(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceInstanceIDValue != 0)
                            Assert.That(property.objectReferenceValue, Is.Not.Null, transform.name + "/" + property.propertyPath);
                }
                foreach (var renderer in transform.GetComponents<Renderer>())
                    foreach (var material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, renderer.name);
                        Assert.That(material.shader, Is.Not.Null, material.name);
                        Assert.That(material.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), material.name);
                    }
            }
        }
    }
}
