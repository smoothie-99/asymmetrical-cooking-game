using System;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 기본 드래곤 알 공통 조리 베이스.
/// - 30초 이후 노랑/초록/빨강 미리보기 색상 변화
/// - 화구에서 꺼내는 순간 해당 색의 고정 알 프리팹으로 치환
/// - 60초 이상이면 즉시 빨간색 알로 치환
/// - 기본 알 상태에서는 절단 불가
/// </summary>
public abstract class DragonEggCookingBaseItem : DragonEggBaseItem, ICookable
{
    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float timeToStartChange = 30f;
    [SerializeField, Min(0.1f)] private float timeToRedEgg = 60f;
    [SerializeField, Min(0.1f)] private float minChangeInterval = 2f;
    [SerializeField, Min(0.1f)] private float maxChangeInterval = 4f;

    [Header("Locked Egg Prefabs")]
    [SerializeField] private NetworkObject yellowEggPrefab;
    [SerializeField] private NetworkObject greenEggPrefab;
    [SerializeField] private NetworkObject redEggPrefab;

    [Networked, OnChangedRender(nameof(OnEggStateChanged))]
    public DragonEggState EggStateNetworked { get; set; }

    [Networked]
    public bool IsBeingCookedNetworked { get; set; }

    [Networked]
    public float CookTimeNetworked { get; set; }

    [Networked]
    public CookState CookStateNetworked { get; set; }

    [Networked]
    private int ChangeStepIndexNetworked { get; set; }

    [Networked]
    private float NextChangeAtCookTimeNetworked { get; set; }

    [Networked]
    private int StableSeedNetworked { get; set; }

    private DragonEggState _localEggState = DragonEggState.Raw;
    private bool _localIsBeingCooked = false;
    private float _localCookTime = 0f;
    private CookState _localCookState = CookState.Raw;
    private int _localChangeStepIndex = -1;
    private float _localNextChangeAtCookTime = 0f;
    private int _localStableSeed = 0;

    private float _queuedHeat = 0f;
    private bool _localInitApplied = false;

    protected abstract string RawItemName { get; }
    protected abstract string ChangingItemName { get; }

    protected override string DisplayItemName => CurrentEggState == DragonEggState.Raw ? RawItemName : ChangingItemName;
    protected override DragonEggState DisplayEggState => CurrentEggState;

    public override bool CanChop => false;

    public DragonEggState CurrentEggState
    {
        get => IsNetworkReady ? EggStateNetworked : _localEggState;
        protected set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                EggStateNetworked = value;
            }

            _localEggState = value;
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

    public float CookTime
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
        }
    }

    private int ChangeStepIndex
    {
        get => IsNetworkReady ? ChangeStepIndexNetworked : _localChangeStepIndex;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                ChangeStepIndexNetworked = value;
            }

            _localChangeStepIndex = value;
        }
    }

    private float NextChangeAtCookTime
    {
        get => IsNetworkReady ? NextChangeAtCookTimeNetworked : _localNextChangeAtCookTime;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                NextChangeAtCookTimeNetworked = value;
            }

            _localNextChangeAtCookTime = value;
        }
    }

    private int StableSeed
    {
        get => IsNetworkReady ? StableSeedNetworked : _localStableSeed;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                StableSeedNetworked = value;
            }

            _localStableSeed = value;
        }
    }

    protected override void Start()
    {
        if (!IsNetworkReady && !_localInitApplied)
        {
            InitializeLocalDefaults();
            _localInitApplied = true;
        }

        base.Start();
    }

    private void OnValidate()
    {
        if (timeToRedEgg < timeToStartChange)
            timeToRedEgg = timeToStartChange;

        if (minChangeInterval < 0.1f)
            minChangeInterval = 0.1f;

        if (maxChangeInterval < minChangeInterval)
            maxChangeInterval = minChangeInterval;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
            InitializeAuthoritativeDefaults();
        else
            SyncLocalCacheFromNetwork();

        base.Spawned();
    }

    public override void Render()
    {
        base.Render();

        if (!IsNetworkReady)
            return;

        bool dirty = false;

        if (_localEggState != EggStateNetworked)
        {
            _localEggState = EggStateNetworked;
            dirty = true;
        }

        if (_localIsBeingCooked != IsBeingCookedNetworked)
            _localIsBeingCooked = IsBeingCookedNetworked;

        if (!Mathf.Approximately(_localCookTime, CookTimeNetworked))
            _localCookTime = CookTimeNetworked;

        if (_localCookState != CookStateNetworked)
            _localCookState = CookStateNetworked;

        if (_localChangeStepIndex != ChangeStepIndexNetworked)
            _localChangeStepIndex = ChangeStepIndexNetworked;

        if (!Mathf.Approximately(_localNextChangeAtCookTime, NextChangeAtCookTimeNetworked))
            _localNextChangeAtCookTime = NextChangeAtCookTimeNetworked;

        if (_localStableSeed != StableSeedNetworked)
            _localStableSeed = StableSeedNetworked;

        if (dirty)
            RefreshVisualState();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessSimulationStep();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
        {
            _queuedHeat = 0f;
            return;
        }

        ProcessSimulationStep();
    }

    public void CookInFire(float heat)
    {
        if (heat <= 0f) return;
        if (CookTime >= timeToRedEgg) return;  // 60초 이상이면 더 이상 heat 받지 않음

        if (IsNetworkReady)
        {
            if (!HasStateAuthority) return;
            _queuedHeat += heat;
            return;
        }

        _queuedHeat += heat;
    }

    private void InitializeLocalDefaults()
    {
        _localEggState = DragonEggState.Raw;
        _localIsBeingCooked = false;
        _localCookTime = 0f;
        _localCookState = CookState.Raw;
        _localChangeStepIndex = -1;
        _localNextChangeAtCookTime = 0f;
        _localStableSeed = GenerateSeed();
    }

    private void InitializeAuthoritativeDefaults()
    {
        StableSeed = GenerateSeed();
        CurrentEggState = DragonEggState.Raw;
        isBeingCooked = false;
        CookTime = 0f;
        CurrentCookState = CookState.Raw;
        ChangeStepIndex = -1;
        NextChangeAtCookTime = 0f;
    }

    private void SyncLocalCacheFromNetwork()
    {
        _localEggState = EggStateNetworked;
        _localIsBeingCooked = IsBeingCookedNetworked;
        _localCookTime = CookTimeNetworked;
        _localCookState = CookStateNetworked;
        _localChangeStepIndex = ChangeStepIndexNetworked;
        _localNextChangeAtCookTime = NextChangeAtCookTimeNetworked;
        _localStableSeed = StableSeedNetworked;
    }

    private void ProcessSimulationStep()
    {
        bool hasValidHeatThisStep = _queuedHeat > 0f;

        if (!hasValidHeatThisStep)
        {
            isBeingCooked = false;

            // 화구에서 꺼낼 때 (heat = 0) Raw가 아닌 상태면 고정 알로 교체
            // 단, 플레이어가 손에 들고 있는 중이면 교체 지연 (손에서 놓을 때까지 대기)
            if (CurrentEggState != DragonEggState.Raw && !isHeld)
            {
                ReplaceWithLockedEgg(CurrentEggState);
                return;  // 교체 후 추가 처리 방지
            }

            _queuedHeat = 0f;
            return;
        }

        isBeingCooked = true;
        CookTime += _queuedHeat;
        _queuedHeat = 0f;

        EvaluateEggState(CookTime);
    }

    private void EvaluateEggState(float currentCookTime)
    {
        if (currentCookTime >= timeToRedEgg)
        {
            CurrentEggState = DragonEggState.Red;
            CurrentCookState = CookState.Burned;
            ReplaceWithLockedEgg(DragonEggState.Red);
            return;
        }

        if (currentCookTime < timeToStartChange)
        {
            CurrentEggState = DragonEggState.Raw;
            CurrentCookState = CookState.Raw;
            return;
        }

        if (ChangeStepIndex < 0)
        {
            ChangeStepIndex = 0;
            ApplyStateForChangeStep(ChangeStepIndex);
            NextChangeAtCookTime = timeToStartChange + GetDeterministicInterval(ChangeStepIndex);
        }

        while (currentCookTime >= NextChangeAtCookTime && currentCookTime < timeToRedEgg)
        {
            ChangeStepIndex++;
            ApplyStateForChangeStep(ChangeStepIndex);
            NextChangeAtCookTime += GetDeterministicInterval(ChangeStepIndex);
        }

        SyncCookStateFromEggState();
    }

    private void ApplyStateForChangeStep(int stepIndex)
    {
        int colorIndex = GetColorIndexForStep(stepIndex);

        switch (colorIndex)
        {
            case 0:
                CurrentEggState = DragonEggState.Yellow;
                break;
            case 1:
                CurrentEggState = DragonEggState.Green;
                break;
            default:
                CurrentEggState = DragonEggState.Red;
                break;
        }

        SyncCookStateFromEggState();
    }

    private void SyncCookStateFromEggState()
    {
        switch (CurrentEggState)
        {
            case DragonEggState.Raw:
                CurrentCookState = CookState.Raw;
                break;
            case DragonEggState.Yellow:
            case DragonEggState.Green:
            case DragonEggState.Red:
                CurrentCookState = CookState.Cooked;
                break;
        }
    }

    private void ReplaceWithLockedEgg(DragonEggState state)
    {
        NetworkObject prefab = GetLockedEggPrefab(state);
        if (prefab == null)
        {
            Debug.LogError($"[DragonEggCookingBaseItem] {state} locked egg prefab is not assigned.");
            return;
        }

        SpawnReplacementAndDespawnSelf(prefab, false);
    }

    private NetworkObject GetLockedEggPrefab(DragonEggState state)
    {
        switch (state)
        {
            case DragonEggState.Yellow:
                return yellowEggPrefab;
            case DragonEggState.Green:
                return greenEggPrefab;
            case DragonEggState.Red:
                return redEggPrefab;
            default:
                return null;
        }
    }

    private int GetColorIndexForStep(int stepIndex)
    {
        int seed = unchecked(StableSeed ^ (stepIndex * 12345678));
        float t = HashToUnitFloat(seed);
        return Mathf.FloorToInt(t * 3f);
    }

    private float GetDeterministicInterval(int stepIndex)
    {
        int seed = unchecked(StableSeed ^ (stepIndex * 73856093));
        float t = HashToUnitFloat(seed);
        return Mathf.Lerp(minChangeInterval, maxChangeInterval, t);
    }

    private static int GenerateSeed()
    {
        return Guid.NewGuid().GetHashCode();
    }

    private static float HashToUnitFloat(int value)
    {
        unchecked
        {
            uint x = (uint)value;
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777215f;
        }
    }

    private void OnEggStateChanged()
    {
        _localEggState = EggStateNetworked;
        RefreshVisualState();
    }
}
