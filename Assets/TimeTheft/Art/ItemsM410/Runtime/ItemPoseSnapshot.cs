using UnityEngine;

namespace TimeTheft.ItemPresentation
{
    // 검증 Preview와 실제 연출이 같은 Transform/Morph 복원을 사용한다.
    public sealed class ItemPoseSnapshot
    {
        private readonly Transform[] parts;
        private readonly Vector3[] positions, scales;
        private readonly Quaternion[] rotations;
        private readonly SkinnedMeshRenderer[] skins;
        private readonly float[][] weights;

        public ItemPoseSnapshot(GameObject root)
        {
            parts = root.GetComponentsInChildren<Transform>(true);
            positions = new Vector3[parts.Length];
            scales = new Vector3[parts.Length];
            rotations = new Quaternion[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                positions[i] = parts[i].localPosition;
                scales[i] = parts[i].localScale;
                rotations[i] = parts[i].localRotation;
            }
            skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            weights = new float[skins.Length][];
            for (int i = 0; i < skins.Length; i++)
            {
                weights[i] = new float[skins[i].sharedMesh.blendShapeCount];
                for (int j = 0; j < weights[i].Length; j++) weights[i][j] = skins[i].GetBlendShapeWeight(j);
            }
        }

        public void Restore()
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                parts[i].localPosition = positions[i];
                parts[i].localRotation = rotations[i];
                parts[i].localScale = scales[i];
            }
            for (int i = 0; i < skins.Length; i++)
                if (skins[i] != null)
                    for (int j = 0; j < weights[i].Length; j++) skins[i].SetBlendShapeWeight(j, weights[i][j]);
        }
    }
}
