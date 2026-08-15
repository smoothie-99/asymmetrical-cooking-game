using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

public enum MimicBoxType
{
    Bronze,
    Silver,
    Gold
}

public enum MimicBoxSolveAction
{
    Knife,
    Wash,
    Heat
}

/// <summary>
/// 실제 금/은/동 미믹 상자 공통 베이스.
/// - 동 상자: 칼 정답 → 고기 보상 스폰 후 소멸
/// - 은 상자: 세척 정답 → 세척 게이지 100% 달성 시 고기 보상 스폰 후 소멸
/// - 금 상자: 가열 정답 → 일정 시간 가열 시 고기 보상 스폰 후 소멸
/// - 오답 액션 시 손상 상태로 전환
///   * 특히 동/금 상자를 세척하면 즉시 손상
/// - 칼 상호작용은 ICuttable + CuttingStation 경로와 직접 Interact 경로 모두 지원
/// - FixedUpdateNetwork 끝에서 base.FixedUpdateNetwork()를 호출해 PickableItem 기본 위치 동기화 유지
/// </summary>
public abstract class MimicBoxVariantBaseItem : PickableItem, ICookable, IWashable, ICuttable
{
    [Header("Reward")]
    [SerializeField] private NetworkObject wholeMeatPrefab;

    [Header("Visuals")]
    [SerializeField] private GameObject trapEffectPrefab;

    [Header("Requirements")]
    [SerializeField, Min(0.01f)] private float requiredWashAmount = 100f;
    [SerializeField, Min(0.01f)] private float requiredCookSeconds = 5f;

    [Header("Knife Guideline")]
    [SerializeField] private CutGuidelineConfig[] knifeGuidelineConfigs = { new CutGuidelineConfig() };
    private CutGuidelineVisual[] _guidelineVisuals;

    [Networked, OnChangedRender(nameof(OnRuinedChanged))]
    public bool IsRuinedNetworked { get; set; }

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked]
    public bool IsBeingCookedNetworked { get; set; }

    [Networked]
    public float CookProgressNetworked { get; set; }

    [Networked]
    public float CleanRatioNetworked { get; set; }

    private bool _localIsRuined;
    private CookState _localCookState = CookState.Raw;
    private bool _localIsBeingCooked;
    private float _localCookProgress;
    private float _localCleanRatio;

    private bool _queuedKnifeRequest;
    private float _queuedWashAmount;
    private float _queuedHeat;
    private bool _isResolving;

    protected abstract MimicBoxType BoxType { get; }
    protected abstract MimicBoxSolveAction RequiredSolveAction { get; }

    protected float RequiredWashAmount => Mathf.Max(0.01f, requiredWashAmount);
    protected float RequiredCookSeconds => Mathf.Max(0.01f, requiredCookSeconds);

    protected bool IsRuined
    {
        get => IsNetworkReady ? IsRuinedNetworked : _localIsRuined;
        private set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority)
                    return;

                IsRuinedNetworked = value;
            }

            _localIsRuined = value;
            UpdateVisuals();
        }
    }

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState previous = CurrentCookState;
            if (previous == value)
                return;

            if (IsNetworkReady)
            {
                if (!HasStateAuthority)
                    return;

                CookStateNetworked = value;
            }

            _localCookState = value;
            RecordCookStateChange(previous, value);
            UpdateVisuals();
        }
    }

    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : _localIsBeingCooked;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority)
                    return;

                IsBeingCookedNetworked = value;
            }

            _localIsBeingCooked = value;
        }
    }

    private float CookProgressValue
    {
        get => IsNetworkReady ? CookProgressNetworked : _localCookProgress;
        set
        {
            float clamped = Mathf.Max(0f, value);

            if (IsNetworkReady)
            {
                if (!HasStateAuthority)
                    return;

                CookProgressNetworked = clamped;
            }

            _localCookProgress = clamped;
        }
    }

    public float CleanRatio
    {
        get => IsNetworkReady ? CleanRatioNetworked : _localCleanRatio;
        private set
        {
            float clamped = Mathf.Clamp(value, 0f, RequiredWashAmount);

            if (IsNetworkReady)
            {
                if (!HasStateAuthority)
                    return;

                CleanRatioNetworked = clamped;
            }

            _localCleanRatio = clamped;
        }
    }

    public override bool CanChop => !isHeld && !IsRuined && !_isResolving;

    protected virtual void Start()
    {
        itemName = GetBoxDisplayName();
        metadata["ingredientID"] = GetBoxDisplayName();
        EnsureKnifeGuidelines();

        if (!IsNetworkReady)
            UpdateVisuals();
    }

    public override void Spawned()
    {
        EnsureKnifeGuidelines();

        if (HasStateAuthority)
        {
            IsRuined = false;
            CurrentCookState = CookState.Raw;
            isBeingCooked = false;
            CookProgressValue = 0f;
            CleanRatio = 0f;
            ClearQueuedRequests();
            _isResolving = false;
        }
        else if (IsNetworkReady)
        {
            _localIsRuined = IsRuinedNetworked;
            _localCookState = CookStateNetworked;
            _localIsBeingCooked = IsBeingCookedNetworked;
            _localCookProgress = CookProgressNetworked;
            _localCleanRatio = CleanRatioNetworked;
        }

        UpdateVisuals();
    }

    public override void Render()
    {
        base.Render();

        if (!IsNetworkReady)
            return;

        bool dirty = false;

        if (_localIsRuined != IsRuinedNetworked)
        {
            _localIsRuined = IsRuinedNetworked;
            dirty = true;
        }

        if (_localCookState != CookStateNetworked)
        {
            _localCookState = CookStateNetworked;
            dirty = true;
        }

        if (_localIsBeingCooked != IsBeingCookedNetworked)
            _localIsBeingCooked = IsBeingCookedNetworked;

        if (!Mathf.Approximately(_localCookProgress, CookProgressNetworked))
            _localCookProgress = CookProgressNetworked;

        if (!Mathf.Approximately(_localCleanRatio, CleanRatioNetworked))
            _localCleanRatio = CleanRatioNetworked;

        if (dirty)
            UpdateVisuals();
    }

    // ─────────────────────────────────────────
    // ICuttable
    // ─────────────────────────────────────────

    private void EnsureKnifeGuidelines()
    {
        if (knifeGuidelineConfigs == null || knifeGuidelineConfigs.Length == 0)
        {
            _guidelineVisuals = System.Array.Empty<CutGuidelineVisual>();
            return;
        }

        bool valid = _guidelineVisuals != null && _guidelineVisuals.Length == knifeGuidelineConfigs.Length;
        if (valid)
        {
            for (int i = 0; i < _guidelineVisuals.Length; i++)
            {
                if (_guidelineVisuals[i] == null)
                {
                    valid = false;
                    break;
                }
            }
        }

        if (valid)
            return;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("CutGuideline_"))
                continue;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        _guidelineVisuals = new CutGuidelineVisual[knifeGuidelineConfigs.Length];

        for (int i = 0; i < knifeGuidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = knifeGuidelineConfigs[i].localPosition;
            go.transform.localEulerAngles = knifeGuidelineConfigs[i].localEulerAngles;

            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType = "Knife";
            vis.cutName = string.IsNullOrEmpty(knifeGuidelineConfigs[i].cutName) ? "처리" : knifeGuidelineConfigs[i].cutName;
            vis.resultPrefabs = knifeGuidelineConfigs[i].resultPrefabs;
            vis.resultCounts = knifeGuidelineConfigs[i].resultCounts;

            _guidelineVisuals[i] = vis;
        }
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        if (!CanChop)
            return System.Array.Empty<CutGuidelineVisual>();

        EnsureKnifeGuidelines();

        return _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();
    }

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem && IsOnStation)
        {
            if (!CanChop || player == null)
                return false;

            PickableItem held = player.GetActiveHandItem();
            return held is ITool tool && tool.ToolType == "Knife" && GetGuidelineVisuals(tool.ToolType).Length > 0;
        }

        return base.CanInteract(player, type);
    }

    public override void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem && IsOnStation)
        {
            if (!CanChop || player == null)
                return;

            PickableItem held = player.GetActiveHandItem();
            if (held is not ITool tool || tool.ToolType != "Knife")
                return;

            if (SelectedGuidelineIndex < 0)
                return;

            Chop(null);
            return;
        }

        base.Interact(player, type);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem && IsOnStation)
        {
            if (!CanChop || player == null)
                return "";

            PickableItem held = player.GetActiveHandItem();
            if (held is ITool tool && tool.ToolType == "Knife" && GetGuidelineVisuals(tool.ToolType).Length > 0)
                return SelectedGuidelineIndex >= 0 ? "[L-Click] 처리하기" : "[점선에 조준하세요]";

            return "";
        }

        return base.GetInteractionLabel(player, type);
    }

    // ─────────────────────────────────────────
    // IWashable
    // ─────────────────────────────────────────

    public void Wash(float amount)
    {
        amount = Mathf.Max(0f, amount);

        if (amount <= 0f || IsRuined || _isResolving)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                RPC_RequestWash(amount);
                return;
            }

            _queuedWashAmount += amount;
            return;
        }

        ResolveWashLocal(amount);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestWash(float amount)
    {
        amount = Mathf.Max(0f, amount);

        if (amount <= 0f || IsRuined || _isResolving)
            return;

        _queuedWashAmount += amount;
    }

    // ─────────────────────────────────────────
    // Knife
    // ─────────────────────────────────────────

    public override void Chop(CuttingStation board)
    {
        if (!CanChop)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                RPC_RequestKnife();
                return;
            }

            _queuedKnifeRequest = true;
            return;
        }

        ResolveKnifeLocal();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestKnife()
    {
        if (!CanChop)
            return;

        _queuedKnifeRequest = true;
    }

    // ─────────────────────────────────────────
    // Tick
    // ─────────────────────────────────────────

    public override void FixedUpdateNetwork()
    {
        if (!IsNetworkReady)
            return;

        if (!HasStateAuthority)
        {
            ClearQueuedRequests();

            if (Object != null && Object.IsValid)
                base.FixedUpdateNetwork();

            return;
        }

        if (IsRuined || _isResolving)
        {
            ClearQueuedRequests();
            isBeingCooked = false;

            if (Object != null && Object.IsValid)
                base.FixedUpdateNetwork();

            return;
        }

        bool hadHeatThisTick = _queuedHeat > 0f;

        if (_queuedKnifeRequest)
        {
            _queuedKnifeRequest = false;
            ResolveKnifeAuthority();
        }

        if (!IsRuined && !_isResolving && _queuedWashAmount > 0f)
        {
            float washAmount = _queuedWashAmount;
            _queuedWashAmount = 0f;
            ResolveWashAuthority(washAmount);
        }

        if (!IsRuined && !_isResolving && _queuedHeat > 0f)
        {
            float heat = _queuedHeat;
            _queuedHeat = 0f;
            ResolveHeatAuthority(heat);
        }
        else
        {
            _queuedHeat = 0f;
        }

        if (IsRuined || _isResolving)
            isBeingCooked = false;
        else
            isBeingCooked = hadHeatThisTick;

        if (Object != null && Object.IsValid)
            base.FixedUpdateNetwork();
    }

    // ─────────────────────────────────────────
    // ICookable
    // ─────────────────────────────────────────

    public void CookInFire(float heat)
    {
        heat = Mathf.Max(0f, heat);

        if (heat <= 0f || IsRuined || _isResolving)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
                return;

            _queuedHeat += heat;
            return;
        }

        ResolveHeatLocal(heat);
    }

    // ─────────────────────────────────────────
    // Resolve
    // ─────────────────────────────────────────

    private void ResolveKnifeAuthority()
    {
        if (RequiredSolveAction == MimicBoxSolveAction.Knife)
        {
            ResolveSuccessAuthority();
            return;
        }

        TriggerDamageAuthority(MimicBoxSolveAction.Knife);
    }

    private void ResolveWashAuthority(float amount)
    {
        if (RequiredSolveAction == MimicBoxSolveAction.Wash)
        {
            ApplyWashProgressAuthority(amount);
            return;
        }

        // 동/금 상자를 세척하면 손상
        TriggerDamageAuthority(MimicBoxSolveAction.Wash);
    }

    private void ResolveHeatAuthority(float heat)
    {
        if (RequiredSolveAction == MimicBoxSolveAction.Heat)
        {
            ApplyHeatProgressAuthority(heat);
            return;
        }

        TriggerDamageAuthority(MimicBoxSolveAction.Heat);
    }

    private void ResolveKnifeLocal()
    {
        if (RequiredSolveAction == MimicBoxSolveAction.Knife)
        {
            ResolveSuccessLocal();
            return;
        }

        TriggerDamageLocal(MimicBoxSolveAction.Knife);
    }

    private void ResolveWashLocal(float amount)
    {
        if (RequiredSolveAction == MimicBoxSolveAction.Wash)
        {
            ApplyWashProgressLocal(amount);
            return;
        }

        // 동/금 상자를 세척하면 손상
        TriggerDamageLocal(MimicBoxSolveAction.Wash);
    }

    private void ResolveHeatLocal(float heat)
    {
        isBeingCooked = true;

        if (RequiredSolveAction == MimicBoxSolveAction.Heat)
        {
            ApplyHeatProgressLocal(heat);
            return;
        }

        TriggerDamageLocal(MimicBoxSolveAction.Heat);
    }

    private void ApplyWashProgressAuthority(float amount)
    {
        CleanRatio += amount;

        if (CleanRatio >= RequiredWashAmount)
        {
            CleanRatio = RequiredWashAmount;
            RecordWashed();
            ResolveSuccessAuthority();
        }
    }

    private void ApplyWashProgressLocal(float amount)
    {
        CleanRatio += amount;

        if (CleanRatio >= RequiredWashAmount)
        {
            CleanRatio = RequiredWashAmount;
            RecordWashed();
            ResolveSuccessLocal();
            return;
        }

        UpdateVisuals();
    }

    private void ApplyHeatProgressAuthority(float heat)
    {
        CookProgressValue += heat;

        if (CookProgressValue >= RequiredCookSeconds)
            ResolveSuccessAuthority();
    }

    private void ApplyHeatProgressLocal(float heat)
    {
        CookProgressValue += heat;

        if (CookProgressValue >= RequiredCookSeconds)
        {
            ResolveSuccessLocal();
            return;
        }

        UpdateVisuals();
    }

    private void TriggerDamageAuthority(MimicBoxSolveAction wrongAction)
    {
        if (IsRuined || _isResolving)
            return;

        IsRuined = true;
        isBeingCooked = false;
        ClearQueuedRequests();

        if (wrongAction == MimicBoxSolveAction.Heat)
            CurrentCookState = CookState.Burned;

        RPC_PlayTrapEffect();
    }

    private void TriggerDamageLocal(MimicBoxSolveAction wrongAction)
    {
        if (IsRuined || _isResolving)
            return;

        IsRuined = true;
        isBeingCooked = false;
        ClearQueuedRequests();

        if (wrongAction == MimicBoxSolveAction.Heat)
            CurrentCookState = CookState.Burned;

        PlayLocalTrapEffect();
        UpdateVisuals();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayTrapEffect()
    {
        PlayLocalTrapEffect();
    }

    private void PlayLocalTrapEffect()
    {
        if (trapEffectPrefab == null)
            return;

        GameObject fx = Instantiate(trapEffectPrefab, transform.position, transform.rotation);
        Destroy(fx, 2f);
    }

    private void ResolveSuccessAuthority()
    {
        if (_isResolving)
            return;

        _isResolving = true;
        isBeingCooked = false;
        ClearQueuedRequests();

        SpawnMeatAndDestroy();
    }

    private void ResolveSuccessLocal()
    {
        if (_isResolving)
            return;

        _isResolving = true;
        isBeingCooked = false;
        ClearQueuedRequests();

        SpawnMeatAndDestroy();
    }

    private void SpawnMeatAndDestroy()
    {
        if (wholeMeatPrefab != null)
        {
            if (IsNetworkReady)
            {
                if (Runner != null && HasStateAuthority)
                {
                    List<string> seqCopy = new List<string>(cookingSeq);
                    Dictionary<string, string> metaCopy = new Dictionary<string, string>(metadata);

                    Runner.Spawn(wholeMeatPrefab, transform.position, transform.rotation,
                        onBeforeSpawned: (_, obj) =>
                        {
                            PickableItem meat = obj.GetComponent<PickableItem>();
                            if (meat == null)
                                return;

                            meat.cookingSeq = new List<string>(seqCopy);

                            // 보상 고기는 자기 ingredientID를 유지해야 하므로
                            // 박스 쪽 ingredientID는 덮어쓰지 않는다.
                            foreach (var kv in metaCopy)
                            {
                                if (kv.Key == "ingredientID")
                                    continue;

                                meat.metadata[kv.Key] = kv.Value;
                            }
                        });
                }
            }
            else
            {
                GameObject go = Instantiate(wholeMeatPrefab.gameObject, transform.position, transform.rotation);
                PickableItem meat = go.GetComponent<PickableItem>();

                if (meat != null)
                {
                    meat.cookingSeq = new List<string>(cookingSeq);

                    foreach (var kv in metadata)
                    {
                        if (kv.Key == "ingredientID")
                            continue;

                        meat.metadata[kv.Key] = kv.Value;
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning($"[{GetType().Name}] wholeMeatPrefab이 비어 있습니다.");
        }

        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }

    private void ClearQueuedRequests()
    {
        _queuedKnifeRequest = false;
        _queuedWashAmount = 0f;
        _queuedHeat = 0f;
    }

    private void OnRuinedChanged()
    {
        _localIsRuined = IsRuinedNetworked;
        UpdateVisuals();
    }

    private void OnCookStateChanged()
    {
        _localCookState = CookStateNetworked;
        UpdateVisuals();
    }

    protected virtual void UpdateVisuals()
    {
        itemName = IsRuined ? $"손상된 {GetBoxDisplayName()}" : GetBoxDisplayName();
        metadata["ingredientID"] = itemName;
    }

    protected string GetBoxDisplayName()
    {
        return BoxType switch
        {
            MimicBoxType.Bronze => "동 미믹 상자",
            MimicBoxType.Silver => "은 미믹 상자",
            MimicBoxType.Gold => "금 미믹 상자",
            _ => "미믹 상자"
        };
    }
}