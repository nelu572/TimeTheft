using System;
using UnityEngine;

namespace TimeTheft
{
    [DisallowMultipleComponent]
    public sealed class TabletopFocusTarget : MonoBehaviour
    {
        [Tooltip("카메라의 목표 월드 위치와 방향. 파란 Z축이 바라보는 방향이다.")]
        [SerializeField] private Transform focusPoint;
        [SerializeField, Min(.1f)] private float transitionDuration = .7f;
        [SerializeField] private bool overrideFieldOfView = true;
        [SerializeField, Range(15, 90)] private float fieldOfView = 42;
        [Tooltip("끄면 집중 이동 없이 클릭 이벤트만 전달한다. 테이블 조작부에서 사용한다.")]
        [SerializeField] private bool focusOnClick = true;
        [Header("호버 강조 (공유 머티리얼은 변경하지 않음)")]
        [SerializeField] private SpriteRenderer[] highlightRenderers = new SpriteRenderer[0];
        [SerializeField, Range(1, 1.5f)] private float hoverBrightness = 1.18f;
        private Color[] originalColors;
        private bool highlighted;

        public Transform FocusPoint => focusPoint;
        public float TransitionDuration => Mathf.Max(.1f, transitionDuration);
        public bool OverrideFieldOfView => overrideFieldOfView;
        public float FieldOfView => Mathf.Clamp(fieldOfView, 15, 90);
        public bool IsAvailable => isActiveAndEnabled && focusPoint && focusPoint.gameObject.activeInHierarchy;
        public bool InputEnabled { get; private set; } = true;
        public bool FocusOnClick => focusOnClick;
        public bool IsHighlighted => highlighted;
        public event Action Clicked;

        public void SetInputEnabled(bool value)
        {
            if (!value) SetHighlighted(false);
            InputEnabled = value;
        }

        public void Activate()
        {
            if (IsAvailable && InputEnabled) Clicked?.Invoke();
        }

        public void SetHighlighted(bool value)
        {
            if (highlighted == value) return;
            if (value)
            {
                originalColors = new Color[highlightRenderers.Length];
                for (int i = 0; i < highlightRenderers.Length; i++)
                {
                    var renderer = highlightRenderers[i];
                    if (!renderer) continue;
                    Color color = renderer.color;
                    originalColors[i] = color;
                    renderer.color = new Color(color.r * hoverBrightness, color.g * hoverBrightness,
                        color.b * hoverBrightness, color.a);
                }
            }
            else
            {
                for (int i = 0; i < highlightRenderers.Length && i < originalColors.Length; i++)
                    if (highlightRenderers[i]) highlightRenderers[i].color = originalColors[i];
            }
            highlighted = value;
        }

        private void OnDisable() => SetHighlighted(false);

        private void OnDrawGizmosSelected()
        {
            if (!focusPoint) return;
            Gizmos.color = new Color(.8f, .65f, .25f);
            Gizmos.DrawLine(focusPoint.position, transform.position);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = focusPoint.localToWorldMatrix;
            Gizmos.DrawFrustum(Vector3.zero, fieldOfView, .65f, .05f, 16f / 9);
            Gizmos.matrix = previous;
        }
    }
}
