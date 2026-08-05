 using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 용암 치즈.
///
/// [주요 기능]
/// - 안전해지기 전에는 ColdPouchItem이 있어야 집을 수 있음
/// - 칼로 자르면 CutGuidelineConfig.resultPrefabs 에 지정된 CutCheeseItem 조각들로 분리
/// - 익힘 / 태움은 FixedUpdateNetwork에서 열량 버퍼 방식으로 처리
///
/// [네트워크]
/// - 절단은 PickableItem 기본 Rpc_Cut / LocalCut 경로를 그대로 사용 (CabbageItem과 동일)
/// - TransferStateTo 로 CutCheeseItem 에 조리 상태 전달
/// </summary>
public class LavaCheeseItem : PickableItem, ICookable, ICuttable
{
    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float cookSeconds = 60f;
    [SerializeField, Min(0.1f)] private float burnSeconds = 90f;

    [Header("Slice Settings")]
    [SerializeField] private bool canBeSliced = true;
    [SerializeField] private bool startsVented = false;

    [Header("Visual Settings")]
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private Color rawColor    = new Color(1f, 0.4f, 0f);
    [SerializeField] private Color cookedColor = new Color(0.8f, 0.2f, 0f);
    [SerializeField] private Color burnedColor = Color.black;
    [SerializeField] private GameObject hotEffect;

    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };
    private CutGuidelineVisual[] _guidelineVisuals;

    // ─────────────────────────────────────────
    // ICookable 네트워크 상태
    // ─────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    private CookState _localCookState = CookState.Raw;

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState prev = CurrentCookState;
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookStateNetworked = value;
            }
            _localCookState = value;
            RecordCookStateChange(prev, value);
            RefreshVisualState();
        }
    }

    // ─────────────────────────────────────────
    // IsVented — 자른 이후 열기가 빠져 안전해진 상태
    // ─────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnVentStateChanged))]
    public bool IsVentedNetworked { get; set; }

    private bool _localIsVented = false;

    public bool IsVented
    {
        get => IsNetworkReady ? IsVentedNetworked : _localIsVented;
        private set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                IsVentedNetworked = value;
            }
            _localIsVented = value;
            UpdateHotEffect();
        }
    }

    public bool IsSafeToTouch => IsVented;

    // ─────────────────────────────────────────
    // CanChop — CutGuidelineConfig.resultPrefabs가 있어야 자를 수 있음
    // ─────────────────────────────────────────

    public override bool CanChop =>
        canBeSliced &&
        CurrentCookState != CookState.Burned &&
        _guidelineConfigs != null &&
        _guidelineConfigs.Length > 0 &&
        _guidelineConfigs[0].resultPrefabs != null &&
        _guidelineConfigs[0].resultPrefabs.Length > 0;

    // ─────────────────────────────────────────
    // 열량 버퍼 (FixedUpdateNetwork용)
    // ─────────────────────────────────────────

    private float _queuedHeat = 0f;
    private bool _initializedFromParent = false;

    // ─────────────────────────────────────────
    // ICuttable 구현
    // ─────────────────────────────────────────

    private void CreateGuidelineVisuals()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0) return;
        _guidelineVisuals = new CutGuidelineVisual[_guidelineConfigs.Length];
        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform);
            go.transform.localPosition    = _guidelineConfigs[i].localPosition;
            go.transform.localEulerAngles = _guidelineConfigs[i].localEulerAngles;
            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType      = "Knife";
            vis.cutName       = _guidelineConfigs[i].cutName;
            vis.resultPrefabs = _guidelineConfigs[i].resultPrefabs;
            vis.resultCounts  = _guidelineConfigs[i].resultCounts;
            _guidelineVisuals[i] = vis;
        }
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType) =>
        _guidelineVisuals != null
            ? System.Array.FindAll(_guidelineVisuals, g => g != null && g.toolType == toolType)
            : System.Array.Empty<CutGuidelineVisual>();

    // ─────────────────────────────────────────
    // Unity / Fusion 생명주기
    // ─────────────────────────────────────────

    private void Start()
    {
        itemName = "용암 치즈";
        metadata["ingredientID"] = "용암 치즈";

        if (!IsNetworkReady)
        {
            _localCookState  = CookState.Raw;
            _localIsVented   = startsVented;
        }

        CreateGuidelineVisuals();
        RefreshVisualState();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessHeat();
    }

    private void OnValidate()
    {
        if (burnSeconds < cookSeconds) burnSeconds = cookSeconds;
    }

    public override void Spawned()
    {
        if (HasStateAuthority && !_initializedFromParent)
        {
            CurrentCookState = CookState.Raw;
            IsVented = startsVented;
        }
        CreateGuidelineVisuals();
        RefreshVisualState();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
            ProcessHeat();
        else
            _queuedHeat = 0f;

        base.FixedUpdateNetwork();
    }

    // ─────────────────────────────────────────
    // ICookable — CookInFire
    // ─────────────────────────────────────────

    public void CookInFire(float heat)
    {
        if (heat <= 0f) return;
        if (CurrentCookState == CookState.Burned) return;
        if (IsNetworkReady && !HasStateAuthority) return;
        _queuedHeat += heat;
    }

    private void ProcessHeat()
    {
        if (_queuedHeat <= 0f || CurrentCookState == CookState.Burned)
        {
            _queuedHeat = 0f;
            return;
        }

        cookTime += _queuedHeat;
        _queuedHeat = 0f;

        float burnAt = Mathf.Max(burnSeconds, cookSeconds);
        if (cookTime >= burnAt)
            CurrentCookState = CookState.Burned;
        else if (cookTime >= cookSeconds)
            CurrentCookState = CookState.Cooked;
    }

    // ─────────────────────────────────────────
    // 상호작용
    // ─────────────────────────────────────────

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        // 집기 (E키): 냉기 주머니 없이는 불가
        if (type == InteractionType.Primary && !isHeld)
        {
            if (!IsSafeToTouch && !HasColdPouch(player))
                return false;
        }

        // 썰기: CanChop + 가이드라인 선택 여부 (CabbageItem과 동일)
        if (type == InteractionType.UseItem && IsOnStation)
        {
            PickableItem held = player.GetActiveHandItem();
            if (held is ITool tool && tool.ToolType == "Knife")
                return CanChop && GetGuidelineVisuals("Knife").Length > 0;
        }

        return base.CanInteract(player, type);
    }

    public override void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem && IsOnStation)
        {
            PickableItem held = player.GetActiveHandItem();
            if (held is ITool tool && tool.ToolType == "Knife")
            {
                if (SelectedGuidelineIndex < 0) return;

                // 표준 Rpc_Cut / LocalCut 경로 — CabbageItem과 동일
                if (IsNetworkReady) Rpc_Cut(SelectedGuidelineIndex, tool.ToolType);
                else LocalCut(SelectedGuidelineIndex, tool.ToolType);
                return;
            }
        }

        base.Interact(player, type);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary && !isHeld && !player.IsActiveHandFull())
        {
            if (!IsSafeToTouch && !HasColdPouch(player))
                return $"<color=red>[{CookingMasterHandsManager.PickupKey}] {itemName} 줍기 불가 (냉기 주머니 필요!)</color>";
        }

        return base.GetInteractionLabel(player, type);
    }

    private bool HasColdPouch(CookingMasterHandsManager player) =>
        player != null && player.HasItemInEitherHand<ColdPouchItem>();

    // ─────────────────────────────────────────
    // 상태 전달 — Rpc_Cut / LocalCut 이 결과물(CutCheeseItem)에 호출
    // ─────────────────────────────────────────

    public override void TransferStateTo(PickableItem target)
    {
        if (target is CutCheeseItem cut)
        {
            cut.ApplyInheritedState(CurrentCookState, cookTime);
            cut.cookingSeq = new List<string>(cookingSeq);
            foreach (var kv in metadata) cut.metadata[kv.Key] = kv.Value;
        }
    }

    // ─────────────────────────────────────────
    // 부모 상태 상속 (FloatingPoint 스폰 후 호출용)
    // ─────────────────────────────────────────

    public void ApplyInheritedState(CookState inheritedState, float inheritedCookTime, bool vented = false)
    {
        _initializedFromParent = true;

        _localCookState = inheritedState;
        _localIsVented  = vented;
        cookTime        = inheritedCookTime;

        if (IsNetworkReady && HasStateAuthority)
        {
            CookStateNetworked  = inheritedState;
            IsVentedNetworked   = vented;
        }

        RefreshVisualState();
    }

    // ─────────────────────────────────────────
    // 렌더 / 외형
    // ─────────────────────────────────────────

    private void OnCookStateChanged()
    {
        _localCookState = CookStateNetworked;
        RefreshVisualState();
    }

    private void OnVentStateChanged()
    {
        _localIsVented = IsVentedNetworked;
        UpdateHotEffect();
    }

    private void RefreshVisualState()
    {
        UpdateItemName();
        UpdateVisuals();
        UpdateHotEffect();
    }

    private void UpdateVisuals()
    {
        if (targetRenderer == null) return;
        switch (CurrentCookState)
        {
            case CookState.Raw:    targetRenderer.material.color = rawColor;    break;
            case CookState.Cooked: targetRenderer.material.color = cookedColor; break;
            case CookState.Burned: targetRenderer.material.color = burnedColor; break;
        }
    }

    private void UpdateHotEffect()
    {
        if (hotEffect != null)
            hotEffect.SetActive(!IsVented && CurrentCookState != CookState.Burned);
    }

    private void UpdateItemName()
    {
        switch (CurrentCookState)
        {
            case CookState.Cooked: itemName = "익은 용암 치즈"; break;
            case CookState.Burned: itemName = "타버린 용암 치즈"; break;
            default:               itemName = "용암 치즈";     break;
        }
    }
}