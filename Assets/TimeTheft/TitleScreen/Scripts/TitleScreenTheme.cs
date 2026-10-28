using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeTheft.TitleScreen
{
    [CreateAssetMenu(menuName = "TimeTheft/Title Screen Theme")]
    public sealed class TitleScreenTheme : ScriptableObject
    {
        public TMP_FontAsset HeadingFont;
        public TMP_FontAsset BodyFont;
        public Color Background = new Color32(12, 17, 19, 255);
        public Color Panel = new Color32(17, 25, 27, 255);
        public Color Text = new Color32(232, 228, 214, 255);
        public Color MutedText = new Color32(153, 164, 160, 255);
        public Color Accent = new Color32(192, 158, 101, 255);
        public Color Rule = new Color32(66, 70, 65, 255);
        public Color Button = new Color32(23, 33, 34, 255);
        public Color Hover = new Color32(49, 65, 64, 255);
        public Color Pressed = new Color32(76, 93, 87, 255);
        public Color Disabled = new Color32(22, 27, 28, 100);

        public void StyleButton(Button button)
        {
            var colors = button.colors;
            colors.normalColor = Button;
            colors.highlightedColor = Hover;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = Pressed;
            colors.disabledColor = Disabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .15f;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = colors;
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                label.font = BodyFont;
                label.color = Text;
            }
            if (button is TitleMenuButton decorated) decorated.SetTheme(this);
        }
    }
}
