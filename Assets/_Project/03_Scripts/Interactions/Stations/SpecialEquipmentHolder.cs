using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 특수장비 거치대.
/// 조리 매뉴얼, 냉기 주머니 등의 특수 장비를 보관합니다.
///
/// [조작법]
/// E키 (Primary) : 빈 손이면 장비 착용 / 장비를 들고 있으면 반납
///
/// [멀티플레이]
/// ItemNetworkId([Networked])를 통해 모든 클라이언트가 거치 상태를 동기화합니다.
/// StateAuthority가 FixedUpdateNetwork에서 거치 상태를 검증합니다.
/// </summary>
public class SpecialEquipmentHolder : NetworkBehaviour, IInteractable
{
    [Header("Holder Settings")]
    [Tooltip("스폰 시 자동으로 생성할 장비 프리팹 (null이면 씬에 미리 배치된 아이템을 OnTrigger로 수령)")]
    [SerializeField] private PickableItem equipmentPrefab;

    [Tooltip("장비가 거치될 위치 Transform (없으면 이 오브젝트 기준)")]
    [SerializeField] private Transform displayPoint;

    [Tooltip("못 기준 아이템이 매달리는 오프셋 (m). 양수 = 위, 음수 = 아래)")]
    [SerializeField] private float hangOffset = -0.1f;

    // ── 네트워크 상태: 거치된 아이템 ID ──────────────────────────
    [Networked] private NetworkId ItemNetworkId { get; set; }

    // ── 로컬 캐시 ────────────────────────────────────────────────
    private PickableItem _currentItem;

    private bool IsNetworkReady => Object != null && Object.IsValid;

    private Vector3 HomePosition =>
        (displayPoint != null ? displayPoint.position : transform.position)
        + Vector3.up * hangOffset;

    // ── Unity 생명주기 ────────────────────────────────────────────

    private void Awake()
    {
        if (displayPoint == null)
            displayPoint = transform;
    }

    // ── Fusion 생명주기 ───────────────────────────────────────────

    public override void Spawned()
    {
        if (!HasStateAuthority) return;
        if (equipmentPrefab == null) return;

        NetworkObject spawned = Runner.Spawn(
            equipmentPrefab.gameObject,
            HomePosition,
            Quaternion.identity
        );

        if (spawned == null) return;

        PickableItem item = spawned.GetComponent<PickableItem>();
        if (item != null) PlaceOnHolder(item);
    }

    /// <summary>
    /// StateAuthority: 거치된 아이템이 다른 플레이어에게 집혔는지 매 틱 검증합니다.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (ItemNetworkId == default) return;

        NetworkObject obj = Runner?.FindObject(ItemNetworkId);
        if (obj == null)
        {
            ItemNetworkId = default;
            _currentItem  = null;
            return;
        }

        PickableItem item = obj.GetComponent<PickableItem>();
        if (item == null || !item.IsOnStation || item.StationId != Object.Id)
        {
            ItemNetworkId = default;
            _currentItem  = null;
        }
    }

    /// <summary>
    /// 모든 클라이언트: ItemNetworkId에서 _currentItem을 매 렌더 프레임 동기화합니다.
    /// </summary>
    public override void Render()
    {
        base.Render();
        SyncItemFromNetworkId();
    }

    // ── 로컬(Sandbox) 대응 ────────────────────────────────────────

    private void Start()
    {
        if (IsNetworkReady) return;
        if (equipmentPrefab == null) return;

        GameObject spawned = Instantiate(
            equipmentPrefab.gameObject,
            HomePosition,
            Quaternion.identity
        );

        PickableItem item = spawned.GetComponent<PickableItem>();
        if (item != null) PlaceOnHolder(item);
    }

    private void Update()
    {
        if (IsNetworkReady) return;

        // 로컬: 아이템이 집혀갔으면 캐시 클리어
        if (_currentItem != null && !_currentItem.IsOnStation)
            _currentItem = null;
    }

    // ── 물리 트리거: 반납된 장비 자동 거치 ───────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (_currentItem != null) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item == null || item.IsPhysicallyHeld) return;
        if (!IsMatchingEquipment(item)) return;

        PlaceOnHolder(item);
    }

    // ── IInteractable ─────────────────────────────────────────────

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return false;

        bool isWearable = equipmentPrefab is IWearable;

        if (_currentItem != null)
        {
            // 홀더에 장비 있음 → 착용/집기 가능 여부
            return isWearable ? !player.IsWearingItem : !player.IsActiveHandFull();
        }
        else
        {
            // 홀더 비어있음 → 반납 가능 여부
            PickableItem returning = isWearable ? player.WornItem : player.GetActiveHandItem();
            return returning != null && IsMatchingEquipment(returning);
        }
    }

    public void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return;

        bool isWearable = equipmentPrefab is IWearable;

        if (_currentItem != null)
        {
            // 장비 착용 / 집기
            PickableItem item = _currentItem;
            _currentItem = null;

            if (IsNetworkReady && HasStateAuthority)
                ItemNetworkId = default;

            if (isWearable)
                player.WearItem(item);
            else
                player.PickupItem(item);
        }
        else
        {
            // 장비 반납
            PickableItem returning = isWearable ? player.WornItem : player.GetActiveHandItem();
            if (returning == null || !IsMatchingEquipment(returning)) return;

            Vector3 pos = HomePosition;

            if (isWearable)
            {
                player.TakeOffItem();
            }
            else
            {
                returning.SetHeldLayer(false);
                player.ClearActiveHandSlot();
            }

            returning.LocalDrop(pos);
            if (returning.Object != null && returning.Object.IsValid)
                returning.Rpc_Drop(pos);

            PlaceOnHolder(returning);
        }
    }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return "";

        bool isWearable = equipmentPrefab is IWearable;

        if (_currentItem != null)
        {
            bool canTake = isWearable ? !player.IsWearingItem : !player.IsActiveHandFull();
            if (canTake)
                return $"[거치대]\n[E] {_currentItem.itemName} {(isWearable ? "착용" : "집기")}";
        }
        else
        {
            PickableItem returning = isWearable ? player.WornItem : player.GetActiveHandItem();
            if (returning != null && IsMatchingEquipment(returning))
                return $"[거치대]\n[E] {returning.itemName} 반납";
        }

        return "";
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────

    private void PlaceOnHolder(PickableItem item)
    {
        _currentItem = item;
        Vector3 pos  = HomePosition;

        if (IsNetworkReady)
        {
            item.Rpc_PutOnStation(Object.Id, pos);
            if (HasStateAuthority && item.Object != null && item.Object.IsValid)
                ItemNetworkId = item.Object.Id;
        }
        else
        {
            item.LocalPutOnStation(pos);
        }
    }

    private void SyncItemFromNetworkId()
    {
        if (!IsNetworkReady) return;

        if (ItemNetworkId == default)
        {
            _currentItem = null;
            return;
        }

        // 이미 올바른 아이템이 캐시되어 있으면 스킵
        if (_currentItem != null &&
            _currentItem.Object != null &&
            _currentItem.Object.Id == ItemNetworkId)
            return;

        NetworkObject obj = Runner?.FindObject(ItemNetworkId);
        _currentItem = obj != null ? obj.GetComponent<PickableItem>() : null;
    }

    /// <summary>
    /// 프리팹과 같은 타입의 아이템인지 확인합니다.
    /// equipmentPrefab이 null이면 모든 PickableItem을 허용합니다.
    /// </summary>
    private bool IsMatchingEquipment(PickableItem item)
    {
        if (item == null) return false;
        if (equipmentPrefab == null) return true;
        return item.GetType() == equipmentPrefab.GetType();
    }
}
