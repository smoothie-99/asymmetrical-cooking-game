using System;
using System.Collections.Generic;
using Fusion;
using Interactions;
using UnityEngine;

/// <summary>
/// 냄비 안에서 각 재료 종류별 떠다니는 비주얼 설정.
/// Inspector에서 prefab, size, amount, distributionRadius 를 재료마다 지정합니다.
/// </summary>
[Serializable]
public class PotIngredientVisualConfig
{
    [Tooltip("재료 시각화 프리팹")]
    public GameObject prefab;
    [Tooltip("비주얼 스케일 (0 이하 = 1)")]
    public float size = 1f;
    [Tooltip("동시에 떠다닐 복사본 수 (0 이하 = 1)")]
    public int amount = 1;
    [Tooltip("냄비 내 분포 반경 (m). 0 이하 = potRadius 전역값 사용")]
    public float distributionRadius = 0.08f;
}

/// <summary>
/// PotStation — 고정 냄비 스테이션.
///
/// [상호작용]
/// - 재료를 냄비 트리거 안에 드롭 → 자동 흡수
/// - F키: 젓기 (조리 속도 UP)
/// - E키: SoupBowlItem을 들고 있을 때 → 국물 전체를 그릇에 퍼냄 + 냄비 초기화
///
/// [조리]
/// - 재료가 1개 이상이면 자동으로 타이머 진행
/// - 불 파티클은 Inspector에서 할당 (fireEffect)
/// </summary>
public class PotStation : NetworkBehaviour, IInteractable
{
    private const int MaxIngredients = 12;

    [Serializable]
    private struct NetworkPlatedIngredient : INetworkStruct
    {
        public NetworkString<_64> IngredientID;
        public NetworkString<_64> ItemClassName;
        public int CookState;
        public float CookTime;
        public NetworkBool Used;
    }

    // ── 조리 설정 ──────────────────────────────────────────────────

    [Header("Cooking")]
    public float baseCookSpeed     = 1.0f;
    public float stirPowerPerClick = 0.5f;
    public float stirDecayRate     = 2.0f;
    [SerializeField] private float cookedThreshold = 60f;
    [SerializeField] private float burnedThreshold = 100f;

    [Header("Fire Effect")]
    [Tooltip("불 파티클 오브젝트. Inspector에서 할당.")]
    [SerializeField] private ParticleSystem fireEffect;

    // ── ICookable ──────────────────────────────────────────────────

    [Networked] public CookState CookStateNetworked { get; set; }
    private CookState _localCookState = CookState.Raw;
    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            if (IsNetworkReady) { if (HasStateAuthority) CookStateNetworked = value; }
            else _localCookState = value;
        }
    }

    [Networked] public bool IsBeingCookedNetworked { get; set; }
    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : _localIsBeingCooked;
        set
        {
            if (IsNetworkReady) { if (HasStateAuthority) IsBeingCookedNetworked = value; }
            else _localIsBeingCooked = value;
        }
    }
    private bool _localIsBeingCooked;

    [Networked] private float CookTimer  { get; set; }
    [Networked] private float BonusSpeed { get; set; }
    private float _localCookTimer  = 0f;
    private float _localBonusSpeed = 0f;

    public void CookInFire(float heat) => AdvanceCook(heat);

    // ── 내부 재료 저장 ─────────────────────────────────────────────

    [Networked] private int IngredientCountNetworked { get; set; }
    [Networked, Capacity(MaxIngredients)]
    private NetworkArray<NetworkPlatedIngredient> IngredientSlots => default;

    [Header("Debug View")]
    [SerializeField] private List<PlatedIngredient> _ingredients = new List<PlatedIngredient>();
    private bool IsEmpty => Count <= 0;
    private int  Count   => IsNetworkReady ? IngredientCountNetworked : _ingredients.Count;
    private bool IsFull  => Count >= MaxIngredients;

    // ── 액체 & 플로팅 비주얼 ──────────────────────────────────────

    [Header("Liquid Visual")]
    [SerializeField] private Transform liquidTransform;
    [SerializeField] private float liquidBottomY   = 0.172f;
    [SerializeField] private float liquidMinScaleY = 0.01f;
    [SerializeField] private float liquidMaxScaleY = 0.3f;
    [SerializeField] private Color liquidColorRaw    = new Color(0.8f, 0.7f, 0.3f, 0.6f);
    [SerializeField] private Color liquidColorCooked = new Color(0.6f, 0.4f, 0.1f, 0.6f);
    [SerializeField] private Color liquidColorBurned = new Color(0.2f, 0.1f, 0.0f, 0.6f);

    [Header("Floating Ingredients")]
    [SerializeField] private Transform floatingParent;
    [SerializeField] private float potRadius      = 0.12f;
    [SerializeField] private float floatAmplitude = 0.015f;
    [SerializeField] private float floatSpeed     = 1.2f;
    [Tooltip("0 = 바닥, 1 = 표면. 재료가 액체 내 어느 높이에 떠있을지 비율로 설정")]
    [SerializeField] [Range(0f, 1f)] private float floatHeightRatio = 0.5f;
    [Tooltip("재료별 비주얼 설정 목록 — 각 재료 종류마다 prefab/size/amount/distributionRadius 설정")]
    [SerializeField] private List<PotIngredientVisualConfig> ingredientVisualConfigs = new List<PotIngredientVisualConfig>();

    private readonly List<GameObject> _floatingVisuals       = new List<GameObject>();
    private readonly List<float>      _floatBaseY            = new List<float>();
    private readonly List<float>      _floatPhase            = new List<float>();
    // 재료 i에 대해 생성된 비주얼 복사본 수를 기록 (_ingredientVisualCounts[i])
    private readonly List<int>        _ingredientVisualCounts = new List<int>();

    private Renderer _liquidRenderer;
    private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _mpb;

    // ── 편의 ──────────────────────────────────────────────────────

    private bool IsNetworkReady => Object != null && Object.IsValid;
    private Outline _outline;

    // ── Fusion 생명주기 ────────────────────────────────────────────

    private void Start()
    {
        _mpb = new MaterialPropertyBlock();
        if (liquidTransform != null)
            _liquidRenderer = liquidTransform.GetComponent<Renderer>();
        SyncFromNetworkState();
    }

    public override void Spawned()
    {
        if (HasStateAuthority) ExecuteClear();
        SyncFromNetworkState();

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

    public void OnFocus(CookingMasterHandsManager player) => _outline.enabled = true;
    public void OnFocusLost() => _outline.enabled = false;

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (BonusSpeed > 0f)
            BonusSpeed = Mathf.Max(0f, BonusSpeed - stirDecayRate * Runner.DeltaTime);

        if (IngredientCountNetworked > 0 && CookStateNetworked != CookState.Burned)
        {
            float heat = (baseCookSpeed + BonusSpeed) * Runner.DeltaTime;
            CookTimer += heat;
            AdvanceCookState();
        }
    }

    private void Update()
    {
        if (IsNetworkReady) return;

        // 샌드박스
        if (_localBonusSpeed > 0f)
            _localBonusSpeed = Mathf.Max(0f, _localBonusSpeed - stirDecayRate * Time.deltaTime);

        if (_ingredients.Count > 0 && _localCookState != CookState.Burned)
        {
            float heat = (baseCookSpeed + _localBonusSpeed) * Time.deltaTime;
            _localCookTimer += heat;
            AdvanceCookStateLocal();
        }
    }

    public override void Render()
    {
        SyncFromNetworkState();
        AnimateFloating();
    }

    // ── IInteractable ──────────────────────────────────────────────

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        switch (type)
        {
            case InteractionType.Primary:
                PickableItem handItem = player.GetActiveHandItem();
                if (handItem is SoupBowlItem) return !IsEmpty;
                if (handItem is LadleItem)    return true; // 국자 → 젓기
                return false;

            case InteractionType.Secondary:
                return false;

            default:
                return false;
        }
    }

    public void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary)
        {
            PickableItem handItem = player.GetActiveHandItem();
            if (handItem is SoupBowlItem bowl)
            {
                if (IsNetworkReady)
                {
                    if (!HasStateAuthority) { Rpc_RequestLadle(bowl.Object.Id); return; }
                    LadleIntoBowl(bowl);
                }
                else LadleIntoBowlLocal(bowl);
            }
            else if (handItem is LadleItem)
            {
                // 국자 들고 E → 젓기
                if (IsNetworkReady) Rpc_Stir();
                else _localBonusSpeed += stirPowerPerClick;
            }
        }
    }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        switch (type)
        {
            case InteractionType.Primary:
                if (player.GetActiveHandItem() is SoupBowlItem && !IsEmpty) return "[냄비]\n[E] 국물 퍼내기";
                if (player.GetActiveHandItem() is LadleItem)                 return "[냄비]\n[E] 젓기 (조리 속도 UP)";
                return "[냄비]\n[Q] 재료를 던져서 넣으세요";
            case InteractionType.Secondary:
                return "";
            default:
                return "";
        }
    }

    // ── 재료 흡수 (트리거) ─────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (IsFull) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item == null || item.IsPhysicallyHeld) return;
        if (GetConfigByClassName(item.GetType().Name) == null) return;

        TryAbsorbIngredient(item);
    }

    private bool TryAbsorbIngredient(PickableItem item)
    {
        if (item == null || IsFull) return false;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                if (item.Object != null && item.Object.IsValid)
                    Rpc_RequestAbsorb(item.Object.Id);
                return true; // 낙관적 처리
            }
            CommitIngredient(item);
        }
        else CommitIngredientLocal(item);
        return true;
    }

    // ── 퍼내기 ────────────────────────────────────────────────────

    private void LadleIntoBowl(SoupBowlItem bowl)
    {
        if (bowl == null) return;
        CookState soupState = IsNetworkReady ? CookStateNetworked : _localCookState;
        float time = IsNetworkReady ? CookTimer : _localCookTimer;
        Debug.Log($"[Pot.Ladle] _ingredients={_ingredients.Count}, IngredientCountNetworked={IngredientCountNetworked}");
        bowl.FillFromPot(StampCookTime(_ingredients, time), soupState);
        ExecuteClear();
    }

    private void LadleIntoBowlLocal(SoupBowlItem bowl)
    {
        if (bowl == null) return;
        bowl.FillFromPot(StampCookTime(_ingredients, _localCookTimer), _localCookState);
        _ingredients.Clear();
        RefreshVisuals();
        _localCookTimer = 0f;
        _localCookState = CookState.Raw;
    }

    private static List<PlatedIngredient> StampCookTime(List<PlatedIngredient> src, float time)
    {
        var list = new List<PlatedIngredient>(src.Count);
        foreach (var p in src) { var c = p; c.cookTime = time; list.Add(c); }
        return list;
    }

    // ── RPC ───────────────────────────────────────────────────────

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_Stir() => BonusSpeed += stirPowerPerClick;

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestAbsorb(NetworkId id)
    {
        NetworkObject obj = Runner.FindObject(id);
        PickableItem item = obj?.GetComponent<PickableItem>();
        if (item != null) CommitIngredient(item);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestLadle(NetworkId bowlId)
    {
        NetworkObject obj = Runner.FindObject(bowlId);
        SoupBowlItem bowl = obj?.GetComponent<SoupBowlItem>();
        if (bowl != null) LadleIntoBowl(bowl);
    }

    // ── 커밋 ──────────────────────────────────────────────────────

    private void CommitIngredient(PickableItem source)
    {
        if (source == null || IsFull) return;
        string id = ResolveId(source);
        if (string.IsNullOrWhiteSpace(id)) return;

        CookState state = source is ICookable c ? c.CurrentCookState : CookState.Raw;
        
        // 조리 기록 및 손질 방법 추출
        source.metadata.TryGetValue("cutMethod", out string cutMethod);
        var seqList = source.cookingSeq != null ? new List<string>(source.cookingSeq) : new List<string>();

        var netData = new NetworkPlatedIngredient
        {
            IngredientID  = Truncate(id),
            ItemClassName = Truncate(source.GetType().Name),
            CookState     = (int)state,
            CookTime      = CookTimer,
            Used          = true
        };
        IngredientSlots.Set(IngredientCountNetworked, netData);
        IngredientCountNetworked++;

        // [호스트 로컬 보온] 
        _ingredients.Add(new PlatedIngredient(id, state, seqList, cutMethod ?? "", source.GetType().Name, CookTimer));

        SyncFromNetworkState();
        Consume(source);
    }

    private void CommitIngredientLocal(PickableItem source)
    {
        if (source == null || IsFull) return;
        string id = ResolveId(source);
        if (string.IsNullOrWhiteSpace(id)) return;

        CookState state = source is ICookable c ? c.CurrentCookState : CookState.Raw;
        source.metadata.TryGetValue("cutMethod", out string cutMethod);
        var seqList = source.cookingSeq != null ? new List<string>(source.cookingSeq) : new List<string>();

        _ingredients.Add(new PlatedIngredient(id, state, seqList, cutMethod ?? "", source.GetType().Name));
        RefreshVisuals();
        Consume(source);
    }

    private void ExecuteClear()
    {
        IngredientCountNetworked = 0;
        for (int i = 0; i < MaxIngredients; i++)
            IngredientSlots.Set(i, default);
        CookTimer              = 0f;
        BonusSpeed             = 0f;
        CookStateNetworked     = CookState.Raw;
        IsBeingCookedNetworked = false;
        _ingredients.Clear();
        // 플로팅 비주얼을 명시적으로 제거 (RefreshFloatingIngredients의 while 루프 의존 금지)
        foreach (var v in _floatingVisuals) if (v != null) Destroy(v);
        _floatingVisuals.Clear();
        _floatBaseY.Clear();
        _floatPhase.Clear();
        _ingredientVisualCounts.Clear();
        RefreshVisuals();
    }

    private void SyncFromNetworkState()
    {
        if (!IsNetworkReady) return;
        _ingredients.Clear();
        int safe = Mathf.Clamp(IngredientCountNetworked, 0, MaxIngredients);
        for (int i = 0; i < safe; i++)
        {
            var slot = IngredientSlots[i];
            if (!slot.Used) continue;
            _ingredients.Add(new PlatedIngredient(
                slot.IngredientID.ToString(),
                (CookState)slot.CookState,
                null, 
                "", 
                slot.ItemClassName.ToString(),
                slot.CookTime
            ));
        }
        RefreshVisuals();
    }

    // ── 조리 진행 ─────────────────────────────────────────────────

    private void AdvanceCook(float heat)
    {
        if (IsNetworkReady)
        {
            if (!HasStateAuthority) return;
            CookTimer += heat;
            AdvanceCookState();
        }
        else
        {
            _localCookTimer += heat;
            AdvanceCookStateLocal();
        }
    }

    private void AdvanceCookState()
    {
        bool stateChanged = false;
        if (CookTimer >= burnedThreshold && CookStateNetworked != CookState.Burned)
        {
            CookStateNetworked = CookState.Burned;
            AddCookingSeqToAll("가열");
            stateChanged = true;
        }
        else if (CookTimer >= cookedThreshold && CookStateNetworked == CookState.Raw)
        {
            CookStateNetworked = CookState.Cooked;
            AddCookingSeqToAll("가열");
            stateChanged = true;
        }

        // 상태가 변하거나 조리 중일 때 네트워크 데이터 시간 갱신 (핵심!)
        if (HasStateAuthority)
        {
            int count = Mathf.Clamp(IngredientCountNetworked, 0, MaxIngredients);
            for (int i = 0; i < count; i++)
            {
                var slot = IngredientSlots[i];
                if (!slot.Used) continue;
                slot.CookState = (int)CookStateNetworked;
                slot.CookTime = CookTimer;
                IngredientSlots.Set(i, slot);
            }
            UpdateLiquidColor();
        }
    }

    private void AdvanceCookStateLocal()
    {
        if (_localCookTimer >= burnedThreshold && _localCookState != CookState.Burned)
        {
            _localCookState = CookState.Burned;
            AddCookingSeqToAll("가열"); // [추가] 모든 재료에 가열 기록 추가
            UpdateLiquidColor();
        }
        else if (_localCookTimer >= cookedThreshold && _localCookState == CookState.Raw)
        {
            _localCookState = CookState.Cooked;
            AddCookingSeqToAll("가열"); // [추가] 모든 재료에 가열 기록 추가
            UpdateLiquidColor();
        }
    }

    /// <summary>
    /// 냄비 안의 모든 재료들의 조리 기록에 특정 행위를 추가합니다. 중복 방지 포함.
    /// </summary>
    private void AddCookingSeqToAll(string action)
    {
        if (string.IsNullOrEmpty(action)) return;
        
        foreach (var ingr in _ingredients)
        {
            if (ingr.cookingSeq == null) continue;
            if (!ingr.cookingSeq.Contains(action))
            {
                ingr.cookingSeq.Add(action);
                Debug.Log($"[PotStation] 재료 '{ingr.ingredientID}'에 조리 기록 추가됨: {action}");
            }
        }
    }

    // ── 비주얼 ────────────────────────────────────────────────────

    private void RefreshVisuals()
    {
        UpdateLiquidLevel();
        UpdateLiquidColor();
        RefreshFloatingIngredients();
    }

    private void UpdateLiquidLevel()
    {
        if (liquidTransform == null) return;
        if (_liquidRenderer != null) _liquidRenderer.enabled = true;

        if (_liquidRenderer != null) _liquidRenderer.enabled = !IsEmpty;
        if (IsEmpty) return;

        float scaleY = liquidMaxScaleY;
        Vector3 s = liquidTransform.localScale; s.y = scaleY; liquidTransform.localScale = s;
        Vector3 p = liquidTransform.localPosition; p.y = liquidBottomY + scaleY; liquidTransform.localPosition = p;
    }

    private void UpdateLiquidColor()
    {
        if (_liquidRenderer == null) return;
        CookState state = IsNetworkReady ? CookStateNetworked : _localCookState;
        Color target = state switch
        {
            CookState.Cooked => liquidColorCooked,
            CookState.Burned => liquidColorBurned,
            _                => liquidColorRaw,
        };
        _mpb.SetColor(ColorProp, target);
        _liquidRenderer.SetPropertyBlock(_mpb);
    }

    // floatingParent 로컬 기준 재료 부유 Y
    // liquidBottomY / liquidMaxScaleY 는 PotBox 루트 로컬 기준이므로
    // floatingParent.localPosition.y 를 빼서 floatingParent 로컬로 변환한다
    private float GetFloatY()
    {
        float bottomInRoot  = liquidBottomY;
        float surfaceInRoot = liquidBottomY + liquidMaxScaleY * 2f;
        float targetInRoot  = Mathf.Lerp(bottomInRoot, surfaceInRoot, floatHeightRatio);
        float parentOffsetY = floatingParent != null ? floatingParent.localPosition.y : 0f;
        return targetInRoot - parentOffsetY;
    }

    private void RefreshFloatingIngredients()
    {
        if (floatingParent == null) return;

        // 재료 수가 줄었을 때: 뒤에서부터 그룹 단위로 비주얼 제거
        while (_ingredientVisualCounts.Count > _ingredients.Count)
        {
            int groupIdx = _ingredientVisualCounts.Count - 1;
            int copies   = _ingredientVisualCounts[groupIdx];
            for (int k = 0; k < copies; k++)
            {
                int last = _floatingVisuals.Count - 1;
                if (last < 0) break;
                if (_floatingVisuals[last] != null) Destroy(_floatingVisuals[last]);
                _floatingVisuals.RemoveAt(last);
                _floatBaseY.RemoveAt(last);
                _floatPhase.RemoveAt(last);
            }
            _ingredientVisualCounts.RemoveAt(groupIdx);
        }

        // 기존 비주얼 baseY 갱신 (floatHeightRatio 변경 시 반영)
        float floatY = GetFloatY();
        for (int i = 0; i < _floatBaseY.Count; i++)
            _floatBaseY[i] = floatY;

        // 재료가 새로 추가됐을 때: 해당 재료의 cfg로 amount개 생성
        for (int i = _ingredientVisualCounts.Count; i < _ingredients.Count; i++)
        {
            PotIngredientVisualConfig cfg = GetConfigByClassName(_ingredients[i].itemClassName);

            GameObject prefab  = cfg?.prefab;
            int        copies  = (cfg != null && cfg.amount  > 0) ? cfg.amount  : 1;
            float      radius  = (cfg != null && cfg.distributionRadius > 0f) ? cfg.distributionRadius : potRadius;
            float      size    = (cfg != null && cfg.size    > 0f) ? cfg.size    : 1f;

            int spawnedCount = 0;
            for (int k = 0; k < copies; k++)
            {
                if (prefab == null)
                {
                    _floatingVisuals.Add(null);
                    _floatBaseY.Add(floatY);
                    _floatPhase.Add(0f);
                    spawnedCount++;
                    continue;
                }

                // 원형 균등 분포: copies개를 등간격 각도로, 재료 그룹마다 위상 오프셋
                float angleStep   = copies > 1 ? Mathf.PI * 2f / copies : 0f;
                float groupOffset = i * (Mathf.PI * 2f / Mathf.Max(1, MaxIngredients));
                float angle = k * angleStep + groupOffset;
                float r     = copies == 1 ? 0f : radius;

                // 회전 결정적 난수 (위치는 결정적 공식으로 고정)
                var rng = new System.Random(i * 100 + k);

                GameObject visual = Instantiate(prefab, floatingParent);
                visual.transform.localPosition = new Vector3(Mathf.Cos(angle) * r, floatY, Mathf.Sin(angle) * r);
                visual.transform.localRotation = Quaternion.Euler(
                    (float)(rng.NextDouble() * 30f - 15f),
                    (float)(rng.NextDouble() * 360f),
                    (float)(rng.NextDouble() * 30f - 15f));
                visual.transform.localScale = Vector3.one * size;
                foreach (var rb  in visual.GetComponentsInChildren<Rigidbody>())  rb.isKinematic = true;
                foreach (var col in visual.GetComponentsInChildren<Collider>())   col.enabled = false;
                foreach (var ol  in visual.GetComponentsInChildren<Outline>())    Destroy(ol);

                _floatingVisuals.Add(visual);
                _floatBaseY.Add(floatY);
                _floatPhase.Add(i * 1.7f + k * 0.9f);
                spawnedCount++;
            }
            _ingredientVisualCounts.Add(spawnedCount);
        }
    }

    private void AnimateFloating()
    {
        for (int i = 0; i < _floatingVisuals.Count; i++)
        {
            if (_floatingVisuals[i] == null) continue;
            Vector3 local = _floatingVisuals[i].transform.localPosition;
            local.y = _floatBaseY[i] + Mathf.Sin(Time.time * floatSpeed + _floatPhase[i]) * floatAmplitude;
            _floatingVisuals[i].transform.localPosition = local;
        }
    }

    // ── 유틸 ──────────────────────────────────────────────────────

    private string ResolveId(PickableItem source)
    {
        if (source == null) return string.Empty;

        // 1순위: 메타데이터
        if (source.metadata != null &&
            source.metadata.TryGetValue("ingredientID", out string mid) &&
            !string.IsNullOrWhiteSpace(mid)) return mid.Trim();

        // 2순위: 리플렉션으로 ingredientID 속성 확인 (PlateItem과 동일 로직)
        var type = source.GetType();
        var prop = type.GetProperty("ingredientID", 
            System.Reflection.BindingFlags.Instance | 
            System.Reflection.BindingFlags.Public | 
            System.Reflection.BindingFlags.NonPublic);

        if (prop != null && prop.PropertyType == typeof(string))
        {
            try
            {
                string val = (string)prop.GetValue(source);
                if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
            }
            catch { /* ignore */ }
        }

        // 3순위: 기본 itemName (최후의 보단)
        if (!string.IsNullOrWhiteSpace(source.itemName)) return source.itemName.Trim();

        return source.gameObject.name.Replace("(Clone)", "").Trim();
    }

    private void Consume(PickableItem source)
    {
        if (source == null) return;
        if (IsNetworkReady && source.Object != null && source.Object.IsValid)
            Runner.Despawn(source.Object);
        else
            Destroy(source.gameObject);
    }

    private NetworkString<_64> Truncate(string s)
    {
        s ??= string.Empty;
        if (s.Length > 64) s = s.Substring(0, 64);
        return s;
    }

    private PotIngredientVisualConfig GetConfigByClassName(string itemClassName)
    {
        if (ingredientVisualConfigs == null || string.IsNullOrEmpty(itemClassName)) return null;
        foreach (var cfg in ingredientVisualConfigs)
        {
            if (cfg?.prefab == null) continue;
            PickableItem item = cfg.prefab.GetComponent<PickableItem>();
            if (item != null && item.GetType().Name == itemClassName) return cfg;
        }
        return null;
    }
}
