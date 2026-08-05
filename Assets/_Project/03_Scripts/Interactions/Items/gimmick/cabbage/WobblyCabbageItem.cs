using UnityEngine;
using Fusion;
using Interactions;
using System.Collections.Generic;

public enum WobblyCabbageStage
{
    Whole,
    Half
}

/// <summary>
/// 비틀비틀 양배추
///
/// [규칙]
/// - Whole 상태에서 자르면 Half 2개로 분리
/// - Half를 또 자르면 풍미 상실(너무 잘게 썬 상태)
/// - 자르는 순간 플레이어 이동 입력 방향이 무작위 반전
/// - 단, 조리 매뉴얼을 들고 있으면 디버프 무효
/// - 조리 시간 기본값은 90~120초
/// </summary>
public class WobblyCabbageItem : PickableItem, ICookable, ICuttable
{
    [Header("Cabbage Settings")]
    [SerializeField] private NetworkObject cabbagePrefab;
    [SerializeField] private WobblyCabbageStage initialStage = WobblyCabbageStage.Whole;
    [SerializeField, Min(0.1f)] private float disorientationDuration = 5f;

    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float cookSeconds = 90f;
    [SerializeField, Min(0.1f)] private float burnSeconds = 120f;

    [Header("Visual Models")]
    [SerializeField] private GameObject wholeModel;
    [SerializeField] private GameObject halfModel;
    [SerializeField] private GameObject ruinedModel;

    [Header("Visual Colors")]
    [SerializeField] private Color rawColor = new Color(0.75f, 1f, 0.75f);
    [SerializeField] private Color cookedColor = new Color(0.5f, 0.8f, 0.35f);
    [SerializeField] private Color burnedColor = Color.black;
    [SerializeField] private Color ruinedColor = new Color(0.7f, 0.7f, 0.5f);
    
    [Header("Cutting Guidelines")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };
    private CutGuidelineVisual[] _guidelineVisuals;

    // ─────────────────────────────────────────
    // Stage
    // ─────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnVisualStateChanged))]
    public WobblyCabbageStage StageNetworked { get; set; }

    private WobblyCabbageStage _localStage = WobblyCabbageStage.Whole;

    public WobblyCabbageStage CurrentStage
    {
        get => IsNetworkReady ? StageNetworked : _localStage;
        private set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                StageNetworked = value;
            }

            _localStage = value;
            RefreshVisualState();
        }
    }

    [Networked, OnChangedRender(nameof(OnVisualStateChanged))]
    public bool FlavorLostNetworked { get; set; }

    private bool _localFlavorLost = false;

    public bool FlavorLost
    {
        get => IsNetworkReady ? FlavorLostNetworked : _localFlavorLost;
        private set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                FlavorLostNetworked = value;
            }

            _localFlavorLost = value;
            RefreshVisualState();
        }
    }

    // ─────────────────────────────────────────
    // ICookable
    // ─────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnVisualStateChanged))]
    public CookState CookStateNetworked { get; set; }

    private CookState _localCookState = CookState.Raw;

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState _prev = CurrentCookState;
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookStateNetworked = value;
            }

            _localCookState = value;
            RecordCookStateChange(_prev, value);
            RefreshVisualState();
        }
    }

    [Networked]
    public bool IsBeingCookedNetworked { get; set; }

    private bool _localIsBeingCooked = false;

    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : _localIsBeingCooked;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                IsBeingCookedNetworked = value;
            }

            _localIsBeingCooked = value;
        }
    }

    [Networked]
    public float CookProgressNetworked { get; set; }

    private float _localCookProgress = 0f;

    public float CookProgress
    {
        get => IsNetworkReady ? CookProgressNetworked : _localCookProgress;
        private set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookProgressNetworked = value;
            }

            _localCookProgress = value;
        }
    }

    // ─────────────────────────────────────────
    // 내부 버퍼
    // ─────────────────────────────────────────

    private float _queuedHeat = 0f;
    private bool _sliceRequested = false;

    private bool _localInitApplied = false;
    private bool _initializedFromParent = false;

    public override bool CanChop
    {
        get
        {
            if (isHeld) return false;
            if (CurrentCookState == CookState.Burned) return false;
            if (FlavorLost) return false;

            if (CurrentStage == WobblyCabbageStage.Whole)
                return cabbagePrefab != null;

            return CurrentStage == WobblyCabbageStage.Half;
        }
    }

    public float BurnSeconds => burnSeconds;
    
    // ── ICuttable 구현 ──
    private void CreateGuidelineVisuals()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0) return;
        _guidelineVisuals = new CutGuidelineVisual[_guidelineConfigs.Length];
        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform);
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

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType) =>
        _guidelineVisuals != null
            ? System.Array.FindAll(_guidelineVisuals, g => g != null && g.toolType == toolType)
            : System.Array.Empty<CutGuidelineVisual>();

    private void Start()
    {
        itemName = "비틀비틀 양배추";
        metadata["ingredientID"] = "비틀비틀 양배추";

        RefreshVisualState();
        CreateGuidelineVisuals();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessSimulationStep();
    }

    private void OnValidate()
    {
        if (burnSeconds < cookSeconds)
            burnSeconds = cookSeconds;
    }

    public override void Spawned()
    {
        if (HasStateAuthority && !_initializedFromParent)
        {
            CurrentStage = initialStage;
            FlavorLost = false;
            CurrentCookState = CookState.Raw;
            CookProgress = 0f;
            isBeingCooked = false;
        }

        RefreshVisualState();
    }

    public override void Render()
    {
        base.Render();

        if (!IsNetworkReady)
            return;

        bool dirty = false;

        if (_localStage != StageNetworked)
        {
            _localStage = StageNetworked;
            dirty = true;
        }

        if (_localFlavorLost != FlavorLostNetworked)
        {
            _localFlavorLost = FlavorLostNetworked;
            dirty = true;
        }

        if (_localCookState != CookStateNetworked)
        {
            _localCookState = CookStateNetworked;
            dirty = true;
        }

        if (dirty)
            RefreshVisualState();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
            ProcessSimulationStep();
        else
            _queuedHeat = 0f;
        base.FixedUpdateNetwork();
    }

    public void CookInFire(float heat)
    {
        if (heat <= 0f)
            return;

        if (CurrentCookState == CookState.Burned)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
                return;

            _queuedHeat += heat;
            return;
        }

        _queuedHeat += heat;
    }

    /// <summary>
    /// CuttingStation에서 실제 Chop 직전에 호출해 주세요.
    /// 책이 없으면 이동 방향 반전 디버프를 플레이어에게 적용합니다.
    /// </summary>
    public void ApplySliceDisorientation(CookingMasterHandsManager player)
    {
        if (player == null)
            return;

        if (player.HasItemInEitherHand<ManualBookItem>())
            return;

        CookingMasterMovement movement = player.GetComponentInParent<CookingMasterMovement>();
        if (movement == null)
            return;

        bool invertForward = Random.value > 0.5f;
        bool invertStrafe = Random.value > 0.5f;

        // 최소 한 축은 뒤집히게 보장
        if (!invertForward && !invertStrafe)
            invertForward = true;

        movement.ApplyDirectionInvertEffect(disorientationDuration, invertForward, invertStrafe);
    }

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem && IsOnStation)
        {
            PickableItem held = player.GetActiveHandItem();
            if (held is ITool tool && tool.ToolType == "Knife")
            {
                return CanChop;
            }
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
                
                // 썰기 시도 전 플레이어에게 조작 반전 주입
                ApplySliceDisorientation(player);
                
                // 실제 Chop 요청
                Chop(null);
                return;
            }
        }
        base.Interact(player, type);
    }

    public override void Chop(CuttingStation board)
    {
        if (!CanChop)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                RPC_RequestSlice();
                return;
            }

            _sliceRequested = true;
            return;
        }

        _sliceRequested = true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSlice()
    {
        if (!CanChop)
            return;

        _sliceRequested = true;
    }

    private void ProcessSimulationStep()
    {
        bool hasValidHeatThisStep = _queuedHeat > 0f && CurrentCookState != CookState.Burned;
        isBeingCooked = hasValidHeatThisStep;

        if (hasValidHeatThisStep)
        {
            CookProgress += _queuedHeat;
            EvaluateCookingState();
        }

        _queuedHeat = 0f;

        if (_sliceRequested)
        {
            _sliceRequested = false;

            if (CanChop)
            {
                ExecuteSlice();
                return;
            }
        }
    }

    private void EvaluateCookingState()
    {
        float burnAt = Mathf.Max(burnSeconds, cookSeconds);

        if (CookProgress >= burnAt)
        {
            CurrentCookState = CookState.Burned;
            isBeingCooked = false;
            return;
        }

        if (CookProgress >= cookSeconds)
        {
            CurrentCookState = CookState.Cooked;
        }
    }

    private void ExecuteSlice()
    {
        if (CurrentStage == WobblyCabbageStage.Whole)
        {
            SplitWholeIntoHalves();
            return;
        }

        if (CurrentStage == WobblyCabbageStage.Half)
        {
            // 너무 잘게 썰면 풍미 상실
            FlavorLost = true;
        }
    }

    private void SplitWholeIntoHalves()
    {
        if (cabbagePrefab == null)
            return;

        // 절단 기록 추가
        if (!cookingSeq.Contains("조각내기")) cookingSeq.Add("조각내기");
        metadata["cutMethod"] = "조각내기";

        Vector3 basePos = transform.position;
        Quaternion baseRot = transform.rotation;

        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -0.18f : 0.18f;
            Vector3 spawnPos = basePos + transform.right * side + Vector3.up * 0.05f;

            if (IsNetworkReady && Runner != null && Object != null && Object.IsValid)
            {
                NetworkObject spawnedObj = Runner.Spawn(cabbagePrefab, spawnPos, baseRot);
                WobblyCabbageItem cabbage = spawnedObj != null ? spawnedObj.GetComponent<WobblyCabbageItem>() : null;

                if (cabbage != null)
                {
                    cabbage.ApplyInheritedState(
                        WobblyCabbageStage.Half,
                        false,
                        CurrentCookState,
                        CookProgress,
                        new List<string>(cookingSeq),
                        new Dictionary<string, string>(metadata)
                    );
                }
            }
            else
            {
                NetworkObject spawnedObj = Instantiate(cabbagePrefab, spawnPos, baseRot);
                WobblyCabbageItem cabbage = spawnedObj != null ? spawnedObj.GetComponent<WobblyCabbageItem>() : null;

                if (cabbage != null)
                {
                    cabbage.ApplyInheritedState(
                        WobblyCabbageStage.Half,
                        false,
                        CurrentCookState,
                        CookProgress,
                        new List<string>(cookingSeq),
                        new Dictionary<string, string>(metadata)
                    );
                }
            }
        }

        SafeRemoveSelf();
    }

    public void ApplyInheritedState(
        WobblyCabbageStage stage,
        bool flavorLost,
        CookState cookState,
        float cookProgress,
        List<string> seq,
        Dictionary<string, string> meta
    )
    {
        _initializedFromParent = true;
        _localInitApplied = true;

        CurrentStage = stage;
        FlavorLost = flavorLost;
        CurrentCookState = cookState;
        CookProgress = cookProgress;
        isBeingCooked = false;

        if (seq != null) cookingSeq = seq;
        if (meta != null) metadata = meta;

        RefreshVisualState();
    }

    private void SafeRemoveSelf()
    {
        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }

    private void OnVisualStateChanged()
    {
        _localStage = StageNetworked;
        _localFlavorLost = FlavorLostNetworked;
        _localCookState = CookStateNetworked;
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        UpdateItemName();
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        bool showRuined = FlavorLost && ruinedModel != null;

        if (wholeModel != null) wholeModel.SetActive(!showRuined && CurrentStage == WobblyCabbageStage.Whole);
        if (halfModel != null) halfModel.SetActive(!showRuined && CurrentStage == WobblyCabbageStage.Half);
        if (ruinedModel != null) ruinedModel.SetActive(showRuined);

        GameObject activeModel = GetActiveModel();
        if (activeModel == null)
            return;

        MeshRenderer renderer = activeModel.GetComponentInChildren<MeshRenderer>(true);
        if (renderer == null)
            return;

        if (FlavorLost)
        {
            renderer.material.color = ruinedColor;
            return;
        }

        switch (CurrentCookState)
        {
            case CookState.Raw:
                renderer.material.color = rawColor;
                break;

            case CookState.Cooked:
                renderer.material.color = cookedColor;
                break;

            case CookState.Burned:
                renderer.material.color = burnedColor;
                break;
        }
    }

    private GameObject GetActiveModel()
    {
        if (FlavorLost && ruinedModel != null)
            return ruinedModel;

        if (CurrentStage == WobblyCabbageStage.Whole)
            return wholeModel;

        return halfModel;
    }

    private void UpdateItemName()
    {
        string prefix = "";

        if (CurrentCookState == CookState.Cooked)
            prefix = "구운 ";
        else if (CurrentCookState == CookState.Burned)
            prefix = "타버린 ";

        if (FlavorLost)
        {
            itemName = prefix + "풍미가 날아간 비틀비틀 양배추";
            return;
        }

        itemName = CurrentStage == WobblyCabbageStage.Whole
            ? prefix + "비틀비틀 양배추"
            : prefix + "반쪽 비틀비틀 양배추";
    }
}