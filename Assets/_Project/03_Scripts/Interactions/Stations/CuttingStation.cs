using System;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Interactions;

[RequireComponent(typeof(Collider))]
public class CuttingStation : NetworkBehaviour, IInteractable
{
    [Header("Float Settings")]
    public float floatHeight    = 0.5f;
    public float floatAmplitude = 0.05f;
    public float floatSpeed     = 2.0f;
    public float itemSpacing    = 0.25f;

    // ── 상태 ────────────────────────────────────────────────
    private List<PickableItem> containedItems = new List<PickableItem>();
    private Predicate<PickableItem> _isItemInvalid;
    private Outline _outline;

    public bool IsOccupied => containedItems.Count > 0;

    // ── Fusion ──────────────────────────────────────────────

    public override void Spawned()
    {
        _isItemInvalid = item =>
            item == null ||
            !item.IsOnStation ||
            (Object != null && Object.IsValid
                ? item.StationId != Object.Id
                : item.CurrentStationId != default(NetworkId));

        _outline = GetComponent<Outline>();
        if (_outline == null)
        {
            _outline = gameObject.AddComponent<Outline>();
            _outline.OutlineMode  = Outline.Mode.OutlineAll;
            _outline.OutlineColor = Color.yellow;
            _outline.OutlineWidth = 3f;
        }
        _outline.enabled = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.IsValid) return;
        if (!HasStateAuthority) return;

        CleanupContainedItemsList();
        if (IsOccupied) UpdateItemPositions();
    }

    private void Update()
    {
        if (Object != null && Object.IsValid) return;

        CleanupContainedItemsList();
        if (IsOccupied) UpdateItemPositions();
    }

    // ── 위치 갱신: 단순 위아래 bob ───────────────────────────

    private void UpdateItemPositions()
    {
        float time = (Object != null && Object.IsValid) ? (float)Runner.SimulationTime : Time.time;
        float bobY = floatHeight + Mathf.Sin(time * floatSpeed) * floatAmplitude;

        for (int i = 0; i < containedItems.Count; i++)
        {
            if (containedItems[i] == null) continue;
            float xOff = (i - (containedItems.Count - 1) * 0.5f) * itemSpacing;
            Vector3 pos = transform.position + transform.right * xOff + Vector3.up * bobY;
            containedItems[i].SetStationPosition(pos);
        }
    }

    // ── Item 관리 ────────────────────────────────────────────

    public bool PutItem(PickableItem item)
    {
        if (item == null) return false;
        if (Object != null && Object.IsValid && !HasStateAuthority) return false;

        containedItems.Add(item);
        Vector3 stationPos = transform.position + Vector3.up * floatHeight;

        if (Object != null && Object.IsValid)
            item.Rpc_PutOnStation(Object.Id, stationPos);
        else
        {
            item.LocalPutOnStation(stationPos);
            item.LocalStationRef = this;
        }

        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Object != null && Object.IsValid && !HasStateAuthority) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item != null && !item.IsPhysicallyHeld && !containedItems.Contains(item) && item.CanBePickedUpByStation)
        {
            if (item is ITool) return;
            PutItem(item);
        }
    }

    // ── IInteractable ────────────────────────────────────────

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type) => false;

    public void Interact(CookingMasterHandsManager player, InteractionType type) { }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return "";

        if (!IsOccupied)
            return "[마법 도마]\n[Q] 재료를 던져서 올려두세요";

        PickableItem held = player?.GetActiveHandItem();
        bool hasKnife = held is ITool tool && tool.ToolType == "Knife";

        if (hasKnife)
            return "[마법 도마]\n[L-Click] 재료 썰기";

        return "[마법 도마]\n칼을 들고 재료를 썰 수 있습니다";
    }

    private void CleanupContainedItemsList()
    {
        if (_isItemInvalid == null) return;
        containedItems.RemoveAll(_isItemInvalid);
    }
}
