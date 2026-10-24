using UnityEngine;

namespace TimeTheft.CounterStudy
{
    // 실험 씬 전용. 저장된 메시를 변형하며 게임 데이터와 연결하지 않는다.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CounterStudyModel : MonoBehaviour
    {
        [Header("구조 비교용 크기 · 앞뒤 깊이 배율")]
        [Range(.55f, 1.1f)] public float size = .7f;
        [Range(.65f, 1.25f)] public float depth = 1;
        public Transform geometry;
        public Transform inspectionPoint;
        public string designName;

        public void ApplyDimensions()
        {
            transform.localScale = Vector3.one * Mathf.Clamp(size, .55f, 1.1f);
            if (geometry) geometry.localScale = new Vector3(1, 1, Mathf.Clamp(depth, .65f, 1.25f));
        }

        private void OnValidate() => ApplyDimensions();
    }
}
