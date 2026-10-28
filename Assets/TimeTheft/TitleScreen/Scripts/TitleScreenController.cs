using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TimeTheft.TitleScreen
{
    [Serializable]
    public sealed class TutorialCallout
    {
        [Tooltip("실제 스크린샷과 위치를 설정한 뒤 켜세요. 이미지가 없으면 표시하지 않습니다.")]
        public bool Visible;
        [Tooltip("실제 이미지 기준 위치. (0,0)은 좌하단, (1,1)은 우상단입니다. 비율 유지 여백은 제외합니다.")]
        public Vector2 HighlightPosition = new Vector2(.5f, .5f);
        [Tooltip("실제 이미지 기준 패널 중심. 이미지 밖 여백에 배치하려면 0~1 범위를 벗어나도 됩니다. 화면 안으로 제한됩니다.")]
        public Vector2 PanelPosition = new Vector2(.5f, .5f);
        public string Title;
        [TextArea(1, 2)] public string Description;
    }

    [Serializable]
    public sealed class TutorialPage
    {
        public string Title;
        [TextArea(3, 6)] public string Description;
        [Tooltip("실제 인게임 스크린샷. 비워 두면 임시 이미지 영역을 표시합니다.")]
        public Sprite Screenshot;
        public TutorialCallout[] Callouts = Array.Empty<TutorialCallout>();
    }

    public sealed class TitleScreenController : MonoBehaviour
    {
        [Header("스타일 · 안내 내용")]
        [SerializeField] private TitleScreenTheme theme;
        [SerializeField] private TutorialPage[] pages;
        [Header("게임 씬 · Build Profiles 등록 필요")]
        [SerializeField] private string gameScenePath = "Assets/Scenes/TimeTheftGame.unity";
        [Header("화면")]
        [SerializeField] private GameObject titlePanel;
        [SerializeField] private GameObject tutorialPanel;
        [SerializeField] private TMP_Text pageTitle, description, pageNumber, status;
        [SerializeField] private Image screenshot;
        [SerializeField] private GameObject screenshotPlaceholder;
        [SerializeField] private TutorialCalloutLayer calloutLayer;
        [SerializeField] private TitleScreenMotion motion;
        [SerializeField] private Button startButton, helpButton, quitButton, previousButton, nextButton, backButton;
        [Header("테마 바인딩")]
        [SerializeField] private TMP_Text[] headings, bodyLabels, mutedLabels, accentLabels;
        [SerializeField] private Graphic[] backgrounds, panels, rules, accentGraphics;
        [SerializeField] private Outline[] panelBorders;

        private int currentPage;
        private int lastNavigationFrame = -1;
        private bool transitioning;
        private bool initialized;
        private Coroutine introRoutine, transitionRoutine;

        public int CurrentPage => currentPage;
        public bool IsTutorialOpen => tutorialPanel.activeSelf;
        public bool IsTransitioning => transitioning;
        public string GameScenePath => gameScenePath;

        private void LateUpdate()
        {
            if (!calloutLayer || !IsTutorialOpen) return;
            int count = pages == null ? 0 : pages.Length;
            var page = count > 0 ? pages[Mathf.Clamp(currentPage, 0, count - 1)] : null;
            calloutLayer.Refresh(page?.Callouts, theme);
        }

        private void Awake()
        {
            ApplyTheme();
            startButton.onClick.AddListener(StartGame);
            helpButton.onClick.AddListener(OpenTutorial);
            quitButton.onClick.AddListener(Quit);
            previousButton.onClick.AddListener(PreviousPage);
            nextButton.onClick.AddListener(NextPage);
            backButton.onClick.AddListener(ReturnToTitle);
            if (motion) motion.Initialize();
            initialized = true;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || !initialized) return;
            transitioning = false;
            currentPage = 0;
            if (motion) motion.ResetAll();
            if (calloutLayer) calloutLayer.ResetPresentation(false);
            titlePanel.SetActive(true);
            tutorialPanel.SetActive(false);
            status.text = string.Empty;
            RefreshPage();
            BeginTitleEntry();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            introRoutine = transitionRoutine = null;
            transitioning = false;
            if (calloutLayer) calloutLayer.ResetPresentation(false);
            if (motion) motion.ResetAll();
        }

        private void BeginTitleEntry()
        {
            if (motion) introRoutine = StartCoroutine(RunTitleEntry());
        }

        private IEnumerator RunTitleEntry()
        {
            yield return motion.TitleEntry();
            introRoutine = null;
        }

        private void BeginTransition(IEnumerator routine)
        {
            if (introRoutine != null) StopCoroutine(introRoutine);
            introRoutine = null;
            transitioning = true;
            if (motion) motion.SetInput(false);
            transitionRoutine = StartCoroutine(RunTransition(routine));
        }

        private IEnumerator RunTransition(IEnumerator routine)
        {
            yield return routine;
            transitioning = false;
            if (motion) motion.SetInput(true);
            transitionRoutine = null;
        }

        public void ApplyTheme()
        {
            if (!theme) return;
            StyleText(headings, theme.HeadingFont, theme.Text);
            StyleText(bodyLabels, theme.BodyFont, theme.Text);
            StyleText(mutedLabels, theme.BodyFont, theme.MutedText);
            StyleText(accentLabels, theme.BodyFont, theme.Accent);
            StyleGraphics(backgrounds, theme.Background);
            StyleGraphics(panels, theme.Panel);
            StyleGraphics(rules, theme.Rule);
            StyleGraphics(accentGraphics, theme.Accent);
            if (panelBorders != null)
                foreach (var border in panelBorders) if (border) border.effectColor = theme.Rule;
            theme.StyleButton(startButton);
            foreach (var button in new[] { helpButton, quitButton, previousButton, nextButton, backButton })
                theme.StyleButton(button);
        }

        private static void StyleText(TMP_Text[] labels, TMP_FontAsset font, Color color)
        {
            if (labels == null) return;
            foreach (var label in labels)
            {
                if (!label) continue;
                label.font = font;
                label.color = color;
            }
        }

        private static void StyleGraphics(Graphic[] graphics, Color color)
        {
            if (graphics == null) return;
            foreach (var graphic in graphics) if (graphic) graphic.color = color;
        }

        private bool AcceptNavigation()
        {
            if (!isActiveAndEnabled || transitioning || lastNavigationFrame == Time.frameCount) return false;
            lastNavigationFrame = Time.frameCount;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            return true;
        }

        public void OpenTutorial()
        {
            if (!AcceptNavigation() || IsTutorialOpen) return;
            BeginTransition(ShowGuide());
        }

        private IEnumerator ShowGuide()
        {
            if (motion) yield return motion.FadeOut(true);
            currentPage = 0;
            if (calloutLayer) calloutLayer.ResetPresentation(false);
            if (motion) motion.PrepareGuide();
            RefreshPage();
            titlePanel.SetActive(false);
            tutorialPanel.SetActive(true);
            if (motion) yield return motion.GuideEntry();
            if (calloutLayer) calloutLayer.BeginPage();
        }

        public void ReturnToTitle()
        {
            if (!AcceptNavigation() || !IsTutorialOpen) return;
            BeginTransition(ShowTitle());
        }

        private IEnumerator ShowTitle()
        {
            if (calloutLayer) calloutLayer.PausePresentation();
            if (motion) yield return motion.FadeOut(false);
            if (calloutLayer) calloutLayer.ResetPresentation(false);
            if (motion) motion.PrepareTitle();
            tutorialPanel.SetActive(false);
            titlePanel.SetActive(true);
            BeginTitleEntry();
            if (motion) yield return motion.RevealTitle();
        }

        public void PreviousPage()
        {
            if (!IsTutorialOpen || currentPage <= 0 || !AcceptNavigation()) return;
            BeginTransition(ChangePage(-1));
        }

        public void NextPage()
        {
            if (!IsTutorialOpen || pages == null || currentPage >= pages.Length - 1 || !AcceptNavigation()) return;
            BeginTransition(ChangePage(1));
        }

        private IEnumerator ChangePage(int direction)
        {
            if (calloutLayer) calloutLayer.PausePresentation();
            if (motion) yield return motion.PageOut(direction);
            if (calloutLayer) calloutLayer.ResetPresentation(false);
            currentPage += direction;
            if (motion) motion.PreparePage(direction);
            RefreshPage();
            if (motion) yield return motion.PageIn(direction);
            if (calloutLayer) calloutLayer.BeginPage();
        }

        private void RefreshPage()
        {
            int count = pages == null ? 0 : pages.Length;
            var page = count > 0 ? pages[Mathf.Clamp(currentPage, 0, count - 1)] : null;
            pageTitle.text = page != null ? page.Title : "게임 방법";
            description.text = page != null ? page.Description : string.Empty;
            pageNumber.text = count > 0 ? $"{currentPage + 1:00}  /  {count:00}" : "00  /  00";
            screenshot.sprite = page != null ? page.Screenshot : null;
            screenshot.preserveAspect = true;
            screenshot.gameObject.SetActive(screenshot.sprite != null);
            screenshotPlaceholder.SetActive(screenshot.sprite == null);
            if (calloutLayer) calloutLayer.Refresh(page?.Callouts, theme);
            previousButton.interactable = currentPage > 0;
            nextButton.interactable = currentPage < count - 1;
        }

        public void StartGame()
        {
            if (!isActiveAndEnabled || transitioning || IsTutorialOpen) return;
            if (!Application.CanStreamedLevelBeLoaded(gameScenePath))
            {
                status.text = "게임 씬을 불러올 수 없습니다. 빌드 씬 목록을 확인해 주세요.";
                return;
            }
            status.text = "불러오는 중…";
            BeginTransition(LeaveMenu(false));
        }

        public void Quit()
        {
            if (!isActiveAndEnabled || transitioning || IsTutorialOpen) return;
            BeginTransition(LeaveMenu(true));
        }

        private IEnumerator LeaveMenu(bool quit)
        {
            if (motion) yield return motion.FadeOut(true);
            if (!quit)
            {
                var load = SceneManager.LoadSceneAsync(gameScenePath, LoadSceneMode.Single);
                if (load != null) yield return load;
                yield break;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            startButton.onClick.RemoveListener(StartGame);
            helpButton.onClick.RemoveListener(OpenTutorial);
            quitButton.onClick.RemoveListener(Quit);
            previousButton.onClick.RemoveListener(PreviousPage);
            nextButton.onClick.RemoveListener(NextPage);
            backButton.onClick.RemoveListener(ReturnToTitle);
        }
    }
}
