using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class TabletopItemLayoutTests
    {
        [Test]
        public void HiddenSamplesFitTableAndDoNotCoverPlayAreaAtLookLimitsOrFocusPoints()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TabletopGame.unity");
            try
            {
                var roots = scene.GetRootGameObjects();
                var items = roots.Single(r => r.name == "ItemSlots").transform;
                Assert.That(items.gameObject.activeSelf, Is.False, "일반 플레이는 배치 샘플을 표시하지 않는다");
                items.gameObject.SetActive(true);
                var groups = new[] { items.Find("PlayerItems"), items.Find("OpponentItems") };
                Assert.That(groups.All(g => g && g.childCount >= 2), Is.True, "수량 규칙 없이 양측 다중 소지품 배치를 검증한다");
                var samples = groups.SelectMany(g => g.Cast<Transform>()).Select(t => t.Find("LayoutSample")).ToArray();
                var sampleBounds = new Bounds[samples.Length];
                for (int i = 0; i < samples.Length; i++)
                {
                    Assert.That(samples[i], Is.Not.Null);
                    Assert.That(samples[i].parent.Find("SlotOutline"), Is.Null);
                    Assert.That(samples[i].position.z, samples[i].IsChildOf(groups[0]) ? Is.LessThan(-1f) : Is.GreaterThan(1f));
                    Assert.That(samples[i].parent.GetComponentsInChildren<Collider>(true).Any(c => c.enabled), Is.False);
                    Assert.That(samples[i].parent.GetComponentsInChildren<TabletopFocusTarget>(true).Any(t => t.enabled), Is.False);
                    var renderers = samples[i].GetComponentsInChildren<Renderer>().Where(r => r.name != "ContactShadow").ToArray();
                    sampleBounds[i] = GeometryBounds(renderers[0]);
                    foreach (var renderer in renderers) sampleBounds[i].Encapsulate(GeometryBounds(renderer));
                    Assert.That(Mathf.Max(sampleBounds[i].size.x, sampleBounds[i].size.z), Is.InRange(.65f, 1.3f), "식별 가능한 소지품 크기");
                    // SpriteRenderer의 보수적인 깊이 AABB 대신 실제 입체 받침으로 접지를 확인한다.
                    Bounds body = samples[i].Find("Body").GetComponent<Renderer>().bounds;
                    Assert.That(body.min.y, Is.EqualTo(1.0075f).Within(.002f), "테이블 표면에 놓임");
                    Assert.That(sampleBounds[i].max.y, Is.LessThan(1.3f));
                    Assert.That(sampleBounds[i].min.x, Is.GreaterThan(-5));
                    Assert.That(sampleBounds[i].max.x, Is.LessThan(5));
                    Assert.That(sampleBounds[i].min.z, Is.GreaterThan(-3.06f));
                    Assert.That(sampleBounds[i].max.z, Is.LessThan(3.06f));
                }
                for (int i = 0; i < samples.Length; i++)
                    for (int j = i + 1; j < samples.Length; j++)
                        Assert.That(sampleBounds[i].Intersects(sampleBounds[j]), Is.False);

                var view = roots.Single(r => r.name == "TabletopGame").GetComponent<TabletopGameView>();
                var camera = view.FocusCamera.GetComponent<Camera>();
                camera.aspect = 16f / 9;
                var basePosition = camera.transform.localPosition;
                var baseRotation = camera.transform.localRotation;
                float baseFov = camera.fieldOfView;
                var settings = new SerializedObject(camera.GetComponent<TabletopCameraLook>());
                var core = view.Clocks.Select(c => c.Visual.Find("Case").GetComponent<Renderer>()).ToArray();
                foreach (float y in new[] { -1f, 0, 1f })
                foreach (float x in new[] { -1f, 0, 1f })
                {
                    camera.transform.localPosition = basePosition + new Vector3(x * settings.FindProperty("horizontalTravel").floatValue,
                        y * settings.FindProperty("verticalTravel").floatValue, 0);
                    camera.transform.localRotation = baseRotation * Quaternion.Euler(-y * settings.FindProperty("verticalDegrees").floatValue,
                        x * settings.FindProperty("horizontalDegrees").floatValue, 0);
                    foreach (var bounds in sampleBounds)
                    {
                        Rect projected = Project(camera, bounds);
                        Assert.That(projected.xMin, Is.GreaterThanOrEqualTo(0));
                        Assert.That(projected.xMax, Is.LessThanOrEqualTo(1));
                        Assert.That(projected.yMin, Is.GreaterThanOrEqualTo(0));
                        Assert.That(projected.yMax, Is.LessThanOrEqualTo(1));
                        foreach (var renderer in core) AssertClear(projected, Project(camera, renderer.bounds));
                        foreach (var number in new[] { view.PlayerLife, view.AiLife, view.Pot })
                        {
                            number.ForceMeshUpdate(true, true);
                            AssertClear(projected, Project(camera, number.GetComponent<Renderer>().bounds));
                        }
                    }
                }
                foreach (var target in roots.SelectMany(r => r.GetComponentsInChildren<TabletopFocusTarget>(true))
                    .Where(t => t.enabled && t.FocusOnClick).Concat(view.Clocks.Select(c => c.FocusTarget)))
                {
                    camera.transform.SetPositionAndRotation(target.FocusPoint.position, target.FocusPoint.rotation);
                    camera.fieldOfView = target.FieldOfView;
                    var clock = target.GetComponent<TabletopClockView>();
                    var visual = clock ? clock.Visual.Find("Case").GetComponent<Renderer>()
                        : target.GetComponentsInChildren<TextMeshPro>().First(t => t.name == "StaticValue").GetComponent<Renderer>();
                    Rect focusBounds = Project(camera, visual.bounds);
                    for (int i = 0; i < sampleBounds.Length; i++)
                        if (GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), sampleBounds[i]))
                            AssertClear(Project(camera, sampleBounds[i]), focusBounds, target.name + " / " + samples[i].parent.name);
                }
                camera.fieldOfView = baseFov;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static Bounds GeometryBounds(Renderer renderer)
        {
            var sprite = renderer as SpriteRenderer;
            if (!sprite) return renderer.bounds;
            // 평면 스프라이트의 가짜 깊이를 제외한 실제 모서리로 배치/가림을 확인한다.
            var local = sprite.sprite.bounds;
            var bounds = new Bounds(sprite.transform.TransformPoint(local.center), Vector3.zero);
            foreach (float x in new[] { -1f, 1f })
            foreach (float y in new[] { -1f, 1f })
                bounds.Encapsulate(sprite.transform.TransformPoint(local.center + new Vector3(local.extents.x * x, local.extents.y * y, 0)));
            return bounds;
        }

        private static void AssertClear(Rect sample, Rect target, string context = "기본 시점")
        {
            float overlap = Mathf.Max(0, Mathf.Min(sample.xMax, target.xMax) - Mathf.Max(sample.xMin, target.xMin)) *
                Mathf.Max(0, Mathf.Min(sample.yMax, target.yMax) - Mathf.Max(sample.yMin, target.yMin));
            Assert.That(overlap, Is.LessThanOrEqualTo(target.width * target.height * .05f), "검증용 아이템이 핵심 표시를 가림: " + context);
        }

        private static Rect Project(Camera camera, Bounds bounds)
        {
            // 근평면 뒤 꼭짓점은 투영하면 화면 반대쪽으로 뒤집힌다. 보이는 상자만 잘라 투영한다.
            var points = new Vector3[8];
            var clipped = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 8; i++)
            {
                Vector3 world = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                points[i] = camera.transform.InverseTransformPoint(world);
                if (points[i].z >= camera.nearClipPlane) clipped.Add(points[i]);
            }
            for (int i = 0; i < 8; i++)
            foreach (int bit in new[] { 1, 2, 4 })
            {
                int j = i ^ bit;
                if (i >= j || (points[i].z >= camera.nearClipPlane) == (points[j].z >= camera.nearClipPlane)) continue;
                float t = (camera.nearClipPlane - points[i].z) / (points[j].z - points[i].z);
                clipped.Add(Vector3.Lerp(points[i], points[j], t));
            }
            if (clipped.Count == 0) return Rect.zero;
            Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
            foreach (var local in clipped)
            {
                Vector2 point = camera.WorldToViewportPoint(camera.transform.TransformPoint(local));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
