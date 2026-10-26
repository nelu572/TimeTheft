using System;
using TMPro;
using UnityEngine;

namespace TimeTheft
{
    // 규칙 상태를 소유하지 않는 임시 표시부. 최종 모델은 Visual 아래만 교체한다.
    [DisallowMultipleComponent]
    public sealed class ItemWorldView : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private Renderer body;
        [SerializeField] private TextMeshPro label;
        [SerializeField] private TabletopFocusTarget target;
        [SerializeField, Min(0)] private float selectedLift = .07f;
        [SerializeField, Min(.05f)] private float reactionDuration = .14f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock properties;
        private Vector3 visualPosition, visualScale;
        private Color baseColor;
        private Action<int> select;
        private bool initialized, cutRemoval;
        private float age;

        public Participant Owner { get; private set; }
        public int? ItemId { get; private set; }
        public ItemKind? Kind { get; private set; }
        public TextMeshPro Label => label;
        public TabletopFocusTarget Target => target;
        public bool IsSelected { get; private set; }
        public bool IsRemoving { get; private set; }

        public void Initialize(Participant owner, int? itemId, ItemKind? kind, Action<int> selectItem)
        {
            if (!visual || !body || !label || !target)
                throw new InvalidOperationException("아이템의 Visual·Body·Label·Target을 연결하세요.");
            if (!initialized)
            {
                visualPosition = visual.localPosition;
                visualScale = visual.localScale;
                properties = new MaterialPropertyBlock();
                target.Clicked += Select;
                initialized = true;
            }
            Owner = owner;
            // 상대는 공개 종류/수량의 익명 표시이며 실제 ID를 받지 않는다.
            ItemId = owner == Participant.Player ? itemId : null;
            Kind = kind;
            select = selectItem;
            label.text = DisplayName(kind);
            label.raycastTarget = false;
            baseColor = ItemColor(kind);
            age = 0;
            IsRemoving = false;
            IsSelected = false;
            target.SetInputEnabled(false);
            ApplyVisual();
        }

        public void SetState(bool selected, bool inputEnabled)
        {
            IsSelected = selected && Owner == Participant.Player && !IsRemoving;
            target.SetInputEnabled(inputEnabled && !IsRemoving);
            ApplyVisual();
        }

        public void BeginRemoval(bool cut)
        {
            if (IsRemoving) return;
            IsRemoving = true;
            IsSelected = false;
            cutRemoval = cut;
            age = 0;
            target.SetInputEnabled(false);
            // 물리 입력은 즉시 차단하고 Visual만 짧게 반응시킨다.
            var collider = GetComponent<Collider>();
            if (collider) collider.enabled = false;
            ApplyVisual();
        }

        private void Select()
        {
            if (isActiveAndEnabled && !IsRemoving && Owner == Participant.Player && ItemId.HasValue)
                select?.Invoke(ItemId.Value);
        }

        private void Update()
        {
            if (!initialized) return;
            age += Time.unscaledDeltaTime;
            ApplyVisual();
            if (IsRemoving && age >= reactionDuration) Destroy(gameObject);
        }

        private void ApplyVisual()
        {
            if (!initialized) return;
            float t = Mathf.Clamp01(age / Mathf.Max(.05f, reactionDuration));
            float size = IsRemoving ? 1 - Mathf.SmoothStep(0, 1, t) * .9f : Mathf.Lerp(.9f, 1, Mathf.SmoothStep(0, 1, t));
            visual.localScale = visualScale * size;
            visual.localPosition = visualPosition + Vector3.up * (IsSelected ? selectedLift : IsRemoving ? t * .05f : 0);
            Color color = IsRemoving ? (cutRemoval ? new Color(.85f, .22f, .14f) : new Color(.94f, .79f, .4f))
                : IsSelected ? Color.Lerp(baseColor, new Color(1, .84f, .38f), .65f)
                : target.IsHighlighted ? Color.Lerp(baseColor, Color.white, .3f) : baseColor;
            properties.SetColor(BaseColor, color);
            properties.SetColor(ColorProperty, color);
            body.SetPropertyBlock(properties);
            label.color = IsSelected ? new Color(1, .91f, .61f) : Color.white;
        }

        private void OnDisable()
        {
            if (target) target.SetInputEnabled(false);
        }

        private void OnDestroy()
        {
            if (initialized && target) target.Clicked -= Select;
        }

        public static string DisplayName(ItemKind? kind)
        {
            if (!kind.HasValue) return "아이템";
            switch (kind.Value)
            {
                case ItemKind.SpareKey: return "여벌 열쇠";
                case ItemKind.Screwdriver: return "드라이버";
                case ItemKind.TimeBypass: return "우회선";
                case ItemKind.Scissors: return "가위";
                case ItemKind.RubberDuck: return "고무 오리";
                default: return "수갑";
            }
        }

        private static Color ItemColor(ItemKind? kind)
        {
            if (!kind.HasValue) return new Color(.24f, .28f, .3f);
            switch (kind.Value)
            {
                case ItemKind.SpareKey: return new Color(.5f, .37f, .16f);
                case ItemKind.Screwdriver: return new Color(.24f, .37f, .46f);
                case ItemKind.TimeBypass: return new Color(.17f, .41f, .35f);
                case ItemKind.Scissors: return new Color(.45f, .25f, .24f);
                case ItemKind.RubberDuck: return new Color(.55f, .44f, .12f);
                default: return new Color(.35f, .31f, .43f);
            }
        }
    }
}
