using UnityEngine;
using Fusion;

/// <summary>
/// 파스타 공통 조리 베이스.
/// - 썰기 없음
/// - 생 -> 익음 -> 탐
/// - 네트워크 조리 상태 동기화
/// - FixedUpdateNetwork 끝에서 base.FixedUpdateNetwork() 호출하여 기본 위치 동기화 유지
/// </summary>
public abstract class PastaCookableBaseItem : PickableItem, ICookable
{
    [Header("Cooking")]
    [SerializeField, Min(0.01f)] private float cookSeconds = 30f;
    [SerializeField, Min(0.01f)] private float burnAfterCookedSeconds = 30f;

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

    private bool _hasPendingInheritedState;
    private CookState _pendingCookState = CookState.Raw;
    private float _pendingCookProgress;

    protected abstract string RawItemName { get; }
    protected abstract string CookedItemName { get; }
    protected abstract string BurnedItemName { get; }

    protected abstract string RawIngredientId { get; }
    protected abstract string CookedIngredientId { get; }
    protected abstract string BurnedIngredientId { get; }

    protected virtual CookState DefaultSpawnCookState => CookState.Raw;

    protected virtual float GetDefaultSpawnCookProgress()
    {
        return DefaultSpawnCookState switch
        {
            CookState.Burned => CurrentBurnSeconds,
            CookState.Cooked => CurrentCookSeconds,
            _ => 0f
        };
    }

    protected float CurrentCookSeconds => Mathf.Max(0.01f, cookSeconds);
    protected float CurrentBurnAfterCookedSeconds => Mathf.Max(0.01f, burnAfterCookedSeconds);
    protected float CurrentBurnSeconds => CurrentCookSeconds + CurrentBurnAfterCookedSeconds;

    public override bool CanChop => false;

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
            RefreshDisplayState();
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

    protected float CookProgress
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

    protected virtual void OnValidate()
    {
        if (cookSeconds < 0.01f)
            cookSeconds = 30f;

        if (burnAfterCookedSeconds < 0.01f)
            burnAfterCookedSeconds = 30f;
    }

    protected virtual void Start()
    {
        ApplyLocalViewFromPendingOrDefault();
        RefreshDisplayState();
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            ApplyAuthoritySpawnState();
        }
        else if (IsNetworkReady)
        {
            CopyFromNetworkToLocal();
        }
        else
        {
            ApplyLocalViewFromPendingOrDefault();
        }

        RefreshDisplayState();
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
            RefreshDisplayState();
    }

    protected virtual void FixedUpdate()
    {
        if (IsNetworkReady)
            return;

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

    public virtual void ApplyInheritedState(CookState inheritedCookState, float inheritedCookProgress)
    {
        _hasPendingInheritedState = true;
        _pendingCookState = inheritedCookState;
        _pendingCookProgress = Mathf.Max(0f, inheritedCookProgress);

        if (IsNetworkReady && HasStateAuthority)
        {
            CookStateNetworked = _pendingCookState;
            CookProgressNetworked = _pendingCookProgress;
            IsBeingCookedNetworked = false;
        }

        _localCookState = _pendingCookState;
        _localCookProgress = _pendingCookProgress;
        _localIsBeingCooked = false;

        RefreshDisplayState();
    }

    public override void TransferStateTo(PickableItem target)
    {
        if (target is PastaCookableBaseItem pasta)
            pasta.ApplyInheritedState(CurrentCookState, CookProgress);
    }

    protected virtual void OnCookProgressProcessed()
    {
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

    private void ProcessCookingStep()
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
            OnCookProgressProcessed();
        }

        _queuedHeat = 0f;
    }

    private void ApplyAuthoritySpawnState()
    {
        CookState state;
        float progress;

        if (_hasPendingInheritedState)
        {
            state = _pendingCookState;
            progress = _pendingCookProgress;
        }
        else
        {
            state = DefaultSpawnCookState;
            progress = GetDefaultSpawnCookProgress();
        }

        CookStateNetworked = state;
        CookProgressNetworked = Mathf.Max(0f, progress);
        IsBeingCookedNetworked = false;

        _localCookState = state;
        _localCookProgress = Mathf.Max(0f, progress);
        _localIsBeingCooked = false;
    }

    private void ApplyLocalViewFromPendingOrDefault()
    {
        if (_hasPendingInheritedState)
        {
            _localCookState = _pendingCookState;
            _localCookProgress = Mathf.Max(0f, _pendingCookProgress);
            _localIsBeingCooked = false;
            return;
        }

        _localCookState = DefaultSpawnCookState;
        _localCookProgress = Mathf.Max(0f, GetDefaultSpawnCookProgress());
        _localIsBeingCooked = false;
    }

    private void CopyFromNetworkToLocal()
    {
        _localCookState = CookStateNetworked;
        _localIsBeingCooked = IsBeingCookedNetworked;
        _localCookProgress = CookProgressNetworked;
    }

    private void OnCookStateChanged()
    {
        _localCookState = CookStateNetworked;
        RefreshDisplayState();
    }

    private void RefreshDisplayState()
    {
        itemName = CurrentCookState switch
        {
            CookState.Cooked => CookedItemName,
            CookState.Burned => BurnedItemName,
            _ => RawItemName
        };

        metadata["ingredientID"] = CurrentCookState switch
        {
            CookState.Cooked => CookedIngredientId,
            CookState.Burned => BurnedIngredientId,
            _ => RawIngredientId
        };
    }
}