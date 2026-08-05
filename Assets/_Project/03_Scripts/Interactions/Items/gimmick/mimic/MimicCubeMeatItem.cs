using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 미믹 큐브 고기 — MimicMeatItem을 칼로 자른 결과물.
/// - 더 이상 자를 수 없음
/// - 불에 구우면 Cooked, 더 구우면 Burned
/// </summary>
public class MimicCubeMeatItem : PickableItem, ICookable
{
    [Header("Cook Settings")]
    [SerializeField, Min(0.01f)] private float cookSeconds = 8f;
    [SerializeField, Min(0.01f)] private float burnSeconds = 16f;

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

    public override bool CanChop => false;

    private void Start()
    {
        itemName = "미믹 큐브 고기";
        metadata["ingredientID"] = "미믹 고기";
        CacheRenderers();
        UpdateVisuals();
    }

    public override void Spawned()
    {
        CacheRenderers();
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
    // Visuals
    // ─────────────────────────────────────────

    private void OnCookStateChanged() => UpdateVisuals();

    private void CacheRenderers()
    {
        if (_renderers != null) return;
        _renderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb = new MaterialPropertyBlock();
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
            CookState.Cooked => "잘 익은 미믹 큐브 고기",
            CookState.Burned => "타버린 미믹 큐브 고기",
            _                => "미믹 큐브 고기"
        };
    }
}
