using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Interactions;

[RequireComponent(typeof(Rigidbody))]
public class PickableItem : NetworkBehaviour, IInteractable
{
    public string itemName = "Object";

    // ── 물리 컴포넌트 ──────────────────────────────────────────
    private Rigidbody rb;
    private Collider[] cols;
    private Outline outline;
    private Transform originalParent;

    // ── Networked 상태 (모든 클라이언트 동기화) ────────────────
    [Networked] public ItemState State { get; set; } = ItemState.Free;
    private ItemState _localState = ItemState.Free; // Sandbox fallback
    public ItemState CurrentState => IsNetworkReady ? State : _localState;

    [Networked] public NetworkId HolderId { get; set; }
    private NetworkId _localHolderId = default;
    public NetworkId CurrentHolderId => IsNetworkReady ? HolderId : _localHolderId;

    [Networked] public NetworkId StationId { get; set; }
    private NetworkId _localStationId = default;
    public NetworkId CurrentStationId => IsNetworkReady ? StationId : _localStationId;

    [Networked] public int HeldHandIndex { get; set; }  // 어느 손에 들었는지 동기화

    [Networked] public Vector3 WorldPosition { get; set; }
    private Vector3 _localWorldPosition = Vector3.zero;
    public Vector3 CurrentWorldPosition => IsNetworkReady ? WorldPosition : _localWorldPosition;

    // ── 편의 프로퍼티 ─────────────────────────────────────────
    public bool isHeld      => CurrentState == ItemState.Held;
    public bool IsOnStation => CurrentState == ItemState.OnStation;
    public bool IsNetworkReady => Object != null && Object.IsValid;

    /// <summary>
    /// 로컬 상태 기준으로 "손에 들려있는지" 판단합니다.
    /// LocalDrop() 호출 직후처럼 네트워크 State가 아직 Held인 상태에서도
    /// 물리적으로 내려놓인 아이템을 올바르게 판단합니다.
    /// OnTriggerEnter 등 물리 이벤트에서 사용하세요.
    /// </summary>
    public bool IsPhysicallyHeld => _localState == ItemState.Held;

    /// <summary>스테이션 OnTriggerEnter 자동 픽업 허용 여부. 서브클래스에서 override 가능.</summary>
    public virtual bool CanBePickedUpByStation => true;

    /// <summary>샌드박스 모드에서 올려진 스테이션 참조 (네트워크 모드에서는 StationId 사용).</summary>
    public IInteractable LocalStationRef { get; set; }

    // ── ISliceable 대체 virtual 메서드 (구 ISliceable 아이템은 override 해서 사용) ──
    /// <summary>도마에서 칼로 자를 수 있는지 여부. 기본값 false.</summary>
    public virtual bool CanChop => false;
    /// <summary>도마에서 칼로 잘릴 때 호출됩니다.</summary>
    public virtual void Chop(CuttingStation board) { }

    // ── ICuttable 기본 구현 (서브클래스가 ICuttable 선언 시 자동 충족) ──
    [Header("Guideline Settings")]
    public float guideSelectRadius = 80f;

    private CutGuidelineVisual[] _activeGuides;
    private string _activeToolType;
    private Camera _focusCamera;
    public int SelectedGuidelineIndex { get; private set; } = -1;

    public virtual void ShowGuidelines(string toolType)
    {
        if (this is not ICuttable processable) return;
        if (_activeToolType == toolType && _activeGuides != null) return;
        HideGuidelines();
        _activeToolType = toolType;
        _activeGuides = processable.GetGuidelineVisuals(toolType);
        if (_activeGuides.Length == 0) { _activeGuides = null; _activeToolType = null; return; }
        foreach (var g in _activeGuides) g.SetVisible(true);
    }

    public virtual void HideGuidelines()
    {
        if (_activeGuides != null)
            foreach (var g in _activeGuides)
                if (g != null) g.SetVisible(false);
        _activeGuides = null;
        _activeToolType = null;
        SelectedGuidelineIndex = -1;
    }

    public virtual void UpdateGuidelineHighlight()
    {
        if (_activeGuides == null || _activeGuides.Length == 0) return;
        Camera cam = _focusCamera != null ? _focusCamera : Camera.main;
        if (cam == null) return;

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float minDist = float.MaxValue;
        int newSelected = -1;

        for (int i = 0; i < _activeGuides.Length; i++)
        {
            if (_activeGuides[i] == null) continue;
            float dist = _activeGuides[i].ScreenDistanceToCenter(cam, screenCenter);
            if (dist < minDist) { minDist = dist; newSelected = i; }
        }

        if (newSelected != SelectedGuidelineIndex)
        {
            if (SelectedGuidelineIndex >= 0 && SelectedGuidelineIndex < _activeGuides.Length)
                _activeGuides[SelectedGuidelineIndex].SetHighlight(false);
            if (newSelected >= 0)
                _activeGuides[newSelected].SetHighlight(true);
            SelectedGuidelineIndex = newSelected;
        }
    }

    // ── 로컬 상태 ─────────────────────────────────────────────
    private Transform _heldByTransform;   // 손에 들려있을 때의 손 Transform
    private Vector3 _renderWorldPosition; // OnStation 보간용 렌더 위치
    private Vector3 _originalLocalScale;  // 스케일 복원용 (bone lossyScale 부동소수점 오차 방지)

    [Header("Item Properties")]
    public Dictionary<string, string> metadata = new Dictionary<string, string>();

    /// <summary>이 재료가 거쳐온 조리 단계 목록. 채점에 사용됩니다.</summary>
    public List<string> cookingSeq = new List<string>();

    /// <summary>총 누적 조리 시간 (초).</summary>
    public float cookTime = 0f;

    /// <summary>
    /// 조리 상태 변화를 cookingSeq에 기록합니다.
    /// CurrentCookState setter에서 상태가 실제로 바뀔 때 호출하세요.
    /// </summary>
    protected void RecordCookStateChange(CookState oldState, CookState newState)
    {
        if (oldState == newState) return;
        if (newState == CookState.Raw) return;
        if (IsNetworkReady && !HasStateAuthority) return;

        // "가열" (또는 가열 작업) 내역을 기록합니다. 중복 기록되지 않게 합니다.
        if (newState == CookState.Cooked || newState == CookState.Burned)
        {
            if (!cookingSeq.Contains("가열"))
                cookingSeq.Add("가열");
        }
    }

    /// <summary>세척 완료 시 cookingSeq에 기록합니다. 중복 기록하지 않습니다.</summary>
    protected void RecordWashed()
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (!cookingSeq.Contains("세척"))
            cookingSeq.Add("세척");
    }

    // ─────────────────────────────────────────────────────────
    protected virtual void Awake()
    {
        rb   = GetComponent<Rigidbody>();
        cols = GetComponentsInChildren<Collider>();
        originalParent = transform.parent;
        _originalLocalScale = transform.localScale;
        
        if (originalParent == null)
        {
            GameObject folder = GameObject.Find("InteractionObjects");
            if (folder != null) originalParent = folder.transform;
            else
            {
                folder = GameObject.Find("Items");
                if (folder != null) originalParent = folder.transform;
            }
        }

        outline = GetComponent<Outline>();
        if (outline == null)
        {
            outline = gameObject.AddComponent<Outline>();
            outline.OutlineMode  = Outline.Mode.OutlineAll;
            outline.OutlineColor = Color.yellow;
            outline.OutlineWidth = 3f;
        }
        outline.enabled = false;
    }

    public override void Render()
    {
        // [핵심 수정] 로컬 상태(_localState)가 Held면, 네트워크 State가 아직 OnStation이더라도
        // 위치 보정을 건너뜁니다. RequestStateAuthority()는 비동기라 과도기 동안
        // OnStation 케이스가 손에 든 아이템을 스테이션으로 당기는 현상을 막습니다.
        if (_localState == ItemState.Held)
        {
            // 내가 들고 있는 경우: SetParent로 이미 손을 따라가므로 처리 불필요
            if (_heldByTransform != null) return;

            // 관찰자: 다른 플레이어가 들고 있는 경우 손 위치 추적
            if (Runner != null)
            {
                NetworkObject playerObj = Runner.FindObject(CurrentHolderId);
                if (playerObj != null)
                {
                    var hands = playerObj.GetComponentInChildren<CookingMasterHandsManager>();
                    if (hands != null)
                    {
                        Transform handTf = HeldHandIndex == 2
                            ? (hands.wearPoint != null ? hands.wearPoint : hands.transform)
                            : (HeldHandIndex == 0 ? hands.leftHandPoint : hands.rightHandPoint);
                        if (handTf != null)
                        {
                            transform.position = handTf.position;
                            transform.rotation = handTf.rotation;
                        }
                    }
                }
            }
            return;
        }

        switch (CurrentState)
        {
            case ItemState.Held:
                if (_heldByTransform != null) break;
                if (Runner != null)
                {
                    NetworkObject playerObj = Runner.FindObject(CurrentHolderId);
                    if (playerObj != null)
                    {
                        var hands = playerObj.GetComponentInChildren<CookingMasterHandsManager>();
                        if (hands != null)
                        {
                            Transform handTf = HeldHandIndex == 2
                                ? (hands.wearPoint != null ? hands.wearPoint : hands.transform)
                                : (HeldHandIndex == 0 ? hands.leftHandPoint : hands.rightHandPoint);
                            if (handTf != null)
                            {
                                transform.position = handTf.position;
                                transform.rotation = handTf.rotation;
                            }
                        }
                    }
                }
                break;

            case ItemState.OnStation:
                // WorldPosition은 틱마다 갱신되므로 Lerp로 보간하여 끊김 방지
                _renderWorldPosition = Vector3.Lerp(_renderWorldPosition, CurrentWorldPosition, Time.deltaTime * 20f);
                transform.position = _renderWorldPosition;
                break;

            case ItemState.Free:
                if (!HasStateAuthority)
                {
                    if (rb != null && !rb.isKinematic) rb.isKinematic = true;
                    transform.position = Vector3.Lerp(transform.position, CurrentWorldPosition, Time.deltaTime * 15f);
                }
                break;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsNetworkReady) return;

        if (CurrentState == ItemState.Free && HasStateAuthority)
        {
            WorldPosition = transform.position; // 물리 연산 위치 저장
        }
    }

    // ── 비네트워크 샘드박스용 LateUpdate: Render()가 호출되지 않으니 쿄직접 쉽기
    void LateUpdate()
    {
        if (_activeGuides != null) UpdateGuidelineHighlight();

        if (IsNetworkReady) return; // 네트워크 상태에서는 Render()가 담당합니다

        if (CurrentState == ItemState.Held && _heldByTransform != null)
        {
            transform.position = _heldByTransform.position;
            transform.rotation = _heldByTransform.rotation;
        }
        else if (CurrentState == ItemState.OnStation)
        {
            _renderWorldPosition = Vector3.Lerp(_renderWorldPosition, CurrentWorldPosition, Time.deltaTime * 20f);
            transform.position = _renderWorldPosition;
        }
    }

    public void SetHighlight(bool state)
    {
        if (outline != null) outline.enabled = state;
    }

    // ── 상태 전환 API ─────────────────────────────────────────

    /// <summary>로컬에서 즉시 줍기. RPC로 HolderId를 설정해 동기화.</summary>
    public void LocalPickup(Transform handTransform)
    {
        _heldByTransform = handTransform; // 손 위치 저장 (LateUpdate에서 사용)
        _localState = ItemState.Held; // Sandbox fallback

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic    = true;
        rb.useGravity     = false;
        foreach (var c in cols) if (c != null) c.enabled = false;

        // [중요] worldPositionStays: false로 설정하여 손 위치(0,0,0)에 즉시 안착시킵니다.
        // 기존의 worldPositionStays: true 옵션이 칼이 손보다 앞에 떠있는 현상을 유발했을 수 있습니다.
        transform.SetParent(handTransform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void Rpc_Pickup(NetworkId playerNetId, int handIndex = 1)
    {
        Debug.Log($"🟢 [PickableItem] {itemName} Rpc_Pickup 호출됨 (나의 권한: {HasStateAuthority})");
        if (HasStateAuthority)
        {
            Debug.Log($"👑 [PickableItem] {itemName} 권한 소유자로서 State -> Held 변경");
            HolderId      = playerNetId;
            StationId     = default;
            State         = ItemState.Held;
            HeldHandIndex = handIndex;
        }
        _localHolderId  = playerNetId;
        _localStationId = default;
        _localState     = ItemState.Held;

        // 🟢 추가: 해당 플레이어가 '나' 라면, 내 HandsManager의 인벤토리에 이 아이템을 직접 등록해 줍니다!
        if (Runner != null)
        {
            NetworkObject playerObj = Runner.FindObject(playerNetId);
            if (playerObj != null && playerObj.HasStateAuthority)
            {
                var hands = playerObj.GetComponentInChildren<CookingMasterHandsManager>();
                if (hands != null)
                {
                    hands.SetHandItem(this, handIndex);

                    // 로컬 클라이언트에서도 SetParent가 작동해야 버릴 때 정상 Detach가 됩니다.
                    if (_heldByTransform == null)
                    {
                        Transform handTf;
                        if (handIndex == 2)
                            handTf = hands.wearPoint != null ? hands.wearPoint : hands.transform;
                        else
                            handTf = handIndex == 0 ? hands.leftHandPoint : hands.rightHandPoint;
                        if (handTf != null) transform.SetParent(handTf);
                    }
                    
                    Debug.Log($"[PickableItem] {itemName} 이 내 손 인벤토리에 등록되었습니다! (Hand: {handIndex})");
                }
            }
        }

        if (HasStateAuthority)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic    = true;
            rb.useGravity     = false;
        }
        foreach (var c in cols) if (c != null) c.enabled = false;
    }

    /// <summary>로컬에서 즉시 내려놓기.</summary>
    public void LocalDrop(Vector3 dropPosition)
    {
        HideGuidelines();
        _heldByTransform = null;
        _localState = ItemState.Free;
        LocalStationRef = null;
        transform.SetParent(originalParent);
        transform.localScale = _originalLocalScale;
        transform.position = dropPosition;

        rb.isKinematic = false;
        rb.useGravity  = true;
        rb.linearVelocity = Vector3.zero;   // 잔류 속도 초기화 (날아가는 현상 방지)
        rb.angularVelocity = Vector3.zero;
        foreach (var c in cols) if (c != null) { c.enabled = true; c.isTrigger = false; }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void Rpc_Drop(Vector3 dropPosition)
    {
        Debug.Log($"🟢 [PickableItem] {itemName} Rpc_Drop 호출됨 (나의 권한: {HasStateAuthority})");
        if (HasStateAuthority)
        {
            Debug.Log($"👑 [PickableItem] {itemName} 권한 소유자로서 State -> Free 변경");
            HolderId  = default;
            StationId = default;
            State     = ItemState.Free;
        }
        _localHolderId  = default;
        _localStationId = default;
        _localState     = ItemState.Free;
        _heldByTransform = null;
        LocalStationRef = null;

        transform.SetParent(originalParent);
        transform.localScale = _originalLocalScale;
        transform.position = dropPosition;
        if (HasStateAuthority)
        {
            rb.isKinematic = false;
            rb.useGravity  = true;
        }
        foreach (var c in cols) if (c != null) { c.enabled = true; c.isTrigger = false; }
    }

    /// <summary>스테이션이 아이템을 가져갈 때 호출.</summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void Rpc_PutOnStation(NetworkId stationNetId, Vector3 position)
    {
        Debug.Log($"🟢 [PickableItem] {itemName} Rpc_PutOnStation 호출됨 (나의 권한: {HasStateAuthority})");
        if (HasStateAuthority)
        {
            Debug.Log($"👑 [PickableItem] {itemName} 권한 소유자로서 State -> OnStation 변경");
            StationId     = stationNetId;
            HolderId      = default;
            State         = ItemState.OnStation;
            WorldPosition = position;
        }
        _localStationId   = stationNetId;
        _localHolderId      = default;
        _localState         = ItemState.OnStation;
        _localWorldPosition = position;
        _renderWorldPosition = position; // 보간 시작점 초기화 (순간이동 방지)

        transform.SetParent(originalParent); // 스테이션에 있을 때도 씬 관리용 부모 유지
        transform.localScale = _originalLocalScale;
        if (HasStateAuthority)
        {
            rb.isKinematic    = true;
            rb.useGravity     = false;
            rb.linearVelocity = Vector3.zero;
        }
        transform.position = position;
        foreach (var c in cols) if (c != null) { c.enabled = true; c.isTrigger = true; }
    }

    public void LocalPutOnStation(Vector3 position)
    {
        if (IsNetworkReady && HasStateAuthority)
        {
            StationId     = default;
            HolderId      = default;
            State         = ItemState.OnStation;
            WorldPosition = position;
        }
        _localStationId     = default;
        _localHolderId      = default;
        _localState         = ItemState.OnStation;
        _localWorldPosition = position;

        transform.SetParent(originalParent);
        transform.localScale = _originalLocalScale;
        rb.isKinematic    = true;
        rb.useGravity     = false;
        rb.linearVelocity = Vector3.zero;
        transform.position = position;
        foreach (var c in cols) if (c != null) { c.enabled = true; c.isTrigger = true; }
    }

    /// <summary>스테이션 위치 갱신 (매 FixedUpdateNetwork에서 스테이션이 호출).</summary>
    public void SetStationPosition(Vector3 position)
    {
        if (CurrentState != ItemState.OnStation) return;
        
        if (IsNetworkReady && HasStateAuthority)
        {
            WorldPosition = position;
        }
        _localWorldPosition = position;

        if (!IsNetworkReady) transform.position = position;
    }

    public void LocalThrow(Vector3 dropPosition, Vector3 velocity, Vector3 angularVelocity)
    {
        LocalDrop(dropPosition);
        if (rb != null)
        {
            rb.linearVelocity  = velocity;
            rb.angularVelocity = angularVelocity;
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_Throw(Vector3 dropPosition, Vector3 velocity, Vector3 angularVelocity)
    {
        if (HasStateAuthority)
        {
            HolderId  = default;
            StationId = default;
            State     = ItemState.Free;
        }
        _localHolderId = default;
        _localStationId = default;
        _localState = ItemState.Free;

        LocalThrow(dropPosition, velocity, angularVelocity);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_RequestDespawn()
    {
        if (Object != null && Object.IsValid)
            Runner.Despawn(Object);
    }

    public void CopyPropertiesFrom(PickableItem original)
    {
        this.itemName = original.itemName;
    }

    /// <summary>
    /// 절단/처리 후 결과물에 자신의 상태를 전달합니다.
    /// 서브클래스에서 override하여 CookState 등 고유 상태를 복사하세요.
    /// </summary>
    public virtual void OnFocus(CookingMasterHandsManager player)
    {
        if (this is not ICuttable) return;
        if (!IsOnStation) return;

        _focusCamera = player.mainCamera;

        PickableItem held = player.GetActiveHandItem();
        if (held is ITool tool) ShowGuidelines(tool.ToolType);
        else HideGuidelines();
    }

    public virtual void OnFocusLost()
    {
        HideGuidelines();
    }

    public virtual void TransferStateTo(PickableItem target) { }

    /// <summary>절단 직전 서브클래스가 반응할 수 있는 훅. Rpc_Cut / LocalCut 양쪽에서 호출됩니다.</summary>
    protected virtual void OnCutExecuted(CutGuidelineVisual guide) { }

    /// <summary>네트워크: 가이드라인 기반 절단. StateAuthority가 실행합니다.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_Cut(int guideIndex, string toolType)
    {
        if (this is not ICuttable processable) return;

        CutGuidelineVisual[] guides = processable.GetGuidelineVisuals(toolType);
        if (guideIndex < 0 || guideIndex >= guides.Length) return;

        CutGuidelineVisual guide = guides[guideIndex];
        OnCutExecuted(guide);

        // 절단 기록
        string cutName = guide.cutName;
        if (!string.IsNullOrEmpty(cutName))
        {
            cookingSeq.Add(cutName);
            metadata["cutMethod"] = cutName;
        }

        Vector3 basePos = transform.position;
        int total = guide.TotalResultCount();
        int spawnIdx = 0;

        for (int i = 0; i < guide.resultPrefabs.Length; i++)
        {
            if (guide.resultPrefabs[i] == null) continue;
            int count = (i < guide.resultCounts.Length) ? guide.resultCounts[i] : 1;

            for (int j = 0; j < count; j++)
            {
                float xOff = (spawnIdx - (total - 1) * 0.5f) * 0.2f;
                Vector3 spawnPos = basePos + transform.right * xOff + Vector3.up * 0.3f;
                NetworkObject spawned = Runner.Spawn(guide.resultPrefabs[i], spawnPos, Quaternion.identity);
                if (spawned != null)
                {
                    PickableItem spawnedItem = spawned.GetComponent<PickableItem>();
                    TransferStateTo(spawnedItem);
                    if (spawnedItem != null)
                    {
                        spawnedItem.cookingSeq = new List<string>(cookingSeq);
                        foreach (var kv in metadata)
                            spawnedItem.metadata[kv.Key] = kv.Value;
                    }
                }
                spawnIdx++;
            }
        }

        Runner.Despawn(Object);
    }

    /// <summary>샌드박스: 가이드라인 기반 절단.</summary>
    public void LocalCut(int guideIndex, string toolType)
    {
        if (this is not ICuttable processable) return;

        CutGuidelineVisual[] guides = processable.GetGuidelineVisuals(toolType);
        if (guideIndex < 0 || guideIndex >= guides.Length) return;

        CutGuidelineVisual guide = guides[guideIndex];
        OnCutExecuted(guide);

        // 절단 기록
        string cutName = guide.cutName;
        if (!string.IsNullOrEmpty(cutName))
        {
            cookingSeq.Add(cutName);
            metadata["cutMethod"] = cutName;
        }

        Vector3 basePos = transform.position;
        int total = guide.TotalResultCount();
        int spawnIdx = 0;

        for (int i = 0; i < guide.resultPrefabs.Length; i++)
        {
            if (guide.resultPrefabs[i] == null) continue;
            int count = (i < guide.resultCounts.Length) ? guide.resultCounts[i] : 1;

            for (int j = 0; j < count; j++)
            {
                float xOff = (spawnIdx - (total - 1) * 0.5f) * 0.2f;
                Vector3 spawnPos = basePos + transform.right * xOff + Vector3.up * 0.3f;
                GameObject spawned = Instantiate(guide.resultPrefabs[i].gameObject, spawnPos, Quaternion.identity);
                PickableItem spawnedItem = spawned.GetComponent<PickableItem>();
                TransferStateTo(spawnedItem);
                if (spawnedItem != null)
                {
                    spawnedItem.cookingSeq = new List<string>(cookingSeq);
                    foreach (var kv in metadata)
                        spawnedItem.metadata[kv.Key] = kv.Value;
                }
                spawnIdx++;
            }
        }

        Destroy(gameObject);
    }

    // ── 스테이션 물리 트래킹 (Free 상태 아이템의 스테이션 위임용) ─
    private GeneralStation _currentStation;

    protected virtual void OnTriggerEnter(Collider other)
    {
        var gs = other.GetComponentInParent<GeneralStation>();
        if (gs != null) _currentStation = gs;
    }

    protected virtual void OnTriggerExit(Collider other)
    {
        if (_currentStation != null && other.GetComponentInParent<GeneralStation>() == _currentStation)
            _currentStation = null;
    }

    // ── IInteractable ─────────────────────────────────────────

    private bool IsOnCuttingStation()
    {
        if (!IsOnStation) return false;
        if (!IsNetworkReady) return LocalStationRef is CuttingStation;
        NetworkObject stationObj = Runner.FindObject(StationId);
        return stationObj != null && stationObj.GetComponent<CuttingStation>() != null;
    }

    public virtual bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary)
            return !isHeld && !player.IsActiveHandFull();
        if (type == InteractionType.Secondary && !isHeld && !(this is IServable))
        {
            PickableItem held = player.GetActiveHandItem();
            return held is SkeweredItem skewer && skewer.CanSkewer(this);
        }
        if (type == InteractionType.UseItem && IsOnCuttingStation() && this is ICuttable processable)
        {
            PickableItem held = player.GetActiveHandItem();
            return held is ITool tool && processable.GetGuidelineVisuals(tool.ToolType).Length > 0;
        }
        return false;
    }

    public virtual void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary)
        {
            // [수정] 즉석 재스폰 대상(도구/빈 접시)이 스테이션 범위 내에 있다면, 스테이션에게 상호작용을 위임합니다.
            // 아이템의 상태가 OnStation이 아닌 Free(물리 활성) 상태여도 스테이션의 툴 재스폰 로직을 태우기 위함입니다.
            if (IsNetworkReady && (this is ITool || (this is PlateItem p && p.IsEmpty)))
            {
                GeneralStation station = _currentStation;
                if (station == null && IsOnStation)
                {
                    NetworkObject sObj = Runner.FindObject(StationId);
                    if (sObj != null) station = sObj.GetComponent<GeneralStation>();
                }

                if (station != null && station.CanInteract(player, type))
                {
                    station.Interact(player, type);
                    return;
                }
            }

            player.PickupItem(this);
        }
        else if (type == InteractionType.Secondary && !isHeld)
        {
            PickableItem held = player.GetActiveHandItem();
            if (held is SkeweredItem skewer && skewer.CanSkewer(this))
                skewer.TrySkewer(this);
        }
        else if (type == InteractionType.UseItem && IsOnCuttingStation() && this is ICuttable)
        {
            PickableItem held = player.GetActiveHandItem();
            if (held is not ITool tool) return;
            if (SelectedGuidelineIndex < 0) return;

            if (IsNetworkReady) Rpc_Cut(SelectedGuidelineIndex, tool.ToolType);
            else LocalCut(SelectedGuidelineIndex, tool.ToolType);
        }
    }

    public virtual string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary && CanInteract(player, type))
            return $"[{itemName}]\n[{CookingMasterHandsManager.PickupKey}] 집기";
        if (type == InteractionType.Secondary && !isHeld && !(this is IServable))
        {
            PickableItem held = player.GetActiveHandItem();
            if (held is SkeweredItem skewer && skewer.CanSkewer(this))
                return $"[{itemName}]\n[F] 꼬치에 끼우기 ({skewer.Count}/{skewer.MaxCount})";
        }
        if (type == InteractionType.UseItem && IsOnCuttingStation() && this is ICuttable processable)
        {
            PickableItem held = player.GetActiveHandItem();
            if (held is ITool tool && processable.GetGuidelineVisuals(tool.ToolType).Length > 0)
                return SelectedGuidelineIndex >= 0 ? $"[{itemName}]\n[L-Click] 썰기" : $"[{itemName}]\n점선에 조준하세요";
        }
        return "";
    }

    public void SetHeldLayer(bool held)
    {
        int layer = LayerMask.NameToLayer(held ? "HeldItem" : "Interactable");
        foreach (var t in GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    // ── 유틸 ────────────────────────────────────────────────
    [ContextMenu("Fit Box Collider to Children")]
    public void FitBoxColliderToChildren()
    {
        BoxCollider boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null) { Debug.LogError("BoxCollider 없음"); return; }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        boxCollider.center = transform.InverseTransformPoint(bounds.center);
        Vector3 s = bounds.size;
        boxCollider.size = new Vector3(s.x / transform.lossyScale.x,
                                       s.y / transform.lossyScale.y,
                                       s.z / transform.lossyScale.z);
    }
}
