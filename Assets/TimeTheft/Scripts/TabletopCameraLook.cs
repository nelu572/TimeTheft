using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeTheft
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class TabletopCameraLook : MonoBehaviour
    {
        [Header("기본 구도는 부모 CameraRig의 Transform에서 편집")]
        [SerializeField, Range(0, 1)] private float strength = 1;
        [SerializeField, Range(0, 15)] private float horizontalDegrees = 7;
        [SerializeField, Range(0, 15)] private float verticalDegrees = 4.5f;
        [Header("앉은 자리에서의 미세 이동 (CameraRig 로컬 단위)")]
        [SerializeField, Min(0)] private float horizontalTravel = .22f;
        [SerializeField, Min(0)] private float verticalTravel = .12f;
        [SerializeField, Min(.1f)] private float response = 6;
        [Tooltip("각 축의 화면 중앙 무반응 영역. 0.12면 화면 너비/높이의 중앙 12%이며, 밖에서는 부드럽게 증가한다.")]
        [SerializeField, Range(0, .5f)] private float deadZone = .12f;
        private Camera viewCamera;
        private Quaternion baseRotation;
        private Vector3 basePosition;
        private Vector2 look;
        private bool focused;
        private bool suspended;
        private bool hoverLocked;
        private bool releasingHover;
        private Vector3 hoverStartPosition;
        private Quaternion hoverStartRotation;
        private Vector3 hoverPosition;
        private Quaternion hoverRotation;
        private float hoverElapsed;
        private float hoverDuration;

        public bool IsHoverLocked => hoverLocked;

        public void MoveToHoverPose(Vector3 position, Quaternion rotation, float seconds)
        {
            if (suspended || !isActiveAndEnabled) return;
            BeginHoverTransition(seconds);
            hoverPosition = position;
            hoverRotation = rotation;
            hoverLocked = true;
            releasingHover = false;
        }

        public void ReleaseHover(float seconds)
        {
            if (!hoverLocked) return;
            BeginHoverTransition(seconds);
            hoverLocked = false;
            releasingHover = true;
        }

        private void BeginHoverTransition(float seconds)
        {
            // 다른 대상/자유 시차로 전환할 때도 현재 화면에서 이어 간다.
            hoverStartPosition = transform.position;
            hoverStartRotation = transform.rotation;
            hoverElapsed = 0;
            hoverDuration = Mathf.Max(.01f, seconds);
        }

        // 집중 카메라가 pose를 소유하는 동안 기준 구도를 다시 캡처하지 않는다.
        public void SetSuspended(bool value)
        {
            suspended = value;
            hoverLocked = false;
            releasingHover = false;
            look = Vector2.zero;
        }

        private void OnEnable()
        {
            viewCamera = GetComponent<Camera>();
            focused = Application.isFocused;
            hoverLocked = false;
            releasingHover = false;
            if (suspended) return;
            baseRotation = transform.localRotation;
            basePosition = transform.localPosition;
            look = Vector2.zero;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (!hasFocus) ReleaseHover(.18f);
        }

        private void LateUpdate()
        {
            if (suspended) return;
            if (hoverLocked)
            {
                ApplyHoverTransition(hoverPosition, hoverRotation);
                return;
            }
            Vector2 target = Vector2.zero;
            var mouse = Mouse.current;
            Rect viewport = viewCamera.pixelRect;
            if (focused && mouse != null && viewport.width > 0 && viewport.height > 0)
            {
                Vector2 pointer = mouse.position.ReadValue();
                if (viewport.Contains(pointer))
                {
                    target = new Vector2((pointer.x - viewport.center.x) * 2 / viewport.width,
                        (pointer.y - viewport.center.y) * 2 / viewport.height);
                    target = new Vector2(OutsideDeadZone(target.x), OutsideDeadZone(target.y));
                    target *= Mathf.Clamp01(strength);
                }
            }

            // 기준 pose에서 매번 계산해 회전과 이동이 누적되지 않게 한다.
            float blend = 1 - Mathf.Exp(-Mathf.Max(.1f, response) * Time.unscaledDeltaTime);
            look = Vector2.Lerp(look, target, blend);
            Quaternion authoredRotation = transform.parent ? transform.parent.rotation * baseRotation : baseRotation;
            // yaw는 월드 수직축, pitch는 카메라 가로축 기준. 기울어진 Rig에서도 수평선에 roll을 더하지 않는다.
            Quaternion rotation = Quaternion.AngleAxis(look.x * horizontalDegrees, Vector3.up) * authoredRotation
                * Quaternion.AngleAxis(-look.y * verticalDegrees, Vector3.right);
            Vector3 position = basePosition + new Vector3(look.x * horizontalTravel, look.y * verticalTravel, 0);
            position = transform.parent ? transform.parent.TransformPoint(position) : position;
            if (releasingHover)
            {
                ApplyHoverTransition(position, rotation);
                if (hoverElapsed >= hoverDuration) releasingHover = false;
            }
            else transform.SetPositionAndRotation(position, rotation);
        }

        private void ApplyHoverTransition(Vector3 position, Quaternion rotation)
        {
            hoverElapsed = Mathf.Min(hoverDuration, hoverElapsed + Time.unscaledDeltaTime);
            float blend = Mathf.SmoothStep(0, 1, hoverElapsed / hoverDuration);
            // 시간제 보간으로 끝점에 정확히 도달한다. 정착 후에는 잔여 필터가 없다.
            transform.SetPositionAndRotation(Vector3.Lerp(hoverStartPosition, position, blend),
                Quaternion.Slerp(hoverStartRotation, rotation, blend));
        }

        private float OutsideDeadZone(float value)
        {
            float dead = Mathf.Clamp(deadZone, 0, .5f);
            float amount = Mathf.Clamp01((Mathf.Abs(value) - dead) / (1 - dead));
            return Mathf.Sign(value) * Mathf.SmoothStep(0, 1, amount);
        }

        private void OnDisable()
        {
            hoverLocked = false;
            releasingHover = false;
            if (suspended) return;
            transform.localRotation = baseRotation;
            transform.localPosition = basePosition;
            look = Vector2.zero;
        }
    }
}
