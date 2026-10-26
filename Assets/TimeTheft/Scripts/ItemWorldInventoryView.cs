using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeTheft
{
    // 자기 ID와 상대 공개 관찰만 동기화한다. Match·RNG·숨은 상대 인벤토리를 받지 않는다.
    [DisallowMultipleComponent]
    public sealed class ItemWorldInventoryView : MonoBehaviour
    {
        [SerializeField] private ItemWorldView itemPrefab;
        [SerializeField] private Transform playerAnchor, opponentAnchor;
        [SerializeField, Min(.1f)] private float spacing = 1.25f;
        [SerializeField, Min(.1f)] private float playerScale = 1;
        [SerializeField, Min(.1f)] private float opponentScale = 1.15f;
        private readonly List<ItemWorldView> playerItems = new List<ItemWorldView>();
        private readonly List<ItemWorldView> opponentItems = new List<ItemWorldView>();
        private readonly List<ItemWorldView> retiringItems = new List<ItemWorldView>();
        private System.Collections.ObjectModel.ReadOnlyCollection<ItemWorldView> playerView, opponentView;
        private Action<int> select;
        private bool? opponentKindsVisible;

        public IReadOnlyList<ItemWorldView> PlayerItems => playerView ?? (playerView = playerItems.AsReadOnly());
        public IReadOnlyList<ItemWorldView> OpponentItems => opponentView ?? (opponentView = opponentItems.AsReadOnly());
        public ItemWorldView ItemPrefab => itemPrefab;
        public Transform PlayerAnchor => playerAnchor;
        public Transform OpponentAnchor => opponentAnchor;

        public void Initialize(Action<int> selectItem)
        {
            if (!itemPrefab || !playerAnchor || !opponentAnchor)
                throw new InvalidOperationException("아이템 Prefab과 양측 배치 앵커를 연결하세요.");
            select = selectItem;
        }

        public void Refresh(IReadOnlyList<ItemState> player, ItemInventoryObservation opponent, int? selectedId, bool inputEnabled)
        {
            for (int i = playerItems.Count - 1; i >= 0; i--)
            {
                bool held = false;
                foreach (var item in player)
                    if (!item.IsConsumed && playerItems[i].ItemId == item.Id) { held = true; break; }
                if (!held) Retire(playerItems, i, false);
            }
            for (int i = 0; i < player.Count; i++)
            {
                var item = player[i];
                if (item.IsConsumed) continue;
                var token = FindPlayer(item.Id);
                if (!token)
                {
                    token = Create(playerAnchor, Participant.Player, item.Id, item.Kind, playerScale);
                    playerItems.Add(token);
                }
                token.SetState(selectedId == item.Id, inputEnabled);
            }
            Layout(playerItems);
            RefreshOpponent(opponent, inputEnabled);
            retiringItems.RemoveAll(item => !item);
        }

        private void RefreshOpponent(ItemInventoryObservation observation, bool inputEnabled)
        {
            if (opponentKindsVisible.HasValue && opponentKindsVisible.Value != observation.RevealsKinds)
                Clear(opponentItems); // 비공개로 바뀌면 이전 이름을 즉시 숨긴다.
            opponentKindsVisible = observation.RevealsKinds;
            var previous = new List<ItemWorldView>(opponentItems);
            opponentItems.Clear();
            for (int i = 0; i < observation.Count; i++)
            {
                ItemKind? kind = observation.RevealsKinds ? observation.Kinds[i] : (ItemKind?)null;
                int index = previous.FindIndex(item => item.Kind == kind);
                ItemWorldView token;
                if (index >= 0) { token = previous[index]; previous.RemoveAt(index); }
                else token = Create(opponentAnchor, Participant.Ai, null, kind, opponentScale);
                token.SetState(false, inputEnabled);
                opponentItems.Add(token);
            }
            for (int i = previous.Count - 1; i >= 0; i--) Retire(previous, i, false);
            Layout(opponentItems);
        }

        public void ShowUse(ItemUseResult result)
        {
            if (!result.Succeeded) return;
            if (result.Actor == Participant.Player) RemovePlayer(result.ItemId, false);
            else RemoveOpponent(result.Effect.Value.Source.Kind, false);
            if (!result.RemovedItem.HasValue) return;
            var removed = result.RemovedItem.Value;
            if (removed.Owner == Participant.Player) RemovePlayer(removed.Id, true);
            else RemoveOpponent(removed.Kind, true);
        }

        private void RemovePlayer(int id, bool cut)
        {
            int index = playerItems.FindIndex(item => item.ItemId == id);
            if (index >= 0) Retire(playerItems, index, cut);
        }

        private void RemoveOpponent(ItemKind kind, bool cut)
        {
            int index = opponentItems.FindIndex(item => !item.Kind.HasValue || item.Kind.Value == kind);
            if (index >= 0) Retire(opponentItems, index, cut);
        }

        private void Retire(List<ItemWorldView> list, int index, bool cut)
        {
            var item = list[index];
            list.RemoveAt(index);
            if (!item) return;
            item.BeginRemoval(cut);
            retiringItems.Add(item);
        }

        private ItemWorldView FindPlayer(int id)
        {
            foreach (var item in playerItems) if (item.ItemId == id) return item;
            return null;
        }

        private ItemWorldView Create(Transform anchor, Participant owner, int? id, ItemKind? kind, float scale)
        {
            var item = Instantiate(itemPrefab, anchor, false);
            item.name = owner == Participant.Player ? "Item " + id : "Opponent Item";
            item.transform.localScale = Vector3.one * scale;
            item.Initialize(owner, id, kind, select);
            return item;
        }

        private void Layout(List<ItemWorldView> items)
        {
            for (int i = 0; i < items.Count; i++)
                items[i].transform.localPosition = Vector3.right * ((i - (items.Count - 1) * .5f) * spacing);
        }

        public void SetVisible(bool visible)
        {
            playerAnchor.gameObject.SetActive(visible);
            opponentAnchor.gameObject.SetActive(visible);
        }

        public void ResetItems()
        {
            Clear(playerItems);
            Clear(opponentItems);
            Clear(retiringItems);
            opponentKindsVisible = null;
        }

        private static void Clear(List<ItemWorldView> items)
        {
            foreach (var item in items)
            {
                if (!item) continue;
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
            }
            items.Clear();
        }
    }
}
