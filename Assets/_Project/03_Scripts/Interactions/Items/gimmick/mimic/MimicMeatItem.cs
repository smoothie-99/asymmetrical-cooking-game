using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 미믹 통고기.
/// - 칼로 한 번 자르면 MimicCubeMeatItem 여러 개로 분리됨 (프리팹 교체)
/// - 불에 구우면 Cooked, 더 구우면 Burned
/// </summary>
public class MimicMeatItem : PickableItem, ICookable, ICuttable
{
    [Header("Cook Settings")]
    [SerializeField, Min(0.01f)] private float cookSeconds = 10f;
    [SerializeField, Min(0.01f)] private float burnSeconds = 20f;

    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new() };
    private CutGuidelineVisual[] _guidelineVisuals;

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

    private MeshRenderer[] _renderers;
    private MaterialPropertyBlock _mpb;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color RawColor    = new(0.8f, 0.2f, 0.2f);
    private static readonly Color CookedColor = new(0.55f, 0.27f, 0.07f);
    private static readonly Color BurnedColor = new(0.1f, 0.1f, 0.1f);

    private void Start()
    {
        itemName = "미믹 통고기";
        metadata["ingredientID"] = "미믹 고기";
        CacheRenderers();
        CreateGuidelineVisuals();
        UpdateVisuals();
    }

    public override void Spawned()
    {
        CacheRenderers();
        CreateGuidelineVisuals();
        if (HasStateAuthority)
            CurrentCookState = CookState.Raw;
        UpdateVisuals();
    }

    // ─────────────────────────────────────────
    // ICookable
    // ─────────────────────────────────────────

    public void CookInFire(float heat)
    {
        if (CurrentCookState == CookState.Burned) return;
        cookTime += heat;
        if      (cookTime >= burnSeconds) CurrentCookState = CookState.Burned;
        else if (cookTime >= cookSeconds) CurrentCookState = CookState.Cooked;
    }

    // ─────────────────────────────────────────
    // ICuttable
    // ─────────────────────────────────────────

    public override bool CanChop => CurrentCookState != CookState.Burned && HasValidCutResults();

    private bool HasValidCutResults()
    {
        if (_guidelineConfigs == null) return false;
        foreach (var cfg in _guidelineConfigs)
        {
            if (cfg.resultPrefabs != null && cfg.resultPrefabs.Length > 0) return true;
        }
        return false;
    }

    private void CreateGuidelineVisuals()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0) return;
        if (_guidelineVisuals != null && _guidelineVisuals.Length == _guidelineConfigs.Length) return;

        _guidelineVisuals = new CutGuidelineVisual[_guidelineConfigs.Length];
        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform, false);
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

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        if (!CanChop) return System.Array.Empty<CutGuidelineVisual>();
        return _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();
    }

    public override void Chop(CuttingStation board)
    {
        if (!CanChop) return;
        const string toolType = "Knife";
        if (IsNetworkReady) Rpc_Cut(0, toolType);
        else                LocalCut(0, toolType);
    }

    public override void TransferStateTo(PickableItem target)
    {
        if (target is MimicCubeMeatItem cube)
        {
            metadata["ingredientID"] = "미믹 고기";
            cube.metadata["ingredientID"] = "미믹 고기";
            cube.CurrentCookState = CurrentCookState;
            cube.cookTime = cookTime;
        }
    }

    // ─────────────────────────────────────────
    // Visuals
    // ─────────────────────────────────────────

    private void OnCookStateChanged() => UpdateVisuals();

    private void CacheRenderers()
    {
        if (_renderers != null) return;
        _renderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb = new();
    }

    private void UpdateVisuals()
    {
        if (_renderers == null) CacheRenderers();

        Color color = CurrentCookState switch
        {
            CookState.Cooked => CookedColor,
            CookState.Burned => BurnedColor,
            _                => RawColor
        };

        foreach (var r in _renderers)
        {
            if (r == null) continue;
            _mpb.SetColor(ColorId, color);
            r.SetPropertyBlock(_mpb);
        }

        itemName = CurrentCookState switch
        {
            CookState.Cooked => "잘 구워진 미믹 통고기",
            CookState.Burned => "타버린 미믹 통고기",
            _                => "미믹 통고기"
        };
    }
}
