using UnityEngine;
using Fusion;

/// <summary>
/// 맨드레이크 머리. 맨드레이크를 자를 때 분리되는 머리 부위. 더 이상 자를 수 없습니다.
/// </summary>
public class MandrakeHeadItem : PickableItem, ICookable
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

    private MeshRenderer[]        _renderers;
    private MaterialPropertyBlock  _mpb;
    private static readonly int    ColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly Color  BurnedColor = new Color(0.15f, 0.12f, 0.08f, 1f);

    private void Start()
    {
        itemName = "맨드레이크 머리";
        metadata["ingredientID"] = "맨드레이크";
        CacheRenderers();
    }

    public override void Spawned()
    {
        CacheRenderers();
        if (HasStateAuthority) CurrentCookState = CookState.Raw;
    }

    private void OnCookStateChanged() => UpdateVisuals();

    public override void TransferStateTo(PickableItem target)
    {
        if (target is MandrakeHeadItem other)
            other.CurrentCookState = CurrentCookState;
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
