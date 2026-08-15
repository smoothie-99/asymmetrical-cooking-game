using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 반쪽 양배추.
/// 칼로 자르면 CutGuidelineConfig.resultPrefabs에 지정된 결과물(SlicedCabbageItem)로 분리됩니다.
///
/// [기믹]
/// - 자르는 순간 플레이어 이동 방향 무작위 반전 디버프
/// - ManualBookItem을 손에 들고 있으면 디버프 무효
///
/// [네트워크]
/// - 절단은 PickableItem 기본 Rpc_Cut / LocalCut 경로를 그대로 사용
/// - 디버프만 Interact()에서 앞에 주입
/// </summary>
public class CutCabbageItem : PickableItem, ICookable, ICuttable
{
    [Header("Gimmick Settings")]
    [SerializeField] private float disorientationDuration = 5f;

    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float burnSeconds = 90f;

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
            UpdateVisuals();
        }
    }

    // ─────────────────────────────────────────
    // CanChop — resultPrefabs가 있어야 자를 수 있음
    // ─────────────────────────────────────────

    public override bool CanChop =>
        !isHeld &&
        CurrentCookState != CookState.Burned &&
        _guidelineConfigs != null &&
        _guidelineConfigs.Length > 0 &&
        _guidelineConfigs[0].resultPrefabs != null &&
        _guidelineConfigs[0].resultPrefabs.Length > 0;

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
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();

    // ─────────────────────────────────────────
    // Unity / Fusion 생명주기
    // ─────────────────────────────────────────

    private void Start()
    {
        itemName = "반쪽 양배추";
        metadata["ingredientID"] = "비틀비틀 양배추";
        CacheRenderers();
        CreateGuidelineVisuals();
    }

    public override void Spawned()
    {
        CacheRenderers();
        if (HasStateAuthority) CurrentCookState = CookState.Raw;
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
    }

    // ─────────────────────────────────────────
    // 상호작용
    // ─────────────────────────────────────────

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
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

                // 디버프 주입 (책 체크 포함) — 로컬 플레이어에게만 적용
                ApplySliceDisorientation(player);

                // 실제 절단 — 기존 Rpc_Cut / LocalCut 경로 사용
                if (IsNetworkReady) Rpc_Cut(SelectedGuidelineIndex, tool.ToolType);
                else LocalCut(SelectedGuidelineIndex, tool.ToolType);
                return;
            }
        }
        base.Interact(player, type);
    }

    // ─────────────────────────────────────────
    // ICookable — CookInFire
    // ─────────────────────────────────────────

    public void CookInFire(float heat)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (CurrentCookState == CookState.Burned) return;
        cookTime += heat;
        if (cookTime >= burnSeconds) CurrentCookState = CookState.Burned;
    }

    // ─────────────────────────────────────────
    // 디버프 (책 체크)
    // ─────────────────────────────────────────

    /// <summary>
    /// ManualBookItem을 들지 않은 플레이어에게 이동 반전 디버프 적용.
    /// </summary>
    private void ApplySliceDisorientation(CookingMasterHandsManager player)
    {
        if (player == null) return;

        // ManualBookItem을 양손 중 하나에 들고 있으면 무효
        if (player.HasItemInEitherHand<ManualBookItem>()) return;

        CookingMasterMovement movement = player.GetComponentInParent<CookingMasterMovement>();
        if (movement == null) return;

        // 상하좌우 4방향 모두 반전
        movement.ApplyDirectionInvertEffect(disorientationDuration, invertForward: true, invertStrafe: true);
    }

    // ─────────────────────────────────────────
    // 비주얼
    // ─────────────────────────────────────────

    private MeshRenderer[]       _renderers;
    private MaterialPropertyBlock _mpb;
    private static readonly int   ColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly Color  BurnedColor = new Color(0.15f, 0.12f, 0.08f, 1f);

    private void OnCookStateChanged()
    {
        _localCookState = CookStateNetworked;
        UpdateVisuals();
    }

    private void CacheRenderers()
    {
        if (_renderers != null) return;
        _renderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb       = new MaterialPropertyBlock();
    }

    private void UpdateVisuals()
    {
        if (_renderers == null) CacheRenderers();
        bool burned = CurrentCookState == CookState.Burned;
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            if (burned) { _mpb.SetColor(ColorId, BurnedColor); r.SetPropertyBlock(_mpb); }
            else        { r.SetPropertyBlock(null); }
        }
    }

    // ─────────────────────────────────────────
    // TransferStateTo — Rpc_Cut / LocalCut이 결과물에 상태 전달 시 사용
    // ─────────────────────────────────────────

    public override void TransferStateTo(PickableItem target)
    {
        if (target is SlicedCabbageItem sliced)
        {
            sliced.CurrentCookState = CurrentCookState;
            sliced.cookingSeq = new List<string>(cookingSeq);
        }
        else if (target is CutCabbageItem other)
        {
            other.CurrentCookState = CurrentCookState;
            other.cookingSeq = new List<string>(cookingSeq);
        }
    }
}
