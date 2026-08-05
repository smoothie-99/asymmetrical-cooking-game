using UnityEngine;
using Fusion;
using Interactions;
using System.Collections.Generic;

/// <summary>
/// 요리사의 손 상호작용 관리
/// </summary>
public class CookingMasterHandsManager : NetworkBehaviour
{
    [Header("View & Raycast")]
    public Camera mainCamera;
    public float interactionDistance = 3.0f;
    public LayerMask interactableLayer;

    public const string PickupKey = "E";
    public const string InteractionKey = "F";

    [Header("Hand Settings")]
    public Transform leftHandPoint;
    public Transform rightHandPoint;

    [Header("Wear Settings")]
    [Tooltip("귀마개 등 착용 아이템이 붙을 위치 (머리/몸통). 없으면 자동으로 이 Transform 사용")]
    public Transform wearPoint;

    [Header("HUD")]
    public CookingMasterHUD hud;

    private PickableItem leftItem = null;
    private PickableItem rightItem = null;

    // ── 착용 슬롯 (손을 차지하지 않는 장착 아이템) ────────────────
    private PickableItem _wornItem = null;
    public PickableItem WornItem => _wornItem;
    public bool IsWearingItem => _wornItem != null;

    private int activeHandIndex = 1;

    private PickableItem currentLookItem = null;
    private IInteractable currentLookInteractable = null;
    private Outline currentOutline = null;

    private CookingMasterMovement movement;

    private IInteractable _prevLookInteractable;
    private PickableItem _prevLookItem;
    private PickableItem _prevLeftItem, _prevRightItem;
    private int _prevActiveHandIndex = -1;

    private bool IsNetworkReady => Object != null && Object.IsValid;

    void Awake()
    {
        if (mainCamera == null) mainCamera = GetComponentInChildren<Camera>();
        movement = GetComponentInParent<CookingMasterMovement>();
        interactableLayer = LayerMask.GetMask("Default", "Interactable");
        FindHandPointsIfNeeded();

        hud = GetComponentInChildren<CookingMasterHUD>(true);
        if (hud == null)
        {
            hud = gameObject.AddComponent<CookingMasterHUD>();
            Debug.LogWarning("[HandsManager] CookingMasterHUD가 없어 자동 추가됨. CookingMaster.prefab의 Camera 오브젝트에 수동으로 추가 후 저장하세요.");
        }
    }

    public override void Spawned()
    {
        // [중요] 내가 조종하는 캐릭터가 아니라면 HUD를 생성하지 않거나 숨겨야 합니다.
        if (!HasStateAuthority)
        {
            if (hud != null)
            {
                hud.SetVisibility(false);
                hud.enabled = false;
            }
            enabled = false;
            return;
        }

        // [추가] Awake 타이밍에 연산 및 순서상 카메라를 못 찾았을 수 있으므로 다시 확인합니다.
        if (mainCamera == null)
        {
            mainCamera = GetComponentInChildren<Camera>();
            if (mainCamera != null) Debug.Log("🎥 [HandsManager] Spawned() 시점에 카메라를 성공적으로 찾았습니다.");
        }

        // [추가] 이제 로컬 플레이어인 경우에만 정식으로 HUD 캔버스를 생성합니다.
        if (hud != null)
        {
            hud.BuildHUD();
        }
    }

    private void FindHandPointsIfNeeded()
    {
        if (leftHandPoint == null || rightHandPoint == null)
        {
            Transform[] allChildren = GetComponentsInChildren<Transform>(true);
            foreach (var t in allChildren)
            {
                if (leftHandPoint == null && t.name.Contains("LeftHandPoint")) leftHandPoint = t;
                if (rightHandPoint == null && t.name.Contains("RightHandPoint")) rightHandPoint = t;
            }
        }
    }

    public void ApplySleepEffect(float duration)
    {
        if (movement != null) movement.ApplySleepEffect(duration);
    }

    private bool IsStunned => movement != null && movement.StunTimer > 0f;

    private void OnEnable()
    {
        if (SystemManager.Instance != null)
            SystemManager.Instance.OnMetaPhaseEntered += HandleMetaStateChanged;
        
        if (GamePlayManager.Instance != null)
            GamePlayManager.Instance.OnCookingPhaseEntered += HandleCookingPhaseChanged;
    }

    private void OnDisable()
    {
        if (SystemManager.Instance != null)
            SystemManager.Instance.OnMetaPhaseEntered -= HandleMetaStateChanged;
            
        if (GamePlayManager.Instance != null)
            GamePlayManager.Instance.OnCookingPhaseEntered -= HandleCookingPhaseChanged;
    }

    private void HandleMetaStateChanged(MetaState state) => UpdateHUDVisibility();
    private void HandleCookingPhaseChanged(CookingState phase) => UpdateHUDVisibility();

    void Update()
    {
        UpdateHUDVisibility();

        if (IsStunned)
        {
            ClearLookTarget();
            FlushHUDIfDirty();
            return;
        }

        if (SystemManager.Instance != null)
        {
            MetaState meta = SystemManager.Instance.CurrentMetaState;
            if (meta != MetaState.Cooking)
            {
                ClearLookTarget();
                FlushHUDIfDirty();
                return;
            }
        }

        if (GamePlayManager.Instance != null)
        {
            CookingState cookState = GamePlayManager.Instance.CurrentCookingState;
            bool canInteract = (cookState == CookingState.Cooking);
            if (InGameMenuUI.IsMenuOpen || !canInteract)
            {
                ClearLookTarget();
                FlushHUDIfDirty();
                return;
            }
        }

        HandleInputs();
        CheckLookTarget();
        FlushHUDIfDirty();
    }

    private void UpdateHUDVisibility()
    {
        if (hud == null) return;

        // 1. 전체 게임이 요리 단계인가?
        bool isMetaCooking = (SystemManager.Instance != null && SystemManager.Instance.CurrentMetaState == MetaState.Cooking);
        
        // 2. 실제 카운트다운이 끝나고 요리가 진행 중인가?
        bool isActualCooking = (GamePlayManager.Instance != null && GamePlayManager.Instance.CurrentCookingState == CookingState.Cooking);
        
        // 3. 메뉴가 닫혀 있는가?
        bool isMenuClosed = !InGameMenuUI.IsMenuOpen;
        
        // 모든 조건이 맞을 때만 HUD를 활성화합니다. (카운트다운 중이거나 결과창에서는 꺼짐)
        hud.SetVisibility(isMetaCooking && isActualCooking && isMenuClosed);
    }

    private void HandleInputs()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) { activeHandIndex = 0; }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { activeHandIndex = 1; }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f) { activeHandIndex = (activeHandIndex == 0) ? 1 : 0; }

        if (Input.GetKeyDown(KeyCode.Q)) { OnThrowRequested(); }
    }

    private void CheckLookTarget()
    {
        if (mainCamera == null) return;

        Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);

        // RaycastAll로 모든 충돌을 수집한 뒤 PickableItem(음식/도구)을 스테이션보다 우선시
        RaycastHit[] hits = Physics.RaycastAll(ray, interactionDistance, interactableLayer, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        GameObject bestHitObj        = null;
        PickableItem bestItem        = null;
        IInteractable bestInteractable = null;

        // 1차 패스: 손에 들려있지 않은 PickableItem 우선 검색
        foreach (var h in hits)
        {
            PickableItem item = h.collider.GetComponentInParent<PickableItem>();
            if (item != null && !item.isHeld)
            {
                bestHitObj       = h.collider.gameObject;
                bestItem         = item;
                bestInteractable = h.collider.GetComponentInParent<IInteractable>();
                break;
            }
        }

        // 2차 패스: PickableItem이 없으면 가장 가까운 IInteractable(스테이션 등) 사용
        if (bestHitObj == null)
        {
            foreach (var h in hits)
            {
                IInteractable interactable = h.collider.GetComponentInParent<IInteractable>();
                if (interactable != null)
                {
                    bestHitObj       = h.collider.gameObject;
                    bestInteractable = interactable;
                    bestItem         = null;
                    break;
                }
            }
        }

        if (bestHitObj != null)
        {
            UpdateOutline(bestHitObj, bestItem, bestInteractable);

            currentLookItem        = bestItem;
            currentLookInteractable = bestInteractable;

            currentLookInteractable?.OnFocus(this);

            if (Input.GetKeyDown(KeyCode.E)) HandleInteraction(InteractionType.Primary);
            if (Input.GetKeyDown(KeyCode.F)) HandleInteraction(InteractionType.Secondary);
            if (Input.GetMouseButtonDown(0)) HandleInteraction(InteractionType.UseItem);
        }
        else
        {
            ClearLookTarget();
        }
    }

    private void HandleInteraction(InteractionType type)
    {
        bool holdingOilBacon = HasItemInEitherHand<OilBaconItem>();

        // 1) 현재 정면 대상 우선
        if (currentLookInteractable != null)
        {
            // 기름 베이컨을 들고 있으면 "직접 아이템 줍기"만 차단
            // 스테이션 상호작용은 허용
            if (type == InteractionType.Primary && holdingOilBacon && currentLookInteractable is PickableItem)
            {
                return;
            }

            if (currentLookInteractable.CanInteract(this, type))
            {
                currentLookInteractable.Interact(this, type);

                bool isItem = currentLookInteractable is PickableItem;
                if (type == InteractionType.Primary && isItem) ClearLookTarget();
                return;
            }
        }

        // 2) 스테이션이 우선권을 가졌지만, 아이템 자체의 기본 줍기 기능을 다시 시도하는 fallback
        if (type == InteractionType.Primary && currentLookItem != null)
        {
            IInteractable itemInt = currentLookItem as IInteractable;
            if (itemInt != null && itemInt != currentLookInteractable)
            {
                if (holdingOilBacon)
                    return;

                if (itemInt.CanInteract(this, type))
                {
                    itemInt.Interact(this, type);
                    ClearLookTarget();
                    return;
                }
            }
        }
    }

    private void UpdateOutline(GameObject hitObj, PickableItem item, IInteractable interactable)
    {
        Outline hitOutline = null;
        if (item != null) hitOutline = item.GetComponentInChildren<Outline>();

        if (hitOutline == null && interactable != null && interactable is MonoBehaviour mb)
            hitOutline = mb.GetComponentInChildren<Outline>();

        if (hitOutline == null) hitOutline = hitObj.GetComponentInParent<Outline>();

        if (hitOutline != currentOutline)
        {
            if (currentOutline != null) currentOutline.enabled = false;
            currentOutline = hitOutline;
            if (currentOutline != null) currentOutline.enabled = true;
        }
    }

    private void OnThrowRequested()
    {
        PickableItem itemToThrow = GetActiveHandItem();
        if (itemToThrow == null) return;

        Vector3 throwStartPos = mainCamera.transform.position + mainCamera.transform.forward * 0.5f;

        float throwForce = 6f;
        Vector3 velocity = mainCamera.transform.forward * throwForce + mainCamera.transform.up * 1f;
        Vector3 angularVelocity = new Vector3(Random.Range(-5f, 5f), Random.Range(-5f, 5f), Random.Range(-5f, 5f));

        itemToThrow.SetHeldLayer(false);
        itemToThrow.LocalThrow(throwStartPos, velocity, angularVelocity);
        if (itemToThrow.Object != null) itemToThrow.Rpc_Throw(throwStartPos, velocity, angularVelocity);

        ClearActiveHandSlot();
        Debug.Log($"🚀 [Hands] {itemToThrow.itemName} 던짐!");
    }

    private void OnDropRequested()
    {
        PickableItem itemToDrop = GetActiveHandItem();
        if (itemToDrop == null) return;

        Vector3 dropPosition = GetDropPosition();

        itemToDrop.SetHeldLayer(false);
        itemToDrop.LocalDrop(dropPosition);
        if (itemToDrop.Object != null) itemToDrop.Rpc_Drop(dropPosition);

        ClearActiveHandSlot();
    }

    private Vector3 GetDropPosition()
    {
        Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
        RaycastHit hit;
        float dropDistance = 2.1f; // 에임으로 놓는 위치 (조금 더 멀리)
        Vector3 dropPosition;

        if (Physics.Raycast(ray, out hit, dropDistance))
        {
            // 에임 위치 바닥/벽에 콜라이더가 끼여서 장풍이 나가는 현상 방지: 표면에서 살짝 띄우기 (normal 방향 넓힘)
            dropPosition = hit.point + hit.normal * 0.2f + new Vector3(0, 0.1f, 0);
        }
        else
        {
            // 몸(플레이어 콜라이더)과 겹쳐 폭발하는 현상 방지 차원에서 위치 살짝 올림
            dropPosition = mainCamera.transform.position + mainCamera.transform.forward * dropDistance + new Vector3(0, 0.2f, 0);
        }

        return dropPosition;
    }

    public bool IsActiveHandFull() => activeHandIndex == 0 ? (leftItem != null) : (rightItem != null);
    public PickableItem GetActiveHandItem() => activeHandIndex == 0 ? leftItem : rightItem;

    public void ClearActiveHandSlot()
    {
        if (activeHandIndex == 0) leftItem = null;
        else rightItem = null;
    }

    // 🟢 추가: RPC를 통해 로컬 클라이언트 인벤토리를 업데이트할 수 있게 수신자 제공
    public void SetHandItem(PickableItem item, int handIndex)
    {
        if (handIndex == 2) _wornItem = item;
        else if (handIndex == 0) leftItem = item;
        else rightItem = item;
    }

    public Transform GetActiveHandTransform()
    {
        return activeHandIndex == 0 ? leftHandPoint : rightHandPoint;
    }

    public void PickupItem(PickableItem item)
    {
        if (item == null) return;

        Transform handPoint = GetActiveHandTransform();
        if (handPoint == null) return;

        // 이미 기름 베이컨을 들고 있으면 어떤 아이템도 더 줍지 못함
        if (HasItemInEitherHand<OilBaconItem>())
        {
            return;
        }

        // 기름 베이컨을 줍는 순간 양손의 다른 물건은 전부 떨어뜨림
        if (item is OilBaconItem)
        {
            ForceDropAllItems();
            handPoint = GetActiveHandTransform();
            if (handPoint == null) return;
        }
        else
        {
            // 일반 아이템은 활성 손이 차 있으면 픽업 불가
            if (IsActiveHandFull()) return;
        }

        item.LocalPickup(handPoint);

        if (item.Object != null && item.Object.IsValid)
        {
            NetworkId holderId = (Object != null) ? Object.Id : default;
            item.Rpc_Pickup(holderId, activeHandIndex);
        }

        item.SetHeldLayer(true);

        if (activeHandIndex == 0) leftItem = item;
        else rightItem = item;
    }

    private void ClearLookTarget()
    {
        if (currentOutline != null) { currentOutline.enabled = false; currentOutline = null; }
        currentLookItem = null;
        currentLookInteractable = null;
    }

    private void FlushHUDIfDirty()
    {
        if (hud == null) return;

        bool dirty = currentLookInteractable != _prevLookInteractable
                  || currentLookItem != _prevLookItem
                  || leftItem != _prevLeftItem
                  || rightItem != _prevRightItem
                  || activeHandIndex != _prevActiveHandIndex;

        if (!dirty) return;

        if (_prevLookInteractable != null && _prevLookInteractable != currentLookInteractable)
            _prevLookInteractable.OnFocusLost();

        _prevLookInteractable = currentLookInteractable;
        _prevLookItem = currentLookItem;
        _prevLeftItem = leftItem;
        _prevRightItem = rightItem;
        _prevActiveHandIndex = activeHandIndex;

        bool holdingOilBacon = HasItemInEitherHand<OilBaconItem>();

        string labelE = currentLookInteractable?.GetInteractionLabel(this, InteractionType.Primary) ?? "";

        // 베이컨 소지 중에는 직접 아이템 줍기 안내는 숨김
        if (holdingOilBacon && currentLookInteractable is PickableItem)
            labelE = "";

        string labelF = currentLookInteractable?.GetInteractionLabel(this, InteractionType.Secondary) ?? "";
        string labelL = currentLookInteractable?.GetInteractionLabel(this, InteractionType.UseItem) ?? "";

        string secondaryE = "";
        string secondaryF = "";

        if (currentLookItem != null && (currentLookItem as IInteractable) != currentLookInteractable)
        {
            IInteractable itemInt = currentLookItem as IInteractable;
            secondaryE = itemInt?.GetInteractionLabel(this, InteractionType.Primary) ?? "";
            secondaryF = itemInt?.GetInteractionLabel(this, InteractionType.Secondary) ?? "";

            if (holdingOilBacon)
                secondaryE = "";
        }

        string leftText = leftItem != null ? leftItem.itemName : "비어있음";
        string rightText = rightItem != null ? rightItem.itemName : "비어있음";

        hud.UpdateHUD(
            labelE, labelF, labelL,
            secondaryE, secondaryF,
            leftText, rightText,
            activeHandIndex
        );
    }

    /// <summary>
    /// 양손 중 어느 한 곳에라도 특정 타입의 아이템을 들고 있는지 검사.
    /// 멀티플레이에서 StateAuthority가 다른 클라이언트일 경우를 대비해
    /// 로컬 캐시 실패 시 HolderId 기반 네트워크 검색을 수행합니다.
    /// </summary>
    public bool HasItemInEitherHand<T>() where T : PickableItem
    {
        if ((leftItem != null && leftItem is T) || (rightItem != null && rightItem is T))
            return true;

        if (!IsNetworkReady || Object == null || !Object.IsValid) return false;

        foreach (T item in FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            if (item != null && item.isHeld && item.HolderId == Object.Id)
                return true;
        }

        return false;
    }

    // ── 착용 슬롯 API ─────────────────────────────────────────────

    /// <summary>
    /// 아이템을 착용합니다. 손 슬롯을 차지하지 않으며 wearPoint에 부착됩니다.
    /// </summary>
    public void WearItem(PickableItem item)
    {
        if (item == null || _wornItem != null) return;

        Transform point = wearPoint != null ? wearPoint : transform;
        item.LocalPickup(point);

        // 네트워크 동기화: HeldHandIndex = 2 (착용 슬롯)
        if (item.Object != null && item.Object.IsValid)
        {
            NetworkId holderId = (Object != null) ? Object.Id : default;
            item.Rpc_Pickup(holderId, 2);
        }

        _wornItem = item;
    }

    /// <summary>
    /// 착용 중인 아이템을 벗습니다. 반환된 아이템은 호출자가 처리합니다.
    /// </summary>
    public PickableItem TakeOffItem()
    {
        if (_wornItem == null) return null;
        PickableItem item = _wornItem;
        _wornItem = null;
        return item;
    }

    /// <summary>
    /// 양손의 모든 아이템을 강제로 바닥에 떨어뜨림
    /// </summary>
    private void ForceDropAllItems()
    {
        if (leftItem != null) DropSpecificItem(leftItem, 0);
        if (rightItem != null) DropSpecificItem(rightItem, 1);
    }

    private void DropSpecificItem(PickableItem item, int slotIndex)
    {
        if (item == null) return;

        Vector3 dropPosition = GetDropPosition();
        float sideOffset = slotIndex == 0 ? -0.25f : 0.25f;
        dropPosition += transform.right * sideOffset;

        item.SetHeldLayer(false);
        item.LocalDrop(dropPosition);
        if (item.Object != null) item.Rpc_Drop(dropPosition);

        if (slotIndex == 0) leftItem = null;
        else rightItem = null;
    }

    /// <summary>
    /// 현재 손에 들고 있거나 착용 중인 모든 아이템을 반환합니다.
    /// </summary>
    public IEnumerable<PickableItem> GetAllHeldItems()
    {
        if (leftItem != null) yield return leftItem;
        if (rightItem != null) yield return rightItem;
        if (_wornItem != null) yield return _wornItem;
    }
}