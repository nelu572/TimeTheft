using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TimeTheft
{
    public sealed class ClockView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public RectTransform Rect { get; private set; }
        public CanvasGroup Group => group;
        public UnityEngine.UI.Button Button => button;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private UnityEngine.UI.Button button;
        [SerializeField] private RectTransform artwork;
        [SerializeField] private TableShape rim, halo, face;
        [SerializeField] private TextMeshProUGUI number, caption, owner;
        [SerializeField] private RectTransform hand;
        private Vector3 restScale, artworkScale;
        private Quaternion restRotation;
        private int index;
        private System.Action<int> select;
        private bool hovered;
        private bool selectable;
        private Color tone;

        public void Initialize(int index, System.Action<int> select)
        {
            Rect = (RectTransform)transform;
            this.index = index;
            this.select = select;
            restScale = Rect.localScale;
            restRotation = Rect.localRotation;
            artworkScale = artwork.localScale;
            Button.onClick.RemoveListener(Select);
            Button.onClick.AddListener(Select);
            SetSelectable(false);
        }

        private void Select() => select?.Invoke(index);
        public void SetScale(float scale) => Rect.localScale = restScale * scale;
        public void SetRotation(float angle) => Rect.localRotation = restRotation * Quaternion.Euler(0, 0, angle);

        private void OnDestroy()
        {
            if (button) button.onClick.RemoveListener(Select);
        }

        public void Show(int value, bool revealed, string label, Color color)
        {
            tone = color;
            rim.color = color;
            face.color = revealed ? new Color(.12f, .17f, .17f) : TableView.Ink;
            number.text = revealed ? value.ToString() : "?";
            number.color = revealed ? TableView.Cream : TableView.Muted;
            caption.text = revealed ? "시간 · h" : "비공개";
            owner.text = label;
            owner.color = color;
            hand.gameObject.SetActive(revealed);
            hand.localRotation = Quaternion.Euler(0, 0, -value * 30);
        }

        public void SetSelectable(bool enabled)
        {
            selectable = enabled;
            Button.interactable = enabled;
            Group.blocksRaycasts = enabled;
            hovered = false;
            halo.gameObject.SetActive(enabled);
        }

        public void OnPointerEnter(PointerEventData eventData) { if (selectable) hovered = true; }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; }

        private void Update()
        {
            if (!artwork) return;
            bool focused = selectable && (hovered || (EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject));
            artwork.localScale = Vector3.Lerp(artwork.localScale, artworkScale * (focused ? 1.08f : 1f), 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime));
            if (selectable)
            {
                halo.color = new Color(tone.r, tone.g, tone.b, focused ? 1 : .4f + .18f * Mathf.Sin(Time.unscaledTime * 4));
                owner.text = focused ? "선택하기" : "선택 가능";
            }
        }
    }
}
