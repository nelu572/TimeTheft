using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TimeTheft
{
    public sealed partial class TimeTheftGameView
    {
        private static readonly Color UiInk = new Color(.91f, .88f, .78f);
        private static readonly Color UiQuiet = new Color(.53f, .70f, .65f);
        private static readonly Color UiBrass = new Color(.78f, .65f, .40f);
        private RectTransform uiRoot, itemCard, actionCard, noticeCard, itemLeader, actionLeader;
        private CanvasGroup itemGroup, actionGroup, noticeGroup;
        private TextMeshProUGUI itemTitle, itemTag, itemEffectTag, itemState, itemHint, actionTitle, actionHint, noticeText;
        private UiIndexGraphic itemFrame;
        private TMP_FontAsset interfaceFont, interfaceFallback;
        private float noticeUntil;
        private bool itemWasVisible, actionWasVisible;
        private TurnStage lastUiStage = (TurnStage)(-1);
        private Participant lastUiActor;
        private Camera uiCamera;
        private Vector2 previousItemCardSize;

        private void UseInterfaceFont()
        {
            // 별도 정적 아틀라스를 사용해 플레이 중 원본 폰트 캐시를 갱신하지 않는다.
            var template = Resources.Load<TMP_FontAsset>("TimeTheftUi");
            if (!template) return;
            var previousFont = phaseLabel.font;
            // 새 명칭의 누락 글리프만 런타임 fallback으로 보충한다. 원본 폰트/아틀라스는 수정하지 않는다.
            interfaceFont = Instantiate(template); interfaceFont.name = "TimeTheftUi Runtime";
            interfaceFont.hideFlags = HideFlags.DontSave;
            interfaceFont.material = new Material(template.material);
            var atlases = new Texture2D[template.atlasTextures.Length];
            for (int i = 0; i < atlases.Length; i++) atlases[i] = Instantiate(template.atlasTextures[i]);
            interfaceFont.atlasTextures = atlases;
            interfaceFont.material.mainTexture = atlases[0];
            interfaceFont.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
            if (previousFont.sourceFontFile)
            {
                interfaceFallback = TMP_FontAsset.CreateFontAsset(previousFont.sourceFontFile);
                interfaceFallback.hideFlags = HideFlags.DontSave;
                interfaceFont.fallbackFontAssetTable.Add(interfaceFallback);
            }
            interfaceFont.ReadFontAssetDefinition();
            foreach (var text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text.font != previousFont) continue;
                text.font = interfaceFont;
                text.fontSharedMaterial = interfaceFont.material;
            }
            if (worldItems) worldItems.SetLabelFont(interfaceFont);
        }

        private void InitializeContextUi()
        {
            if (uiRoot) return;
            uiCamera = focusCamera.GetComponent<Camera>();
            uiRoot = new GameObject("Context UI", typeof(RectTransform)).GetComponent<RectTransform>();
            uiRoot.SetParent(phaseLabel.canvas.transform, false);
            uiRoot.anchorMin = Vector2.zero; uiRoot.anchorMax = Vector2.one;
            uiRoot.offsetMin = uiRoot.offsetMax = Vector2.zero;
            phaseLabel.enabled = false;
            batchLabel.enabled = false;
            batchLabel.GetComponent<Renderer>().enabled = false;
            batchLabel.gameObject.SetActive(false);
            // 실제 계수기와 분리된 묶음 표시의 임시 2D 받침만 숨긴다.
            var marker = batchLabel.transform.parent;
            if (marker && marker.name == "RoundCounter")
                foreach (var sprite in marker.GetComponentsInChildren<SpriteRenderer>(true)) sprite.enabled = false;
            inventoryLabel.enabled = false;
            itemCard = Panel("Item Index", new Vector2(480, 380), out itemGroup);
            itemFrame = itemCard.GetComponent<UiIndexGraphic>();
            itemCard.gameObject.AddComponent<UiContextCancel>().Cancel = CancelItemSelection;
            itemTag = Label(itemCard, "Index", 18, UiQuiet, new Vector2(76, -22), new Vector2(310, 24));
            itemTitle = Label(itemCard, "Name", 34, UiInk, new Vector2(76, -50), new Vector2(368, 48));
            itemTitle.fontStyle = FontStyles.Bold;
            itemEffectTag = Label(itemCard, "Effect Index", 18, UiQuiet, new Vector2(32, -130), new Vector2(416, 24));
            itemEffectTag.text = "효과";
            // 기존 설명 API와 같은 텍스트를 유지하되 실제 레이아웃은 오브젝트 카드에서 제공한다.
            itemDetailLabel.transform.SetParent(itemCard, false);
            Place(itemDetailLabel.rectTransform, new Vector2(32, -164), new Vector2(480, 76));
            itemDetailLabel.fontSize = 26; itemDetailLabel.color = UiInk;
            itemDetailLabel.lineSpacing = 8; itemDetailLabel.wordWrappingRatios = 0;
            itemDetailLabel.alignment = TextAlignmentOptions.TopLeft;
            itemState = Label(itemCard, "State", 21, UiBrass, Vector2.zero, new Vector2(416, 50));
            itemHint = Label(itemCard, "Gesture", 22, UiInk, Vector2.zero, new Vector2(436, 54));

            itemLeader = Leader("Item Anchor", UiQuiet);
            actionCard = Panel("Clock Decision", new Vector2(496, 236), out actionGroup);
            actionCard.GetComponent<UiIndexGraphic>().SetBands(100, 0);
            actionTitle = Label(actionCard, "Decision Index", 18, UiQuiet, new Vector2(76, -22), new Vector2(388, 26));
            actionHint = Label(actionCard, "Decision Hint", 24, UiInk, new Vector2(76, -54), new Vector2(388, 44));
            StyleButton(collectButton, actionCard, new Vector2(28, -116), new Vector2(212, 94), "<b>징수</b>\n<size=19>수명 → Bank</size>", UiBrass);
            StyleButton(rationButton, actionCard, new Vector2(256, -116), new Vector2(212, 94), "<b>배급</b>\n<size=19>Bank → 수명</size>", UiQuiet);
            collectButton.targetGraphic.GetComponent<UiIndexGraphic>().Symbol = UiIndexGraphic.FrameSymbol.Collect;
            rationButton.targetGraphic.GetComponent<UiIndexGraphic>().Symbol = UiIndexGraphic.FrameSymbol.Ration;
            actionLeader = Leader("Clock Anchor", UiBrass);

            noticeCard = Panel("Brief Table Cue", new Vector2(600, 66), out noticeGroup);
            noticeCard.GetComponent<UiIndexGraphic>().Layout = UiIndexGraphic.FrameLayout.Compact;
            noticeText = Label(noticeCard, "Cue", 24, UiInk, new Vector2(20, -16), new Vector2(560, 38));
            noticeText.alignment = TextAlignmentOptions.Center;
            noticeCard.GetComponent<UiIndexGraphic>().raycastTarget = false;
            InitializeClockContext();
            InitializeClockHover();
            InitializeCounterDeltas();
            BuildHelpCard();
            InitializeUxCues();
            Canvas.preWillRenderCanvases += RefreshContextUi;
        }

        private RectTransform Panel(string name, Vector2 size, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UiIndexGraphic), typeof(CanvasGroup));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(uiRoot, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size;
            group = go.GetComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false;
            return rect;
        }

        private TextMeshProUGUI Label(RectTransform parent, string name, float size, Color ink, Vector2 pos, Vector2 extent)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false); label.font = phaseLabel.font;
            label.fontSharedMaterial = phaseLabel.fontSharedMaterial;
            label.fontSize = size; label.color = ink; label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            Place(label.rectTransform, pos, extent); return label;
        }

        private static void Place(RectTransform rect, Vector2 pos, Vector2 extent)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = pos; rect.sizeDelta = extent;
        }

        private void StyleButton(Button button, RectTransform parent, Vector2 pos, Vector2 extent, string title, Color ink)
        {
            button.transform.SetParent(parent, false); Place((RectTransform)button.transform, pos, extent);
            var old = button.GetComponent<Image>(); if (old) old.enabled = false;
            var graphic = new GameObject("Index Frame", typeof(RectTransform), typeof(UiIndexGraphic)).GetComponent<UiIndexGraphic>();
            graphic.transform.SetParent(button.transform, false); graphic.transform.SetAsFirstSibling();
            var frameRect = graphic.rectTransform;
            frameRect.anchorMin = Vector2.zero; frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;
            button.targetGraphic = graphic;
            graphic.Accent = ink;
            graphic.Layout = extent.y >= 72 ? UiIndexGraphic.FrameLayout.Action : UiIndexGraphic.FrameLayout.Compact;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.52f, .66f, .60f); colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.37f, .43f, .41f); colors.fadeDuration = .08f; button.colors = colors;
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            text.text = title; text.fontSize = extent.y > 58 ? 28 : 23; text.color = ink;
            text.alignment = extent.y >= 72 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.pivot = new Vector2(.5f, .5f);
            text.rectTransform.offsetMin = new Vector2(extent.y >= 72 ? 76 : 16, 12);
            text.rectTransform.offsetMax = new Vector2(-16, -10);
        }

        private void StyleMouseControls()
        {
            StyleButton(helpButton, uiRoot, Vector2.zero, new Vector2(176, 46), "도움말", UiInk);
            StyleButton(overviewButton, uiRoot, Vector2.zero, new Vector2(176, 46), "테이블로", UiQuiet);
            StyleButton(skipButton, uiRoot, Vector2.zero, new Vector2(176, 46), "연출 단축", UiQuiet);
            int index = 0;
            foreach (var button in new[] { helpButton, overviewButton, skipButton })
            {
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = new Vector2(-24, -24 - index++ * 54);
            }
            // 도움말은 H로 열고, 닫기는 열린 안내 안에서만 제공한다.
            helpButton.transform.SetParent(helpCard.transform, false);
            var close = (RectTransform)helpButton.transform;
            close.anchorMin = close.anchorMax = close.pivot = Vector2.one;
            close.anchoredPosition = new Vector2(-20, -20); close.sizeDelta = new Vector2(98, 44);
            ((RectTransform)overviewButton.transform).anchoredPosition = new Vector2(-24, -24);
        }

        private void BuildHelpCard()
        {
            helpCard.SetActive(false);
            CanvasGroup group;
            var help = Panel("Table Guide", new Vector2(850, 740), out group);
            help.GetComponent<UiIndexGraphic>().Layout = UiIndexGraphic.FrameLayout.Guide;
            help.GetComponent<UiIndexGraphic>().SetBands(130, 0);
            group.alpha = 1; group.blocksRaycasts = true;
            Label(help, "Guide Index", 18, UiBrass, new Vector2(76, -28), new Vector2(620, 28)).text = "TIME THEFT  /  TABLE GUIDE";
            var title = Label(help, "Guide Title", 36, UiInk, new Vector2(76, -64), new Vector2(742, 58));
            title.text = "테이블 위에서 플레이하세요"; title.fontStyle = FontStyles.Bold;
            Label(help, "Guide Body", 25, UiInk, new Vector2(36, -158), new Vector2(778, 550)).text =
                "01  아이템\n마우스를 올려 확인 → 클릭해 선택 → 잠시 뒤 다시 클릭해 사용\n빈 테이블 또는 우클릭: 미사용 선택 취소\n\n"
                + "02  회중시계\n아이템 선택을 취소한 뒤 닫힌 시계 클릭 → 징수 / 배급\n시계 선택 뒤에는 아이템을 추가로 사용할 수 없습니다.\n\n"
                + "03  행동과 정보\n징수: 일반은 상대, 빨강은 내 수명에서 Bank로 이동\n배급: 일반은 나, 빨강은 상대가 Bank에서 시계 시간의 1/3 수령\n마지막 시계는 공개하지 않고 폐기합니다.\n\n"
                + "계수기 클릭: 확대 · 우클릭 / 테이블로: 복귀\n종료 전표 클릭: 다시 플레이 · 도움말은 일시 정지가 아닙니다.";
            helpCard = help.gameObject; helpCard.SetActive(false);
        }

        public void ShowContextNotice(string message)
        {
            if (!noticeText) return;
            noticeText.text = message; noticeUntil = Time.unscaledTime + 3.6f;
        }

        private void LateUpdate()
        {
            if (!uiRoot || itemMatch == null) return;
            // Focus 카메라의 LateUpdate 뒤 위치를 사용하도록 아래 preWillRenderCanvases에서도 갱신한다.
            RefreshContextUi();
        }

        private void RefreshContextUi()
        {
            if (!uiRoot || itemMatch == null || !isActiveAndEnabled) return;
            RefreshUxCues();
            bool busy = itemPresentation || (inputController && inputController.IsPresentingClock) ||
                focusCamera.IsPresentationCameraOwned || IsHelpVisible;
            if (lastUiStage != itemMatch.Stage || lastUiActor != itemMatch.ActiveParticipant)
            {
                lastUiStage = itemMatch.Stage; lastUiActor = itemMatch.ActiveParticipant;
                if (itemMatch.Stage == TurnStage.GameOver) noticeUntil = 0;
            }
            bool canShow = !busy && CanHoverItems;
            ItemWorldView shown = canShow && hoveredItem && !hoveredItem.IsRemoving ? hoveredItem : null;
            if (!shown && canShow && SelectedItemId.HasValue)
                foreach (var item in worldItems.PlayerItems) if (item.ItemId == SelectedItemId) { shown = item; break; }
            bool itemVisible = shown && shown.gameObject.activeInHierarchy;
            bool selectedItemVisible = false;
            if (itemVisible)
            {
                bool selected = shown.Owner == Participant.Player && shown.ItemId == SelectedItemId;
                selectedItemVisible = selected;
                bool own = shown.Owner == Participant.Player;
                bool usable = own && itemMatch.CanUseItems(Participant.Player);
                itemFrame.Emphasized = selected;
                itemFrame.Layout = selected ? UiIndexGraphic.FrameLayout.Detail : UiIndexGraphic.FrameLayout.Compact;
                itemFrame.SetBands(112, 80);
                itemFrame.Accent = usable ? UiBrass : UiQuiet;
                itemTag.text = "소지품 / 선택";
                itemTitle.text = ItemWorldView.DisplayName(shown.Kind);
                itemTag.gameObject.SetActive(selected);
                itemEffectTag.gameObject.SetActive(selected);
                itemDetailLabel.gameObject.SetActive(selected);
                itemTitle.fontSize = selected ? 34 : 28;
                Place(itemTitle.rectTransform, selected ? new Vector2(76, -50) : new Vector2(24, -13),
                    selected ? new Vector2(368, 48) : new Vector2(232, 36));
                itemDetailLabel.text = selected ? DescribeItem(shown.Kind) : "";
                float bodyHeight = Mathf.Ceil(itemDetailLabel.GetPreferredValues(itemDetailLabel.text, 480, 0).y);
                itemDetailLabel.rectTransform.sizeDelta = new Vector2(480, bodyHeight);
                float bodyEnd = 164 + bodyHeight;
                Place(itemState.rectTransform, new Vector2(32, -bodyEnd - 12), new Vector2(416, 50));
                Place(itemHint.rectTransform, new Vector2(76, -bodyEnd - 50), new Vector2(436, 54));
                itemCard.sizeDelta = selected ? new Vector2(544, bodyEnd + 116) : new Vector2(280, 88);
                itemState.text = !own ? "상대의 보유품 · 사용 불가" : !usable ?
                    (itemMatch.IsItemUseBlocked(Participant.Player) ? "러버덕 · 이번 차례 아이템 사용 봉쇄" :
                     itemMatch.ActiveParticipant == Participant.Ai ? "상대 차례 · 살펴보기만 가능" : "시계 선택 이후에는 사용 불가") : selected && shown.ItemId.HasValue
                    ? ItemFailureText(itemMatch.CheckItemUse(Participant.Player, shown.ItemId.Value)) : "클릭해 선택 · 아직 소모되지 않음";
                if (!selected)
                {
                    Place(itemState.rectTransform, new Vector2(24, -51), new Vector2(232, 24));
                    itemState.fontSize = 19;
                    itemState.text = !own ? (shown.Kind.HasValue ? "상대 보유품" : "종류 비공개") : usable ? "클릭해 선택" : "지금은 사용 불가";
                }
                else itemState.fontSize = 21;
                itemHint.text = selected ? (Time.unscaledTimeAsDouble - lastItemClick < ItemConfirmQuietSeconds
                    ? "선택 중 · 잠시 뒤 다시 클릭해 사용" : "같은 아이템을 다시 클릭해 사용")
                    + "\n<size=19><color=#8AB3A6>빈 테이블 / 우클릭: 선택 취소</color></size>" : "";
                itemHint.gameObject.SetActive(selected);
                itemVisible = PlaceItemCard(shown, selected);
                previousItemCardSize = itemCard.sizeDelta;
                if (selected) Connect(itemLeader, shown.Target.transform.position, itemCard, true);
            }
            Fade(itemGroup, itemVisible, ref itemWasVisible);
            itemLeader.gameObject.SetActive(itemVisible && selectedItemVisible);

            bool action = !busy && collectButton.gameObject.activeSelf && collectButton.interactable;
            bool actionVisible = action && itemMatch.CurrentClock >= 0;
            if (actionVisible)
            {
                actionTitle.text = "회중시계 / 행동 선택";
                actionHint.text = "숫자와 종류 비공개";
                actionHint.fontSize = 24;
                actionHint.rectTransform.sizeDelta = new Vector2(388, 44);
                var clock = clocks[itemMatch.CurrentClock];
                var h2 = clock.H2Presentation;
                Vector3 point = h2 && h2.MotionRoot ? h2.MotionRoot.position : clock.transform.position;
                actionVisible = TrackCard(actionCard, point, new Vector2(128, -actionCard.sizeDelta.y - 16), true);
                Connect(actionLeader, point, actionCard, true);
            }
            Fade(actionGroup, actionVisible, ref actionWasVisible);
            actionLeader.gameObject.SetActive(actionVisible);
            RefreshClockContext();
            RefreshClockHover();
            RefreshCounterDeltas();
            noticeCard.anchoredPosition = new Vector2(0, -uiRoot.rect.height * .5f + 72);
            bool noticeVisible = Time.unscaledTime < noticeUntil && !IsHelpVisible && itemMatch.Stage != TurnStage.GameOver;
            bool wasNotice = noticeGroup.alpha > 0;
            Fade(noticeGroup, noticeVisible, ref wasNotice, false);
            if (helpCard.activeSelf) helpCard.transform.SetAsLastSibling();
            helpButton.transform.SetAsLastSibling();
        }

        private bool TrackCard(RectTransform card, Vector3 point, Vector2 offset, bool beside, bool freezeUnderPointer = true)
        {
            Vector3 screen = uiCamera.WorldToScreenPoint(point);
            if (screen.z <= uiCamera.nearClipPlane || !uiCamera.pixelRect.Contains(screen)) return false;
            // UI에 포인터가 들어온 동안 클릭 대상이 시차/자석 Hover 복귀를 쫓아 움직이지 않게 한다.
            var group = card.GetComponent<CanvasGroup>();
            if (freezeUnderPointer && group.blocksRaycasts && !focusCamera.IsTransitioning && Mouse.current != null &&
                RectTransformUtility.RectangleContainsScreenPoint(card, Mouse.current.position.ReadValue())) return true;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, screen, null, out local);
            Vector2 half = card.sizeDelta * .5f;
            Vector2 pos = beside ? local + new Vector2(half.x + offset.x, half.y + offset.y)
                : local + new Vector2(offset.x, -half.y + offset.y);
            Rect bounds = uiRoot.rect;
            if (beside && pos.x + half.x > bounds.xMax - 20) pos.x = local.x - half.x - offset.x;
            pos.x = Mathf.Clamp(pos.x, bounds.xMin + half.x + 20, bounds.xMax - half.x - 20);
            pos.y = Mathf.Clamp(pos.y, bounds.yMin + half.y + 20, bounds.yMax - half.y - 20);
            card.anchoredPosition = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
            return true;
        }

        private bool PlaceItemCard(ItemWorldView item, bool selected)
        {
            var collider = item.GetComponent<Collider>();
            if (!collider) return false;
            Vector2 min, max;
            if (!ScreenBounds(collider.bounds, out min, out max)) return false;
            var half = itemCard.sizeDelta * .5f; var bounds = uiRoot.rect;
            if (selected)
            {
                // 사용 중인 상세 카드만 빈 테이블 앞쪽의 안전 여백으로 옮긴다.
                itemCard.anchoredPosition = new Vector2(Mathf.Round(bounds.xMax - half.x - 24),
                    Mathf.Round(bounds.yMin + half.y + 24));
                return true;
            }
            if (itemCard.sizeDelta == previousItemCardSize && PointerOverItemCard()) return true;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, new Vector2((min.x + max.x) * .5f, min.y), null, out local);
            local.y -= half.y + 18;
            itemCard.anchoredPosition = new Vector2(Mathf.Round(Mathf.Clamp(local.x, bounds.xMin + half.x + 20, bounds.xMax - half.x - 20)),
                Mathf.Round(Mathf.Clamp(local.y, bounds.yMin + half.y + 20, bounds.yMax - half.y - 20)));
            return true;
        }

        private static void Fade(CanvasGroup group, bool visible, ref bool previous, bool blocks = true)
        {
            if (visible && !previous) group.alpha = 0;
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1 : 0, Time.unscaledDeltaTime / .14f);
            group.blocksRaycasts = blocks && visible; group.interactable = visible; previous = visible;
            group.transform.localScale = Vector3.one * Mathf.Lerp(.985f, 1, group.alpha);
        }

        private bool PointerOverItemCard()
        {
            return itemGroup && itemGroup.blocksRaycasts && Mouse.current != null &&
                RectTransformUtility.RectangleContainsScreenPoint(itemCard, Mouse.current.position.ReadValue());
        }

        private RectTransform Leader(string name, Color ink)
        {
            var line = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            line.transform.SetParent(uiRoot, false); line.transform.SetAsFirstSibling();
            line.color = ink * new Color(1, 1, 1, .55f); line.raycastTarget = false;
            return line.rectTransform;
        }

        private void Connect(RectTransform line, Vector3 world, RectTransform card, bool beside)
        {
            Vector2 source;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, uiCamera.WorldToScreenPoint(world), null, out source);
            Vector2 destination = card.anchoredPosition + (beside
                ? new Vector2(source.x < card.anchoredPosition.x ? -card.sizeDelta.x * .5f : card.sizeDelta.x * .5f, 0)
                : new Vector2(0, card.sizeDelta.y * .5f));
            source += (destination - source).normalized * 24;
            Vector2 delta = destination - source;
            line.anchorMin = line.anchorMax = line.pivot = new Vector2(.5f, .5f);
            line.anchoredPosition = (source + destination) * .5f;
            line.sizeDelta = new Vector2(delta.magnitude, 1.5f);
            line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private string ShortItemDescription(ItemKind? kind)
        {
            if (!kind.HasValue) return "종류 비공개";
            switch (kind.Value)
            {
                case ItemKind.SpareKey: return "첫 시계를 나만 확인 · 유지 또는 1회 재선택";
                case ItemKind.Screwdriver: return "빨간 시계 징수 손실 " + screwdriverPercent + "% 경감";
                case ItemKind.TimeBypass: return "이번 징수 실제 손실의 " + bypassPercent + "%를 징수 대상의 상대에게 지급";
                case ItemKind.Scissors: return "상대 미사용 아이템 1개 무작위 제거";
                case ItemKind.RubberDuck: return "상대의 다음 행동 차례에 아이템 사용 봉쇄";
                default: return "상대 다음 차례 생략 · 시계는 미사용";
            }
        }

        private void DestroyContextUi()
        {
            Canvas.preWillRenderCanvases -= RefreshContextUi;
            EndBypassCue();
            if (uiRoot) Destroy(uiRoot.gameObject);
            if (interfaceFont) Destroy(interfaceFont);
            if (interfaceFallback) Destroy(interfaceFallback);
        }
    }
}
