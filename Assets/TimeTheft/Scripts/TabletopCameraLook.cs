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
        private Camera viewCamera;
        private Quaternion baseRotation;
        private Vector3 basePosition;
        private Vector2 look;
        private bool focused;
        private bool suspended;

        // 집중 카메라가 pose를 소유하는 동안 기준 구도를 다시 캡처하지 않는다.
        public void SetSuspended(bool value)
        {
            suspended = value;
            look = Vector2.zero;
        }

        private void OnEnable()
        {
            viewCamera = GetComponent<Camera>();
            focused = Application.isFocused;
            if (suspended) return;
            baseRotation = transform.localRotation;
            basePosition = transform.localPosition;
            look = Vector2.zero;
        }

        private void OnApplicationFocus(bool hasFocus) => focused = hasFocus;

        private void LateUpdate()
        {
            if (suspended) return;
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
                    target *= Mathf.Clamp01(strength);
                }
            }

            // 기준 pose에서 매번 계산해 회전과 이동이 누적되지 않게 한다.
            float blend = 1 - Mathf.Exp(-Mathf.Max(.1f, response) * Time.unscaledDeltaTime);
            look = Vector2.Lerp(look, target, blend);
            transform.localRotation = baseRotation * Quaternion.Euler(-look.y * verticalDegrees, look.x * horizontalDegrees, 0);
            transform.localPosition = basePosition + new Vector3(look.x * horizontalTravel, look.y * verticalTravel, 0);
        }

        private void OnDisable()
        {
            if (suspended) return;
            transform.localRotation = baseRotation;
            transform.localPosition = basePosition;
            look = Vector2.zero;
        }
    }
}
