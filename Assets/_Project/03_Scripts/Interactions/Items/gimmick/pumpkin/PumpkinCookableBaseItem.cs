using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 호박 계열 공통 조리 베이스.
/// - 자동 렌더러 캐싱
/// - Raw / Cooked / Burned 색상 갱신
/// - 상태 상속
/// </summary>
public abstract class PumpkinCookableBaseItem : PickableItem, ICookable
{
    [Header("Cooking Settings")]
    [SerializeField, Min(0.01f)] private float cookSeconds = 40f;
    [SerializeField, Min(0.01f)] private float burnSeconds = 60f;

    [Header("Auto Visuals")]
    [SerializeField] private MeshRenderer[] targetRenderers;
    [SerializeField] private Color rawColor = new Color(1f, 0.6f, 0.1f);
    [SerializeField] private Color cookedColor = new Color(0.8f, 0.45f, 0.15f);
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
    private bool _initializedFromParent;
    private bool _localInitApplied;

    private MaterialPropertyBlock _propertyBlock;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    protected abstract string RawItemName { get; }
    protected abstract string CookedItemName { get; }
    protected abstract string BurnedItemName { get; }
    protected abstract string IngredientId { get; }

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState previous = CurrentCookState;
            if (previous == value) return;

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

    protected virtual bool CanReceiveHeat => CurrentCookState != CookState.Burned;
    protected virtual bool ShouldRenderForLocalViewer() => true;
    protected virtual string GetRawDisplayName() => RawItemName;

    protected virtual void OnValidate()
    {
        if (cookSeconds < 0.01f)
            cookSeconds = 40f;

        if (burnSeconds < cookSeconds)
            burnSeconds = cookSeconds;
    }

    protected virtual void Start()
    {
        if (!IsNetworkReady && !_localInitApplied)
        {
            _localCookState = CookState.Raw;
            _localIsBeingCooked = false;
            _localCookProgress = 0f;
            _localInitApplied = true;
        }

        CacheRenderers();
        RefreshVisualState();
    }

    public override void Spawned()
    {
        CacheRenderers();

        if (HasStateAuthority && !_initializedFromParent)
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

    protected virtual void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessSimulationStep(Time.fixedDeltaTime);
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

        float deltaTime = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
        ProcessSimulationStep(deltaTime);

        if (Object != null && Object.IsValid)
            base.FixedUpdateNetwork();
    }

    public virtual void CookInFire(float heat)
    {
        heat = Mathf.Max(0f, heat);
        if (heat <= 0f || !CanReceiveHeat)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority) return;
            _queuedHeat += heat;
            return;
        }

        _queuedHeat += heat;
    }

    private void ProcessSimulationStep(float deltaTime)
    {
        if (CanReceiveHeat && _queuedHeat > 0f)
        {
            isBeingCooked = true;
            CookProgress += _queuedHeat;
            UpdateCookStateFromProgress();
        }
        else
        {
            isBeingCooked = false;
        }

        _queuedHeat = 0f;
        OnAfterCookStep(deltaTime);
    }

    protected virtual void OnAfterCookStep(float deltaTime)
    {
    }

    protected virtual void UpdateCookStateFromProgress()
    {
        if (CookProgress >= burnSeconds)
            CurrentCookState = CookState.Burned;
        else if (CookProgress >= cookSeconds)
            CurrentCookState = CookState.Cooked;
        else
            CurrentCookState = CookState.Raw;
    }

    public virtual void ApplyInheritedState(CookState inheritedCookState, float inheritedCookProgress)
    {
        _initializedFromParent = true;
        _localInitApplied = true;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority) return;
            CookStateNetworked = inheritedCookState;
            CookProgressNetworked = Mathf.Max(0f, inheritedCookProgress);
            IsBeingCookedNetworked = false;
        }

        _localCookState = inheritedCookState;
        _localCookProgress = Mathf.Max(0f, inheritedCookProgress);
        _localIsBeingCooked = false;

        RefreshVisualState();
    }

    public override void TransferStateTo(PickableItem target)
    {
        if (target is PumpkinCookableBaseItem pumpkin)
            pumpkin.ApplyInheritedState(CurrentCookState, CookProgress);
    }

    protected void CacheRenderers()
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
        UpdateVisuals();
        UpdateItemName();
        metadata["ingredientID"] = IngredientId;
    }

    protected virtual void UpdateItemName()
    {
        itemName = CurrentCookState switch
        {
            CookState.Cooked => CookedItemName,
            CookState.Burned => BurnedItemName,
            _ => GetRawDisplayName()
        };
    }

    protected virtual void UpdateVisuals()
    {
        CacheRenderers();

        Color color = CurrentCookState switch
        {
            CookState.Cooked => cookedColor,
            CookState.Burned => burnedColor,
            _ => rawColor
        };

        bool shouldRender = ShouldRenderForLocalViewer();

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            MeshRenderer renderer = targetRenderers[i];
            if (renderer == null) continue;

            renderer.enabled = shouldRender;

            _propertyBlock.Clear();
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}