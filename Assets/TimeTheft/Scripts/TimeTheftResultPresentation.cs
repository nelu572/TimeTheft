using System;
using System.Collections;
using TMPro;
using TimeTheft.TitleScreen;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TimeTheft
{
    // 규칙 상태는 읽기만 한다. 공유 Volume/재질을 수정하지 않는 씬 전용 연출이다.
    public sealed class TimeTheftResultPresentation : MonoBehaviour
    {
        [SerializeField] private Volume sourceVolume;
        [SerializeField] private Light[] sceneLights = Array.Empty<Light>();
        [SerializeField] private TitleScreenTheme theme;
        private TimeTheftGameController controller;
        private TimeTheftGameView view;
        private Volume pressureVolume;
        private VolumeProfile profile;
        private ChromaticAberration chromatic;
        private Vignette vignette;
        private ColorAdjustments grading;
        private LensDistortion distortion;
        private float baseChromatic, baseVignette, baseSaturation;
        private Color baseFilter;
        private float[] lightIntensity;
        private Color[] lightColor;
        private Renderer[] counters;
        private MaterialPropertyBlock[] originalBlocks;
        private MaterialPropertyBlock glowBlock;
        private float[] baseGlow;
        private GameObject overlay;
        private CanvasGroup content;
        private UnityEngine.UI.Image shade;
        private UnityEngine.UI.Button mainMenuButton, skipButton;
        private TextMeshProUGUI heading, balance, caption;
        private TMP_FontAsset resultFont;
        private int initialLife, shownFrame, armedFrame;
        private float damageStartsAt, damageStrength, shownAt;
        private bool initialized, skip, endingComplete, ownsCamera, damageGlowMuted;
        private Action returnToMainMenu;
        private static readonly int GlowId = Shader.PropertyToID("_GlowIntensity");

        public bool IsEnding { get; private set; }
        public bool IsVisible { get; private set; }
        public bool CanLeaveResult => IsVisible && mainMenuButton && mainMenuButton.interactable && Time.frameCount > armedFrame;
        public float Pressure { get; private set; }
        public float DamageReaction { get; private set; }
        public Volume PressureVolume => pressureVolume;
        public UnityEngine.UI.Button MainMenuButton => mainMenuButton;
        public TMP_Text Heading => heading;

        public void Initialize(TimeTheftGameController owner, TimeTheftGameView gameView, Action mainMenu)
        {
            if (initialized) return;
            if (!sourceVolume || !sourceVolume.sharedProfile || !theme)
                throw new InvalidOperationException("결과 연출의 기존 Volume과 타이틀 테마를 연결하세요.");
            controller = owner; view = gameView; returnToMainMenu = mainMenu;
            var baseline = sourceVolume.sharedProfile;
            baseChromatic = baseline.TryGet<ChromaticAberration>(out var ca) ? ca.intensity.value : 0;
            baseVignette = baseline.TryGet<Vignette>(out var vi) ? vi.intensity.value : 0;
            baseSaturation = baseline.TryGet<ColorAdjustments>(out var co) ? co.saturation.value : 0;
            baseFilter = co ? co.colorFilter.value : Color.white;
            var volumeObject = new GameObject("Life Pressure Volume");
            volumeObject.transform.SetParent(transform, false);
            volumeObject.layer = sourceVolume.gameObject.layer;
            pressureVolume = volumeObject.AddComponent<Volume>();
            pressureVolume.isGlobal = true;
            pressureVolume.priority = sourceVolume.priority + 1;
            pressureVolume.weight = 0;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Life Pressure Runtime";
            profile.hideFlags = HideFlags.DontSave;
            pressureVolume.sharedProfile = profile;
            chromatic = profile.Add<ChromaticAberration>();
            chromatic.intensity.overrideState = true;
            vignette = profile.Add<Vignette>();
            vignette.intensity.overrideState = true;
            grading = profile.Add<ColorAdjustments>();
            grading.saturation.overrideState = grading.colorFilter.overrideState = true;
            distortion = profile.Add<LensDistortion>();
            distortion.intensity.overrideState = false;
            lightIntensity = new float[sceneLights.Length];
            lightColor = new Color[sceneLights.Length];
            for (int i = 0; i < sceneLights.Length; i++)
                if (sceneLights[i]) { lightIntensity[i] = sceneLights[i].intensity; lightColor[i] = sceneLights[i].color; }
            counters = new[] { view.PlayerLife.GetComponent<Renderer>(), view.AiLife.GetComponent<Renderer>(), view.Bank.GetComponent<Renderer>() };
            originalBlocks = new MaterialPropertyBlock[counters.Length];
            baseGlow = new float[counters.Length];
            glowBlock = new MaterialPropertyBlock();
            for (int i = 0; i < counters.Length; i++)
            {
                originalBlocks[i] = new MaterialPropertyBlock();
                counters[i].GetPropertyBlock(originalBlocks[i]);
                var material = counters[i].sharedMaterial;
                baseGlow[i] = material && material.HasProperty(GlowId) ? material.GetFloat(GlowId) : 0;
            }
            CreateUi();
            initialized = true;
            ResetPresentation();
        }

        public void ResetForMatch(TimeTheftMatch match)
        {
            ResetPresentation();
            initialLife = Mathf.Max(1, match.PlayerLifeMinutes);
        }

        // 각 경계에서 연속이며 회복 시 같은 경로를 되짚는다.
        public static float EvaluatePressure(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            if (ratio >= .6f) return 0;
            if (ratio >= .3f) return Mathf.Lerp(0, .35f, Mathf.SmoothStep(0, 1, (.6f - ratio) / .3f));
            if (ratio >= .15f) return Mathf.Lerp(.35f, .65f, Mathf.SmoothStep(0, 1, (.3f - ratio) / .15f));
            return Mathf.Lerp(.65f, 1, Mathf.SmoothStep(0, 1, (.15f - ratio) / .15f));
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            if (IsVisible)
            {
                content.alpha = Mathf.MoveTowards(content.alpha, 1, Time.unscaledDeltaTime / .22f);
                // 스킵의 press/release와 UI Submit이 메인 이동에 재사용되지 않게 모두 놓은 뒤 무장한다.
                if (!mainMenuButton.interactable && Time.frameCount > shownFrame + 1 &&
                    Time.unscaledTime - shownAt >= .15f && !InputHeld())
                { mainMenuButton.interactable = true; armedFrame = Time.frameCount; }
                return;
            }
            if (IsEnding || endingComplete || controller.Match == null || !controller.isActiveAndEnabled) return;
            int life = controller.Match.PlayerLifeMinutes;
            float ratio = life / (float)initialLife;
            float target = EvaluatePressure(ratio);
            Pressure = Mathf.Lerp(Pressure, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime / .35f));
            if (Mathf.Abs(Pressure - target) < .0001f) Pressure = target;
            UpdateDamageReaction();
            ApplyEffects(Pressure, 0, DamageReaction);
        }

        private static bool InputHeld()
        {
            var keys = Keyboard.current;
            return (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
                (keys != null && (keys.spaceKey.isPressed || keys.rKey.isPressed ||
                    keys.enterKey.isPressed || keys.numpadEnterKey.isPressed));
        }

        // 표시된 수명 감소가 끝난 뒤 한 번만 반응한다. 작은 손실도 인지 가능한 강도를 보장한다.
        public void ReactToLifeLoss(int lostMinutes)
        {
            if (!initialized || !isActiveAndEnabled || IsEnding || endingComplete || IsVisible || lostMinutes <= 0) return;
            damageStrength = Mathf.Lerp(.65f, 1, Mathf.Clamp01(lostMinutes / (initialLife * .25f)));
            damageStartsAt = Time.unscaledTime + .08f;
            DamageReaction = 0;
        }

        private void UpdateDamageReaction()
        {
            if (damageStrength <= 0) return;
            float elapsed = Time.unscaledTime - damageStartsAt;
            if (elapsed < 0) DamageReaction = 0;
            else if (elapsed < .055f) DamageReaction = damageStrength * Mathf.SmoothStep(0, 1, elapsed / .055f);
            else if (elapsed < .10f) DamageReaction = damageStrength;
            else if (elapsed < .50f) DamageReaction = damageStrength * (1 - Mathf.SmoothStep(0, 1, (elapsed - .10f) / .40f));
            else CancelDamageReaction();
            // 발광 패스는 후처리 전, 선명한 숫자는 후처리 후 그려진다. 반응 중 잔상만 억제한다.
            if (DamageReaction > 0) { SetGlowMultiplier(0); damageGlowMuted = true; }
        }

        private void CancelDamageReaction()
        {
            damageStrength = damageStartsAt = DamageReaction = 0;
            if (damageGlowMuted) RestoreGlow();
            damageGlowMuted = false;
        }

        private void ApplyEffects(float strength, float collapse = 0, float hit = 0)
        {
            pressureVolume.weight = strength > 0 || collapse > 0 || hit > 0 ? 1 : 0;
            chromatic.intensity.value = Mathf.Min(.38f, baseChromatic + .14f * strength + .14f * collapse + .20f * hit);
            vignette.intensity.value = Mathf.Min(.55f, baseVignette + .16f * strength + .16f * collapse + .08f * hit);
            grading.saturation.value = baseSaturation - 12 * strength - 8 * collapse - 18 * hit;
            grading.colorFilter.value = Color.Lerp(baseFilter, baseFilter * new Color(.9f, .95f, 1, 1), Mathf.Clamp01(strength + .45f * hit));
            distortion.intensity.overrideState = collapse > 0 || hit > 0;
            distortion.intensity.value = -.025f * collapse - .015f * hit;
            distortion.scale.overrideState = hit > 0;
            distortion.scale.value = 1 + .012f * hit;
        }

        public IEnumerator PlayEnding(GameWinner winner)
        {
            if (!initialized || IsEnding || endingComplete || IsVisible || winner == GameWinner.None) yield break;
            CancelDamageReaction();
            IsEnding = true; skip = false;
            view.PrepareResultPresentation();
            view.FocusCamera.BeginPresentationCamera(); ownsCamera = true;
            overlay.SetActive(true);
            skipButton.gameObject.SetActive(true);
            bool won = winner == GameWinner.Player;
            float startPressure = Pressure, elapsed = 0;
            while (elapsed < 2.15f && !skip && IsEnding && isActiveAndEnabled)
            {
                // 마지막 숫자를 잠깐 유지한 뒤 해방/붕괴, 암전 순서로 진행한다.
                float transition = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, 1.15f, elapsed));
                float pulse = !won && elapsed >= .35f && elapsed <= .67f
                    ? Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.35f, .67f, elapsed)) : 0;
                ApplyEffects(won ? Mathf.Lerp(startPressure, 0, transition) : Mathf.Lerp(startPressure, 1, transition), pulse);
                SetLights(won ? 1 : Mathf.Lerp(1, .08f, transition), won ? transition * .14f : 0);
                SetGlow(won ? Mathf.Sin(Mathf.PI * transition) : 0);
                SetShade(Mathf.SmoothStep(0, won ? .82f : 1, Mathf.InverseLerp(1.05f, 1.95f, elapsed)));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!IsEnding || !isActiveAndEnabled) yield break;
            IsEnding = false; endingComplete = true; skip = false;
            ApplyEffects(0);
            RestoreGlow();
            SetLights(won ? .65f : .08f, won ? .14f : 0);
            SetShade(.82f);
            skipButton.gameObject.SetActive(false);
        }

        public void SkipEnding() { if (IsEnding) skip = true; }

        public void ShowResult(TimeTheftMatch match)
        {
            if (!initialized || IsVisible) return;
            CancelDamageReaction();
            IsVisible = true;
            bool won = match.Winner == GameWinner.Player;
            heading.text = won ? "승리" : "패배";
            heading.color = won ? theme.Accent : new Color32(189, 112, 100, 255);
            caption.text = won ? "당신의 시간은 계속됩니다" : "당신의 시간이 다했습니다";
            balance.text = "남은 수명  " + GameTime.Format(match.PlayerLifeMinutes) +
                "\nTIME BANK  " + GameTime.Format(match.BankMinutes);
            ApplyEffects(0);
            overlay.SetActive(true);
            SetShade(.82f);
            content.gameObject.SetActive(true); content.alpha = 0;
            mainMenuButton.interactable = false;
            shownFrame = armedFrame = Time.frameCount; shownAt = Time.unscaledTime;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        private void SetLights(float factor, float warmth)
        {
            for (int i = 0; i < sceneLights.Length; i++)
                if (sceneLights[i])
                {
                    sceneLights[i].intensity = lightIntensity[i] * factor;
                    sceneLights[i].color = Color.Lerp(lightColor[i], new Color(1, .82f, .62f), warmth);
                }
        }

        private void SetGlow(float amount) => SetGlowMultiplier(1 + .45f * amount);

        private void SetGlowMultiplier(float multiplier)
        {
            for (int i = 0; i < counters.Length; i++)
            {
                if (!counters[i] || baseGlow[i] == 0) continue;
                counters[i].GetPropertyBlock(glowBlock);
                glowBlock.SetFloat(GlowId, baseGlow[i] * multiplier);
                counters[i].SetPropertyBlock(glowBlock);
            }
        }

        private void RestoreGlow()
        {
            if (counters == null) return;
            for (int i = 0; i < counters.Length; i++)
                if (counters[i]) counters[i].SetPropertyBlock(originalBlocks[i]);
        }

        private void SetShade(float alpha) => shade.color = new Color(0.015f, .02f, .022f, alpha);

        public void ResetPresentation()
        {
            if (!initialized) return;
            IsEnding = IsVisible = endingComplete = skip = false;
            Pressure = 0;
            CancelDamageReaction();
            ApplyEffects(0); SetLights(1, 0); RestoreGlow();
            content.gameObject.SetActive(false); content.alpha = 0;
            mainMenuButton.interactable = false; skipButton.gameObject.SetActive(false);
            overlay.SetActive(false);
            if (ownsCamera && view && view.FocusCamera) view.FocusCamera.EndPresentationCamera();
            ownsCamera = false;
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(overlay.transform))
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void CreateUi()
        {
            // 타이틀의 픽셀 폰트도 런타임 사본을 써서 동적 글리프가 원본 에셋을 갱신하지 않는다.
            resultFont = Instantiate(theme.BodyFont);
            resultFont.hideFlags = HideFlags.DontSave;
            resultFont.material = new Material(theme.BodyFont.material);
            var atlases = new Texture2D[theme.BodyFont.atlasTextures.Length];
            for (int i = 0; i < atlases.Length; i++) atlases[i] = Instantiate(theme.BodyFont.atlasTextures[i]);
            resultFont.atlasTextures = atlases;
            resultFont.material.mainTexture = atlases[0];
            resultFont.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { view.PhaseLabel.font };
            resultFont.ReadFontAssetDefinition();
            overlay = new GameObject("Result UI", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            overlay.transform.SetParent(transform, false);
            var canvas = overlay.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
            var scaler = overlay.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            shade = new GameObject("World Shade", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
            shade.transform.SetParent(overlay.transform, false);
            shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one;
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
            shade.raycastTarget = true; SetShade(0);
            var panel = Rect(overlay.transform, "Result Content", Vector2.zero, new Vector2(900, 630));
            content = panel.gameObject.AddComponent<CanvasGroup>();
            Text(panel, "Eyebrow", "TIME THEFT", 24, theme.MutedText, new Vector2(0, 250), new Vector2(800, 40));
            Rule(panel, 195); Rule(panel, -112);
            heading = Text(panel, "Outcome", "", 116, theme.Accent, new Vector2(0, 100), new Vector2(860, 150));
            caption = Text(panel, "Caption", "", 30, theme.Text, new Vector2(0, -5), new Vector2(860, 50));
            balance = Text(panel, "Balance", "", 27, theme.MutedText, new Vector2(0, -70), new Vector2(860, 80));
            mainMenuButton = Button(panel, "Main Menu", "메인으로", new Vector2(0, -192), new Vector2(320, 66), () =>
            { if (CanLeaveResult) { view.FocusCamera.SuppressPointerForCurrentFrame(); returnToMainMenu?.Invoke(); } });
            skipButton = Button(overlay.transform, "Skip Ending", "연출 단축  [SPACE]", new Vector2(-220, 54), new Vector2(350, 54), () => controller.SkipAnimation());
            var skipRect = (RectTransform)skipButton.transform;
            skipRect.anchorMin = skipRect.anchorMax = new Vector2(1, 0);
            content.gameObject.SetActive(false);
            overlay.SetActive(false);
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }

        private TextMeshProUGUI Text(Transform parent, string name, string value, float size, Color color, Vector2 position, Vector2 extent)
        {
            var label = Rect(parent, name, position, extent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = resultFont; label.fontSize = size; label.text = value; label.color = color;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            return label;
        }

        private void Rule(Transform parent, float y)
        {
            var line = Rect(parent, "Rule", new Vector2(0, y), new Vector2(640, 1)).gameObject.AddComponent<UnityEngine.UI.Image>();
            line.color = theme.Rule; line.raycastTarget = false;
        }

        private UnityEngine.UI.Button Button(Transform parent, string name, string title, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction click)
        {
            var rect = Rect(parent, name, position, size);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            var button = rect.gameObject.AddComponent<TitleMenuButton>();
            button.targetGraphic = image;
            Text(rect, "Label", title, 28, theme.Text, Vector2.zero, size);
            theme.StyleButton(button);
            button.GetComponentInChildren<TMP_Text>().font = resultFont;
            button.onClick.AddListener(click);
            var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
            return button;
        }

        private void OnDisable() => ResetPresentation();
        private void OnDestroy()
        {
            ResetPresentation();
            if (profile)
            {
                foreach (var component in profile.components) if (component) Destroy(component);
                Destroy(profile);
            }
            if (resultFont)
            {
                foreach (var atlas in resultFont.atlasTextures) if (atlas) Destroy(atlas);
                Destroy(resultFont.material); Destroy(resultFont);
            }
        }
    }
}
