using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView
    {
        private Button helpButton, overviewButton, skipButton;
        private TextMeshProUGUI helpButtonLabel;
        private TimeTheftGameController inputController;
        private Action skipAnimation;

        public Button HelpButton => helpButton;
        public Button OverviewButton => overviewButton;
        public Button SkipButton => skipButton;
        public bool IsHelpVisible => helpCard && helpCard.activeSelf;

        public void InitializeMouseControls(Action skip)
        {
            skipAnimation = skip;
            inputController = GetComponent<TimeTheftGameController>();
            if (helpButton) return;
            focusCamera.EmptyTableClicked += CancelItemSelection;
            focusCamera.SecondaryClicked += CancelItemSelection;
            helpButton = CreateMouseButton("Help", "도움말", 38, ClickHelp);
            helpButtonLabel = helpButton.GetComponentInChildren<TextMeshProUGUI>();
            overviewButton = CreateMouseButton("Return To Table", "테이블로", 100, ClickOverview);
            skipButton = CreateMouseButton("Skip Animation", "연출 단축", 162, ClickSkip);
            // 기존 도움말과 규칙 문구를 재사용하고 사용자 입력 안내만 갱신한다.
            foreach (var label in helpCard.GetComponentsInChildren<TextMeshPro>(true))
                label.text = label.text.Replace("H 도움말 · R 새 게임 · Space 연출 단축",
                    "우클릭: 확대 복귀 · 종료 전표: 다시하기");
            RefreshMouseControls();
            StyleMouseControls();
        }

        private Button CreateMouseButton(string name, string title, float top, UnityEngine.Events.UnityAction click)
        {
            var button = CreateItemButton(name, title, 0, click);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-120, -top);
            rect.sizeDelta = new Vector2(200, 52);
            return button;
        }

        private void RefreshMouseControls()
        {
            if (!helpButton) return;
            helpButtonLabel.text = "닫기";
            SetButton(helpButton, IsHelpVisible);
            SetButton(overviewButton, focusCamera && focusCamera.CurrentTarget && !focusCamera.IsPresentationCameraOwned);
            SetButton(skipButton, false);
        }

        private void ClickHelp()
        {
            focusCamera.SuppressPointerForCurrentFrame();
            ToggleHelp();
            RefreshMouseControls();
        }

        private void ClickOverview()
        {
            focusCamera.SuppressPointerForCurrentFrame();
            focusCamera.ReturnToOverview();
            RefreshMouseControls();
        }

        private void ClickSkip()
        {
            if (!inputController || !inputController.CanSkipAnimation) return;
            focusCamera.SuppressPointerForCurrentFrame();
            skipAnimation?.Invoke();
            RefreshMouseControls();
        }

        private void DestroyMouseControls()
        {
            if (focusCamera)
            {
                focusCamera.EmptyTableClicked -= CancelItemSelection;
                focusCamera.SecondaryClicked -= CancelItemSelection;
            }
            if (helpButton) { helpButton.onClick.RemoveListener(ClickHelp); Destroy(helpButton.gameObject); }
            if (overviewButton) { overviewButton.onClick.RemoveListener(ClickOverview); Destroy(overviewButton.gameObject); }
            if (skipButton) { skipButton.onClick.RemoveListener(ClickSkip); Destroy(skipButton.gameObject); }
        }
    }
}
