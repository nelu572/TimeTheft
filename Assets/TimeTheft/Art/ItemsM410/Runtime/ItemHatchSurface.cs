using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TimeTheft.ItemPresentation
{
    // 원본 상판에서 새 개구부만 뺀다. 기존 H2 구멍과 submesh/재질/UV는 유지한다.
    public sealed class ItemHatchSurface : MonoBehaviour
    {
        public MeshFilter Surface;
        public MeshCollider CollisionSurface;
        public Collider[] SolidSurfaceColliders = Array.Empty<Collider>();
        public Collider[] ProtectedOpenings = Array.Empty<Collider>();
        public Bounds[] ProtectedWorldBounds = Array.Empty<Bounds>();
        [Min(0)] public float OpeningClearance = .03f;
        public Vector2 TableSize = new Vector2(9.84f, 6.528f); // 과거 직렬화 호환. 실제 경계는 원본 메시를 사용한다.
        public bool HasOpening { get; private set; }
        public Rect ActiveRoomRect { get; private set; }
        public string LastError { get; private set; }
        public Mesh OriginalMesh => original;
        private Mesh original, originalCollision, rendered, collision, openingWalls;
        private GameObject wallObject;
        private bool captured, colliderEnabled, colliderConvex, createdCollider;
        private Vector3[] sourcePositions;
        private Vector2[] sourceUV;
        private int[] sourceTriangles;
        private readonly Dictionary<Collider, bool> solidStates = new Dictionary<Collider, bool>();
        private readonly Dictionary<MeshFilter, Mesh> panelOriginals = new Dictionary<MeshFilter, Mesh>();
        private readonly Dictionary<MeshFilter, Mesh> panelCopies = new Dictionary<MeshFilter, Mesh>();

        private bool Capture()
        {
            if (captured) return true;
            if (!Surface || !Surface.sharedMesh || !Surface.sharedMesh.isReadable)
            { LastError = "읽을 수 있는 원본 상판 메시가 필요합니다."; return false; }
            original = Surface.sharedMesh;
            sourcePositions = original.vertices; sourceUV = original.uv;
            var indices = new List<int>();
            for (int i = 0; i < original.subMeshCount; i++)
            {
                if (original.GetTopology(i) != MeshTopology.Triangles)
                { LastError = "상판은 삼각형 메시여야 합니다."; return false; }
                indices.AddRange(original.GetTriangles(i));
            }
            sourceTriangles = indices.ToArray();
            if (!CollisionSurface) CollisionSurface = Surface.GetComponent<MeshCollider>();
            if (!CollisionSurface)
            { CollisionSurface = Surface.gameObject.AddComponent<MeshCollider>(); CollisionSurface.enabled = false; createdCollider = true; }
            originalCollision = CollisionSurface.sharedMesh;
            colliderEnabled = CollisionSurface.enabled; colliderConvex = CollisionSurface.convex;
            foreach (var value in SolidSurfaceColliders) if (value && value != CollisionSurface) solidStates[value] = value.enabled;
            captured = true;
            return true;
        }

        public bool ValidateOpening(Transform location, Vector2 aperture)
        {
            LastError = null;
            if (!Capture()) return false;
            if (!location || aperture.x <= .01f || aperture.y <= .01f || Vector3.Dot(location.up, Vector3.up) < .999f)
            { LastError = "개구부는 수평의 유효한 직사각형이어야 합니다."; return false; }
            var rectangle = WorldRectangle(location, aperture);
            var outerMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity); var outerMax = -outerMin;
            var originalBounds = original.bounds;
            for (int i = 0; i < 8; i++)
            {
                var point = Surface.transform.TransformPoint(originalBounds.center + Vector3.Scale(originalBounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                outerMin = Vector2.Min(outerMin, new Vector2(point.x, point.z)); outerMax = Vector2.Max(outerMax, new Vector2(point.x, point.z));
            }
            if (rectangle.xMin < outerMin.x + OpeningClearance || rectangle.xMax > outerMax.x - OpeningClearance ||
                rectangle.yMin < outerMin.y + OpeningClearance || rectangle.yMax > outerMax.y - OpeningClearance)
            { LastError = "개구부가 원본 상판 테두리의 여유를 침범합니다."; return false; }
            foreach (var value in ProtectedOpenings)
                if (value && Intersects(rectangle, value.bounds)) { LastError = "기존 H2 개구부의 여유를 침범합니다."; return false; }
            foreach (var value in ProtectedWorldBounds)
                if (Intersects(rectangle, value)) { LastError = "보호 영역의 여유를 침범합니다."; return false; }
            var bounds = original.bounds; bounds.Expand(.012f);
            for (int i = 0; i < 4; i++)
            {
                var point = Corner(location, aperture, i);
                if (!bounds.Contains(Surface.transform.InverseTransformPoint(point)) || !TryGetSurfaceUV(point, out _))
                { LastError = "개구부가 원본 상판의 표면/UV 경계를 벗어납니다."; return false; }
            }
            return true;
        }
        private bool Intersects(Rect rectangle, Bounds bounds) => rectangle.Overlaps(Rect.MinMaxRect(
            bounds.min.x - OpeningClearance, bounds.min.z - OpeningClearance, bounds.max.x + OpeningClearance, bounds.max.z + OpeningClearance));
        private static Vector3 Corner(Transform location, Vector2 aperture, int i) => location.position +
            location.right * (((i & 1) == 0 ? -.5f : .5f) * aperture.x) + location.forward * (((i & 2) == 0 ? -.5f : .5f) * aperture.y);
        private static Rect WorldRectangle(Transform location, Vector2 aperture)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity); var max = -min;
            for (int i = 0; i < 4; i++) { var p = Corner(location, aperture, i); min = Vector2.Min(min, new Vector2(p.x, p.z)); max = Vector2.Max(max, new Vector2(p.x, p.z)); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public bool Configure(Transform location, Vector2 aperture)
        {
            if (!ValidateOpening(location, aperture)) return false;
            Mesh nextRendered = null, nextCollision = null;
            try
            {
                nextRendered = ClipMesh(original, Surface.transform.localToWorldMatrix, Matrix4x4.identity, location, aperture);
                var source = originalCollision ? originalCollision : original;
                if (!source.isReadable) throw new InvalidOperationException("기존 상판 Collider 메시를 읽을 수 없습니다.");
                var world = originalCollision ? CollisionSurface.transform.localToWorldMatrix : Surface.transform.localToWorldMatrix;
                var output = originalCollision ? Matrix4x4.identity : CollisionSurface.transform.worldToLocalMatrix * Surface.transform.localToWorldMatrix;
                nextCollision = ClipMesh(source, world, output, location, aperture);
                RestoreGeometry();
                rendered = nextRendered; collision = nextCollision;
                Surface.sharedMesh = rendered;
                CollisionSurface.enabled = false; CollisionSurface.convex = false;
                CollisionSurface.sharedMesh = collision; CollisionSurface.enabled = true;
                foreach (var value in solidStates) if (value.Key) value.Key.enabled = false;
                HasOpening = true; ActiveRoomRect = WorldRectangle(location, aperture);
                BuildOpeningWalls(location, aperture);
                Physics.SyncTransforms();
                return true;
            }
            catch (Exception exception)
            {
                if (nextRendered && nextRendered != rendered) Destroy(nextRendered);
                if (nextCollision && nextCollision != collision) Destroy(nextCollision);
                LastError = exception.Message; RestoreSurface(); return false;
            }
        }

        public bool TryGetSurfaceUV(Vector3 world, out Vector2 uv)
        {
            uv = default;
            if (!Capture() || sourceUV.Length != sourcePositions.Length) return false;
            bool found = false; float nearest = float.PositiveInfinity;
            var matrix = Surface.transform.localToWorldMatrix;
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                int ia = sourceTriangles[i], ib = sourceTriangles[i + 1], ic = sourceTriangles[i + 2];
                var a = matrix.MultiplyPoint3x4(sourcePositions[ia]); var b = matrix.MultiplyPoint3x4(sourcePositions[ib]); var c = matrix.MultiplyPoint3x4(sourcePositions[ic]);
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f || Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) < .9f) continue;
                var v0 = b - a; var v1 = c - a; var v2 = world - a;
                float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1), denominator = d00 * d11 - d01 * d01;
                if (Mathf.Abs(denominator) < 1e-12f) continue;
                float v = (d11 * Vector3.Dot(v2, v0) - d01 * Vector3.Dot(v2, v1)) / denominator;
                float w = (d00 * Vector3.Dot(v2, v1) - d01 * Vector3.Dot(v2, v0)) / denominator;
                float distance = Mathf.Abs(Vector3.Dot(v2, normal.normalized));
                if (v < -.0002f || w < -.0002f || v + w > 1.0002f || distance >= nearest) continue;
                nearest = distance; uv = sourceUV[ia] * (1 - v - w) + sourceUV[ib] * v + sourceUV[ic] * w; found = true;
            }
            return found;
        }
        public Vector2 FeltUV(Vector3 world) => TryGetSurfaceUV(world, out var uv) ? uv : Vector2.zero;
        public void UpdatePanelUV(GameObject model)
        {
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                if (!panelCopies.TryGetValue(filter, out var copy))
                { panelOriginals[filter] = filter.sharedMesh; copy = Instantiate(filter.sharedMesh); panelCopies.Add(filter, copy); filter.sharedMesh = copy; }
                var vertices = copy.vertices; var uv = copy.uv; var normals = copy.normals;
                if (uv == null || uv.Length != vertices.Length) uv = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                    if (i < normals.Length && Vector3.Dot(filter.transform.TransformDirection(normals[i]).normalized, Vector3.up) > .9f)
                        uv[i] = FeltUV(filter.transform.TransformPoint(vertices[i]));
                copy.uv = uv;
            }
        }
        private void BuildOpeningWalls(Transform location, Vector2 aperture)
        {
            var surfaceRenderer = Surface.GetComponent<MeshRenderer>();
            if (!surfaceRenderer) return; // Collider 전용 표면은 시각 내벽을 만들지 않는다.
            var points = new Vector3[16]; var uv = new Vector2[16]; var indices = new int[24];
            int[] order = { 0, 1, 3, 2 };
            float depth = surfaceRenderer.bounds.size.y;
            for (int edge = 0; edge < 4; edge++)
            {
                var a = Corner(location, aperture, order[edge]); var b = Corner(location, aperture, order[(edge + 1) % 4]);
                int k = edge * 4, t = edge * 6;
                points[k] = Surface.transform.InverseTransformPoint(a); points[k+1] = Surface.transform.InverseTransformPoint(b);
                points[k+2] = Surface.transform.InverseTransformPoint(a - Vector3.up * depth);
                points[k+3] = Surface.transform.InverseTransformPoint(b - Vector3.up * depth);
                uv[k] = FeltUV(a); uv[k+1] = FeltUV(b); uv[k+2] = uv[k] + Vector2.up * .0125f; uv[k+3] = uv[k+1] + Vector2.up * .0125f;
                indices[t]=k; indices[t+1]=k+2; indices[t+2]=k+1; indices[t+3]=k+1; indices[t+4]=k+2; indices[t+5]=k+3;
            }
            openingWalls = new Mesh { name = "T3 opening inner thickness" }; openingWalls.vertices=points; openingWalls.uv=uv; openingWalls.triangles=indices; openingWalls.RecalculateNormals(); openingWalls.RecalculateBounds();
            wallObject = new GameObject("Active opening inner walls", typeof(MeshFilter), typeof(MeshRenderer));
            wallObject.transform.SetParent(Surface.transform, false); wallObject.GetComponent<MeshFilter>().sharedMesh=openingWalls;
            wallObject.GetComponent<MeshRenderer>().sharedMaterials=Surface.GetComponent<MeshRenderer>().sharedMaterials;
        }
        private void RestoreGeometry()
        {
            if (wallObject) { wallObject.SetActive(false); Destroy(wallObject); } if (openingWalls) Destroy(openingWalls);
            wallObject = null; openingWalls = null;
            if (Surface && original) Surface.sharedMesh = original;
            if (CollisionSurface)
            { CollisionSurface.enabled = false; CollisionSurface.sharedMesh = originalCollision; CollisionSurface.convex = colliderConvex; CollisionSurface.enabled = colliderEnabled; }
            foreach (var value in solidStates) if (value.Key) value.Key.enabled = value.Value;
            if (rendered) Destroy(rendered); if (collision) Destroy(collision);
            rendered = collision = null; HasOpening = false; ActiveRoomRect = default;
        }
        public void RestoreSurface()
        {
            if (!captured) return;
            RestoreGeometry();
            foreach (var value in panelOriginals) if (value.Key) value.Key.sharedMesh = value.Value;
            foreach (var value in panelCopies.Values) if (value) Destroy(value);
            panelOriginals.Clear(); panelCopies.Clear(); Physics.SyncTransforms();
        }

        private struct Vertex
        {
            public Vector3 Position, Normal;
            public Vector4 Tangent;
            public Color Color;
            public Vector4[] UV;
            public static Vertex Lerp(Vertex a, Vertex b, float t)
            {
                var uv = new Vector4[8]; for (int i = 0; i < 8; i++) uv[i] = Vector4.LerpUnclamped(a.UV[i], b.UV[i], t);
                return new Vertex { Position = Vector3.LerpUnclamped(a.Position, b.Position, t), Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t).normalized,
                    Tangent = Vector4.LerpUnclamped(a.Tangent, b.Tangent, t), Color = UnityEngine.Color.LerpUnclamped(a.Color, b.Color, t), UV = uv };
            }
        }
        private static Mesh ClipMesh(Mesh source, Matrix4x4 toWorld, Matrix4x4 output, Transform opening, Vector2 aperture)
        {
            var positions = source.vertices; var normals = source.normals; var tangents = source.tangents; var colors = source.colors;
            var inputUV = new List<Vector4>[8]; var outputUV = new List<Vector4>[8];
            for (int i = 0; i < 8; i++) { inputUV[i] = new List<Vector4>(); source.GetUVs(i, inputUV[i]); outputUV[i] = new List<Vector4>(); }
            var input = new Vertex[positions.Length];
            for (int i = 0; i < input.Length; i++)
            {
                var uv = new Vector4[8]; for (int channel = 0; channel < 8; channel++) uv[channel] = i < inputUV[channel].Count ? inputUV[channel][i] : Vector4.zero;
                input[i] = new Vertex { Position = positions[i], Normal = normals.Length == positions.Length ? normals[i] : Vector3.up,
                    Tangent = tangents.Length == positions.Length ? tangents[i] : new Vector4(1, 0, 0, 1), Color = colors.Length == positions.Length ? colors[i] : UnityEngine.Color.white, UV = uv };
            }
            var vertices = new List<Vector3>(); var outputNormals = new List<Vector3>(); var outputTangents = new List<Vector4>(); var outputColors = new List<Color>();
            var indices = new List<int>[source.subMeshCount]; var half = aperture * .5f;
            float Distance(Vertex vertex, int plane)
            {
                var delta = toWorld.MultiplyPoint3x4(vertex.Position) - opening.position;
                return plane == 0 ? Vector3.Dot(delta, opening.right) + half.x : plane == 1 ? half.x - Vector3.Dot(delta, opening.right)
                    : plane == 2 ? Vector3.Dot(delta, opening.forward) + half.y : half.y - Vector3.Dot(delta, opening.forward);
            }
            List<Vertex> Clip(List<Vertex> polygon, int plane, bool inside)
            {
                var result = new List<Vertex>(6); if (polygon.Count == 0) return result;
                var a = polygon[polygon.Count - 1]; float da = Distance(a, plane); bool keepA = inside ? da >= 0 : da <= 0;
                foreach (var b in polygon)
                {
                    float db = Distance(b, plane); bool keepB = inside ? db >= 0 : db <= 0;
                    if (keepA != keepB) result.Add(Vertex.Lerp(a, b, Mathf.Clamp01(da / (da - db))));
                    if (keepB) result.Add(b); a = b; da = db; keepA = keepB;
                }
                return result;
            }
            void Add(List<Vertex> polygon, List<int> triangles)
            {
                if (polygon.Count < 3) return;
                int start = vertices.Count;
                foreach (var vertex in polygon)
                {
                    vertices.Add(output.MultiplyPoint3x4(vertex.Position)); outputNormals.Add(vertex.Normal); outputTangents.Add(vertex.Tangent); outputColors.Add(vertex.Color);
                    for (int channel = 0; channel < 8; channel++) if (inputUV[channel].Count > 0) outputUV[channel].Add(vertex.UV[channel]);
                }
                for (int i = 1; i < polygon.Count - 1; i++)
                    if (Vector3.Cross(polygon[i].Position - polygon[0].Position, polygon[i + 1].Position - polygon[0].Position).sqrMagnitude > 1e-14f)
                    { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
            }
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                if (source.GetTopology(submesh) != MeshTopology.Triangles) throw new InvalidOperationException("상판/Collider는 삼각형 메시여야 합니다.");
                indices[submesh] = new List<int>(); var triangles = source.GetTriangles(submesh);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var remaining = new List<Vertex>(6) { input[triangles[i]], input[triangles[i + 1]], input[triangles[i + 2]] };
                    // 각 평면의 바깥을 한 번씩 분리한다. 마지막 내부 다각형만 버리므로 기존 구멍을 채우지 않는다.
                    for (int plane = 0; plane < 4 && remaining.Count > 0; plane++)
                    { Add(Clip(remaining, plane, false), indices[submesh]); remaining = Clip(remaining, plane, true); }
                }
            }
            var mesh = new Mesh { name = source.name + " (runtime item opening)", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(outputNormals);
            if (tangents.Length > 0) mesh.SetTangents(outputTangents); if (colors.Length > 0) mesh.SetColors(outputColors);
            for (int channel = 0; channel < 8; channel++) if (inputUV[channel].Count > 0) mesh.SetUVs(channel, outputUV[channel]);
            mesh.subMeshCount = source.subMeshCount;
            for (int submesh = 0; submesh < indices.Length; submesh++) mesh.SetTriangles(indices[submesh], submesh);
            mesh.RecalculateBounds(); return mesh;
        }
        private void OnDisable() => RestoreSurface();
        private void OnDestroy() { RestoreSurface(); if (createdCollider && CollisionSurface) Destroy(CollisionSurface); }
    }
}
