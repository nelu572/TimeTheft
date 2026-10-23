using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TimeTheft.Tests
{
    public sealed class TabletopSceneTests
    {
        [Test]
        public void StudyKeepsCorePlayAreaVisibleAtEveryLookLimit()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TabletopStudy.unity");
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<PrototypeController>(true)), Is.Empty);
                var camera = Find(scene, "CameraRig/Main Camera").GetComponent<Camera>();
                Assert.That(camera.orthographic, Is.False);
                Assert.That(camera.GetComponent<TabletopCameraLook>(), Is.Not.Null);
                var settings = new SerializedObject(camera.GetComponent<TabletopCameraLook>());
                float yawLimit = settings.FindProperty("horizontalDegrees").floatValue;
                float pitchLimit = settings.FindProperty("verticalDegrees").floatValue;
                float horizontalTravel = settings.FindProperty("horizontalTravel").floatValue;
                float verticalTravel = settings.FindProperty("verticalTravel").floatValue;
                float strength = settings.FindProperty("strength").floatValue;
                Vector3 basePosition = camera.transform.localPosition;
                Quaternion baseRotation = camera.transform.localRotation;
                camera.aspect = 16f / 9;
                var surface = Find(scene, "Environment/Table/Surface");
                Assert.That(TableCoverage(camera, surface), Is.InRange(.6f, .7f));

                var renderers = new List<SpriteRenderer>();
                for (int i = 0; i < 3; i++)
                    renderers.Add(Find(scene, "ClockSlots/ClockSlot" + i + "/ClockPlaceholder/Case").GetComponent<SpriteRenderer>());
                renderers.Add(Find(scene, "Opponent/MannequinPlaceholder").GetComponent<SpriteRenderer>());
                foreach (Transform slot in Find(scene, "ItemSlots")) renderers.Add(slot.Find("SlotOutline").GetComponent<SpriteRenderer>());
                Assert.That(renderers, Has.Count.EqualTo(8));

                var rotations = renderers.Select(r => r.transform.rotation).ToArray();
                foreach (float y in new[] { -1f, 0, 1f })
                foreach (float x in new[] { -1f, 0, 1f })
                {
                    camera.transform.localRotation = baseRotation * Quaternion.Euler(-y * pitchLimit * strength, x * yawLimit * strength, 0);
                    camera.transform.localPosition = basePosition + new Vector3(x * horizontalTravel, y * verticalTravel, 0) * strength;
                    foreach (var renderer in renderers)
                    {
                        Assert.That(renderer.sprite, Is.Not.Null, renderer.name);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(renderer.sprite));
                        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
                        Assert.That(importer.mipmapEnabled, Is.False);
                        Assert.That(renderer.sharedMaterial.shader.isSupported, Is.True);
                        // 투명 여백까지 포함한 보수적인 월드 AABB로 잘림을 확인한다.
                        Bounds bounds = renderer.bounds;
                        for (int corner = 0; corner < 8; corner++)
                        {
                            var world = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                                (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                            Vector3 viewport = camera.WorldToViewportPoint(world);
                            Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane), renderer.name);
                            Assert.That(viewport.x, Is.InRange(0f, 1f), renderer.transform.parent.name);
                            Assert.That(viewport.y, Is.InRange(0f, 1f), renderer.transform.parent.name);
                        }
                    }
                    for (int i = 0; i < renderers.Count; i++)
                    {
                        Assert.That(renderers[i].transform.IsChildOf(camera.transform.parent), Is.False);
                        Assert.That(Quaternion.Angle(rotations[i], renderers[i].transform.rotation), Is.LessThan(.001f));
                    }
                }

                // 회전을 동일하게 둔 채 위치 이동만 비교해 깊이에 따른 시차를 분리한다.
                camera.transform.localRotation = baseRotation;
                camera.transform.localPosition = basePosition;
                Vector3 near = renderers[1].bounds.center, far = renderers[3].bounds.center;
                Vector3 nearBefore = camera.WorldToViewportPoint(near), farBefore = camera.WorldToViewportPoint(far);
                camera.transform.localPosition += Vector3.right * horizontalTravel;
                float nearShift = Mathf.Abs(camera.WorldToViewportPoint(near).x - nearBefore.x);
                float farShift = Mathf.Abs(camera.WorldToViewportPoint(far).x - farBefore.x);
                Assert.That(nearBefore.z, Is.LessThan(farBefore.z));
                Assert.That(nearShift, Is.GreaterThan(farShift * 1.2f));
                Assert.That(nearShift - farShift, Is.GreaterThan(.003f));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void InstalledDevicesAndFocusPointsUsePhysicalWorldPoses()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TabletopStudy.unity");
            try
            {
                var camera = Find(scene, "CameraRig/Main Camera").GetComponent<Camera>();
                camera.aspect = 16f / 9;
                Assert.That(camera.GetComponent<TabletopFocusCamera>(), Is.Not.Null);
                var targets = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TabletopFocusTarget>(true)).ToArray();
                Assert.That(targets, Has.Length.EqualTo(5));
                foreach (var target in targets)
                {
                    Assert.That(target.FocusPoint, Is.Not.Null, target.name);
                    Assert.That(target.FocusPoint.IsChildOf(target.transform), Is.True, target.name);
                    Assert.That(target.GetComponentsInChildren<Collider>(), Is.Not.Empty, target.name);
                    Assert.That(target.TransitionDuration, Is.GreaterThan(0));
                    camera.transform.SetPositionAndRotation(target.FocusPoint.position, target.FocusPoint.rotation);
                    camera.fieldOfView = target.FieldOfView;
                    var visual = target.name == "CentralTimeBank" ? target.transform.Find("Face/Housing")
                        : target.name == "ClockPlaceholder" ? target.transform.Find("Case") : target.transform.Find("PlaceholderItem");
                    Bounds bounds = visual.GetComponent<Renderer>().bounds;
                    Vector3 center = camera.WorldToViewportPoint(bounds.center);
                    Assert.That(center.x, Is.InRange(.4f, .6f), target.name);
                    Assert.That(center.y, Is.InRange(.4f, .6f), target.name);
                    Assert.That(center.z, Is.InRange(camera.nearClipPlane, camera.farClipPlane), target.name);
                    float height = Mathf.Abs(camera.WorldToViewportPoint(bounds.max).y - camera.WorldToViewportPoint(bounds.min).y);
                    Assert.That(height, Is.InRange(.2f, .85f), target.name);
                }

                var bank = Find(scene, "Devices/CentralTimeBank");
                Assert.That(bank.Find("Cabinet").GetComponent<BoxCollider>(), Is.Not.Null);
                Assert.That(bank.Find("Foot"), Is.Not.Null);
                Assert.That(Vector3.Dot(bank.forward, Vector3.left), Is.GreaterThan(.99f));
                camera.transform.SetPositionAndRotation(bank.Find("FocusPoint").position, bank.Find("FocusPoint").rotation);
                camera.fieldOfView = bank.GetComponent<TabletopFocusTarget>().FieldOfView;
                AssertNumberReadable(camera, bank.Find("Face/StaticValue").GetComponent<TextMeshPro>());
                foreach (var name in new[] { "PlayerTimeCounter", "OpponentTimeCounter" })
                {
                    var counter = Find(scene, "Devices/" + name);
                    Assert.That(counter.Find("Head/Case").GetComponent<BoxCollider>(), Is.Not.Null);
                    Assert.That(counter.Find("Bracket"), Is.Not.Null);
                    Assert.That(counter.Find("EdgeClamp"), Is.Not.Null);
                }
                for (int i = 0; i < 3; i++)
                {
                    var watch = Find(scene, "ClockSlots/ClockSlot" + i + "/ClockPlaceholder");
                    Assert.That(Vector3.Dot(watch.TransformDirection(Vector3.back), Vector3.up), Is.GreaterThan(.99f));
                    Assert.That(watch.position.y, Is.InRange(1.01f, 1.07f));
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void PlaySceneUsesThreeReusableClocksAndOneCompactDecisionOverlay()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TabletopGame.unity");
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<PrototypeController>(true)), Is.Empty);
                var canvases = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).ToArray();
                Assert.That(canvases, Has.Length.EqualTo(1));
                Assert.That(canvases[0].renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                var scaler = canvases[0].GetComponent<CanvasScaler>();
                Assert.That(scaler, Is.Not.Null);
                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920, 1080)));
                Assert.That(canvases[0].GetComponent<GraphicRaycaster>(), Is.Not.Null);
                var eventSystems = roots.SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).ToArray();
                Assert.That(eventSystems, Has.Length.EqualTo(1));
                Assert.That(eventSystems[0].GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
                var view = Find(scene, "TabletopGame").GetComponent<TabletopGameView>();
                Assert.That(view.Clocks, Has.Length.EqualTo(3));
                var decisionUI = view.DecisionUI;
                Assert.That(decisionUI, Is.Not.Null);
                Assert.That(decisionUI.transform.IsChildOf(canvases[0].transform), Is.True);
                Assert.That(decisionUI.IsVisible, Is.False, "씬에는 결정 조작부를 숨긴 상태로 저장한다");
                Assert.That(decisionUI.KeepButton, Is.Not.Null);
                Assert.That(decisionUI.SwapButton, Is.Not.SameAs(decisionUI.KeepButton));
                Assert.That(canvases[0].GetComponentsInChildren<Button>(true), Has.Length.EqualTo(2));
                foreach (var button in new[] { decisionUI.KeepButton, decisionUI.SwapButton })
                {
                    Assert.That(button.gameObject.activeInHierarchy, Is.False);
                    Assert.That(button.GetComponentInChildren<TextMeshProUGUI>(true), Is.Not.Null);
                    var icons = button.GetComponentsInChildren<Image>(true).Where(image => image != button.targetGraphic).ToArray();
                    Assert.That(icons, Is.Not.Empty, "버튼 배경과 구분한 교체 가능한 아이콘 Image");
                    foreach (var icon in icons)
                    {
                        Assert.That(icon.sprite, Is.Not.Null);
                        Assert.That(AssetDatabase.GetAssetPath(icon.sprite), Is.Not.Empty, "임시 아이콘도 에셋 Sprite로 연결한다");
                    }
                }
                var camera = Find(scene, "CameraRig/Main Camera").GetComponent<Camera>();
                camera.aspect = 16f / 9;
                foreach (var clock in view.Clocks)
                {
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(clock),
                        Is.EqualTo("Assets/TimeTheft/Prefabs/TabletopClock.prefab"));
                    Assert.That(clock.FocusTarget.FocusOnClick, Is.False);
                    camera.transform.SetPositionAndRotation(clock.FocusTarget.FocusPoint.position, clock.FocusTarget.FocusPoint.rotation);
                    camera.fieldOfView = clock.FocusTarget.FieldOfView;
                    foreach (var control in new[] { clock.Keep, clock.Swap })
                    {
                        Assert.That(control.FocusPoint.IsChildOf(clock.transform), Is.True);
                        Assert.That(control.FocusOnClick, Is.False);
                        // 초기에는 숨겨진 조작부도 실제 집중 시점에서 읽을 수 있어야 한다.
                        control.transform.parent.gameObject.SetActive(true);
                        Assert.That(control.GetComponent<TabletopDecisionObject>(), Is.Not.Null);
                        control.GetComponent<TabletopDecisionObject>().Artwork.gameObject.SetActive(true);
                        AssertNumberReadable(camera, control.GetComponentInChildren<TextMeshPro>(true));
                    }
                }
                foreach (var root in roots)
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
                Assert.That(view.PlayerLife, Is.Not.Null);
                Assert.That(view.AiLife, Is.Not.Null);
                Assert.That(view.Pot, Is.Not.Null);
                Assert.That(view.Restart.FocusOnClick, Is.False);
                Assert.That(Find(scene, "Environment/Table").Find("ControlPlaque"), Is.Null);
                Assert.That(view.Status.transform.parent.gameObject.activeSelf, Is.False);
                Assert.That(view.RoundLabel, Is.Not.Null);
                Assert.That(Find(scene, "ItemSlots").GetComponentsInChildren<TabletopFocusTarget>(true), Is.Empty);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void AssertNumberReadable(Camera camera, TextMeshPro label)
        {
            label.ForceMeshUpdate(true, true);
            float bottom = 2, top = -1;
            foreach (var character in label.textInfo.characterInfo)
            {
                if (!character.isVisible) continue;
                foreach (var local in new[] { character.bottomLeft, character.bottomRight, character.topLeft, character.topRight })
                {
                    Vector3 point = camera.WorldToViewportPoint(label.transform.TransformPoint(local));
                    Assert.That(point.x, Is.InRange(0f, 1f), label.transform.parent.name);
                    Assert.That(point.y, Is.InRange(0f, 1f), label.transform.parent.name);
                    bottom = Mathf.Min(bottom, point.y);
                    top = Mathf.Max(top, point.y);
                }
            }
            Assert.That((top - bottom) * 720, Is.GreaterThanOrEqualTo(20), label.transform.parent.name);
            float facing = Vector3.Dot(label.transform.TransformDirection(Vector3.back),
                (camera.transform.position - label.transform.position).normalized);
            Assert.That(facing, Is.GreaterThan(.6f), label.transform.parent.name);
        }

        private static Transform Find(Scene scene, string path)
        {
            string[] parts = path.Split(new[] { '/' }, 2);
            Transform root = scene.GetRootGameObjects().Single(r => r.name == parts[0]).transform;
            return parts.Length == 1 ? root : root.Find(parts[1]);
        }

        private static float TableCoverage(Camera camera, Transform surface)
        {
            var polygon = new List<Vector2>();
            foreach (var local in new[] { new Vector3(-.5f, .5f, -.5f), new Vector3(.5f, .5f, -.5f),
                new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f) })
                polygon.Add(camera.WorldToViewportPoint(surface.TransformPoint(local)));
            for (int edge = 0; edge < 4; edge++)
            {
                var clipped = new List<Vector2>();
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                    float da = BoundaryDistance(a, edge), db = BoundaryDistance(b, edge);
                    if (da >= 0) clipped.Add(a);
                    if ((da >= 0) != (db >= 0)) clipped.Add(Vector2.Lerp(a, b, da / (da - db)));
                }
                polygon = clipped;
            }
            float area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(area) * .5f;
        }

        private static float BoundaryDistance(Vector2 point, int edge)
        {
            return edge == 0 ? point.x : edge == 1 ? 1 - point.x : edge == 2 ? point.y : 1 - point.y;
        }
    }
}
