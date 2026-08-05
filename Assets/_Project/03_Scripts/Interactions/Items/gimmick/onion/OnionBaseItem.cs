using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 회복 양파 계열 공통 베이스.
/// - 조리 상태 / cookTime 전달
/// - 타버림 비주얼
/// - 단계 복원(옵션)
/// </summary>
public abstract class OnionBaseItem : PickableItem, ICookable
{
    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float timeToBurn = -1f;

    [Header("Restore Settings (restorable stages only)")]
    [SerializeField] private NetworkObject restorePrefab;
    [SerializeField, Min(0.1f)] private float timeToRestore = -1f;

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked]
    private float CookTimeNetworked { get; set; }

    [Networked]
    private float RestoreProgressNetworked { get; set; }

    private CookState _localCookState = CookState.Raw;
    private float _localCookTime = 0f;
    private float _localRestoreProgress = 0f;

    private MeshRenderer[] _renderers;
    private MaterialPropertyBlock _mpb;

    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color BurnedColor = new Color(0.15f, 0.12f, 0.08f, 1f);

    protected abstract string RawItemName { get; }
    protected virtual string CookedItemName => RawItemName;
    protected abstract string BurnedItemName { get; }
    protected abstract string IngredientId { get; }

    protected virtual float DefaultBurnTime => 30f;
    protected virtual float DefaultCookTime => DefaultBurnTime * 0.5f;
    protected virtual float DefaultRestoreTime => 10f;
    protected virtual bool SupportsRestore => false;

    protected float TimeToBurn => timeToBurn > 0f ? timeToBurn : DefaultBurnTime;
    protected float TimeToCook => DefaultCookTime;
    protected float TimeToRestore => timeToRestore > 0f ? timeToRestore : DefaultRestoreTime;
    protected NetworkObject RestorePrefab => restorePrefab;

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState prev = CurrentCookState;
            if (prev == value) return;

            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookStateNetworked = value;
            }

            _localCookState = value;
            RecordCookStateChange(prev, value);
            RefreshVisualState();
        }
    }

    public float cookTime
    {
        get => IsNetworkReady ? CookTimeNetworked : _localCookTime;
        protected set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookTimeNetworked = value;
            }

            _localCookTime = value;
        }
    }

    protected float RestoreProgress
    {
        get => IsNetworkReady ? RestoreProgressNetworked : _localRestoreProgress;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                RestoreProgressNetworked = value;
            }

            _localRestoreProgress = value;
        }
    }

    protected virtual bool CanRestore =>
        SupportsRestore &&
        RestorePrefab != null &&
        !isHeld &&
        cookTime <= 0f &&
        CurrentCookState != CookState.Burned;

    protected virtual void OnValidate()
    {
        if (timeToBurn < 0.1f)
            timeToBurn = DefaultBurnTime;

        if (SupportsRestore && timeToRestore < 0.1f)
            timeToRestore = DefaultRestoreTime;
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
            cookTime = 0f;
            RestoreProgress = 0f;
        }
        else
        {
            _localCookState = IsNetworkReady ? CookStateNetworked : _localCookState;
            _localCookTime = IsNetworkReady ? CookTimeNetworked : _localCookTime;
            _localRestoreProgress = IsNetworkReady ? RestoreProgressNetworked : _localRestoreProgress;
        }

        RefreshVisualState();
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (!HasStateAuthority) return;
        HandleRestoreStep(Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime);
    }

    protected virtual void FixedUpdate()
    {
        if (IsNetworkReady) return;
        HandleRestoreStep(Time.fixedDeltaTime);
    }

    public virtual void ApplyInheritedState(CookState inheritedState, float inheritedCookTime)
    {
        if (IsNetworkReady && !HasStateAuthority) return;

        if (IsNetworkReady)
        {
            CookStateNetworked = inheritedState;
            CookTimeNetworked = inheritedCookTime;
            RestoreProgressNetworked = 0f;
        }

        _localCookState = inheritedState;
        _localCookTime = inheritedCookTime;
        _localRestoreProgress = 0f;

        RefreshVisualState();
    }

    public override void TransferStateTo(PickableItem target)
    {
        if (target is OnionBaseItem onion)
            onion.ApplyInheritedState(CurrentCookState, cookTime);
    }

    public virtual void CookInFire(float heat)
    {
        if (heat <= 0f) return;
        if (CurrentCookState == CookState.Burned) return;
        if (IsNetworkReady && !HasStateAuthority) return;

        cookTime += heat;
        float timeToBurn = TimeToBurn;
        float timeToCook = TimeToCook;

        if (cookTime >= timeToBurn)
        {
            CurrentCookState = CookState.Burned;
        }
        else if (cookTime >= timeToCook)
        {
            CurrentCookState = CookState.Cooked;
        }
    }

    private void HandleRestoreStep(float deltaTime)
    {
        if (!SupportsRestore) return;

        if (CanRestore)
        {
            RestoreProgress += deltaTime;
            if (RestoreProgress >= TimeToRestore)
                RestoreToPreviousStage();
        }
        else if (RestoreProgress > 0f)
        {
            RestoreProgress = 0f;
        }
    }

    protected virtual void RestoreToPreviousStage()
    {
        if (RestorePrefab == null) return;

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;

        if (IsNetworkReady && Runner != null && Object != null && Object.IsValid)
        {
            NetworkObject spawned = Runner.Spawn(RestorePrefab, pos, rot);
            if (spawned != null && spawned.TryGetComponent(out PickableItem target))
                TransferStateTo(target);
        }
        else
        {
            NetworkObject spawned = Instantiate(RestorePrefab, pos, rot);
            if (spawned != null && spawned.TryGetComponent(out PickableItem target))
                TransferStateTo(target);
        }

        SafeDespawnSelf();
    }

    protected void SafeDespawnSelf()
    {
        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }

    private void OnCookStateChanged()
    {
        _localCookState = CookStateNetworked;
        RefreshVisualState();
    }

    protected void CacheRenderers()
    {
        if (_renderers != null) return;

        _renderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb = new MaterialPropertyBlock();
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
            CookState.Burned => BurnedItemName,
            CookState.Cooked => CookedItemName,
            _ => RawItemName,
        };
    }

    protected virtual void UpdateVisuals()
    {
        if (_renderers == null) CacheRenderers();

        bool burned = CurrentCookState == CookState.Burned;

        for (int i = 0; i < _renderers.Length; i++)
        {
            MeshRenderer renderer = _renderers[i];
            if (renderer == null) continue;

            if (burned)
            {
                _mpb.Clear();
                _mpb.SetColor(ColorId, BurnedColor);
                renderer.SetPropertyBlock(_mpb);
            }
            else
            {
                renderer.SetPropertyBlock(null);
            }
        }
    }
}