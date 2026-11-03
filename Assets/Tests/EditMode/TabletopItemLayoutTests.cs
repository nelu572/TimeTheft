using System.Linq;
using NUnit.Framework;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class TabletopItemLayoutTests
    {
        [Test]
        public void HiddenMainSceneSamplesRemainInertAndFitTable()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/TimeTheftGame.unity");
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

    }
}
