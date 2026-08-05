using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 손질된 환영 물고기 공통 베이스.
/// - 토마토/양배추 스타일 단일 조리 진행도
/// - Raw -> Cooked -> Burned
/// - 색상 및 이름 갱신
/// </summary>
public abstract class PreparedPhantomFishBaseItem : PickableItem, ICookable
{
    [Header("Cooking Settings")]
    [SerializeField, Min(0.01f)] private float cookSeconds = 45f;
    [SerializeField, Min(0.01f)] private float burnSeconds = 70f;

    [Header("Visual Settings")]
    [SerializeField] private MeshRenderer[] targetRenderers;
    [SerializeField] private Color rawColor = Color.white;
    [SerializeField] private Color cookedColor = new Color(0.8f, 0.5f, 0.2f);
    [SerializeField] private Color burnedColor = Color.black;

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked]
    public bool IsBeingCookedNetworked { get; set; }

    [Networked]
    public float CookProgressNetworked { get; set; }

    private CookState _localCookState = CookState.Raw;
    private bool _localIsBeingCooked;
    private float _localCookProgress;

    private float _queuedHeat;
    private MaterialPropertyBlock _propertyBlock;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    protected abstract string RawItemName { get; }
    protected abstract string CookedItemName { get; }
    protected abstract string BurnedItemName { get; }
    protected abstract string IngredientId { get; }
    protected abstract float DefaultCookSeconds { get; }
    protected abstract float DefaultBurnSeconds { get; }

    protected float CurrentCookSeconds => Mathf.Max(0.01f, cookSeconds > 0f ? cookSeconds : DefaultCookSeconds);
    protected float CurrentBurnSeconds => Mathf.Max(CurrentCookSeconds, burnSeconds > 0f ? burnSeconds : DefaultBurnSeconds);

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
                if (!HasStateAuthority) return;
                CookStateNetworked = value;
            }

            _localCookState = value;
            RecordCookStateChange(previous, value);
            RefreshVisualState();
        }
    }

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

    protected float CookProgress
    {
        get => IsNetworkReady ? CookProgressNetworked : _localCookProgress;
        set
        {
            float clamped = Mathf.Max(0f, value);

            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookProgressNetworked = clamped;
            }

            _localCookProgress = clamped;
        }
    }

    protected virtual void OnValidate()
    {
        if (cookSeconds < 0.01f)
            cookSeconds = DefaultCookSeconds;

        if (burnSeconds < cookSeconds)
            burnSeconds = Mathf.Max(DefaultBurnSeconds, cookSeconds);
    }

    protected virtual void Start()
    {
        itemName = RawItemName;
        metadata["ingredientID"] = IngredientId;
        CacheRenderers();
        RefreshVisualState();
    }

    public override void Spawned()
    {
        CacheRenderers();

        if (HasStateAuthority)
        {
            CurrentCookState = CookState.Raw;
            CookProgress = 0f;
            isBeingCooked = false;
        }
        else if (IsNetworkReady)
        {
            _localCookState = CookStateNetworked;
            _localIsBeingCooked = IsBeingCookedNetworked;
            _localCookProgress = CookProgressNetworked;
        }

        RefreshVisualState();
    }

    public override void Render()
    {
        base.Render();

        if (!IsNetworkReady)
            return;

        bool dirty = false;

        if (_localCookState != CookStateNetworked)
        {
            _localCookState = CookStateNetworked;
            dirty = true;
        }

        if (_localIsBeingCooked != IsBeingCookedNetworked)
            _localIsBeingCooked = IsBeingCookedNetworked;

        if (!Mathf.Approximately(_localCookProgress, CookProgressNetworked))
            _localCookProgress = CookProgressNetworked;

        if (dirty)
            RefreshVisualState();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessCookingStep();
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsNetworkReady)
            return;

        if (!HasStateAuthority)
        {
            _queuedHeat = 0f;

            if (Object != null && Object.IsValid)
                base.FixedUpdateNetwork();
            return;
        }

        ProcessCookingStep();

        if (Object != null && Object.IsValid)
            base.FixedUpdateNetwork();
    }

    public virtual void CookInFire(float heat)
    {
        heat = Mathf.Max(0f, heat);
        if (heat <= 0f) return;
        if (CurrentCookState == CookState.Burned) return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority) return;
            _queuedHeat += heat;
            return;
        }

        _queuedHeat += heat;
    }

    protected virtual void ProcessCookingStep()
    {
        if (CurrentCookState == CookState.Burned)
        {
            _queuedHeat = 0f;
            isBeingCooked = false;
            return;
        }

        bool hadHeatThisStep = _queuedHeat > 0f;
        isBeingCooked = hadHeatThisStep;

        if (hadHeatThisStep)
        {
            CookProgress += _queuedHeat;
            UpdateCookStateFromProgress();
        }

        _queuedHeat = 0f;
    }

    protected virtual void UpdateCookStateFromProgress()
    {
        if (CookProgress >= CurrentBurnSeconds)
            CurrentCookState = CookState.Burned;
        else if (CookProgress >= CurrentCookSeconds)
            CurrentCookState = CookState.Cooked;
        else
            CurrentCookState = CookState.Raw;
    }

    private void CacheRenderers()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<MeshRenderer>(true);

        _propertyBlock ??= new MaterialPropertyBlock();
    }

    private void OnCookStateChanged()
    {
        _localCookState = CookStateNetworked;
        RefreshVisualState();
    }

    protected void RefreshVisualState()
    {
        UpdateItemName();
        UpdateVisuals();
        metadata["ingredientID"] = IngredientId;
    }

    protected virtual void UpdateItemName()
    {
        itemName = CurrentCookState switch
        {
            CookState.Cooked => CookedItemName,
            CookState.Burned => BurnedItemName,
            _ => RawItemName,
        };
    }

    protected virtual void UpdateVisuals()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            CacheRenderers();

        Color color = CurrentCookState switch
        {
            CookState.Cooked => cookedColor,
            CookState.Burned => burnedColor,
            _ => rawColor,
        };

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            MeshRenderer renderer = targetRenderers[i];
            if (renderer == null) continue;

            _propertyBlock.Clear();
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}