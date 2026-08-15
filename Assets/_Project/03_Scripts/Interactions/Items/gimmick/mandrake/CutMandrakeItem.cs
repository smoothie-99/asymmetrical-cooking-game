using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 잘린 맨드레이크. 칼로 자르면 슬라이스 맨드레이크(SlicedMandrakeItem) 2개로 분리됩니다.
/// </summary>
public class CutMandrakeItem : PickableItem, ICookable, ICuttable
{
    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }
    private CookState _localCookState = CookState.Raw;

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState prev = CurrentCookState;
            if (IsNetworkReady) { if (HasStateAuthority) CookStateNetworked = value; }
            _localCookState = value;
            RecordCookStateChange(prev, value);
            UpdateVisuals();
        }
    }

    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };
    private CutGuidelineVisual[] _guidelineVisuals;

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

    public override void TransferStateTo(PickableItem target)
    {
        if (target is SlicedMandrakeItem sliced) sliced.CurrentCookState = CurrentCookState;
        else if (target is CutMandrakeItem other) other.CurrentCookState = CurrentCookState;
    }


    private MeshRenderer[]        _renderers;
    private MaterialPropertyBlock  _mpb;
    private static readonly int    ColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly Color  BurnedColor = new Color(0.15f, 0.12f, 0.08f, 1f);

    private void Start()
    {
        itemName = "잘린 맨드레이크";
        metadata["ingredientID"] = "맨드레이크";
        CacheRenderers();
        CreateGuidelineVisuals();
    }

    public override void Spawned()
    {
        CacheRenderers();
        if (HasStateAuthority) CurrentCookState = CookState.Raw;
    }

    private void OnCookStateChanged() => UpdateVisuals();

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

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
    }

    public void CookInFire(float heat)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (CurrentCookState == CookState.Burned) return;
        cookTime += heat;
        if (cookTime >= 35f) CurrentCookState = CookState.Burned;
    }
}
