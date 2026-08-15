using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using Interactions;
using UnityEngine;


/// <summary>
/// 재료 하나의 시각 표시 설정 (Inspector에서 재료별로 세부 조정).
/// 0 이하 값은 PlateItem 전역 설정(Plate Visual 헤더)으로 폴백합니다.
/// </summary>
[Serializable]
public class IngredientVisualConfig
{
    [Tooltip("이 설정이 적용될 재료 프리팹 (PickableItem 컴포넌트 타입명으로 매핑)")]
    public GameObject prefab;
    [Tooltip("접시 위에 표시할 복사본 수. 0 이하 → 전역 visualCopiesPerIngredient 사용")]
    public int copies = 0;
    [Tooltip("복사본이 퍼지는 최대 반경(m). 0 이하 → 전역 spreadRadius 사용")]
    public float spreadRadius = 0f;
    [Tooltip("비주얼 스케일 배율. 0 이하 → 전역 visualScale 사용")]
    public float visualScale = 0f;
}

/// <summary>
/// 접시 아이템.
///
/// [규칙]
/// - 내부에 List<PlatedIngredient>를 순서대로 저장
/// - 멀티플레이에서는 StateAuthority가 최종 승인
/// - 실제 접시 상태는 NetworkArray에도 기록하여 late join에도 복원 가능
///
/// 사용 예:
/// plate.TryAddIngredient(somePickableItem);
/// </summary>
public class PlateItem : PickableItem, IServable
{
    private const int MaxPlateIngredients = 16;
    private const int MaxIngredientIdLength = 64;

    [Serializable]
    private struct NetworkPlatedIngredient : INetworkStruct
    {
        public NetworkString<_64> IngredientID;
        public NetworkString<_32> CutMethod;
        public NetworkString<_32> ItemClassName;
        public int CookState;
        public float CookTime; // [추가] 조리 시간 동기화
        public NetworkBool Used;
    }

    [Header("Plate Settings")]
    [SerializeField] private bool consumeSourceObjectOnAdd = true;
    [SerializeField] private string dishType = "Salad";
    public string DishType => dishType;
    public void ClearDish() => ClearPlate();

    [Header("Visual Stacking")]
    [Tooltip("재료가 쌓이기 시작하는 접시 위 기준점 Transform")]
    [SerializeField] private Transform stackAnchor;
    [Tooltip("재료 프리팹 및 개별 시각 설정 목록 — itemClassName이 일치하는 항목의 설정 우선 적용")]
    [SerializeField] private List<IngredientVisualConfig> ingredientVisualConfigs = new List<IngredientVisualConfig>();

    private readonly List<GameObject> _spawnedVisuals        = new List<GameObject>();
    private readonly List<float>      _layerBaseY            = new List<float>(); // 재료 인덱스별 실측 Y 기저
    private readonly List<int>        _ingredientCopyCounts  = new List<int>();   // 재료 인덱스별 복사본 수
    private int _lastSyncedVisualCount = -1;

    [Header("Debug View")]
    [SerializeField] private List<PlatedIngredient> platedIngredients = new List<PlatedIngredient>();

    [Networked]
    private int PlateCountNetworked { get; set; }

    [Networked, Capacity(MaxPlateIngredients)]
    private NetworkArray<NetworkPlatedIngredient> PlateSlots => default;

    private readonly Queue<NetworkId> _pendingNetworkIngredientIds = new Queue<NetworkId>();
    private readonly Queue<PickableItem> _pendingLocalIngredients = new Queue<PickableItem>();
    private bool _clearRequested = false;

    public IReadOnlyList<PlatedIngredient> PlatedIngredients => platedIngredients;

    public int Count => IsNetworkReady ? PlateCountNetworked : platedIngredients.Count;

    public bool IsEmpty => Count <= 0;

    public bool IsFull => Count >= MaxPlateIngredients;

    private void Start()
    {
        itemName = "접시";
        SyncListFromNetworkState();
    }

    // ── IInteractable override ─────────────────────────────────

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        return base.CanInteract(player, type);
    }

    public override void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        base.Interact(player, type);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary)
        {
            PickableItem held = player?.GetActiveHandItem();
            if (held != null && held != this && !IsFull && GetConfigByClassName(held.GetType().Name) != null)
                return $"[{itemName}]\n[Q] {held.itemName} 던져서 담기";
        }
        return base.GetInteractionLabel(player, type);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (isHeld || IsFull) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item == null || item == this || item.IsPhysicallyHeld) return;
        if (item is IServable) return;
        if (GetConfigByClassName(item.GetType().Name) == null) return;

        TryAddIngredient(item);
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            ResetPlateNetworkState();
        }

        SyncListFromNetworkState();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Inspector에서 visualScale 등 값이 바뀌면 기존 비주얼을 전부 파괴 → 다음 Render에서 재생성
        foreach (var v in _spawnedVisuals)
            if (v != null) DestroyImmediate(v);
        _spawnedVisuals.Clear();
        _ingredientCopyCounts.Clear();
        _layerBaseY.Clear();
        _lastSyncedVisualCount = -1;
        _visualRebuildRequested = true;
    }
#endif

    private bool _visualRebuildRequested = false;

    public override void Render()
    {
        base.Render();
        if (!HasStateAuthority)
        {
            SyncListFromNetworkState();
        }
        else
        {
            RefreshStackVisuals();
            if (_visualRebuildRequested)
            {
                _visualRebuildRequested = false;
                RebuildAuthorityVisuals();
            }
        }
    }

    /// <summary>
    /// Authority 전용: 현재 platedIngredients 목록을 기반으로 비주얼을 처음부터 재구성합니다.
    /// OnValidate나 강제 리프레시 시 호출됩니다.
    /// </summary>
    private void RebuildAuthorityVisuals()
    {
        for (int i = 0; i < platedIngredients.Count; i++)
        {
            PlatedIngredient plated  = platedIngredients[i];
            IngredientVisualConfig cfg = GetConfigByClassName(plated.itemClassName);
            int copies               = (cfg != null && cfg.copies > 0) ? cfg.copies : visualCopiesPerIngredient;
            float yBase              = GetLayerBaseY(i);

            for (int j = 0; j < copies; j++)
            {
                int spiralIndex = _spawnedVisuals.Count;
                PlaceVisualAt(CreateIngredientVisual(plated, null, cfg), spiralIndex, yBase, cfg);
            }

            _ingredientCopyCounts.Add(copies);
            RecordLayerHeight(i);
        }
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;

        if (_clearRequested)
        {
            _clearRequested = false;
            platedIngredients.Clear();
            RefreshStackVisuals();
        }

        while (_pendingLocalIngredients.Count > 0)
        {
            PickableItem ingredient = _pendingLocalIngredients.Dequeue();
            LocalCommitAddIngredient(ingredient);
        }
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (!HasStateAuthority)
            return;

        if (_clearRequested)
        {
            _clearRequested = false;
            ResetPlateNetworkState();
        }

        while (_pendingNetworkIngredientIds.Count > 0)
        {
            NetworkId ingredientId = _pendingNetworkIngredientIds.Dequeue();
            NetworkObject ingredientObj = Runner != null ? Runner.FindObject(ingredientId) : null;
            if (ingredientObj == null)
                continue;

            PickableItem ingredient = ingredientObj.GetComponent<PickableItem>();
            if (ingredient == null)
                continue;

            AuthorityCommitAddIngredient(ingredient);
        }

        while (_pendingLocalIngredients.Count > 0)
        {
            PickableItem ingredient = _pendingLocalIngredients.Dequeue();
            AuthorityCommitAddIngredient(ingredient);
        }
    }

    /// <summary>
    /// 재료를 접시에 추가하려고 시도합니다.
    /// </summary>
    public bool TryAddIngredient(PickableItem ingredient)
    {
        if (ingredient == null || ingredient == this)
            return false;

        if (IsFull)
            return false;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                if (ingredient.Object == null || !ingredient.Object.IsValid)
                    return false;

                RPC_RequestAddIngredient(ingredient.Object.Id);
                return true;
            }

            if (ingredient.Object != null && ingredient.Object.IsValid)
                _pendingNetworkIngredientIds.Enqueue(ingredient.Object.Id);
            else
                _pendingLocalIngredients.Enqueue(ingredient);

            return true;
        }

        _pendingLocalIngredients.Enqueue(ingredient);
        return true;
    }

    /// <summary>
    /// 접시 초기화.
    /// </summary>
    public void ClearPlate()
    {
        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                RPC_RequestClearPlate();
                return;
            }

            _clearRequested = true;
            return;
        }

        _clearRequested = true;
    }

    /// <summary>
    /// PickableItem 없이 재료 기록을 직접 추가합니다.
    /// </summary>
    public bool AddDirectIngredient(string ingredientID, CookState cookState)
    {
        if (string.IsNullOrWhiteSpace(ingredientID) || IsFull) return false;
        if (IsNetworkReady)
        {
            if (!HasStateAuthority) { Rpc_RequestAddDirect(ingredientID, (int)cookState); return true; }
            CommitDirect(ingredientID, cookState);
            return true;
        }
        platedIngredients.Add(new PlatedIngredient(ingredientID, cookState));
        RefreshStackVisuals();
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestAddDirect(string ingredientID, int cookStateInt)
        => CommitDirect(ingredientID, (CookState)cookStateInt);

    private void CommitDirect(string ingredientID, CookState cookState)
    {
        if (IsFull) return;
        var netData = new NetworkPlatedIngredient
        {
            IngredientID = ToNetworkIngredientId(ingredientID),
            CutMethod    = "",
            CookState    = (int)cookState,
            Used         = true
        };
        PlateSlots.Set(PlateCountNetworked, netData);
        PlateCountNetworked++;
        SyncListFromNetworkState();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestAddIngredient(NetworkId ingredientObjectId)
    {
        _pendingNetworkIngredientIds.Enqueue(ingredientObjectId);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestClearPlate()
    {
        _clearRequested = true;
    }

    private void AuthorityCommitAddIngredient(PickableItem source)
    {
        if (source == null || IsFull)
            return;

        if (!TryBuildPlatedIngredient(source, out PlatedIngredient plated))
            return;

        int insertIndex = PlateCountNetworked;

        NetworkPlatedIngredient netData = new NetworkPlatedIngredient
        {
            IngredientID  = ToNetworkIngredientId(plated.ingredientID),
            CutMethod     = plated.cutMethod ?? "",
            ItemClassName = plated.itemClassName ?? "",
            CookState     = (int)plated.cookState,
            CookTime      = plated.cookTime, // [추가]
            Used          = true
        };

        PlateSlots.Set(insertIndex, netData);
        PlateCountNetworked++;

        // SyncListFromNetworkState 대신 직접 추가 (cookingSeq 손실 방지)
        platedIngredients.Add(plated);
        PlaceIngredientVisuals(plated, source);

        Debug.Log($"[PlateItem] 재료 추가(Authority) — ID:{plated.ingredientID} cookState:{plated.cookState} cutMethod:'{plated.cutMethod}' seq:[{string.Join(",", plated.cookingSeq ?? new System.Collections.Generic.List<string>())}] 총:{platedIngredients.Count}개");

        if (consumeSourceObjectOnAdd)
            ConsumeIngredientObject(source);
    }

    private void LocalCommitAddIngredient(PickableItem source)
    {
        if (source == null || IsFull)
            return;

        if (!TryBuildPlatedIngredient(source, out PlatedIngredient plated))
            return;

        platedIngredients.Add(plated);
        PlaceIngredientVisuals(plated, source);

        Debug.Log($"[PlateItem] 재료 추가(Local) — ID:{plated.ingredientID} cookState:{plated.cookState} cutMethod:'{plated.cutMethod}' seq:[{string.Join(",", plated.cookingSeq ?? new System.Collections.Generic.List<string>())}] 총:{platedIngredients.Count}개");

        if (consumeSourceObjectOnAdd)
            ConsumeIngredientObject(source);
    }

    private void ResetPlateNetworkState()
    {
        PlateCountNetworked = 0;

        for (int i = 0; i < MaxPlateIngredients; i++)
        {
            PlateSlots.Set(i, default);
        }

        platedIngredients.Clear();
        _ingredientCopyCounts.Clear();
        _lastSyncedVisualCount = -1;
        RefreshStackVisuals();
    }

    private void SyncListFromNetworkState()
    {
        if (!IsNetworkReady)
            return;

        platedIngredients.Clear();

        int safeCount = Mathf.Clamp(PlateCountNetworked, 0, MaxPlateIngredients);
        for (int i = 0; i < safeCount; i++)
        {
            NetworkPlatedIngredient slot = PlateSlots[i];
            if (!slot.Used)
                continue;

            platedIngredients.Add(new PlatedIngredient(
                slot.IngredientID.ToString(),
                (CookState)slot.CookState,
                null,
                slot.CutMethod.ToString(),
                slot.ItemClassName.ToString(),
                slot.CookTime // [추가]
            ));
        }

        // 총 비주얼 수 계산: 이미 기록된 카운트(재료 수 이내만) + 미기록 재료의 예상 수
        while (_ingredientCopyCounts.Count > platedIngredients.Count)
            _ingredientCopyCounts.RemoveAt(_ingredientCopyCounts.Count - 1);
        int trackedVisuals = 0;
        foreach (int c in _ingredientCopyCounts) trackedVisuals += c;
        int expectedTotal = trackedVisuals;
        for (int i = _ingredientCopyCounts.Count; i < platedIngredients.Count; i++)
        {
            IngredientVisualConfig cfg = GetConfigByClassName(platedIngredients[i].itemClassName);
            expectedTotal += (cfg != null && cfg.copies > 0) ? cfg.copies : visualCopiesPerIngredient;
        }

        if (_lastSyncedVisualCount == expectedTotal) return;
        _lastSyncedVisualCount = expectedTotal;

        RefreshStackVisuals(); // 초과 비주얼 제거

        // 재료별로 비주얼 생성 (이미 생성된 재료는 건너뜀)
        for (int i = _ingredientCopyCounts.Count; i < platedIngredients.Count; i++)
        {
            PlatedIngredient plated  = platedIngredients[i];
            IngredientVisualConfig cfg = GetConfigByClassName(plated.itemClassName);
            int copies               = (cfg != null && cfg.copies > 0) ? cfg.copies : visualCopiesPerIngredient;
            float yBase              = GetLayerBaseY(i);

            for (int j = 0; j < copies; j++)
            {
                int spiralIndex = _spawnedVisuals.Count;
                PlaceVisualAt(CreateIngredientVisual(plated, null, cfg), spiralIndex, yBase, cfg);
            }

            _ingredientCopyCounts.Add(copies);
            RecordLayerHeight(i);
        }
    }

    // ── 시각적 쌓기 ────────────────────────────────────────────────

    /// <summary>
    /// platedIngredients 리스트와 _spawnedVisuals 를 동기화합니다.
    /// 재료 추가/제거 모두 처리하며, 모든 클라이언트와 late join 에서 동작합니다.
    /// </summary>
    private void RefreshStackVisuals()
    {
        // _ingredientCopyCounts를 platedIngredients.Count에 맞게 뒤에서 잘라냄
        while (_ingredientCopyCounts.Count > platedIngredients.Count)
            _ingredientCopyCounts.RemoveAt(_ingredientCopyCounts.Count - 1);

        // 실제 비주얼 목표 수 = 기록된 복사본 합계
        int targetCount = 0;
        foreach (int c in _ingredientCopyCounts) targetCount += c;

        while (_spawnedVisuals.Count > targetCount)
        {
            int last = _spawnedVisuals.Count - 1;
            if (_spawnedVisuals[last] != null)
                Destroy(_spawnedVisuals[last]);
            _spawnedVisuals.RemoveAt(last);
        }

        // 레이어 Y 기록 정리
        int targetLayers = platedIngredients.Count + 1;
        while (_layerBaseY.Count > targetLayers)
            _layerBaseY.RemoveAt(_layerBaseY.Count - 1);
    }

    /// <summary>인덱스 i번째 재료가 놓일 Y 기저를 반환합니다.</summary>
    private float GetLayerBaseY(int ingredientIndex)
    {
        if (ingredientIndex == 0) return plateTopY;
        if (ingredientIndex < _layerBaseY.Count) return _layerBaseY[ingredientIndex];
        return plateTopY + ingredientIndex * 0.05f; // 측정 전 폴백
    }

    /// <summary>
    /// ingredientIndex 번째 재료의 비주얼 bounds를 측정하고
    /// 다음 재료의 Y 기저를 _layerBaseY에 기록합니다.
    /// </summary>
    private void RecordLayerHeight(int ingredientIndex)
    {
        int startIdx = GetVisualStartIndex(ingredientIndex);
        int copies   = ingredientIndex < _ingredientCopyCounts.Count
                       ? _ingredientCopyCounts[ingredientIndex]
                       : visualCopiesPerIngredient;
        int endIdx   = Mathf.Min(startIdx + copies, _spawnedVisuals.Count);

        float maxLocalY = GetLayerBaseY(ingredientIndex);
        for (int i = startIdx; i < endIdx; i++)
        {
            var v = _spawnedVisuals[i];
            if (v == null) continue;
            foreach (var r in v.GetComponentsInChildren<Renderer>())
                maxLocalY = Mathf.Max(maxLocalY, transform.InverseTransformPoint(r.bounds.max).y);
        }

        while (_layerBaseY.Count <= ingredientIndex + 1)
            _layerBaseY.Add(0f);
        _layerBaseY[ingredientIndex + 1] = maxLocalY + stackLayerHeight;
    }

    /// <summary>
    /// i번째 재료의 첫 번째 비주얼 인덱스를 반환합니다.
    /// </summary>
    private int GetVisualStartIndex(int ingredientIndex)
    {
        int start = 0;
        for (int i = 0; i < ingredientIndex && i < _ingredientCopyCounts.Count; i++)
            start += _ingredientCopyCounts[i];
        return start;
    }

    [Header("Plate Visual")]
    [Tooltip("첫 재료의 Y 오프셋 (접시 두께)")]
    [SerializeField] private float plateTopY = 0.05f;
    [Tooltip("재료 비주얼 상단과 다음 재료 하단 사이의 여백 (m)")]
    [SerializeField] private float stackLayerHeight = 0.005f;
    [Tooltip("재료가 퍼지는 최대 반경 (m)")]
    [SerializeField] private float spreadRadius = 0.08f;
    [Tooltip("캡처된 메쉬에 적용할 스케일 배율")]
    [SerializeField] private float visualScale = 1.4f;
    [Tooltip("재료 1개당 표시할 시각 복사본 수 (많을수록 풍성해 보임)")]
    [SerializeField] private int visualCopiesPerIngredient = 3;

    /// <summary>
    /// 재료 비주얼을 우선순위에 따라 생성합니다.
    /// 1순위: 클래스 이름으로 룩업 (예: TomatoSliceItem) — 모든 클라이언트 공통
    /// 2순위: ingredientID 문자열로 룩업
    /// 3순위: 원본 아이템 메쉬 캡처 (source 있을 때, authority 전용)
    /// 4순위: 색상 플레이스홀더 (non-authority + 룩업 미등록 재료)
    /// </summary>
    private GameObject CreateIngredientVisual(PlatedIngredient plated, PickableItem source, IngredientVisualConfig cfg = null)
    {
        // cfg가 없으면 클래스 이름으로 재탐색
        if (cfg == null) cfg = GetConfigByClassName(plated.itemClassName);

        // 1순위: 등록된 프리팹
        GameObject prefab = cfg?.prefab;
        if (prefab != null)
        {
            GameObject visual = Instantiate(prefab);
            visual.name = $"[PlateVisual] {plated.itemClassName}";
            foreach (var rb in visual.GetComponentsInChildren<Rigidbody>())
                rb.isKinematic = true;
            foreach (var col in visual.GetComponentsInChildren<Collider>())
                col.enabled = false;
            foreach (var nb in visual.GetComponentsInChildren<NetworkBehaviour>())
                nb.enabled = false;
            Debug.Log($"[PlateVisual] 1순위 프리팹 사용: {plated.itemClassName}");
            return visual;
        }

        // 3순위: 메쉬 캡처
        if (source != null)
        {
            GameObject captured = CaptureIngredientVisual(source);
            if (captured != null)
            {
                Debug.Log($"[PlateVisual] 3순위 메쉬캡처: {plated.itemClassName} (등록 프리팹 없음, className='{plated.itemClassName}')");
                return captured;
            }
        }

        // 4순위: 색상 플레이스홀더
        Debug.Log($"[PlateVisual] 4순위 플레이스홀더: {plated.ingredientID} (className='{plated.itemClassName}', source={source})");
        return CreatePlaceholderVisual(plated.ingredientID);
    }

    /// <summary>
    /// 재료를 접시에 올리기 전에 아이템의 메쉬 렌더러를 복사해 시각 오브젝트를 만듭니다.
    /// Despawn/Destroy 이전에 호출해야 합니다.
    /// </summary>
    private GameObject CaptureIngredientVisual(PickableItem source)
    {
        if (source == null) return null;

        MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0) return null;

        GameObject root = new GameObject($"[PlateVisual] {source.itemName}");

        foreach (MeshFilter origFilter in filters)
        {
            if (origFilter.sharedMesh == null) continue;
            MeshRenderer origRenderer = origFilter.GetComponent<MeshRenderer>();
            if (origRenderer == null) continue;

            GameObject copy = new GameObject(origFilter.gameObject.name);
            copy.transform.SetParent(root.transform, false);

            copy.transform.localPosition = source.transform.InverseTransformPoint(origFilter.transform.position);
            copy.transform.localRotation  = Quaternion.Inverse(source.transform.rotation) * origFilter.transform.rotation;
            Vector3 ls = source.transform.lossyScale;
            Vector3 ws = origFilter.transform.lossyScale;
            copy.transform.localScale = new Vector3(
                ls.x > 0f ? ws.x / ls.x : ws.x,
                ls.y > 0f ? ws.y / ls.y : ws.y,
                ls.z > 0f ? ws.z / ls.z : ws.z);

            copy.AddComponent<MeshFilter>().sharedMesh = origFilter.sharedMesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = origRenderer.sharedMaterials;
        }

        return root;
    }

    /// <summary>
    /// Non-authority 클라이언트용 색상 플레이스홀더 비주얼을 생성합니다.
    /// ingredientID 해시로 색상을 결정하여 재료 종류를 구분할 수 있습니다.
    /// </summary>
    private GameObject CreatePlaceholderVisual(string ingredientID)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"[PlateVisual] {ingredientID}";

        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            int hash = Mathf.Abs(ingredientID.GetHashCode());
            float hue = (hash % 360) / 360f;
            mr.material.color = Color.HSVToRGB(hue, 0.65f, 0.9f);
        }

        return go;
    }

    /// <summary>
    /// 재료 1개를 visualCopiesPerIngredient 개의 복사본으로 퍼뜨려 배치합니다.
    /// platedIngredients에 이미 추가된 상태에서 호출하세요.
    /// </summary>
    private void PlaceIngredientVisuals(PlatedIngredient plated, PickableItem source)
    {
        int ingredientIndex = platedIngredients.Count - 1;
        float yBase         = GetLayerBaseY(ingredientIndex);

        IngredientVisualConfig cfg    = GetConfigByClassName(plated.itemClassName);
        int copies                    = (cfg != null && cfg.copies > 0) ? cfg.copies : visualCopiesPerIngredient;

        for (int i = 0; i < copies; i++)
        {
            int spiralIndex  = _spawnedVisuals.Count;
            GameObject visual = CreateIngredientVisual(plated, source, cfg);
            PlaceVisualAt(visual, spiralIndex, yBase, cfg);
        }

        _ingredientCopyCounts.Add(copies);
        RecordLayerHeight(ingredientIndex);
    }

    /// <summary>
    /// 비주얼을 지정한 나선 인덱스·Y 위치에 배치합니다.
    /// </summary>
    private void PlaceVisualAt(GameObject captured, int spiralIndex, float yPos, IngredientVisualConfig cfg = null)
    {
        if (captured == null) { _spawnedVisuals.Add(null); return; }

        float radius = (cfg != null && cfg.spreadRadius > 0f) ? cfg.spreadRadius : spreadRadius;
        float scale  = (cfg != null && cfg.visualScale  > 0f) ? cfg.visualScale  : visualScale;
        Debug.Log($"[PlateVisual] PlaceVisualAt — cfg={(cfg != null ? "있음" : "없음(전역사용)")} scale={scale} radius={radius}");

        // 원형 배열: 0은 중앙, 이후는 골든앵글(137.5°) 피보나치 나선
        Vector2 offset2D;
        if (spiralIndex == 0)
        {
            offset2D = Vector2.zero;
        }
        else
        {
            float angle = spiralIndex * 137.508f * Mathf.Deg2Rad;
            float r     = radius * Mathf.Sqrt(spiralIndex / (float)(MaxPlateIngredients * Mathf.Max(visualCopiesPerIngredient, 1)));
            offset2D = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        }

        // 재현 가능한 랜덤 Y 회전 + 미세 Y 노이즈
        UnityEngine.Random.State prevState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(GetInstanceID() ^ (spiralIndex * 7919));
        float yRot   = UnityEngine.Random.Range(0f, 360f);
        float yNoise = UnityEngine.Random.Range(-0.005f, 0.005f);
        UnityEngine.Random.state = prevState;

        captured.transform.SetParent(transform, false);
        captured.transform.localPosition = new Vector3(offset2D.x, yPos + yNoise, offset2D.y);
        captured.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
        captured.transform.localScale    = Vector3.one * scale;

        _spawnedVisuals.Add(captured);
    }

    /// <summary>
    /// ingredientVisualConfigs 목록에서 itemClassName이 일치하는 설정을 반환합니다.
    /// 프리팹의 PickableItem 컴포넌트 타입 이름과 비교합니다.
    /// </summary>
    private IngredientVisualConfig GetConfigByClassName(string itemClassName)
    {
        if (ingredientVisualConfigs == null || string.IsNullOrEmpty(itemClassName))
            return null;
        foreach (var cfg in ingredientVisualConfigs)
        {
            if (cfg?.prefab == null) continue;
            PickableItem item = cfg.prefab.GetComponent<PickableItem>();
            if (item != null && item.GetType().Name == itemClassName)
                return cfg;
        }
        return null;
    }

    private bool TryBuildPlatedIngredient(PickableItem source, out PlatedIngredient plated)
    {
        plated = default;

        string ingredientId = ResolveIngredientId(source);
        if (string.IsNullOrWhiteSpace(ingredientId))
            return false;

        CookState cookState = source is ICookable cookable ? cookable.CurrentCookState : CookState.Raw;

        source.metadata.TryGetValue("cutMethod", out string cutMethod);
        var seq = source.cookingSeq != null
            ? new System.Collections.Generic.List<string>(source.cookingSeq)
            : new System.Collections.Generic.List<string>();

        plated = new PlatedIngredient(ingredientId, cookState, seq, cutMethod ?? "", source.GetType().Name, source.cookTime);

        Debug.Log($"[PlateItem] ▶ PlatedIngredient 생성\n" +
                  $"  ingredientID  : {plated.ingredientID}\n" +
                  $"  itemClassName : {plated.itemClassName}\n" +
                  $"  cookState     : {plated.cookState}\n" +
                  $"  cutMethod     : '{plated.cutMethod}'\n" +
                  $"  cookTime      : {plated.cookTime}\n" +
                  $"  cookingSeq    : [{string.Join(" → ", plated.cookingSeq)}]");

        return true;
    }

    private string ResolveIngredientId(PickableItem source)
    {
        if (source == null)
            return string.Empty;

        // 1순위: metadata["ingredientID"]
        if (source.metadata != null &&
            source.metadata.TryGetValue("ingredientID", out string metadataId) &&
            !string.IsNullOrWhiteSpace(metadataId))
        {
            return metadataId.Trim();
        }

        Type type = source.GetType();

        // 2순위: public/private string ingredientID 프로퍼티
        PropertyInfo prop = type.GetProperty(
            "ingredientID",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );

        if (prop != null && prop.PropertyType == typeof(string))
        {
            string value = prop.GetValue(source) as string;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        // 3순위: public/private string ingredientID 필드
        FieldInfo field = type.GetField(
            "ingredientID",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );

        if (field != null && field.FieldType == typeof(string))
        {
            string value = field.GetValue(source) as string;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        // 4순위: itemName fallback
        if (!string.IsNullOrWhiteSpace(source.itemName))
            return source.itemName.Trim();

        return source.name.Replace("(Clone)", string.Empty).Trim();
    }

    private NetworkString<_64> ToNetworkIngredientId(string source)
    {
        string value = source ?? string.Empty;
        if (value.Length > MaxIngredientIdLength)
            value = value.Substring(0, MaxIngredientIdLength);

        NetworkString<_64> result = value;
        return result;
    }

    private void ConsumeIngredientObject(PickableItem source)
    {
        if (source == null)
            return;

        if (source.Object != null && source.Object.IsValid && Runner != null)
        {
            Runner.Despawn(source.Object);
        }
        else
        {
            Destroy(source.gameObject);
        }
    }
}
