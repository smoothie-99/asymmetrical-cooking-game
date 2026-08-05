using System;
using System.Collections.Generic;
using Fusion;
using Interactions;
using UnityEngine;

/// <summary>
/// 꼬치 아이템.
///
/// 들고 있는 상태에서 재료를 바라보고 [F]키를 누르면 재료를 하나씩 끼웁니다.
/// ServingStation에 제출 가능한 IServable입니다.
///
/// [네트워크]
/// SoupBowlItem 패턴과 동일 — StateAuthority가 커밋, Proxy는 OnChangedRender로 동기화.
/// </summary>  
public class SkeweredItem : PickableItem, IServable, ICookable
{
    private const int NetworkCapacity = 8;

    [Serializable]
    private struct NetworkSkeweredIngredient : INetworkStruct
    {
        public NetworkString<_64> IngredientID;
        public NetworkString<_64> ItemClassName;
        public NetworkString<_32> CutMethod;
        public int   CookState;
        public float CookTime;
        public NetworkBool Used;
    }

    // ── Inspector ──────────────────────────────────────────────────

    [Header("Skewer Settings")]
    [SerializeField] private string dishType = "Skewer";
    [SerializeField, Range(1, NetworkCapacity)] private int maxSkewered = 5;

    [Header("Skewer Visual")]
    [Tooltip("꼬치 앞끝 — 재료가 끼워지기 시작하는 지점")]
    [SerializeField] private Transform skewStartPoint;
    [Tooltip("꼬치 뒷끝 — 마지막 재료가 끼워지는 지점")]
    [SerializeField] private Transform skewEndPoint;
    [Tooltip("재료 비주얼 기본 스케일 배율 (재료별 설정이 없을 때 사용)")]
    [SerializeField] private float defaultVisualScale = 1f;
    [Tooltip("재료별 비주얼 프리팹 및 세부 조정")]
    [SerializeField] private List<SkeweredVisualConfig> ingredientVisualConfigs = new List<SkeweredVisualConfig>();

    // ── Networked 상태 ────────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnNetworkStateChanged))]
    private int IngredientCountNetworked { get; set; }

    [Networked, Capacity(NetworkCapacity)]
    private NetworkArray<NetworkSkeweredIngredient> IngredientSlots => default;

    // ── 로컬 상태 ─────────────────────────────────────────────────

    [Header("Debug View")]
    [SerializeField] private List<PlatedIngredient> _ingredients = new List<PlatedIngredient>();

    private readonly Queue<NetworkId>    _pendingNetworkIds    = new Queue<NetworkId>();
    private readonly Queue<PickableItem> _pendingLocalItems    = new Queue<PickableItem>();
    private bool _clearRequested = false;

    private readonly List<GameObject> _skewedVisuals = new List<GameObject>();
    private int     _lastVisualCount = -1;
    private Vector3 _lastStartPos    = Vector3.positiveInfinity;
    private Vector3 _lastEndPos      = Vector3.positiveInfinity;

    // ── IServable ─────────────────────────────────────────────────

    public string DishType => dishType;
    public IReadOnlyList<PlatedIngredient> PlatedIngredients => _ingredients;
    public bool IsEmpty => Count <= 0;

    public int  Count    => IsNetworkReady ? IngredientCountNetworked : _ingredients.Count;
    public int  MaxCount => maxSkewered;
    public bool IsFull   => Count >= maxSkewered;

    public void ClearDish()
    {
        if (IsNetworkReady)
        {
            if (!HasStateAuthority) { Rpc_RequestClear(); return; }
            ExecuteClear();
        }
        else
        {
            _clearRequested = true;
        }
    }

    // ── Fusion 생명주기 ────────────────────────────────────────────

    private void Start()
    {
        itemName = "꼬치";
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary && !isHeld && !player.IsActiveHandFull())
            return $"[꼬치] {Count}/{MaxCount}\n[E] 집기";

        return base.GetInteractionLabel(player, type);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Inspector에서 포인트·설정 변경 시 즉시 비주얼 재빌드
        _lastVisualCount = -1;
        _lastStartPos    = Vector3.positiveInfinity;
        _lastEndPos      = Vector3.positiveInfinity;
        if (Application.isPlaying)
            RefreshVisuals();
    }
#endif

    public override void Spawned()
    {
        base.Spawned();
        if (HasStateAuthority) ExecuteClear();
        SyncFromNetworkState();
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
        if (!HasStateAuthority) return;

        // 매 프레임 리셋 (CookInFire가 호출되면 다시 true가 됨)
        isBeingCooked = false;

        if (_clearRequested)
        {
            _clearRequested = false;
            ExecuteClear();
        }

        while (_pendingNetworkIds.Count > 0)
        {
            NetworkId id = _pendingNetworkIds.Dequeue();
            NetworkObject obj = Runner?.FindObject(id);
            if (obj == null) continue;
            PickableItem item = obj.GetComponent<PickableItem>();
            if (item != null) AuthorityCommit(item);
        }

        while (_pendingLocalItems.Count > 0)
            AuthorityCommit(_pendingLocalItems.Dequeue());
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;

        if (_clearRequested)
        {
            _clearRequested = false;
            ExecuteClear();
        }

        while (_pendingLocalItems.Count > 0)
            LocalCommit(_pendingLocalItems.Dequeue());
    }

    public override void Render()
    {
        base.Render();
        if (!HasStateAuthority)
            SyncFromNetworkState();
    }

    // ── 공개 API ──────────────────────────────────────────────────

    /// <summary>
    /// 해당 재료를 이 꼬치에 끼울 수 있는지 확인합니다.
    /// ingredientVisualConfigs에 등록된 타입만 허용합니다.
    /// </summary>
    public bool CanSkewer(PickableItem ingredient)
    {
        if (ingredient == null || IsFull) return false;
        return GetConfig(ingredient.GetType().Name) != null;
    }

    /// <summary>
    /// 재료를 꼬치에 끼웁니다. PickableItem.Interact(Secondary)에서 호출됩니다.
    /// </summary>
    public bool TrySkewer(PickableItem ingredient)
    {
        if (ingredient == null || ingredient == this) return false;
        if (IsFull) return false;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                if (ingredient.Object == null || !ingredient.Object.IsValid) return false;
                Rpc_RequestSkewer(ingredient.Object.Id);
                return true;
            }
            if (ingredient.Object != null && ingredient.Object.IsValid)
                _pendingNetworkIds.Enqueue(ingredient.Object.Id);
            else
                _pendingLocalItems.Enqueue(ingredient);
            return true;
        }

        _pendingLocalItems.Enqueue(ingredient);
        return true;
    }

    // ── ICookable ──────────────────────────────────────────────────
    
    public CookState CurrentCookState 
    { 
        get => Count > 0 ? (CookState)IngredientSlots[0].CookState : CookState.Raw;
        set { /* 인터페이스 규격 충족용 */ }
    }
    
    // PickableItem의 isBeingCooked 인터페이스 구현
    [Networked] public bool IsBeingCookedNetworked { get; set; }
    private bool _localIsBeingCooked = false;
    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : _localIsBeingCooked;
        set { if (IsNetworkReady) { if (HasStateAuthority) IsBeingCookedNetworked = value; } _localIsBeingCooked = value; }
    }

    public void CookInFire(float heat)
    {
        if (heat <= 0f || !HasStateAuthority || Count <= 0) return;
        isBeingCooked = true;

        for (int i = 0; i < IngredientCountNetworked; i++)
        {
            var slot = IngredientSlots[i];
            if (!slot.Used || slot.CookState == (int)CookState.Burned) continue;

            float newTime = slot.CookTime + heat;
            int newState = (int)slot.CookState;

            // 60초 이상: Cooked, 90초 이상: Burned (7스테이지 기준)
            if (newTime >= 90f) newState = (int)CookState.Burned;
            else if (newTime >= 60f) newState = (int)CookState.Cooked;

            IngredientSlots.Set(i, new NetworkSkeweredIngredient
            {
                IngredientID = slot.IngredientID,
                ItemClassName = slot.ItemClassName,
                CutMethod = slot.CutMethod,
                CookState = newState,
                CookTime = newTime,
                Used = true
            });
            
            // 로컬 리스트 동기화
            if (i < _ingredients.Count)
            {
                var plated = _ingredients[i];
                plated.cookState = (CookState)newState;
                plated.cookTime = newTime;
                if (plated.cookState == CookState.Cooked && (plated.cookingSeq == null || !plated.cookingSeq.Contains("\uAC00\uC5F4")))
                {
                    plated.cookingSeq ??= new List<string>();
                    plated.cookingSeq.Add("\uAC00\uC5F4");
                }
            }
        }
        
        // 시각적 갱신
        RefreshVisuals();
    }

    // ── 내부 커밋 ─────────────────────────────────────────────────

    private void AuthorityCommit(PickableItem source)
    {
        if (source == null || IsFull) return;

        PlatedIngredient plated = BuildPlatedIngredient(source);
        int idx = IngredientCountNetworked;

        IngredientSlots.Set(idx, new NetworkSkeweredIngredient
        {
            IngredientID  = Truncate64(plated.ingredientID),
            ItemClassName = Truncate64(plated.itemClassName),
            CutMethod     = Truncate32(plated.cutMethod),
            CookState     = (int)plated.cookState,
            CookTime      = plated.cookTime,
            Used          = true
        });
        IngredientCountNetworked++;

        _ingredients.Add(plated);
        RefreshVisuals();

        Debug.Log($"[Skewer] 끼우기(Authority) — {plated.ingredientID} ({IngredientCountNetworked}/{maxSkewered})");

        ConsumeObject(source);
    }

    private void LocalCommit(PickableItem source)
    {
        if (source == null || IsFull) return;

        PlatedIngredient plated = BuildPlatedIngredient(source);
        _ingredients.Add(plated);
        RefreshVisuals();

        Debug.Log($"[Skewer] 끼우기(Local) — {plated.ingredientID} ({_ingredients.Count}/{maxSkewered})");

        ConsumeObject(source);
    }

    private void ExecuteClear()
    {
        IngredientCountNetworked = 0;
        for (int i = 0; i < NetworkCapacity; i++)
            IngredientSlots.Set(i, default);
        _ingredients.Clear();
        ClearVisuals();
    }

    // ── 동기화 ────────────────────────────────────────────────────

    private void OnNetworkStateChanged() => SyncFromNetworkState();

    private void SyncFromNetworkState()
    {
        if (!IsNetworkReady) return;
        if (HasStateAuthority) return; // Authority는 커밋 시점에 직접 관리

        _ingredients.Clear();
        int safe = Mathf.Clamp(IngredientCountNetworked, 0, NetworkCapacity);
        for (int i = 0; i < safe; i++)
        {
            var slot = IngredientSlots[i];
            if (!slot.Used) continue;
            _ingredients.Add(new PlatedIngredient(
                slot.IngredientID.ToString(),
                (CookState)slot.CookState,
                null,
                slot.CutMethod.ToString(),
                slot.ItemClassName.ToString(),
                slot.CookTime));
        }

        RefreshVisuals();
    }

    // ── 비주얼 ────────────────────────────────────────────────────

    private void RefreshVisuals()
    {
        int     count    = _ingredients.Count;
        Vector3 startPos = skewStartPoint != null ? skewStartPoint.localPosition : Vector3.zero;
        Vector3 endPos   = skewEndPoint   != null ? skewEndPoint.localPosition   : Vector3.zero;

        bool pointsMoved = startPos != _lastStartPos || endPos != _lastEndPos;
        if (count == _lastVisualCount && !pointsMoved) return;

        _lastVisualCount = count;
        _lastStartPos    = startPos;
        _lastEndPos      = endPos;

        ClearVisuals();
        if (count == 0 || skewStartPoint == null || skewEndPoint == null) return;

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : (float)i / (count - 1);
            Vector3 basePos = Vector3.Lerp(skewStartPoint.localPosition, skewEndPoint.localPosition, t);

            SkeweredVisualConfig cfg = GetConfig(_ingredients[i].itemClassName);
            float scale = (cfg != null && cfg.scale > 0f) ? cfg.scale : defaultVisualScale;

            GameObject visual = CreateVisual(_ingredients[i], cfg);
            if (visual == null) continue;

            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = basePos + (cfg?.positionOffset ?? Vector3.zero);
            visual.transform.localRotation = Quaternion.Euler(cfg?.rotationOffset ?? Vector3.zero);
            visual.transform.localScale    = Vector3.one * scale;

            // 조리 색상 반영
            ApplyVisualCookColor(visual, _ingredients[i].cookState);

            _skewedVisuals.Add(visual);
        }
    }

    private void ApplyVisualCookColor(GameObject visual, CookState state)
    {
        if (visual == null) return;
        Color color = state switch
        {
            CookState.Raw => Color.white,
            CookState.Cooked => new Color(0.8f, 0.4f, 0.2f),
            CookState.Burned => Color.black,
            _ => Color.white
        };

        foreach (var mr in visual.GetComponentsInChildren<MeshRenderer>())
        {
            foreach (var mat in mr.materials) mat.color *= color;
        }
    }

    private void ClearVisuals()
    {
        foreach (var v in _skewedVisuals)
            if (v != null) Destroy(v);
        _skewedVisuals.Clear();
        _lastVisualCount = -1;
    }

    private SkeweredVisualConfig GetConfig(string itemClassName)
    {
        if (string.IsNullOrEmpty(itemClassName)) return null;
        foreach (var cfg in ingredientVisualConfigs)
        {
            if (cfg?.prefab == null) continue;
            PickableItem pi = cfg.prefab.GetComponent<PickableItem>();
            if (pi != null && pi.GetType().Name == itemClassName) return cfg;
        }
        return null;
    }

    private GameObject CreateVisual(PlatedIngredient plated, SkeweredVisualConfig cfg = null)
    {
        // 1순위: Inspector 등록 프리팹
        cfg ??= GetConfig(plated.itemClassName);
        if (cfg != null)
        {
            GameObject visual = Instantiate(cfg.prefab);
            visual.name = $"[SkewVisual] {plated.itemClassName}";
            foreach (var rb  in visual.GetComponentsInChildren<Rigidbody>())  rb.isKinematic = true;
            foreach (var col in visual.GetComponentsInChildren<Collider>())   col.enabled = false;
            foreach (var nb  in visual.GetComponentsInChildren<NetworkBehaviour>()) nb.enabled = false;
            return visual;
        }

        // 2순위: 색상 플레이스홀더
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"[SkewVisual] {plated.ingredientID}";
        if (go.TryGetComponent<Collider>(out var c)) Destroy(c);
        int hash = Mathf.Abs(plated.ingredientID.GetHashCode());
        go.GetComponent<MeshRenderer>().material.color = Color.HSVToRGB((hash % 360) / 360f, 0.65f, 0.9f);
        return go;
    }

    // ── RPC ───────────────────────────────────────────────────────

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestSkewer(NetworkId ingredientId)
        => _pendingNetworkIds.Enqueue(ingredientId);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestClear() => ExecuteClear();

    // ── 유틸 ──────────────────────────────────────────────────────

    private PlatedIngredient BuildPlatedIngredient(PickableItem source)
    {
        string id = source.metadata.TryGetValue("ingredientID", out string mid) && !string.IsNullOrWhiteSpace(mid)
            ? mid.Trim()
            : (!string.IsNullOrWhiteSpace(source.itemName) ? source.itemName.Trim() : source.name.Replace("(Clone)", "").Trim());

        source.metadata.TryGetValue("cutMethod", out string cut);
        CookState cs = source is ICookable cookable ? cookable.CurrentCookState : CookState.Raw;
        var seq = source.cookingSeq != null
            ? new List<string>(source.cookingSeq)
            : new List<string>();

        return new PlatedIngredient(id, cs, seq, cut ?? "", source.GetType().Name, source.cookTime);
    }

    private void ConsumeObject(PickableItem source)
    {
        if (source.Object != null && source.Object.IsValid && Runner != null)
            Runner.Despawn(source.Object);
        else
            Destroy(source.gameObject);
    }

    private NetworkString<_64> Truncate64(string s)
    {
        s ??= string.Empty;
        if (s.Length > 64) s = s.Substring(0, 64);
        return s;
    }

    private NetworkString<_32> Truncate32(string s)
    {
        s ??= string.Empty;
        if (s.Length > 32) s = s.Substring(0, 32);
        return s;
    }
}

/// <summary>꼬치 비주얼 설정 — SkeweredItem Inspector에서 재료별 등록.</summary>
[Serializable]
public class SkeweredVisualConfig
{
    [Tooltip("재료 프리팹 (PickableItem 타입명으로 매핑, 허용 재료 등록용)")]
    public GameObject prefab;

    [Tooltip("꼬치 위 기준 위치에서 추가 오프셋 (로컬 좌표)")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("비주얼 회전 오프셋 (오일러각)")]
    public Vector3 rotationOffset = Vector3.zero;

    [Tooltip("비주얼 스케일 배율. 0 이하면 SkeweredItem의 defaultVisualScale 사용")]
    public float scale = 0f;
}
