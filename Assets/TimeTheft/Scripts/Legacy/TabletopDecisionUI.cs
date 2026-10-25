using TimeTheft.Legacy;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimeTheft
{
    // 화면에 놓인 두 선택지만 담당한다. 값과 판정은 기존 Controller/Match가 소유한다.
    public sealed class TabletopDecisionUI : MonoBehaviour
    {
        [SerializeField] private RectTransform controls;
        [SerializeField] private Button keepButton, swapButton;
        private Action<ClockAction> choose;
        private TabletopFocusCamera worldCamera;
        private bool initialized;

        public Button KeepButton => keepButton;
        public Button SwapButton => swapButton;
        public bool IsVisible => controls && controls.gameObject.activeInHierarchy;

        public void Initialize(Action<ClockAction> chooseAction, TabletopFocusCamera inputCamera = null)
        {
            if (!controls || !keepButton || !swapButton || keepButton == swapButton)
                throw new InvalidOperationException("결정 UI의 영역과 유지·교환 버튼을 연결하세요.");
            choose = chooseAction;
            worldCamera = inputCamera;
            if (!initialized)
            {
                keepButton.onClick.AddListener(Keep);
                swapButton.onClick.AddListener(Swap);
                initialized = true;
            }
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (!controls) return;
            if (keepButton) keepButton.interactable = visible;
            if (swapButton) swapButton.interactable = visible;
            var events = EventSystem.current;
            if (!visible && events && events.currentSelectedGameObject &&
                events.currentSelectedGameObject.transform.IsChildOf(controls))
                events.SetSelectedGameObject(null);
            controls.gameObject.SetActive(visible);
        }

        private void Keep() => Submit(ClockAction.Keep);
        private void Swap() => Submit(ClockAction.Swap);

        private void Submit(ClockAction action)
        {
            if (!IsVisible || !keepButton.interactable || !swapButton.interactable) return;
            if (worldCamera) worldCamera.SuppressPointerForCurrentFrame();
            // 콜백보다 먼저 닫아 같은 프레임의 중복 입력도 막는다.
            SetVisible(false);
            choose?.Invoke(action);
        }

        private void OnDisable() => SetVisible(false);

        private void OnDestroy()
        {
            if (!initialized) return;
            if (keepButton) keepButton.onClick.RemoveListener(Keep);
            if (swapButton) swapButton.onClick.RemoveListener(Swap);
        }
    }
}
