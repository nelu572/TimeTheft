using System;
using UnityEngine;

namespace TimeTheft.ItemPresentation
{
    public enum PresentedItem { SpareKey, Screwdriver, TimeBypass, Scissors, RubberDuck, Handcuffs }
    public enum ItemPresentationPhase { Acquire, Prepare, Deploy, Activate, Hold, Release, Exit, EffectReaction }
    public enum ItemPresentationState { Idle, Acquiring, Preparing, Deploying, Activating, Holding, Releasing, Exiting, WaitingForHatch, Complete, Failed }
    public enum ItemPresentationEvent { Acquired, Prepared, Deployed, Activated, Held, CutImpact, Released, VisualRemoved, Complete, UseReaction, Failed }
    public enum ItemAnchorRole { None, ItemSlot, TargetItem, TargetClock, ClockFocus, CounterContact, HatchLocation, ExitLocation }

    [Serializable]
    public struct ItemPresentationSegment
    {
        public ItemPresentationPhase Phase;
        public int FirstFrame;
        public int LastFrame;
        public ItemAnchorRole Destination;
        public float DurationSeconds;
        public float Duration => DurationSeconds > 0f ? DurationSeconds : Mathf.Max(1, LastFrame - FirstFrame) / 30f;
    }

    public sealed class ItemPresentationProfile : ScriptableObject
    {
        public PresentedItem Item;
        public AnimationClip Clip;
        public AnimationClip OpponentClip;
        public string RootPath;
        public string ContactPath;
        public bool DeployOnUse;
        public bool ActivateOnUse;
        [Min(.05f)] public float InventoryWidth = 1f;
        public Vector3 InventoryEuler;
        public ItemPresentationSegment[] Segments;

        public bool TryGet(ItemPresentationPhase phase, out ItemPresentationSegment result)
        {
            foreach (var segment in Segments)
                if (segment.Phase == phase) { result = segment; return true; }
            result = default;
            return false;
        }
    }

    [Serializable]
    public sealed class ItemPresentationAnchors
    {
        public Transform ItemSlot, TargetItem, TargetClock, ClockFocus, CounterContact;
        public Transform HatchLocation, ExitLocation;
        public Transform PlayerCableContact, OpponentCableContact;
        public bool CableContactsAreJaws;
        public GameObject RemovedTargetVisual, CutProxyPrefab;
        public bool Opponent;

        public Transform Get(ItemAnchorRole role)
        {
            switch (role)
            {
                case ItemAnchorRole.ItemSlot: return ItemSlot;
                case ItemAnchorRole.TargetItem: return TargetItem;
                case ItemAnchorRole.TargetClock: return TargetClock;
                case ItemAnchorRole.ClockFocus: return ClockFocus != null ? ClockFocus : TargetClock;
                case ItemAnchorRole.CounterContact: return CounterContact;
                case ItemAnchorRole.HatchLocation: return HatchLocation;
                case ItemAnchorRole.ExitLocation: return ExitLocation;
                default: return null;
            }
        }
    }
}
