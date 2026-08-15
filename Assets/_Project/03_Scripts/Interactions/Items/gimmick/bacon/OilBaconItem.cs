using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 기름 베이컨 본체.
/// - 칼 가이드라인 표시 / 절단 가능
/// - 조리 상태 유지
/// - 줍는 순간 양손 아이템 강제 드롭 (HandsManager의 OilBaconItem 판정 사용)
/// - 한 손에 이미 기름 베이컨 계열이 있으면 추가 픽업 불가
/// </summary>
public class OilBaconItem : PickableItem, ICookable, ICuttable
{
    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float timeToCook = 30f;
    [SerializeField, Min(0.1f)] private float timeToBurn = 60f;

    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked]
    public float CookProgressNetworked { get; set; }

    private CookState _localCookState = CookState.Raw;
    private float _localCookProgress = 0f;
    private bool _initializedFromParent = false;

    // ─────────────────────────────────────────
    // 비주얼
    // ─────────────────────────────────────────

    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color CookedColor = new Color(0.8f, 0.4f, 0.2f);
    private static readonly Color BurnedColor = new Color(0.15f, 0.12f, 0.08f, 1f);

    private MeshRenderer[] _renderers;
    private MaterialPropertyBlock _mpb;

    // ─────────────────────────────────────────
    // 절단 가이드라인
    // ─────────────────────────────────────────

    private CutGuidelineVisual[] _guidelineVisuals;

    // ─────────────────────────────────────────
    // 확장 포인트
    // ─────────────────────────────────────────

    protected virtual string RawItemName => "기름 베이컨";
    protected virtual string CookedItemName => "구운 기름 베이컨";
    protected virtual string BurnedItemName => "타버린 기름 베이컨";
    protected virtual string IngredientId => "기름 베이컨";
    protected virtual bool SupportsCutting => true;

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState prev = CurrentCookState;
            if (prev == value) return;

            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookStateNetworked = value;
            }

            _localCookState = value;
            RecordCookStateChange(prev, value);
            UpdateVisuals();
        }
    }

    public float CookProgress
    {
        get => IsNetworkReady ? CookProgressNetworked : _localCookProgress;
        protected set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookProgressNetworked = value;
            }

            _localCookProgress = value;
        }
    }

    public override bool CanChop
    {
        get
        {
            if (!SupportsCutting) return false;
            if (CurrentCookState == CookState.Burned) return false;
            if (_guidelineConfigs == null || _guidelineConfigs.Length == 0) return false;

            for (int i = 0; i < _guidelineConfigs.Length; i++)
            {
                if (_guidelineConfigs[i].resultPrefabs != null &&
                    _guidelineConfigs[i].resultPrefabs.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    protected virtual void Start()
    {
        itemName = RawItemName;
        metadata["ingredientID"] = IngredientId;

        CacheRenderers();
        EnsureGuidelineVisuals();
        UpdateVisuals();
    }

    private void OnValidate()
    {
        if (timeToBurn < timeToCook)
            timeToBurn = timeToCook;
    }

    public override void Spawned()
    {
        CacheRenderers();
        EnsureGuidelineVisuals();

        if (HasStateAuthority && !_initializedFromParent)
        {
            CookStateNetworked = CookState.Raw;
            CookProgressNetworked = 0f;
        }

        _localCookState = IsNetworkReady ? CookStateNetworked : _localCookState;
        _localCookProgress = IsNetworkReady ? CookProgressNetworked : _localCookProgress;

        UpdateVisuals();
    }

    private void OnCookStateChanged()
    {
        _localCookState = CookStateNetworked;
        UpdateVisuals();
    }

    protected void CacheRenderers()
    {
        if (_renderers != null) return;

        _renderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb = new MaterialPropertyBlock();
    }

    protected void UpdateVisuals()
    {
        CacheRenderers();

        for (int i = 0; i < _renderers.Length; i++)
        {
            MeshRenderer renderer = _renderers[i];
            if (renderer == null) continue;

            if (CurrentCookState == CookState.Cooked)
            {
                _mpb.Clear();
                _mpb.SetColor(ColorId, CookedColor);
                renderer.SetPropertyBlock(_mpb);
            }
            else if (CurrentCookState == CookState.Burned)
            {
                _mpb.Clear();
                _mpb.SetColor(ColorId, BurnedColor);
                renderer.SetPropertyBlock(_mpb);
            }
            else
            {
                renderer.SetPropertyBlock(null);
            }
        }

        UpdateItemName();
        metadata["ingredientID"] = IngredientId;
    }

    protected void UpdateItemName()
    {
        switch (CurrentCookState)
        {
            case CookState.Burned:
                itemName = BurnedItemName;
                break;
            case CookState.Cooked:
                itemName = CookedItemName;
                break;
            default:
                itemName = RawItemName;
                break;
        }
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
    }

    public void CookInFire(float heat)
    {
        if (heat <= 0f) return;
        if (CurrentCookState == CookState.Burned) return;

        if (IsNetworkReady && !HasStateAuthority)
            return;

        CookProgress += heat;

        if (CookProgress >= timeToBurn)
            CurrentCookState = CookState.Burned;
        else if (CookProgress >= timeToCook)
            CurrentCookState = CookState.Cooked;
    }

    public override void TransferStateTo(PickableItem target)
    {
        if (target is OilBaconItem other)
        {
            other.ApplyInheritedState(CurrentCookState, CookProgress);
        }
    }

    public void ApplyInheritedState(CookState inheritedState, float inheritedCookProgress)
    {
        _initializedFromParent = true;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority) return;

            CookStateNetworked = inheritedState;
            CookProgressNetworked = inheritedCookProgress;
        }

        _localCookState = inheritedState;
        _localCookProgress = inheritedCookProgress;
        UpdateVisuals();
    }

    private void EnsureGuidelineVisuals()
    {
        if (!SupportsCutting)
        {
            ClearGuidelineObjects();
            _guidelineVisuals = new CutGuidelineVisual[0];
            return;
        }

        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0)
        {
            ClearGuidelineObjects();
            _guidelineVisuals = new CutGuidelineVisual[0];
            return;
        }

        bool validCache = _guidelineVisuals != null && _guidelineVisuals.Length == _guidelineConfigs.Length;
        if (validCache)
        {
            for (int i = 0; i < _guidelineVisuals.Length; i++)
            {
                if (_guidelineVisuals[i] == null)
                {
                    validCache = false;
                    break;
                }
            }
        }

        if (validCache) return;

        ClearGuidelineObjects();

        _guidelineVisuals = new CutGuidelineVisual[_guidelineConfigs.Length];

        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _guidelineConfigs[i].localPosition;
            go.transform.localEulerAngles = _guidelineConfigs[i].localEulerAngles;

            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType = "Knife";
            vis.cutName = _guidelineConfigs[i].cutName;
            vis.resultPrefabs = _guidelineConfigs[i].resultPrefabs;
            vis.resultCounts = _guidelineConfigs[i].resultCounts;

            _guidelineVisuals[i] = vis;
        }
    }

    private void ClearGuidelineObjects()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("CutGuideline_")) continue;

            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        EnsureGuidelineVisuals();

        if (_guidelineVisuals == null || _guidelineVisuals.Length == 0)
            return new CutGuidelineVisual[0];

        int count = 0;
        for (int i = 0; i < _guidelineVisuals.Length; i++)
        {
            if (_guidelineVisuals[i] != null && _guidelineVisuals[i].toolType == toolType)
                count++;
        }

        if (count == 0)
            return new CutGuidelineVisual[0];

        CutGuidelineVisual[] result = new CutGuidelineVisual[count];
        int idx = 0;

        for (int i = 0; i < _guidelineVisuals.Length; i++)
        {
            if (_guidelineVisuals[i] != null && _guidelineVisuals[i].toolType == toolType)
                result[idx++] = _guidelineVisuals[i];
        }

        return result;
    }

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary)
        {
            if (player == null) return false;
            if (isHeld) return false;

            // 이미 기름 베이컨 계열을 한 손이라도 들고 있으면 추가 픽업 불가
            if (player.HasItemInEitherHand<OilBaconItem>())
                return false;

            // 일반 PickableItem의 "활성 손이 차 있으면 불가" 규칙을 무시해야
            // 베이컨 픽업 시 HandsManager의 강제 드롭 기믹이 동작함
            return true;
        }

        return base.CanInteract(player, type);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary && CanInteract(player, type))
            return $"[{CookingMasterHandsManager.PickupKey}] {itemName} 줍기";

        return base.GetInteractionLabel(player, type);
    }
}