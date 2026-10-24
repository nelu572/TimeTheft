using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TimeTheft.CounterStudy
{
    // 현재 완성 모델만 보존하는 편집 소스. 과거 구조 생성기나 게임 로직에 의존하지 않는다.
    public static class CounterModelSource
    {
        public const string Root = "Assets/TimeTheft/Counter";
        public const string PrefabPath = Root + "/IntegratedTimeCounter.prefab";
        public const string SourcePath = Root + "/Source/CounterModelSource.json";
        private static readonly string[] ExcludedPropertyRoots =
        {
            "m_Script", "m_GameObject", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset"
        };

        [Serializable] public sealed class SourceData
        {
            public int version = 1;
            public string prefabGuid;
            public MeshData[] meshes;
            public NodeData[] nodes;
        }

        [Serializable] public sealed class AssetReference
        {
            public string guid;
            public long localId;
            public string path;
        }

        [Serializable] public sealed class MeshData
        {
            public string key;
            public AssetReference asset;
            public string name;
            public int indexFormat;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public Color[] colors;
            public Color32[] colors32;
            public UvData[] uv;
            public SubMeshData[] subMeshes;
            public Bounds bounds;
        }

        [Serializable] public sealed class UvData
        {
            public int channel;
            public int dimension;
            public Vector4[] values;
        }

        [Serializable] public sealed class SubMeshData
        {
            public int topology;
            public int baseVertex;
            public int[] indices;
            public Bounds bounds;
            public int firstVertex;
            public int vertexCount;
        }

        [Serializable] public sealed class NodeData
        {
            // 이름이 같은 볼트가 많으므로 이름 대신 형제 순서로 계층을 식별한다.
            public string key;
            public int parent;
            public string name;
            public bool active;
            public int layer;
            public string tag;
            public int staticFlags;
            public int hideFlags;
            public int transformHideFlags;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool hasRect;
            public RectData rect;
            public string mesh;
            public bool meshRenderer;
            public PropertyData[] renderer;
            public bool textMeshPro;
            public PropertyData[] text;
            public bool hasStudyModel;
            public StudyModelData studyModel;
        }

        [Serializable] public sealed class RectData
        {
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 pivot;
            public Vector2 sizeDelta;
            public Vector3 anchoredPosition;
        }

        [Serializable] public sealed class StudyModelData
        {
            public bool enabled;
            public float size;
            public float depth;
            public string geometry;
            public string inspectionPoint;
            public string designName;
        }

        [Serializable] public sealed class ObjectReference
        {
            // 0: null, 1: GUID/localId 자산, 2: 같은 모델의 노드/컴포넌트.
            public int kind;
            public AssetReference asset;
            public string node;
            public string componentType;
        }

        [Serializable] public sealed class PropertyData
        {
            // TMP의 text/font/fontSize/color/alignment/margin/spacing/wrap/autoSize와
            // Renderer의 재질/그림자/정렬/조명 설정을 Unity의 실제 직렬화 이름으로 보존한다.
            public string path;
            public int type;
            public long integer;
            public double number;
            public bool boolean;
            public string text;
            public Vector4 vector;
            public Color color;
            public Quaternion quaternion;
            public Rect rect;
            public Bounds bounds;
            public ObjectReference reference;
        }

        [MenuItem("TimeTheft/Counter/Export Model Source")]
        private static void ExportMenu() => Debug.Log(Export());

        [MenuItem("TimeTheft/Counter/Rebuild Model From Source")]
        private static void RebuildMenu() => Debug.Log(Rebuild());

        public static string Export()
        {
            RequireEditMode();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!root) throw new InvalidOperationException("최종 계수기 프리팹을 찾을 수 없습니다: " + PrefabPath);
            var transforms = new List<Transform>();
            var keys = new Dictionary<Transform, string>();
            var parents = new List<int>();
            Collect(root.transform, -1, "0", transforms, keys, parents);
            var meshes = new Dictionary<string, MeshData>(StringComparer.Ordinal);
            var nodes = new List<NodeData>();
            for (int i = 0; i < transforms.Count; i++)
            {
                Transform t = transforms[i];
                ValidateComponents(t.gameObject);
                var node = new NodeData
                {
                    key = keys[t], parent = parents[i], name = t.name,
                    active = t.gameObject.activeSelf, layer = t.gameObject.layer, tag = t.tag,
                    staticFlags = (int)GameObjectUtility.GetStaticEditorFlags(t.gameObject),
                    hideFlags = (int)t.gameObject.hideFlags, transformHideFlags = (int)t.hideFlags,
                    position = t.localPosition, rotation = t.localRotation, scale = t.localScale
                };
                node.hasRect = t is RectTransform;
                if (t is RectTransform rect)
                    node.rect = new RectData { anchorMin = rect.anchorMin, anchorMax = rect.anchorMax,
                        pivot = rect.pivot, sizeDelta = rect.sizeDelta, anchoredPosition = rect.anchoredPosition3D };
                TextMeshPro text = t.GetComponent<TextMeshPro>();
                MeshFilter filter = t.GetComponent<MeshFilter>();
                if (filter && !text)
                {
                    if (!filter.sharedMesh) throw new InvalidOperationException("비어 있는 MeshFilter: " + t.name);
                    AssetReference asset = CaptureAsset(filter.sharedMesh);
                    node.mesh = AssetKey(asset);
                    if (!meshes.ContainsKey(node.mesh)) meshes.Add(node.mesh, CaptureMesh(filter.sharedMesh, asset));
                }
                MeshRenderer renderer = t.GetComponent<MeshRenderer>();
                node.meshRenderer = renderer;
                if (renderer) node.renderer = CaptureProperties(renderer, keys);
                node.textMeshPro = text;
                if (text) node.text = CaptureProperties(text, keys);
                var model = t.GetComponent<CounterStudyModel>();
                node.hasStudyModel = model;
                if (model)
                    node.studyModel = new StudyModelData { enabled = model.enabled, size = model.size,
                        depth = model.depth, geometry = TransformKey(model.geometry, keys),
                        inspectionPoint = TransformKey(model.inspectionPoint, keys), designName = model.designName };
                nodes.Add(node);
            }
            var data = new SourceData { prefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath),
                meshes = meshes.Values.OrderBy(m => m.key, StringComparer.Ordinal).ToArray(), nodes = nodes.ToArray() };
            string json = JsonUtility.ToJson(data, false).Replace("\r\n", "\n") + "\n";
            Directory.CreateDirectory(Path.GetDirectoryName(SourcePath));
            if (!File.Exists(SourcePath) || File.ReadAllText(SourcePath) != json)
            {
                File.WriteAllText(SourcePath, json, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(SourcePath);
            }
            return $"Counter source exported: {nodes.Count} nodes, {meshes.Count} meshes → {SourcePath}";
        }

        public static string Rebuild()
        {
            RequireEditMode();
            if (!File.Exists(SourcePath)) throw new FileNotFoundException("먼저 계수기 모델 소스를 Export하세요.", SourcePath);
            SourceData data = JsonUtility.FromJson<SourceData>(File.ReadAllText(SourcePath));
            ValidateSource(data);
            string actualGuid = AssetDatabase.AssetPathToGUID(PrefabPath);
            if (!string.IsNullOrEmpty(data.prefabGuid) && actualGuid != data.prefabGuid)
                throw new InvalidOperationException("프리팹 GUID가 소스와 다릅니다. 원본 .meta를 먼저 복원하세요.");

            // 모든 참조와 메시를 검증한 뒤에만 기존 자산에 쓰기 시작한다.
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            var drafts = new List<Mesh>();
            GameObject contents = null;
            try
            {
                foreach (MeshData mesh in data.meshes)
                {
                    Mesh existing = ResolveAsset(mesh.asset) as Mesh;
                    if (!existing) throw new InvalidOperationException("기존 메시/.meta를 복원해야 합니다: " + mesh.asset.path);
                    var draft = new Mesh();
                    drafts.Add(draft);
                    FillMesh(draft, mesh);
                    meshes.Add(mesh.key, existing);
                }
                foreach (NodeData node in data.nodes)
                    foreach (PropertyData property in (node.renderer ?? Array.Empty<PropertyData>()).Concat(node.text ?? Array.Empty<PropertyData>()))
                        if (property.reference != null && property.reference.kind == 1) ResolveAsset(property.reference.asset);

                contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                var oldTransforms = new List<Transform>();
                var oldKeys = new Dictionary<Transform, string>();
                var oldParents = new List<int>();
                Collect(contents.transform, -1, "0", oldTransforms, oldKeys, oldParents);
                foreach (Transform t in oldTransforms) ValidateComponents(t.gameObject);
                var oldByKey = oldKeys.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
                var nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
                contents.SetActive(false);
                foreach (NodeData node in data.nodes)
                {
                    if (!oldByKey.TryGetValue(node.key, out Transform t))
                        t = !node.hasRect ? new GameObject(node.name).transform : new GameObject(node.name, typeof(RectTransform)).transform;
                    else if ((t is RectTransform) != node.hasRect)
                        throw new InvalidOperationException("Transform 종류가 달라 기존 노드를 재사용할 수 없습니다: " + node.key);
                    nodes.Add(node.key, t);
                    t.name = node.name;
                    t.SetParent(node.parent < 0 ? null : nodes[data.nodes[node.parent].key], false);
                    t.SetAsLastSibling();
                    t.gameObject.layer = node.layer;
                    t.gameObject.tag = node.tag;
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, (StaticEditorFlags)node.staticFlags);
                    t.gameObject.hideFlags = (HideFlags)node.hideFlags;
                    t.hideFlags = (HideFlags)node.transformHideFlags;
                    ConfigureComponents(t.gameObject, node);
                }
                foreach (Transform t in oldTransforms)
                    if (t && !nodes.Values.Contains(t)) Object.DestroyImmediate(t.gameObject);
                foreach (MeshData mesh in data.meshes)
                {
                    Mesh existing = meshes[mesh.key];
                    Undo.RegisterCompleteObjectUndo(existing, "Rebuild Counter Mesh");
                    FillMesh(existing, mesh);
                    EditorUtility.SetDirty(existing);
                }
                foreach (NodeData node in data.nodes)
                {
                    Transform t = nodes[node.key];
                    var filter = t.GetComponent<MeshFilter>();
                    if (!string.IsNullOrEmpty(node.mesh)) filter.sharedMesh = meshes[node.mesh];
                    if (node.textMeshPro) ApplyProperties(t.GetComponent<TextMeshPro>(), node.text, nodes);
                    if (node.meshRenderer) ApplyProperties(t.GetComponent<MeshRenderer>(), node.renderer, nodes);
                    if (node.hasStudyModel)
                    {
                        var model = t.GetComponent<CounterStudyModel>();
                        model.size = node.studyModel.size;
                        model.depth = node.studyModel.depth;
                        model.geometry = ResolveTransform(node.studyModel.geometry, nodes);
                        model.inspectionPoint = ResolveTransform(node.studyModel.inspectionPoint, nodes);
                        model.designName = node.studyModel.designName;
                        model.enabled = node.studyModel.enabled;
                    }
                }
                foreach (NodeData node in data.nodes) ApplyTransform(nodes[node.key], node);
                foreach (NodeData node in data.nodes) nodes[node.key].gameObject.SetActive(node.active);
                // TMP가 생성한 임시 메시/캐시가 편집 소스의 설정을 바꾸지 않도록 마지막에 복원한다.
                foreach (NodeData node in data.nodes)
                {
                    Transform t = nodes[node.key];
                    if (node.textMeshPro)
                    {
                        t.GetComponent<TextMeshPro>().ForceMeshUpdate(true, true);
                        ApplyProperties(t.GetComponent<TextMeshPro>(), node.text, nodes);
                    }
                    if (node.meshRenderer) ApplyProperties(t.GetComponent<MeshRenderer>(), node.renderer, nodes);
                }
                foreach (NodeData node in data.nodes) ApplyTransform(nodes[node.key], node);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                if (!saved) throw new InvalidOperationException("계수기 프리팹 저장에 실패했습니다.");
                AssetDatabase.SaveAssets();
                return $"Counter rebuilt: {data.nodes.Length} nodes, {data.meshes.Length} meshes. GUIDs preserved.";
            }
            finally
            {
                if (contents) PrefabUtility.UnloadPrefabContents(contents);
                foreach (Mesh draft in drafts) if (draft) Object.DestroyImmediate(draft);
            }
        }

        private static void Collect(Transform t, int parent, string key, List<Transform> transforms,
            Dictionary<Transform, string> keys, List<int> parents)
        {
            int index = transforms.Count;
            transforms.Add(t); keys.Add(t, key); parents.Add(parent);
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), index, key + "/" + i, transforms, keys, parents);
        }

        private static void ValidateComponents(GameObject node)
        {
            foreach (Component component in node.GetComponents<Component>())
                if (!component || !(component is Transform || component is MeshFilter || component is MeshRenderer ||
                                   component is TextMeshPro || component is CounterStudyModel))
                    throw new InvalidOperationException("저장할 수 없는 컴포넌트: " + node.name + "/" + (component ? component.GetType().Name : "Missing Script"));
        }

        private static string TransformKey(Transform transform, Dictionary<Transform, string> keys)
        {
            if (!transform) return null;
            if (keys.TryGetValue(transform, out string key)) return key;
            throw new InvalidOperationException("모델 바깥 Transform 참조는 보존할 수 없습니다: " + transform.name);
        }

        private static string AssetKey(AssetReference asset) => asset.guid + ":" + asset.localId;

        private static AssetReference CaptureAsset(Object value)
        {
            if (!value) return null;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId) || string.IsNullOrEmpty(guid))
                throw new InvalidOperationException("저장되지 않은 자산 참조: " + value.name);
            return new AssetReference { guid = guid, localId = localId, path = AssetDatabase.GetAssetPath(value) };
        }

        private static Object ResolveAsset(AssetReference reference)
        {
            if (reference == null) return null;
            string path = AssetDatabase.GUIDToAssetPath(reference.guid);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("자산 GUID를 찾을 수 없습니다: " + reference.guid + " (" + reference.path + ")");
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId) &&
                    guid == reference.guid && localId == reference.localId) return asset;
            throw new InvalidOperationException("자산 subasset을 찾을 수 없습니다: " + AssetKey(reference) + " (" + path + ")");
        }

        private static MeshData CaptureMesh(Mesh mesh, AssetReference asset)
        {
            if (!mesh.isReadable || mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0)
                throw new InvalidOperationException("정적 읽기 가능 메시만 지원합니다: " + mesh.name);
            var uv = new List<UvData>();
            for (int channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!mesh.HasVertexAttribute(attribute)) continue;
                var values = new List<Vector4>(); mesh.GetUVs(channel, values);
                uv.Add(new UvData { channel = channel, dimension = mesh.GetVertexAttributeDimension(attribute), values = values.ToArray() });
            }
            bool byteColors = mesh.HasVertexAttribute(VertexAttribute.Color) && mesh.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8;
            var subMeshes = new SubMeshData[mesh.subMeshCount];
            for (int i = 0; i < subMeshes.Length; i++)
            {
                SubMeshDescriptor sub = mesh.GetSubMesh(i);
                subMeshes[i] = new SubMeshData { topology = (int)mesh.GetTopology(i), baseVertex = sub.baseVertex,
                    indices = mesh.GetIndices(i, false), bounds = sub.bounds, firstVertex = sub.firstVertex, vertexCount = sub.vertexCount };
            }
            return new MeshData { key = AssetKey(asset), asset = asset, name = mesh.name, indexFormat = (int)mesh.indexFormat,
                vertices = mesh.vertices, normals = mesh.normals, tangents = mesh.tangents,
                colors = byteColors ? Array.Empty<Color>() : mesh.colors, colors32 = byteColors ? mesh.colors32 : Array.Empty<Color32>(),
                uv = uv.ToArray(), subMeshes = subMeshes, bounds = mesh.bounds };
        }

        private static void FillMesh(Mesh mesh, MeshData source)
        {
            mesh.Clear(false); mesh.name = source.name; mesh.indexFormat = (IndexFormat)source.indexFormat;
            mesh.vertices = source.vertices;
            mesh.normals = source.normals ?? Array.Empty<Vector3>();
            mesh.tangents = source.tangents ?? Array.Empty<Vector4>();
            if (source.colors32 != null && source.colors32.Length > 0) mesh.colors32 = source.colors32;
            else if (source.colors != null && source.colors.Length > 0) mesh.colors = source.colors;
            foreach (UvData uv in source.uv)
            {
                if (uv.dimension == 2) mesh.SetUVs(uv.channel, uv.values.Select(v => new Vector2(v.x, v.y)).ToList());
                else if (uv.dimension == 3) mesh.SetUVs(uv.channel, uv.values.Select(v => new Vector3(v.x, v.y, v.z)).ToList());
                else if (uv.dimension == 4) mesh.SetUVs(uv.channel, uv.values.ToList());
                else throw new InvalidOperationException("지원하지 않는 UV 차원: " + uv.dimension);
            }
            mesh.subMeshCount = source.subMeshes.Length;
            for (int i = 0; i < source.subMeshes.Length; i++)
            {
                SubMeshData sub = source.subMeshes[i];
                mesh.SetIndices(sub.indices, (MeshTopology)sub.topology, i, false, sub.baseVertex);
                SubMeshDescriptor descriptor = mesh.GetSubMesh(i);
                descriptor.bounds = sub.bounds; descriptor.firstVertex = sub.firstVertex; descriptor.vertexCount = sub.vertexCount;
                mesh.SetSubMesh(i, descriptor, MeshUpdateFlags.DontRecalculateBounds);
            }
            mesh.bounds = source.bounds;
            // CopySerialized 뒤 이전 GPU UV가 남던 문제를 피하도록 모든 버퍼를 직접 지정한다.
            mesh.UploadMeshData(false);
        }

        private static PropertyData[] CaptureProperties(Object target, Dictionary<Transform, string> nodes)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            var values = new List<PropertyData>();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                // 구조체/배열 컨테이너만 내려간다. ObjectReference 내부의 FileID/PathID는
                // Native 오브젝트 연결 정보이므로 절대로 순회하거나 직렬화하지 않는다.
                enterChildren = property.propertyType == SerializedPropertyType.Generic;
                if (IsExcludedProperty(property.propertyPath))
                {
                    enterChildren = false;
                    continue;
                }
                if (enterChildren) continue;
                ValidatePropertyPath(property.propertyPath);
                var value = new PropertyData { path = property.propertyPath, type = (int)property.propertyType };
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Character: value.integer = property.longValue; break;
                    case SerializedPropertyType.ArraySize:
                    case SerializedPropertyType.Enum: value.integer = property.intValue; break;
                    case SerializedPropertyType.Boolean: value.boolean = property.boolValue; break;
                    case SerializedPropertyType.Float: value.number = property.doubleValue; break;
                    case SerializedPropertyType.String: value.text = property.stringValue; break;
                    case SerializedPropertyType.Color: value.color = property.colorValue; break;
                    case SerializedPropertyType.Vector2: value.vector = property.vector2Value; break;
                    case SerializedPropertyType.Vector3: value.vector = property.vector3Value; break;
                    case SerializedPropertyType.Vector4: value.vector = property.vector4Value; break;
                    case SerializedPropertyType.Quaternion: value.quaternion = property.quaternionValue; break;
                    case SerializedPropertyType.Rect: value.rect = property.rectValue; break;
                    case SerializedPropertyType.Bounds: value.bounds = property.boundsValue; break;
                    case SerializedPropertyType.ObjectReference: value.reference = CaptureReference(property.objectReferenceValue, nodes); break;
                    default: throw new InvalidOperationException("직렬화 타입을 지원하지 않습니다: " + target.name + "/" + property.propertyPath + " (" + property.propertyType + ")");
                }
                values.Add(value);
            }
            return values.ToArray();
        }

        private static ObjectReference CaptureReference(Object value, Dictionary<Transform, string> nodes)
        {
            if (!value) return new ObjectReference();
            Transform transform = value is GameObject go ? go.transform : (value as Component)?.transform;
            if (transform && nodes.TryGetValue(transform, out string key))
                return new ObjectReference { kind = 2, node = key, componentType = value is Component component ? component.GetType().FullName : null };
            return new ObjectReference { kind = 1, asset = CaptureAsset(value) };
        }

        private static Object ResolveReference(ObjectReference value, Dictionary<string, Transform> nodes)
        {
            if (value == null || value.kind == 0) return null;
            if (value.kind == 1) return ResolveAsset(value.asset);
            if (value.kind != 2 || !nodes.TryGetValue(value.node, out Transform node))
                throw new InvalidOperationException("노드 참조를 찾을 수 없습니다: " + value.node);
            if (string.IsNullOrEmpty(value.componentType)) return node.gameObject;
            Component component = node.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().FullName == value.componentType);
            if (!component) throw new InvalidOperationException("컴포넌트 참조를 찾을 수 없습니다: " + value.node + "/" + value.componentType);
            return component;
        }

        private static void ApplyProperties(Object target, PropertyData[] values, Dictionary<string, Transform> nodes)
        {
            var serialized = new SerializedObject(target);
            foreach (PropertyData value in values ?? Array.Empty<PropertyData>())
            {
                if (value == null) throw new InvalidOperationException("비어 있는 직렬화 필드입니다.");
                ValidatePropertyPath(value.path);
                SerializedProperty property = serialized.FindProperty(value.path);
                if (property == null || (int)property.propertyType != value.type)
                    throw new InvalidOperationException("직렬화 필드가 변경되었습니다: " + target.name + "/" + value.path);
                switch ((SerializedPropertyType)value.type)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Character: property.longValue = value.integer; break;
                    case SerializedPropertyType.ArraySize:
                    case SerializedPropertyType.Enum: property.intValue = checked((int)value.integer); break;
                    case SerializedPropertyType.Boolean: property.boolValue = value.boolean; break;
                    case SerializedPropertyType.Float: property.doubleValue = value.number; break;
                    case SerializedPropertyType.String: property.stringValue = value.text; break;
                    case SerializedPropertyType.Color: property.colorValue = value.color; break;
                    case SerializedPropertyType.Vector2: property.vector2Value = value.vector; break;
                    case SerializedPropertyType.Vector3: property.vector3Value = value.vector; break;
                    case SerializedPropertyType.Vector4: property.vector4Value = value.vector; break;
                    case SerializedPropertyType.Quaternion: property.quaternionValue = value.quaternion; break;
                    case SerializedPropertyType.Rect: property.rectValue = value.rect; break;
                    case SerializedPropertyType.Bounds: property.boundsValue = value.bounds; break;
                    case SerializedPropertyType.ObjectReference: property.objectReferenceValue = ResolveReference(value.reference, nodes); break;
                    default: throw new InvalidOperationException("지원하지 않는 필드 타입: " + value.path);
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureComponents(GameObject node, NodeData source)
        {
            if (!source.textMeshPro && node.TryGetComponent(out TextMeshPro oldText)) Object.DestroyImmediate(oldText);
            if (!string.IsNullOrEmpty(source.mesh))
            {
                if (!node.GetComponent<MeshFilter>()) node.AddComponent<MeshFilter>();
            }
            else if (!source.textMeshPro && node.TryGetComponent(out MeshFilter oldFilter)) Object.DestroyImmediate(oldFilter);
            if (source.textMeshPro && !node.GetComponent<TextMeshPro>()) node.AddComponent<TextMeshPro>();
            if (source.meshRenderer && !node.GetComponent<MeshRenderer>()) node.AddComponent<MeshRenderer>();
            if (!source.meshRenderer && node.TryGetComponent(out MeshRenderer oldRenderer)) Object.DestroyImmediate(oldRenderer);
            if (source.hasStudyModel && !node.GetComponent<CounterStudyModel>()) node.AddComponent<CounterStudyModel>();
            if (!source.hasStudyModel && node.TryGetComponent(out CounterStudyModel oldModel)) Object.DestroyImmediate(oldModel);
        }

        private static Transform ResolveTransform(string key, Dictionary<string, Transform> nodes)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (nodes.TryGetValue(key, out Transform value)) return value;
            throw new InvalidOperationException("Transform 키를 찾을 수 없습니다: " + key);
        }

        private static void ApplyTransform(Transform transform, NodeData node)
        {
            if (node.hasRect)
            {
                var rect = (RectTransform)transform;
                rect.anchorMin = node.rect.anchorMin; rect.anchorMax = node.rect.anchorMax;
                rect.pivot = node.rect.pivot; rect.sizeDelta = node.rect.sizeDelta;
                rect.anchoredPosition3D = node.rect.anchoredPosition;
            }
            transform.localPosition = node.position;
            transform.localRotation = node.rotation;
            transform.localScale = node.scale;
        }

        private static void ValidateSource(SourceData data)
        {
            if (data == null || data.version != 1 || data.nodes == null || data.nodes.Length == 0 || data.meshes == null)
                throw new InvalidOperationException("계수기 소스 형식이 올바르지 않습니다.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var meshKeys = new HashSet<string>(data.meshes.Select(m => m.key), StringComparer.Ordinal);
            if (meshKeys.Count != data.meshes.Length) throw new InvalidOperationException("중복된 메시 키가 있습니다.");
            for (int i = 0; i < data.nodes.Length; i++)
            {
                NodeData node = data.nodes[i];
                if (node == null || string.IsNullOrEmpty(node.key) || (i == 0 && node.key != "0") || !keys.Add(node.key) ||
                    node.parent >= i || (i == 0 ? node.parent != -1 : node.parent < 0))
                    throw new InvalidOperationException("노드 키/부모 순서가 올바르지 않습니다: " + i);
                if (!string.IsNullOrEmpty(node.mesh) && !meshKeys.Contains(node.mesh))
                    throw new InvalidOperationException("메시 키가 없습니다: " + node.mesh);
                if (node.hasRect && node.rect == null || node.hasStudyModel && node.studyModel == null)
                    throw new InvalidOperationException("노드의 명시적 존재 플래그에 해당하는 데이터가 없습니다: " + node.key);
                if (node.textMeshPro && (!node.meshRenderer || !node.hasRect || !string.IsNullOrEmpty(node.mesh)))
                    throw new InvalidOperationException("TMP 노드에는 RectTransform/Renderer가 필요하며 고정 메시를 지정할 수 없습니다.");
                foreach (PropertyData property in (node.renderer ?? Array.Empty<PropertyData>()).Concat(node.text ?? Array.Empty<PropertyData>()))
                {
                    if (property == null) throw new InvalidOperationException("비어 있는 직렬화 필드입니다: " + node.key);
                    ValidatePropertyPath(property.path);
                }
            }
        }

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("계수기 Export/Rebuild는 Play 모드를 완전히 종료한 뒤 실행하세요.");
        }

        private static bool IsExcludedProperty(string path)
        {
            return !string.IsNullOrEmpty(path) && ExcludedPropertyRoots.Any(root =>
                path.Equals(root, StringComparison.Ordinal) || path.StartsWith(root + ".", StringComparison.Ordinal));
        }

        private static void ValidatePropertyPath(string path)
        {
            if (string.IsNullOrEmpty(path) || IsExcludedProperty(path) ||
                path.IndexOf(".m_FileID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(".m_PathID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.Equals("m_FileID", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("m_PathID", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("금지된 Unity 내부 참조 경로입니다. 이전 JSON을 실행하지 말고 새로 Export하세요: " + path);
        }
    }
}
