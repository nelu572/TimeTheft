using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

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
        [Header("Magnetic Hover · 클릭 집중/복귀가 우선")]
        [UnityEngine.Serialization.FormerlySerializedAs("lockCameraOnHover")]
        [SerializeField] private bool magneticHover = true;
        [SerializeField, Range(.05f, .4f)] private float hoverTransitionDuration = .16f;
        [SerializeField, Range(.05f, .5f)] private float hoverReturnDuration = .18f;
        [Tooltip("기본 테이블 구도 기준 좌우/상하 최대 이동. HoverPoint가 없는 대상에 적용한다.")]
        [SerializeField] private Vector2 hoverTravel = new Vector2(.12f, .06f);
        [SerializeField, Min(0)] private float hoverForwardTravel = .06f;
        [Tooltip("대상 방향에 따른 최대 Yaw(X)/Pitch(Y). Roll은 추가하지 않는다.")]
        [SerializeField] private Vector2 hoverDegrees = new Vector2(1.6f, 1);
        [Tooltip("실제 호버가 끝나도 경계 떨림 때문에 카메라가 즉시 움직이지 않도록 기다리는 시간.")]
        [SerializeField, Range(0, .3f)] private float hoverReleaseDelay = .08f;
        [Tooltip("마지막으로 가리킨 Collider의 화면 경계 밖 여유 픽셀. 클릭 영역은 늘리지 않는다.")]
        [SerializeField, Range(0, 24)] private float hoverExitPadding = 6;
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
        private TabletopFocusTarget lockTarget;
        private Collider lockCollider;
        private float hoverExitElapsed;
        private Vector3 hoverRayPosition;
        private Quaternion hoverRayRotation;
        private Vector2 lastPointer;
        private bool hadPointer;
        private EventSystem pointerEventSystem;
        private PointerEventData uiPointer;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private int suppressedPointerFrame = -1;
        private bool presentationCameraOwned;

        public TabletopFocusTarget CurrentTarget { get; private set; }
        public TabletopFocusTarget HoveredTarget { get; private set; }
        public TabletopFocusTarget MagneticTarget => lockTarget;
        public bool HasCameraControl => presentationCameraOwned || state != ViewState.Free;
        public bool IsTransitioning => state == ViewState.MovingToTarget || state == ViewState.Returning;
        public bool IsPresentationCameraOwned => presentationCameraOwned;
        public Vector3 OverviewPosition => transform.parent ? transform.parent.TransformPoint(homePosition) : homePosition;
        public Quaternion OverviewRotation => transform.parent ? transform.parent.rotation * homeRotation : homeRotation;
        public float OverviewFieldOfView => homeFieldOfView;

        public void BeginPresentationCamera()
        {
            SetHover(null); ClearMagneticHover(); CurrentTarget = null; state = ViewState.Free;
            look.SetSuspended(true); presentationCameraOwned = true;
        }

        public void EndPresentationCamera()
        {
            if (!presentationCameraOwned) return;
            transform.localPosition = homePosition; transform.localRotation = homeRotation;
            viewCamera.fieldOfView = homeFieldOfView; state = ViewState.Free; CurrentTarget = null;
            presentationCameraOwned = false; look.SetSuspended(false); SuppressPointerForCurrentFrame();
        }

        // UI가 제출과 동시에 숨겨져도 같은 프레임의 클릭이 뒤의 물체로 통과하지 않는다.
        public void SuppressPointerForCurrentFrame() => suppressedPointerFrame = Time.frameCount;

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
            if (!hasFocus)
            {
                SetHover(null);
                ClearMagneticHover();
            }
        }

        private void Update()
        {
            if (presentationCameraOwned) return;
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
                    ClearMagneticHover();
                    return;
                }
            }

            TabletopFocusTarget target = null;
            Collider hitCollider = null;
            bool pointerAvailable = windowFocused && mouse != null && viewCamera.pixelRect.Contains(mouse.position.ReadValue());
            Vector2 pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            if (pointerAvailable && (suppressedPointerFrame == Time.frameCount || PointerOverUI(pointer))) pointerAvailable = false;
            bool pointerMoved = !hadPointer || (pointer - lastPointer).sqrMagnitude >= 1;
            if (pointerMoved) lastPointer = pointer;
            hadPointer = pointerAvailable;
            if (pointerAvailable)
            {
                Physics.SyncTransforms();
                target = RaycastTarget(viewCamera.ScreenPointToRay(pointer), out hitCollider);
            }
            // 클릭은 보조 ray나 경계 여유로 넓히지 않고, 현재 화면의 실제 대상만 사용한다.
            var clickTarget = target;
            if (pointerAvailable && magneticHover && state == ViewState.Free && ValidMagneticTarget())
            {
                Ray currentRay = viewCamera.ScreenPointToRay(pointer);
                Quaternion inverse = Quaternion.Inverse(transform.rotation);
                Ray referenceRay = new Ray(hoverRayPosition + hoverRayRotation * (inverse * (currentRay.origin - transform.position)),
                    hoverRayRotation * (inverse * currentRay.direction));
                Collider referenceCollider;
                var referenceTarget = RaycastTarget(referenceRay, out referenceCollider);
                // 카메라 자체의 이동은 호버를 해제하거나 이웃 대상으로 바꾸지 않는다.
                // 실제 포인터 이동으로 가리킨 새 대상은 즉시 우선한다.
                if (referenceTarget == lockTarget && (!target || !pointerMoved))
                {
                    target = lockTarget;
                    hitCollider = referenceCollider;
                }
            }
            SetHover(target);
            UpdateMagneticHover(target, hitCollider, pointerAvailable, pointer);
            if (clickTarget && mouse.leftButton.wasPressedThisFrame)
            {
                SetHover(null);
                ClearMagneticHover();
                bool focusOnClick = clickTarget.FocusOnClick;
                clickTarget.Activate();
                if (focusOnClick && clickTarget && clickTarget.IsAvailable) Focus(clickTarget);
            }
        }

        public bool Focus(TabletopFocusTarget target)
        {
            if (presentationCameraOwned || !isActiveAndEnabled || !target || !target.IsAvailable) return false;
            SetHover(null);
            ClearMagneticHover();
            look.SetSuspended(true);
            CurrentTarget = target;
            BeginTransition(target.TransitionDuration);
            state = ViewState.MovingToTarget;
            return true;
        }

        public void ReturnToOverview()
        {
            if (presentationCameraOwned) return;
            ClearMagneticHover();
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
            if (presentationCameraOwned) return;
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

        private TabletopFocusTarget RaycastTarget(Ray ray, out Collider collider)
        {
            collider = null;
            if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, interactionLayers, QueryTriggerInteraction.Ignore)) return null;
            var target = hit.collider.GetComponentInParent<TabletopFocusTarget>();
            if (!target || !target.IsAvailable || !target.InputEnabled) return null;
            collider = hit.collider;
            return target;
        }

        private bool PointerOverUI(Vector2 pointer)
        {
            var events = EventSystem.current;
            if (!events) return false;
            if (pointerEventSystem != events || uiPointer == null)
            {
                pointerEventSystem = events;
                uiPointer = new PointerEventData(events);
            }
            // IsPointerOverGameObject의 이전 프레임 캐시에 의존하지 않는다.
            uiPointer.Reset();
            uiPointer.position = pointer;
            uiHits.Clear();
            events.RaycastAll(uiPointer, uiHits);
            foreach (var hit in uiHits)
                if (hit.module is GraphicRaycaster) return true;
            return false;
        }

        private bool ValidMagneticTarget()
        {
            return lockTarget && lockTarget.IsAvailable && lockTarget.InputEnabled && lockCollider &&
                lockCollider.enabled && lockCollider.gameObject.activeInHierarchy;
        }

        private void UpdateMagneticHover(TabletopFocusTarget target, Collider collider, bool pointerAvailable, Vector2 pointer)
        {
            if (!magneticHover || state != ViewState.Free || !pointerAvailable || !look.isActiveAndEnabled)
            {
                ClearMagneticHover();
                return;
            }
            if (target)
            {
                if (lockTarget != target)
                {
                    hoverRayPosition = transform.position;
                    hoverRayRotation = transform.rotation;
                    MoveToHover(target, collider);
                }
                lockTarget = target;
                lockCollider = collider;
                hoverExitElapsed = 0;
                return;
            }
            if (!ValidMagneticTarget())
            {
                ClearMagneticHover();
                return;
            }
            if (WithinHoverMargin(pointer))
            {
                hoverExitElapsed = 0;
                return;
            }
            hoverExitElapsed += Time.unscaledDeltaTime;
            if (hoverExitElapsed >= hoverReleaseDelay) ClearMagneticHover();
        }

        private void MoveToHover(TabletopFocusTarget target, Collider collider)
        {
            if (target.HoverPoint && target.HoverPoint.gameObject.activeInHierarchy)
            {
                look.MoveToHoverPose(target.HoverPoint.position, target.HoverPoint.rotation, hoverTransitionDuration);
                return;
            }
            Transform parent = transform.parent;
            Vector3 overview = parent ? parent.TransformPoint(homePosition) : homePosition;
            Quaternion rotation = parent ? parent.rotation * homeRotation : homeRotation;
            Vector3 direction = Quaternion.Inverse(rotation) * (collider.bounds.center - overview);
            float height = Mathf.Max(.01f, direction.z) * Mathf.Tan(homeFieldOfView * .5f * Mathf.Deg2Rad);
            Vector2 bias = new Vector2(Mathf.Clamp(direction.x / (height * viewCamera.aspect), -1, 1),
                Mathf.Clamp(direction.y / height, -1, 1)) * target.HoverStrength;
            Vector3 offset = new Vector3(bias.x * hoverTravel.x, bias.y * hoverTravel.y,
                hoverForwardTravel * target.HoverStrength) + target.HoverPositionOffset;
            Vector2 angles = new Vector2(-bias.y * hoverDegrees.y, bias.x * hoverDegrees.x) + target.HoverRotationOffset;
            Quaternion destinationRotation = Quaternion.AngleAxis(angles.y, Vector3.up) * rotation
                * Quaternion.AngleAxis(angles.x, Vector3.right);
            look.MoveToHoverPose(overview + rotation * offset, destinationRotation, hoverTransitionDuration);
        }

        private bool WithinHoverMargin(Vector2 pointer)
        {
            Bounds bounds = lockCollider.bounds;
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 screen = viewCamera.WorldToScreenPoint(corner);
                if (screen.z <= viewCamera.nearClipPlane) return false;
                minimum = Vector2.Min(minimum, screen);
                maximum = Vector2.Max(maximum, screen);
            }
            float padding = Mathf.Max(0, hoverExitPadding);
            return pointer.x >= minimum.x - padding && pointer.x <= maximum.x + padding &&
                pointer.y >= minimum.y - padding && pointer.y <= maximum.y + padding;
        }

        private void ClearMagneticHover()
        {
            if (look) look.ReleaseHover(hoverReturnDuration);
            lockTarget = null;
            lockCollider = null;
            hoverExitElapsed = 0;
        }

        private void OnDisable()
        {
            EndPresentationCamera();
            SetHover(null);
            ClearMagneticHover();
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
