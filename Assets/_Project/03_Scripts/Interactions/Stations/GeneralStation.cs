using UnityEngine;
using System.Collections.Generic;
using Fusion;
using Interactions;

/// <summary>
/// 일반 조리대 (General Station / 테이블)
/// 아이템을 올려두거나 집을 수 있는 기본 스테이션입니다.
/// 
/// [사용 방법]
/// 1. General 조리대 prefab에 이 컴포넌트를 추가합니다.
/// 2. 테이블 윗면에 BoxCollider (IsTrigger = ON)를 추가합니다.
///    → 이 Trigger 영역 안에 아이템이 떨어지면 자동으로 테이블 위에 고정됩니다.
/// 3. placementPoint를 테이블 윗면 Transform으로 지정합니다. (없으면 자동 계산)
/// </summary>
public class GeneralStation : NetworkBehaviour, IInteractable
{
    // ── 도구(Tool) 즉석 리스폰 설정 ─────────────────────────────
    /// <summary>
    /// MapGenerator가 이 테이블에 Tool을 올릴 때 등록하는 프리팹 참조.
    /// 플레이어가 집을 때 기존 오브젝트를 Despawn하고 새 인스턴스를 즉석 스폰합니다.
    /// </summary>
    [HideInInspector] public NetworkObject toolPrefab;

    /// <summary>MapGenerator가 toolPrefab을 등록할 때 호출합니다.</summary>
    public void RegisterToolPrefab(NetworkObject prefab) => toolPrefab = prefab;
    [Header("Station Settings")]
    [Tooltip("테이블 위에 올릴 수 있는 최대 아이템 수")]
    public int maxItems = 1;

    [Tooltip("아이템이 표시될 위치 (없으면 이 Transform 위로 자동 계산)")]
    public Transform placementPoint;

    [Tooltip("아이템이 2개 이상일 때 간격")]
    public float placementSpacing = 0.3f;

    [Tooltip("아이템이 올라가는 높이 오프셋")]
    public float heightOffset = 0.55f;

    // ── 상태 ──────────────────────────────────────────────────────
    private readonly List<PickableItem> _placedItems = new();
    private System.Predicate<PickableItem> _isItemInvalid;

    bool IsNetworkReady => Object != null && Object.IsValid;

    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (placementPoint == null)
            placementPoint = transform;

        _isItemInvalid = item =>
            item == null ||
            (item.CurrentState != ItemState.OnStation && item.CurrentState != ItemState.Free) ||
            (IsNetworkReady
                ? (item.StationId != default && item.StationId != Object.Id)
                : false);
    }

    // ── Fusion: 권한자가 매 틱 아이템 위치·리스트를 관리 ──────────
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        CleanupItems();
        UpdateItemPositions();
    }

    // ── Sandbox(로컬) 대응 ────────────────────────────────────────
    private void Update()
    {
        if (IsNetworkReady) return;
        CleanupItems();
        UpdateItemPositions();
    }

    // ─────────────────────────────────────────────────────────────

    private void UpdateItemPositions()
    {
        for (int i = 0; i < _placedItems.Count; i++)
        {
            PickableItem item = _placedItems[i];
            if (item == null || item.CurrentState != ItemState.OnStation) continue;
            item.SetStationPosition(CalculatePlacementPos(i));
        }
    }

    private Vector3 CalculatePlacementPos(int index)
    {
        Vector3 center = placementPoint.position + Vector3.up * heightOffset;

        if (_placedItems.Count <= 1) return center;

        float angle  = index * (360f / _placedItems.Count);
        Vector3 offset = new Vector3(
            Mathf.Sin(angle * Mathf.Deg2Rad),
            0f,
            Mathf.Cos(angle * Mathf.Deg2Rad)) * placementSpacing;

        return center + offset;
    }

    private void CleanupItems()
    {
        _placedItems.RemoveAll(_isItemInvalid);
    }

    // ── 아이템이 Trigger 영역에 들어오면 테이블 위에 자동 고정 ────
    private void OnTriggerEnter(Collider other)
    {
        // 권한자만 처리 (네트워크 있을 때)
        if (IsNetworkReady && !HasStateAuthority) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        // isHeld는 네트워크 State를 보므로 RPC 처리 전에는 아직 Held로 보임.
        // IsPhysicallyHeld는 로컬 _localState를 보므로 LocalDrop() 이후 정확히 Free를 반환함.
        // 이미 관리 중이거나, 누군가 들고 있으면 무시
        if (item == null || item.IsPhysicallyHeld || _placedItems.Contains(item)) return;

        // [수정] 도구(ITool)나 빈 접시(PlateItem)는 OnStation으로 강제 전환하지 않고
        // 리스트에만 추가하여 물리 엔진(Gravity 등)이 계속 작동하도록 유지합니다.
        // 이렇게 해야 사용자 요청대로 "바닥에 굴러다니는(Free)" 상태를 유지하면서 픽업 시 재스폰 로직을 태울 수 있습니다.
        if (item is ITool || (item is PlateItem p && p.IsEmpty))
        {
            _placedItems.Add(item);
            return;
        }

        if (_placedItems.Count >= maxItems) return;

        PlaceItem(item);
    }

    // ── 아이템을 테이블 위에 올립니다 ────────────────────────────
    public bool PlaceItem(PickableItem item)
    {
        if (item == null || _placedItems.Contains(item)) return false;
        if (_placedItems.Count >= maxItems) return false;

        Vector3 pos = CalculatePlacementPos(_placedItems.Count);

        if (IsNetworkReady)
            item.Rpc_PutOnStation(Object.Id, pos);
        else
            item.LocalPutOnStation(pos);

        _placedItems.Add(item);
        return true;
    }

    // ── 테이블 위의 마지막 아이템을 꺼냅니다 ─────────────────────
    public PickableItem TakeTopItem()
    {
        CleanupItems();
        if (_placedItems.Count == 0) return null;

        PickableItem item = _placedItems[_placedItems.Count - 1];
        _placedItems.RemoveAt(_placedItems.Count - 1);

        // Rpc_Drop을 호출하지 않습니다.
        // player.PickupItem()이 바로 뒤에서 Held 상태로 전환하므로 Drop 불필요.
        return item;
    }

    // [추가] 클라이언트가 특정 아이템을 집으려 할 때 스테이션 목록에서 즉시 제거 요청
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_RequestReleaseItem(NetworkId itemId)
    {
        for (int i = 0; i < _placedItems.Count; i++)
        {
            if (_placedItems[i].Object.Id == itemId)
            {
                _placedItems.RemoveAt(i);
                break;
            }
        }
    }

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return false;
        else
        {
            // 빈 손 → 테이블에 아이템이 있어야 집을 수 있음
            return _placedItems.Count > 0;
        }
    }

    public void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return;

        if (player.IsActiveHandFull())
        {
            // 손에 든 아이템을 테이블에 올림
            PickableItem heldItem = player.GetActiveHandItem();
            if (heldItem == null) return;

            Vector3 placePos = CalculatePlacementPos(_placedItems.Count);
            heldItem.SetHeldLayer(false);
            heldItem.LocalDrop(placePos);
            if (heldItem.Object != null && heldItem.Object.IsValid)
                heldItem.Rpc_Drop(placePos);
            player.ClearActiveHandSlot();

            PlaceItem(heldItem);
        }
        else
        {
            // 테이블 위 아이템을 집음 (Lettuce Logic: 권한 요청 + 스테이션 해제)
            // 아이템 자체의 픽업 조건(예: LavaCheeseItem 냉기 주머니 필요)을 먼저 확인
            if (_placedItems.Count == 0) return;
            PickableItem topItem = _placedItems[_placedItems.Count - 1];
            if (topItem != null && !topItem.CanInteract(player, InteractionType.Primary)) return;

            PickableItem item = TakeTopItem();
            if (item != null)
            {
                if (IsNetworkReady)
                {
                    item.Object.RequestStateAuthority();
                    Rpc_RequestReleaseItem(item.Object.Id);
                }
                player.PickupItem(item);
            }
        }
    }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return "";

        if (player.IsActiveHandFull())
        {
            if (_placedItems.Count < maxItems)
                return $"[E] {player.GetActiveHandItem()?.itemName} 올려두기";
            return "";
        }
        else
        {
            if (_placedItems.Count > 0)
            {
                PickableItem topItem = _placedItems[_placedItems.Count - 1];
                if (topItem == null) return "";

                // 아이템 자체 픽업 조건이 있으면 해당 레이블을 우선 표시
                if (!topItem.CanInteract(player, InteractionType.Primary))
                    return topItem.GetInteractionLabel(player, InteractionType.Primary);

                return $"[E] {topItem.itemName} 집기";
            }
            return "";
        }
    }
}
