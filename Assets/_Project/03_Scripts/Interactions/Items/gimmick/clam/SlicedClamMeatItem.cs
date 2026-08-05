using UnityEngine;
using Fusion;

/// <summary>
/// 썰린 조개살.
/// ClamMeatItem 을 도마에서 자르면 스폰됩니다.
/// flavor 와 조리 상태를 부모로부터 그대로 이어받습니다.
/// </summary>
public class SlicedClamMeatItem : PickableItem, ICookable
{
    [Header("Visual")]
    [SerializeField] private MeshRenderer[] _renderers;

    [Header("Cook Thresholds (seconds)")]
    [SerializeField, Min(0.1f)] private float notCookedAt =  8f;
    [SerializeField, Min(0.1f)] private float cookedAt    = 20f;
    [SerializeField, Min(0.1f)] private float tooCookedAt = 35f;
    [SerializeField, Min(0.1f)] private float burnedAt    = 50f;

    // ── 네트워크 상태 ──────────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked] public float CookProgressNetworked  { get; set; }
    [Networked] public bool  IsBeingCookedNetworked { get; set; }
    [Networked] public int   FlavorIndexNetworked   { get; set; } = -1;

    // ── 로컬 폴백 ─────────────────────────────────────────────────

    private CookState _localCookState    = CookState.Raw;
    private float     _localCookProgress = 0f;
    private int       _localFlavor       = -1;
    private float     _queuedHeat        = 0f;

    private MaterialPropertyBlock _mpb;
    private static readonly int   ColorId = Shader.PropertyToID("_BaseColor");

    private static readonly Color ColorRaw       = new Color(0.85f, 0.75f, 0.65f, 1f);
    private static readonly Color ColorNotCooked = new Color(0.80f, 0.55f, 0.40f, 1f);
    private static readonly Color ColorCooked    = new Color(0.65f, 0.38f, 0.22f, 1f);
    private static readonly Color ColorTooCooked = new Color(0.40f, 0.25f, 0.12f, 1f);
    private static readonly Color ColorBurned    = new Color(0.15f, 0.12f, 0.08f, 1f);

    private bool IsNetworkReady => Object != null && Object.IsValid;

    // ── ICookable ──────────────────────────────────────────────────

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState _prev = CurrentCookState;
            if (IsNetworkReady) { if (HasStateAuthority) CookStateNetworked = value; }
            else _localCookState = value;
            RecordCookStateChange(_prev, value);
            UpdateVisuals();
            UpdateItemName();
        }
    }

    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : false;
        set { if (IsNetworkReady && HasStateAuthority) IsBeingCookedNetworked = value; }
    }

    // ── 생명주기 ──────────────────────────────────────────────────

    private void Start()
    {
        _mpb = new MaterialPropertyBlock();
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<MeshRenderer>(true);

        UpdateVisuals();
        UpdateItemName();
    }

    public override void Spawned()
    {
        _mpb ??= new MaterialPropertyBlock();
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<MeshRenderer>(true);

        UpdateVisuals();
        UpdateItemName();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
            ProcessHeat();
        else
            _queuedHeat = 0f;
        base.FixedUpdateNetwork();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessHeat();
    }

    // ── 로컬 초기화 (ClamMeatItem.ExecuteSlice 로컬 분기용) ───────

    public void InitializeFrom(int flavorIndex, CookState cookState, float cookProgress)
    {
        _localFlavor       = flavorIndex;
        _localCookState    = cookState;
        _localCookProgress = cookProgress;
        UpdateVisuals();
        UpdateItemName();
    }

    public void InitializeFrom(JewelColor flavor, CookState cookState, float cookProgress)
        => InitializeFrom((int)flavor, cookState, cookProgress);

    // ── SetFlavor (리플렉션 호출 대비) ────────────────────────────

    public void SetFlavor(JewelColor flavor)
    {
        int idx = (int)flavor;
        if (IsNetworkReady && HasStateAuthority) FlavorIndexNetworked = idx;
        else _localFlavor = idx;
        UpdateItemName();
    }

    // ── 조리 ──────────────────────────────────────────────────────

    public void CookInFire(float heat)
    {
        if (heat <= 0f) return;
        if (CurrentCookState == CookState.Burned) return;
        if (IsNetworkReady && !HasStateAuthority) return;
        _queuedHeat += heat;
    }

    private void ProcessHeat()
    {
        if (_queuedHeat <= 0f) { isBeingCooked = false; return; }
        if (CurrentCookState == CookState.Burned) { _queuedHeat = 0f; return; }

        isBeingCooked = true;

        if (IsNetworkReady) CookProgressNetworked += _queuedHeat;
        else _localCookProgress += _queuedHeat;
        _queuedHeat = 0f;

        float p = IsNetworkReady ? CookProgressNetworked : _localCookProgress;
        CookState next = ProgressToState(p);
        if (next != CurrentCookState)
            CurrentCookState = next;
    }

    private CookState ProgressToState(float p)
    {
        if (p >= burnedAt)    return CookState.Burned;
        if (p >= tooCookedAt) return CookState.TooCooked;
        if (p >= cookedAt)    return CookState.Cooked;
        if (p >= notCookedAt) return CookState.NotCooked;
        return CookState.Raw;
    }

    // ── 비주얼 ────────────────────────────────────────────────────

    private void OnCookStateChanged() { UpdateVisuals(); UpdateItemName(); }

    private void UpdateVisuals()
    {
        if (_renderers == null) return;
        _mpb ??= new MaterialPropertyBlock();

        Color c = CurrentCookState switch
        {
            CookState.NotCooked => ColorNotCooked,
            CookState.Cooked    => ColorCooked,
            CookState.TooCooked => ColorTooCooked,
            CookState.Burned    => ColorBurned,
            _                   => ColorRaw,
        };

        _mpb.SetColor(ColorId, c);
        foreach (MeshRenderer r in _renderers)
            if (r != null) r.SetPropertyBlock(_mpb);
    }

    private void UpdateItemName()
    {
        int flavor = IsNetworkReady ? FlavorIndexNetworked : _localFlavor;
        string flavorStr = flavor >= 0 ? $" ({(JewelColor)flavor})" : "";
        itemName = $"썰린 조개살{flavorStr} [{CurrentCookState.ToKoreanString()}]";
        metadata["ingredientID"] = "수면 보석 조개";
    }
}
