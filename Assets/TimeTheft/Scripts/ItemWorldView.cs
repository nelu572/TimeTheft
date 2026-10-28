using System;
using TMPro;
using TimeTheft.ItemPresentation;
using UnityEngine;

namespace TimeTheft
{
    // 루트 입력과 보유 표시를 유지하고, 사용 중인 모델의 자세는 연출에 맡긴다.
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
        private GameObject modelRoot;
        private Transform modelPlacement;
        private ItemPoseSnapshot heldModelPose;
        private float heldSourceSeconds;
        private bool modelInUse;

        public Participant Owner { get; private set; }
        public int? ItemId { get; private set; }
        public ItemKind? Kind { get; private set; }
        public TextMeshPro Label => label;
        public TabletopFocusTarget Target => target;
        public bool IsSelected { get; private set; }
        public bool IsRemoving { get; private set; }
        public ItemPresentationController ModelPresentation { get; private set; }

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
            modelInUse = false;
            IsRemoving = false;
            IsSelected = false;
            target.SetInputEnabled(false);
            if (ModelPresentation) ConfigureHeldModel();
            ApplyVisual();
        }

        public void AttachModel(GameObject prefab)
        {
            if (!prefab || !visual || !prefab.GetComponent<ItemPresentationController>())
                throw new InvalidOperationException("아이템 Runtime Prefab과 Visual을 연결하세요.");
            if (modelRoot)
            {
                ResetModel();
                modelRoot.SetActive(false);
                Destroy(modelPlacement ? modelPlacement.gameObject : modelRoot);
            }
            if (!initialized)
            {
                visualPosition = visual.localPosition;
                visualScale = visual.localScale;
            }
            visual.localPosition = visualPosition;
            visual.localScale = visualScale;
            modelPlacement = new GameObject("InventoryPose").transform;
            modelPlacement.SetParent(visual, false);
            modelRoot = Instantiate(prefab, modelPlacement, false);
            ModelPresentation = modelRoot.GetComponent<ItemPresentationController>();
            ModelPresentation.Initialize();
            ConfigureHeldModel();
            body.enabled = false;
            label.enabled = false;
            var labelRenderer = label.GetComponent<Renderer>();
            if (labelRenderer) labelRenderer.enabled = false;
            ApplyVisual();
        }

        private void ConfigureHeldModel()
        {
            var profile = ModelPresentation.Profile;
            if (!profile || !profile.Clip || !profile.TryGet(ItemPresentationPhase.Acquire, out var acquire))
                throw new InvalidOperationException("아이템의 획득 완료 자세를 연결하세요.");
            visual.localPosition = visualPosition;
            visual.localScale = visualScale;
            modelPlacement.localPosition = Vector3.zero;
            modelPlacement.localRotation = Quaternion.Euler(profile.InventoryEuler);
            if (Owner == Participant.Ai && profile.Item == PresentedItem.RubberDuck)
                modelPlacement.localRotation = Quaternion.Euler(0, 180, 0) * modelPlacement.localRotation;
            modelPlacement.localScale = Vector3.one;
            heldSourceSeconds = (acquire.LastFrame - 1) / 30f;
            var clip = Owner == Participant.Ai && profile.OpponentClip ? profile.OpponentClip : profile.Clip;
            clip.SampleAnimation(ModelPresentation.AnimationModel, heldSourceSeconds);
            if (ModelPresentation.Cables) ModelPresentation.Cables.Sample(heldSourceSeconds);

            if (!TryModelBounds(out var bounds))
                throw new InvalidOperationException("아이템 Runtime Prefab에 표시 가능한 Renderer가 없습니다.");
            // 애니메이션이 쓰는 TT_* 회전/스케일은 그대로 두고 외부 보유 부모만 보정한다.
            float width = Mathf.Max(.0001f, Mathf.Max(bounds.size.x, bounds.size.z));
            float ownerScale = Mathf.Max(.0001f, Mathf.Abs(transform.lossyScale.x));
            modelPlacement.localScale *= Mathf.Max(.05f, profile.InventoryWidth) * ownerScale / width;
            TryModelBounds(out bounds);
            modelPlacement.position += new Vector3(transform.position.x - bounds.center.x,
                transform.position.y - bounds.min.y, transform.position.z - bounds.center.z);
            CaptureHeldModelPose();
        }

        // 실제 Acquire 완료 후에도 호출할 수 있다. 사용/종료 중인 물체를 보유 위치로 돌리지 않는다.
        public void CaptureHeldModelPose()
        {
            if (!ModelPresentation || modelInUse || IsRemoving) return;
            if (TryModelBounds(out var bounds)) FitModelCollider(bounds);
            Physics.SyncTransforms();
            heldModelPose = new ItemPoseSnapshot(modelPlacement.gameObject);
            ModelPresentation.SetRestPose();
        }

        public void BeginModelUse(Transform parent)
        {
            if (modelInUse) return;
            modelInUse = true;
            IsRemoving = true;
            IsSelected = false;
            target.SetInputEnabled(false);
            var collider = GetComponent<Collider>();
            if (collider) collider.enabled = false;
            transform.SetParent(parent, true);
        }

        private bool TryModelBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var renderer in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                bool visiblePart = true;
                // 새 묶음 공개 중에는 보유품 앵커가 숨겨져 있어도 모델 자체의 표시 부분을 측정한다.
                for (var part = renderer.transform; part && part != modelRoot.transform; part = part.parent)
                    if (!part.gameObject.activeSelf) { visiblePart = false; break; }
                if (!visiblePart) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        private void FitModelCollider(Bounds bounds)
        {
            var box = GetComponent<BoxCollider>();
            if (!box) return;
            // Hover/선택 lift를 포함한 고정 입력 범위. 포인터를 올릴 때 Collider 자체는 이동하지 않는다.
            var lifted = bounds;
            lifted.center += visual.parent.TransformVector(Vector3.up * selectedLift);
            bounds.Encapsulate(lifted);
            var local = new Bounds(transform.InverseTransformPoint(bounds.min), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                local.Encapsulate(transform.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
            var worldScale = transform.lossyScale;
            var minimum = new Vector3(.35f / Mathf.Max(.0001f, Mathf.Abs(worldScale.x)),
                .35f / Mathf.Max(.0001f, Mathf.Abs(worldScale.y)), .35f / Mathf.Max(.0001f, Mathf.Abs(worldScale.z)));
            box.size = Vector3.Max(local.size, minimum);
            box.center = new Vector3(local.center.x, local.min.y + box.size.y * .5f, local.center.z);
            if (target.FocusPoint)
                target.FocusPoint.position = transform.TransformPoint(box.center + Vector3.up * (box.size.y * .5f));
        }

        private void ResetModel()
        {
            if (!ModelPresentation) return;
            ModelPresentation.ResetPresentation();
            if (modelInUse) return;
            // Runtime의 원본 snapshot은 FullTake 시작 자세이므로 보유 자세를 다시 복원한다.
            heldModelPose?.Restore();
            if (ModelPresentation.Cables) ModelPresentation.Cables.Sample(heldSourceSeconds);
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
            if (!initialized || modelInUse) return;
            age += Time.unscaledDeltaTime;
            ApplyVisual();
            if (IsRemoving && age >= reactionDuration) Destroy(gameObject);
        }

        private void ApplyVisual()
        {
            if (!initialized || modelInUse) return;
            float t = Mathf.Clamp01(age / Mathf.Max(.05f, reactionDuration));
            float size = IsRemoving ? 1 - Mathf.SmoothStep(0, 1, t) * .9f : ModelPresentation ? 1 : Mathf.Lerp(.9f, 1, Mathf.SmoothStep(0, 1, t));
            visual.localScale = visualScale * size;
            visual.localPosition = visualPosition + Vector3.up * (IsSelected ? selectedLift : IsRemoving ? t * .05f :
                ModelPresentation && target.IsHighlighted ? selectedLift * .25f : 0);
            if (ModelPresentation) return; // 모델의 텍스처와 재질 색상은 그대로 유지한다.
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
            ResetModel();
        }

        private void OnDestroy()
        {
            ResetModel();
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
                case ItemKind.RubberDuck: return "러버덕";
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
