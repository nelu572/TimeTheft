using System;
using System.IO;
using UnityEngine;

namespace TimeTheft.ItemPresentation
{
    public sealed class BypassCablePresentation : MonoBehaviour
    {
        public BypassCableData Data;
        public Transform Space;
        public Transform[] ClipRoots, CableEnds;
        public MeshFilter[] CableMeshes;
        // 케이블/클립 사이의 이음새 오차와 실제 게임 접점 오차는 별개다.
        public float MaxEndpointError { get; private set; }
        public float MaxTargetAnchorError
        {
            get
            {
                float error = 0;
                if (!initialized) return error;
                for (int cable = 0; cable < 2; cable++)
                    if (contacts[cable] && CableEnds[cable]) error = Mathf.Max(error, Vector3.Distance(contacts[cable].position,
                        contactsAreJaws ? ClipRoots[cable].TransformPoint(JawLocal(cable)) : CableEnds[cable].position));
                return error;
            }
        }
        public float MaxAttachedTargetError => AttachmentWeight >= .9999f ? MaxTargetAnchorError : 0f;
        public float AttachmentWeight { get; private set; }
        public bool HasTargetAnchors => initialized && contacts[0] && contacts[1];

        private float[] times, centers, endcaps;
        private Vector3[][] vertices, normals, points;
        private Vector3[][] radialVertices, radialNormals;
        private Mesh[] meshes;
        private Transform[] contacts;
        private Quaternion[] sampledClipRotations;
        private float sourceSeconds;
        private bool initialized;
        private bool contactsAreJaws;
        // FBX의 cm 단위 자식 좌표다. 원본 m 좌표를 그대로 쓰면 CanonicalRoot의 100배가 중복 적용된다.
        private static Vector3 JawLocal(int cable) => new Vector3(.21080005f, cable == 0 ? .02639984f : -.02640032f, -.00186835f) * .01f;
        private int lastSampleFrame = -1;

        public void Initialize()
        {
            if (initialized || Data == null) return;
            byte[] bytes = Data.Samples.bytes;
            times = Read(bytes, Data.SecondsOffset, Data.SampleCount);
            centers = Read(bytes, Data.CentersOffset, Data.SampleCount * 2 * Data.RingCount * 3);
            endcaps = Read(bytes, Data.EndcapsOffset, Data.SampleCount * 2 * 3);
            vertices = new Vector3[2][]; normals = new Vector3[2][]; points = new Vector3[2][];
            radialVertices = new Vector3[2][]; radialNormals = new Vector3[2][];
            meshes = new Mesh[2]; contacts = new Transform[2]; sampledClipRotations = new Quaternion[2];
            for (int cable = 0; cable < 2; cable++)
            {
                meshes[cable] = Instantiate(Data.MeshTemplates[cable]);
                meshes[cable].MarkDynamic();
                CableMeshes[cable].sharedMesh = meshes[cable];
                vertices[cable] = new Vector3[Data.Topology[cable].LoopVertex.Length];
                normals[cable] = new Vector3[vertices[cable].Length];
                points[cable] = new Vector3[Data.RingCount];
                radialVertices[cable] = new Vector3[Data.RingCount * Data.RadialCount];
                radialNormals[cable] = new Vector3[Data.RingCount * Data.RadialCount];
            }
            initialized = true;
            Sample(0f);
        }

        private static float[] Read(byte[] bytes, int offset, int count)
        {
            var values = new float[count];
            if (BitConverter.IsLittleEndian) Buffer.BlockCopy(bytes, offset, values, 0, count * 4);
            else
            {
                using (var reader = new BinaryReader(new MemoryStream(bytes)))
                {
                    reader.BaseStream.Position = offset;
                    for (int i = 0; i < count; i++) values[i] = reader.ReadSingle();
                }
            }
            return values;
        }

        public void Bind(Transform playerBlue, Transform opponentRed, bool jawTargets = false)
        {
            Initialize();
            if (!initialized) return;
            contacts[0] = playerBlue; contacts[1] = opponentRed;
            contactsAreJaws = jawTargets;
        }

        private static Vector3 UnityPoint(float[] source, int offset) => new Vector3(source[offset], source[offset + 2], source[offset + 1]);

        private int LowerSample(float seconds)
        {
            int low = 0, high = times.Length - 1;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (times[mid] <= seconds) low = mid; else high = mid;
            }
            return low;
        }

        private Vector3 End(int sample, int cable) => UnityPoint(endcaps, (sample * 2 + cable) * 3);

        private Vector3 AttachedEnd(int cable)
        {
            const float seconds = 128f / 30f; // 원본129f의 연결 완료점. 양쪽 endcap은129~173f 동안 고정된다.
            int a = LowerSample(seconds), b = a + 1;
            return Vector3.Lerp(End(a, cable), End(b, cable), Mathf.InverseLerp(times[a], times[b], seconds));
        }

        public void Sample(float seconds) => Sample(seconds, true);

        private void Sample(float seconds, bool animationSampled)
        {
            if (!initialized) { Initialize(); if (!initialized) return; }
            sourceSeconds = Mathf.Clamp(seconds, 0f, times[times.Length - 1]);
            lastSampleFrame = Time.frameCount;
            int a = LowerSample(sourceSeconds), b = a + 1;
            float t = Mathf.InverseLerp(times[a], times[b], sourceSeconds);
            float frame = 1f + sourceSeconds * 30f;
            float envelope = frame < 61f ? 0f : frame <= 129f ? Mathf.SmoothStep(0f, 1f, (frame - 61f) / 68f) : frame <= 172f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (frame - 173f) / 64f);
            AttachmentWeight = envelope;
            MaxEndpointError = 0f;
            for (int cable = 0; cable < 2; cable++)
            {
                if (animationSampled) sampledClipRotations[cable] = ClipRoots[cable].localRotation;
                else ClipRoots[cable].localRotation = sampledClipRotations[cable];
                // 원본 rigid clip/jaw 회전을 보존한다. 목표 Transform의 축으로 케이블 방향을 재설계하지 않는다.
                var p = points[cable];
                Vector3 offset = contacts[cable] != null ? Space.InverseTransformPoint(contacts[cable].position) - AttachedEnd(cable) : Vector3.zero;
                if (contactsAreJaws && contacts[cable])
                {
                    // M4.10 GripRoot 기준 jaw 접점으로 실제 HexHead를 문다. 원본 케이블 중심선은 끝점만 재지정한다.
                    ClipRoots[cable].rotation = Quaternion.Slerp(ClipRoots[cable].rotation, contacts[cable].rotation, envelope);
                    var nativeEnd = Space.TransformPoint(Vector3.Lerp(End(a, cable), End(b, cable), t));
                    ClipRoots[cable].position += nativeEnd - CableEnds[cable].position;
                    offset = Space.InverseTransformVector(contacts[cable].position - ClipRoots[cable].TransformPoint(JawLocal(cable)));
                }
                for (int ring = 0; ring < Data.RingCount; ring++)
                {
                    int ia = ((a * 2 + cable) * Data.RingCount + ring) * 3;
                    int ib = ((b * 2 + cable) * Data.RingCount + ring) * 3;
                    float u = ring / (float)(Data.RingCount - 1);
                    p[ring] = Vector3.Lerp(UnityPoint(centers, ia), UnityPoint(centers, ib), t) + offset * (envelope * u * u * (3f - 2f * u));
                }
                // FBX CableEnd와 끝점을 동기화하고 root/jaw 회전과 단위 보정을 유지한다.
                Vector3 endpoint = Vector3.Lerp(End(a, cable), End(b, cable), t) + offset * envelope;
                p[p.Length - 1] = endpoint;
                Vector3 desiredWorld = Space.TransformPoint(endpoint);
                ClipRoots[cable].position += desiredWorld - CableEnds[cable].position;
                MaxEndpointError = Mathf.Max(MaxEndpointError, Vector3.Distance(desiredWorld, CableEnds[cable].position));
                BuildTube(cable);
            }
        }

        private void BuildTube(int cable)
        {
            var p = points[cable];
            var topology = Data.Topology[cable];
            var output = vertices[cable];
            var ns = normals[cable];
            // binary는Space(asset local) 좌표다. 모델의 원본 로컬 변환도 유지한 채 Mesh 좌표로 변환한다.
            var toMesh = CableMeshes[cable].transform.worldToLocalMatrix * Space.localToWorldMatrix;
            var normalToMesh = toMesh.inverse.transpose;
            Vector3 previousTangent = (p[1] - p[0]).normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, previousTangent).normalized;
            if (up.sqrMagnitude < .1f) up = Vector3.ProjectOnPlane(Vector3.right, previousTangent).normalized;
            // 면 코너별 UV를 유지한다. 매 프레임 배열과 Mesh를 새로 만들지 않는다.
            for (int ring = 0; ring < Data.RingCount; ring++)
            {
                Vector3 tangent = (p[Mathf.Min(ring + 1, p.Length - 1)] - p[Mathf.Max(ring - 1, 0)]).normalized;
                if (tangent.sqrMagnitude < .1f) tangent = previousTangent;
                up = Quaternion.FromToRotation(previousTangent, tangent) * up;
                up = Vector3.ProjectOnPlane(up, tangent).normalized;
                Vector3 right = Vector3.Cross(tangent, up).normalized;
                for (int radialIndex = 0; radialIndex < Data.RadialCount; radialIndex++)
                {
                    int vertex = ring * Data.RadialCount + radialIndex;
                    float angle = radialIndex * Mathf.PI * 2f / Data.RadialCount;
                    var radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    radialVertices[cable][vertex] = p[ring] + radial * topology.Radius[ring];
                    radialNormals[cable][vertex] = radial;
                }
                previousTangent = tangent;
            }
            for (int loop = 0; loop < output.Length; loop++)
            {
                output[loop] = toMesh.MultiplyPoint3x4(radialVertices[cable][topology.LoopVertex[loop]]);
                ns[loop] = normalToMesh.MultiplyVector(radialNormals[cable][topology.LoopVertex[loop]]).normalized;
            }
            meshes[cable].vertices = output;
            meshes[cable].normals = ns;
            meshes[cable].RecalculateBounds();
        }

        public void ResetPose()
        {
            if (!initialized) return;
            contacts[0] = contacts[1] = null;
            Sample(0f);
        }

        private void LateUpdate()
        {
            if (initialized && lastSampleFrame != Time.frameCount) Sample(sourceSeconds, false);
        }

        private void OnDestroy()
        {
            if (!initialized) return;
            foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
        }
    }
}
