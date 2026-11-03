using System.Collections;
using UnityEngine;

namespace TimeTheft.TitleScreen
{
    // 이 메뉴의 기존 배치만 보간하며 움직이는 중의 값을 원위치로 다시 저장하지 않는다.
    public sealed class TitleScreenMotion : MonoBehaviour
    {
        [SerializeField] private CanvasGroup titleScreen, guideScreen;
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform[] logo, titleButtons, guideHeader, guideNavigation;
        [SerializeField] private RectTransform guideScreenshot, guideDescription;

        private sealed class Element
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector2 Rest;
            public void Pose(float progress, Vector2 offset, bool input = false)
            {
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
                Rect.anchoredPosition = Rest + offset * (1f - eased);
                Group.alpha = eased;
                Group.interactable = input && progress >= .8f;
                Group.blocksRaycasts = Group.interactable;
            }
            public void Restore()
            {
                Rect.anchoredPosition = Rest;
                Group.alpha = 1f;
                Group.interactable = Group.blocksRaycasts = true;
            }
        }

        private Element photo, image, caption;
        private Element accent;
        private Element[] logos, buttons, headers, navigation;
        private Vector2 accentSize;
        private Vector3 backgroundScale;
        private TitleWatchLight watchLight;
        private float breathingTime, titleIdleTime;
        private bool initialized, titleIdle;

        public void Initialize()
        {
            if (initialized) return;
            photo = Cache(background); image = Cache(guideScreenshot); caption = Cache(guideDescription);
            logos = CacheAll(logo); buttons = CacheAll(titleButtons);
            headers = CacheAll(guideHeader); navigation = CacheAll(guideNavigation);
            foreach (var e in logos)
                if (e.Rect.name == "Accent") { accent = e; accentSize = e.Rect.sizeDelta; break; }
            backgroundScale = background.localScale;
            watchLight = background.GetComponent<TitleWatchLight>();
            initialized = true;
        }

        private static Element Cache(RectTransform rect)
        {
            var group = rect.GetComponent<CanvasGroup>();
            if (!group) group = rect.gameObject.AddComponent<CanvasGroup>();
            return new Element { Rect = rect, Group = group, Rest = rect.anchoredPosition };
        }

        private static Element[] CacheAll(RectTransform[] rects)
        {
            var result = new Element[rects.Length];
            for (int i = 0; i < rects.Length; i++) result[i] = Cache(rects[i]);
            return result;
        }

        private void Update()
        {
            if (!initialized || !titleScreen.gameObject.activeInHierarchy) return;
            breathingTime += Time.unscaledDeltaTime;
            float zoom = 1f + .0306f * (.5f - .5f * Mathf.Cos(breathingTime * Mathf.PI * 2f / 8f));
            // 원래 크기보다 작아지지 않아 가장자리가 비지 않는다.
            background.localScale = backgroundScale * zoom;
            if (watchLight) watchLight.SetTime(breathingTime);
            if (!titleIdle) return;
            titleIdleTime += Time.unscaledDeltaTime;
            float rise = 3f * (.5f - .5f * Mathf.Cos(titleIdleTime * Mathf.PI * 2f / 5f));
            foreach (var e in logos)
                if (e != accent) e.Rect.anchoredPosition = e.Rest + Vector2.up * rise;
            if (accent != null)
            {
                float pulse = .5f - .5f * Mathf.Cos(titleIdleTime * Mathf.PI * 2f / 3.5f);
                accent.Rect.sizeDelta = new Vector2(accentSize.x * (1f + .6f * pulse), accentSize.y);
                accent.Group.alpha = 1f - .08f * pulse;
            }
        }

        public void SetInput(bool enabled) => titleScreen.blocksRaycasts = guideScreen.blocksRaycasts = enabled;

        private void ResetTitleIdle()
        {
            titleIdle = false;
            titleIdleTime = 0f;
            if (accent == null) return;
            accent.Rect.sizeDelta = accentSize;
            accent.Group.alpha = 1f;
        }

        public void ResetAll()
        {
            if (!initialized) return;
            photo.Restore(); image.Restore(); caption.Restore();
            foreach (var e in logos) e.Restore();
            foreach (var e in buttons) e.Restore();
            foreach (var e in headers) e.Restore();
            foreach (var e in navigation) e.Restore();
            titleScreen.alpha = guideScreen.alpha = 1f;
            titleScreen.interactable = guideScreen.interactable = true;
            SetInput(true);
            background.localScale = backgroundScale;
            breathingTime = 0f;
            if (watchLight) watchLight.SetTime(0f);
            ResetTitleIdle();
        }

        public void PrepareTitle()
        {
            Initialize(); breathingTime = 0f; background.localScale = backgroundScale;
            if (watchLight) watchLight.SetTime(0f);
            ResetTitleIdle();
            photo.Pose(0f, Vector2.zero);
            foreach (var e in logos) e.Pose(0f, Vector2.up * 24f);
            foreach (var e in buttons) e.Pose(0f, Vector2.down * 22f, true);
        }

        public IEnumerator TitleEntry()
        {
            PrepareTitle(); float time = 0f;
            while (time < 1.18f)
            {
                photo.Pose(time / .65f, Vector2.zero);
                foreach (var e in logos) e.Pose((time - .18f) / .5f, Vector2.up * 24f);
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i].Pose((time - .68f - i * .1f) / .3f, Vector2.down * 22f, true);
                yield return null; time += Time.unscaledDeltaTime;
            }
            photo.Restore();
            foreach (var e in logos) e.Restore();
            foreach (var e in buttons) e.Restore();
            // 진입 보간이 원위치를 복구한 다음에만 상시 모션을 시작한다.
            titleIdleTime = 0f;
            titleIdle = true;
        }

        public IEnumerator FadeOut(bool title)
        {
            var group = title ? titleScreen : guideScreen;
            float from = group.alpha, time = 0f;
            while (time < .16f)
            {
                group.alpha = from * (1f - Mathf.SmoothStep(0f, 1f, time / .16f));
                yield return null; time += Time.unscaledDeltaTime;
            }
            group.alpha = 0f;
        }

        public void PrepareGuide()
        {
            guideScreen.alpha = 0f;
            foreach (var e in headers) e.Pose(0f, Vector2.up * 14f);
            image.Pose(0f, Vector2.down * 16f); caption.Pose(0f, Vector2.down * 12f);
            foreach (var e in navigation) e.Pose(0f, Vector2.down * 10f, true);
        }

        public IEnumerator GuideEntry()
        {
            float time = 0f;
            while (time < .42f)
            {
                guideScreen.alpha = Mathf.SmoothStep(0f, 1f, time / .2f);
                foreach (var e in headers) e.Pose(time / .28f, Vector2.up * 14f);
                image.Pose(time / .32f, Vector2.down * 16f);
                caption.Pose((time - .08f) / .28f, Vector2.down * 12f);
                foreach (var e in navigation) e.Pose((time - .14f) / .28f, Vector2.down * 10f, true);
                yield return null; time += Time.unscaledDeltaTime;
            }
            guideScreen.alpha = 1f; RestoreGuide();
        }

        public IEnumerator RevealTitle()
        {
            titleScreen.alpha = 0f; float time = 0f;
            while (time < .2f)
            {
                titleScreen.alpha = Mathf.SmoothStep(0f, 1f, time / .2f);
                yield return null; time += Time.unscaledDeltaTime;
            }
            titleScreen.alpha = 1f;
        }

        public IEnumerator PageOut(int direction)
        {
            float time = 0f;
            while (time < .13f)
            {
                PagePose(1f - time / .13f, Vector2.left * direction * 28f);
                yield return null; time += Time.unscaledDeltaTime;
            }
            PagePose(0f, Vector2.left * direction * 28f);
        }

        public void PreparePage(int direction) => PagePose(0f, Vector2.right * direction * 28f);

        public IEnumerator PageIn(int direction)
        {
            float time = 0f;
            while (time < .2f)
            {
                PagePose(time / .2f, Vector2.right * direction * 28f);
                yield return null; time += Time.unscaledDeltaTime;
            }
            RestoreGuide();
        }

        private void PagePose(float progress, Vector2 offset)
        {
            foreach (var e in headers) e.Pose(progress, offset);
            image.Pose(progress, offset); caption.Pose(progress, offset);
        }

        private void RestoreGuide()
        {
            foreach (var e in headers) e.Restore();
            image.Restore(); caption.Restore();
            foreach (var e in navigation) e.Restore();
        }
    }
}
