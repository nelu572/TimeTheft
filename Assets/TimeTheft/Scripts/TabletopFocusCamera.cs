using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeTheft
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera), typeof(TabletopCameraLook))]
    public sealed class TabletopFocusCamera : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float returnDuration = .7f;
        [SerializeField, Min(1)] private float rayDistance = 30;
        [SerializeField] private LayerMask interactionLayers = ~0;
        [Tooltip("플레이 씬에서 집중 중에도 유지/교환 등 월드 조작부를 클릭할 수 있게 한다.")]
        [SerializeField] private bool allowInputWhileFocused;
        private enum ViewState { Free, MovingToTarget, Focused, Returning }
        private ViewState state;
        private Camera viewCamera;
        private TabletopCameraLook look;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private float homeFieldOfView;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private float startFieldOfView;
        private float elapsed;
        private float duration;
        private bool windowFocused;

        public TabletopFocusTarget CurrentTarget { get; private set; }
        public TabletopFocusTarget HoveredTarget { get; private set; }
        public bool HasCameraControl => state != ViewState.Free;
        public bool IsTransitioning => state == ViewState.MovingToTarget || state == ViewState.Returning;

        private void Awake()
        {
            viewCamera = GetComponent<Camera>();
            look = GetComponent<TabletopCameraLook>();
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            homeFieldOfView = viewCamera.fieldOfView;
        }

        private void OnEnable() => windowFocused = Application.isFocused;

        private void OnApplicationFocus(bool hasFocus)
        {
            windowFocused = hasFocus;
            if (!hasFocus) SetHover(null);
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (windowFocused && HasCameraControl &&
                ((mouse != null && mouse.rightButton.wasPressedThisFrame) ||
                 (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)))
            {
                ReturnToOverview();
                return;
            }

            if (state != ViewState.Free)
            {
                if (state != ViewState.Returning && (!CurrentTarget || !CurrentTarget.IsAvailable))
                {
                    ReturnToOverview();
                    return;
                }
                if (!allowInputWhileFocused || state != ViewState.Focused)
                {
                    SetHover(null);
                    return;
                }
            }

            TabletopFocusTarget target = null;
            if (windowFocused && mouse != null && viewCamera.pixelRect.Contains(mouse.position.ReadValue()))
            {
                Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, interactionLayers, QueryTriggerInteraction.Ignore))
                {
                    var candidate = hit.collider.GetComponentInParent<TabletopFocusTarget>();
                    if (candidate && candidate.IsAvailable && candidate.InputEnabled) target = candidate;
                }
            }
            SetHover(target);
            if (target && mouse.leftButton.wasPressedThisFrame)
            {
                SetHover(null);
                bool focusOnClick = target.FocusOnClick;
                target.Activate();
                if (focusOnClick && target && target.IsAvailable) Focus(target);
            }
        }

        public bool Focus(TabletopFocusTarget target)
        {
            if (!isActiveAndEnabled || !target || !target.IsAvailable) return false;
            SetHover(null);
            look.SetSuspended(true);
            CurrentTarget = target;
            BeginTransition(target.TransitionDuration);
            state = ViewState.MovingToTarget;
            return true;
        }

        public void ReturnToOverview()
        {
            if (!HasCameraControl || state == ViewState.Returning) return;
            CurrentTarget = null;
            BeginTransition(returnDuration);
            state = ViewState.Returning;
        }

        private void BeginTransition(float seconds)
        {
            // 중간에 복귀하거나 다른 대상을 선택해도 현재 pose에서 이어 간다.
            startPosition = transform.position;
            startRotation = transform.rotation;
            startFieldOfView = viewCamera.fieldOfView;
            elapsed = 0;
            duration = Mathf.Max(.1f, seconds);
        }

        private void LateUpdate()
        {
            if (!HasCameraControl) return;
            bool returning = state == ViewState.Returning;
            if (!returning && (!CurrentTarget || !CurrentTarget.IsAvailable)) return;
            Transform parent = transform.parent;
            Vector3 destination = returning
                ? (parent ? parent.TransformPoint(homePosition) : homePosition) : CurrentTarget.FocusPoint.position;
            Quaternion rotation = returning
                ? (parent ? parent.rotation * homeRotation : homeRotation) : CurrentTarget.FocusPoint.rotation;
            float fov = returning || !CurrentTarget.OverrideFieldOfView ? homeFieldOfView : CurrentTarget.FieldOfView;
            elapsed += Time.unscaledDeltaTime;
            float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
            transform.SetPositionAndRotation(Vector3.Lerp(startPosition, destination, blend),
                Quaternion.Slerp(startRotation, rotation, blend));
            viewCamera.fieldOfView = Mathf.Lerp(startFieldOfView, fov, blend);
            if (elapsed < duration) return;
            if (returning)
            {
                state = ViewState.Free;
                look.SetSuspended(false);
            }
            else state = ViewState.Focused;
        }

        private void SetHover(TabletopFocusTarget target)
        {
            if (HoveredTarget == target) return;
            if (HoveredTarget) HoveredTarget.SetHighlighted(false);
            HoveredTarget = target;
            if (HoveredTarget) HoveredTarget.SetHighlighted(true);
        }

        private void OnDisable()
        {
            SetHover(null);
            if (!HasCameraControl) return;
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            viewCamera.fieldOfView = homeFieldOfView;
            state = ViewState.Free;
            CurrentTarget = null;
            look.SetSuspended(false);
        }
    }
}
