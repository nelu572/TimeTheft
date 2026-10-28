#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TimeTheft.ItemPresentation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class M410HatchSurfaceTests
    {
        private GameObject root;
        private readonly List<Object> assets = new List<Object>();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (root) Object.Destroy(root);
            yield return null;
            foreach (var asset in assets) if (asset) Object.Destroy(asset);
            assets.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator RuntimeOpeningPreservesTheExistingHoleSubmeshesUVAndColliderThenRestoresOriginals()
        {
            BuildSurface(out var surface, out var filter, out var collider, out var original);
            var location = Child("Item opening"); location.position = new Vector3(-1, 0, 0);
            Assert.That(collider.Raycast(new Ray(new Vector3(1.5f, 2, 0), Vector3.down), out _, 4), Is.False, "원본에는 이미 H2 구멍이 있다");
            Assert.That(surface.Configure(location, new Vector2(.8f, .6f)), Is.True, surface.LastError);
            Assert.That(filter.sharedMesh, Is.Not.SameAs(original));
            Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(original.subMeshCount));
            Assert.That(collider.Raycast(new Ray(new Vector3(-1, 2, 0), Vector3.down), out _, 4), Is.False, "새 개구부는 실제 Collider에서도 열린다");
            Assert.That(collider.Raycast(new Ray(new Vector3(1.5f, 2, 0), Vector3.down), out _, 4), Is.False, "기존 H2 구멍도 채우지 않는다");
            Assert.That(collider.Raycast(new Ray(new Vector3(-3, 2, 0), Vector3.down), out _, 4), Is.True, "나머지 상판은 유지한다");
            var vertices = filter.sharedMesh.vertices; var uv = filter.sharedMesh.uv; var uv1 = new List<Vector4>(); filter.sharedMesh.GetUVs(1, uv1);
            Assert.That(uv1.Count, Is.EqualTo(vertices.Length));
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.That(uv[i].x, Is.EqualTo((vertices[i].x + 4) / 8).Within(.00001f));
                Assert.That(uv[i].y, Is.EqualTo((vertices[i].z + 3) / 6).Within(.00001f));
                Assert.That(uv1[i].z, Is.EqualTo(.25f).Within(.00001f));
            }
            location.position = new Vector3(1.5f, 0, 0);
            Assert.That(surface.ValidateOpening(location, Vector2.one * .3f), Is.False, "H2 보호 영역을 침범하지 않는다");
            surface.enabled = false;
            Assert.That(filter.sharedMesh, Is.SameAs(original));
            Assert.That(collider.sharedMesh, Is.SameAs(original));
            Assert.That(collider.Raycast(new Ray(new Vector3(-1, 2, 0), Vector3.down), out _, 4), Is.True);
            Assert.That(surface.HasOpening, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator QueueWaitsForReadyDropsOnlyAfterActualFloorClearAndCancelRestoresTheSurface()
        {
            BuildSurface(out var surface, out var filter, out var collider, out var original);
            var controller = root.AddComponent<ItemHatchController>(); controller.Surface = surface; controller.PlaybackSpeed = 5;
            var moduleRoot = Child("Module"); var model = new GameObject("Model"); model.transform.SetParent(moduleRoot, false);
            var pivot = new GameObject("LowerPanelPivot"); pivot.transform.SetParent(model.transform, false);
            var panel = new GameObject("LowerPanelSurface", typeof(MeshFilter), typeof(MeshRenderer)); panel.transform.SetParent(pivot.transform, false);
            var panelMesh = Rectangle(-.5f, .5f, -.5f, .5f); panel.GetComponent<MeshFilter>().sharedMesh = panelMesh;
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("LowerPanelPivot", typeof(Transform), "localPosition.y", new AnimationCurve(new Keyframe(0, 0), new Keyframe(.35f, -.35f), new Keyframe(1, -.35f), new Keyframe(1.5f, 0)));
            assets.Add(clip);
            controller.Modules = new[] { new ItemHatchModule { Operation = HatchOperation.SingleDispose, Root = moduleRoot.gameObject,
                Model = model, Clip = clip, Floor = panel.GetComponent<Renderer>(), Aperture = Vector2.one, FloorClearFrame = 12, CleanupFrame = 30 } };
            var first = Cube("First", new Vector3(-1, .1f, 0)); var second = Cube("Second", new Vector3(-2, .1f, 0));
            var firstLocation = Child("First location"); firstLocation.position = new Vector3(-1, 0, 0);
            var secondLocation = Child("Second location"); secondLocation.position = new Vector3(-2, 0, 0);
            bool complete = false, floorClear = false, removed = false, secondCancelled = false;
            var firstRequest = new ItemHatchRequest("first", HatchOperation.SingleDispose, firstLocation, new[] { first }, null) { Ready = false };
            firstRequest.Finished = success => { complete = success; Assert.That(filter.sharedMesh, Is.SameAs(original), "복원 후 완료 신호를 보낸다"); };
            var secondRequest = new ItemHatchRequest("second", HatchOperation.SingleDispose, secondLocation, new[] { second }, null) { Ready = false };
            secondRequest.Finished = success => secondCancelled = !success;
            controller.Presented += (id, signal) =>
            {
                if (id != "first") return;
                if (signal == HatchPresentationEvent.FloorClear)
                {
                    floorClear = true;
                    Assert.That(panel.GetComponent<Renderer>().bounds.max.y, Is.LessThan(-.035f));
                    Assert.That(first.transform.position.y, Is.EqualTo(.1f).Within(.001f), "FloorClear 전에는 물체를 떨어뜨리지 않는다");
                }
                if (signal == HatchPresentationEvent.VisualsRemoved)
                { removed = true; Assert.That(first.activeSelf, Is.False); Assert.That(first.GetComponent<Renderer>().bounds.max.y, Is.LessThan(0)); }
            };
            Assert.That(controller.Enqueue(firstRequest), Is.True);
            Assert.That(controller.Enqueue(secondRequest), Is.True);
            Assert.That(controller.WaitingCount, Is.EqualTo(1));
            yield return null; yield return null;
            Assert.That(surface.HasOpening, Is.False, "원본 Exit가 끝나기 전에는 기존 상판을 유지한다");
            Assert.That(first.transform.position.y, Is.EqualTo(.1f).Within(.001f));
            firstRequest.Ready = true;
            float deadline = Time.realtimeSinceStartup + 5;
            while (!complete && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(complete, Is.True, controller.LastError);
            Assert.That(floorClear && removed, Is.True);
            Assert.That(controller.ActiveRequestId, Is.EqualTo("second"));
            Assert.That(controller.Cancel("second"), Is.True);
            Assert.That(secondCancelled, Is.True);
            Assert.That(second.transform.position.y, Is.EqualTo(.1f).Within(.001f));
            Assert.That(filter.sharedMesh, Is.SameAs(original));
            Assert.That(collider.sharedMesh, Is.SameAs(original));
            Assert.That(controller.Enqueue(firstRequest), Is.False, "동일 요청을 두 번 실행하지 않는다");
            controller.CancelAll();
            Assert.That(controller.Busy, Is.False); Assert.That(controller.WaitingCount, Is.Zero);
            Assert.That(surface.HasOpening, Is.False);
        }

        private void BuildSurface(out ItemHatchSurface surface, out MeshFilter filter, out MeshCollider collider, out Mesh original)
        {
            root = new GameObject("Hatch integration test");
            var table = new GameObject("Original tabletop", typeof(MeshFilter), typeof(MeshCollider)); table.transform.SetParent(root.transform, false);
            original = TableWithHole(); filter = table.GetComponent<MeshFilter>(); collider = table.GetComponent<MeshCollider>();
            filter.sharedMesh = original; collider.sharedMesh = original;
            surface = root.AddComponent<ItemHatchSurface>(); surface.Surface = filter; surface.CollisionSurface = collider;
            surface.ProtectedWorldBounds = new[] { new Bounds(new Vector3(1.5f, 0, 0), new Vector3(1, .2f, 1)) };
            Physics.SyncTransforms();
        }
        private Transform Child(string name) { var value = new GameObject(name).transform; value.SetParent(root.transform, false); return value; }
        private GameObject Cube(string name, Vector3 position)
        { var value = GameObject.CreatePrimitive(PrimitiveType.Cube); value.name = name; value.transform.SetParent(root.transform, false); value.transform.position = position; value.transform.localScale = new Vector3(.3f, .2f, .3f); return value; }
        private Mesh Rectangle(float xmin, float xmax, float zmin, float zmax)
        {
            var mesh = new Mesh(); mesh.vertices = new[] { new Vector3(xmin, 0, zmin), new Vector3(xmin, 0, zmax), new Vector3(xmax, 0, zmax), new Vector3(xmax, 0, zmin) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); assets.Add(mesh); return mesh;
        }
        private Mesh TableWithHole()
        {
            var positions = new List<Vector3>(); var uv = new List<Vector2>(); var uv1 = new List<Vector4>();
            var first = new List<int>(); var second = new List<int>();
            void Rect(float xmin, float xmax, float zmin, float zmax, List<int> indices)
            {
                int start = positions.Count;
                var corners = new[] { new Vector3(xmin, 0, zmin), new Vector3(xmin, 0, zmax), new Vector3(xmax, 0, zmax), new Vector3(xmax, 0, zmin) };
                foreach (var point in corners) { positions.Add(point); uv.Add(new Vector2((point.x + 4) / 8, (point.z + 3) / 6)); uv1.Add(new Vector4(point.x, point.z, .25f, 1)); }
                indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            Rect(-4, 1, -3, 3, first); Rect(2, 4, -3, 3, first); Rect(1, 2, -3, -.5f, second); Rect(1, 2, .5f, 3, second);
            var mesh = new Mesh { name = "Original tabletop with existing hole", subMeshCount = 2 };
            mesh.SetVertices(positions); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv1); mesh.SetTriangles(first, 0); mesh.SetTriangles(second, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); assets.Add(mesh); return mesh;
        }
    }
}
#endif
