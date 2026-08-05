using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 흩어진 뒤 남는 환영 물고기 대상.
/// - startsAsFake=true  면 가짜 물고기. 칼질 시 즉시 소멸.
/// - startsAsFake=false 면 진짜 물고기. 일정 시간 안에 찾지 못하면 소멸.
/// - 진짜를 칼질하면 손질된 환영 물고기 프리팹으로 교체.
/// </summary>
public class PhantomFishTargetItem : PhantomFishKnifeTargetBaseItem
{
    [Header("Identity")]
    [SerializeField] private bool startsAsFake = false;

    [Header("Result")]
    [SerializeField] private NetworkObject preparedFishPrefab;
    [SerializeField] private GameObject vanishEffectPrefab;

    [Header("Escape")]
    [SerializeField, Min(0.1f)] private float defaultEscapeTime = 10f;

    [Networked]
    public bool IsFakeNetworked { get; set; }

    [Networked]
    public float EscapeTimerNetworked { get; set; }

    private bool _localIsFake;
    private float _localEscapeTimer;
    private bool _queuedChopRequest;
    private bool _isDespawning;

    // Spawn 직전 onBeforeSpawned / 로컬 Instantiate 직후 identity를 주입하기 위한 임시값
    private bool _hasPendingSpawnInit;
    private bool _pendingStartsAsFake;
    private float _pendingEscapeTime;

    protected override string DefaultItemName => "환영 물고기";
    protected override string DefaultIngredientId => "환영 물고기";
    protected override string KnifeLabel => IsFake ? "정체 확인" : "손질하기";
    protected override bool CanKnifeProcess => !_isDespawning && (IsFake || preparedFishPrefab != null);

    private bool IsFake
    {
        get => IsNetworkReady ? IsFakeNetworked : _localIsFake;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                IsFakeNetworked = value;
            }

            _localIsFake = value;
        }
    }

    private float EscapeTimerValue
    {
        get => IsNetworkReady ? EscapeTimerNetworked : _localEscapeTimer;
        set
        {
            float clamped = Mathf.Max(0f, value);

            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                EscapeTimerNetworked = clamped;
            }

            _localEscapeTimer = clamped;
        }
    }

    /// <summary>
    /// Dummy가 스폰 직전에 호출해서 진짜/가짜 여부와 탈출 타이머를 주입한다.
    /// 네트워크에서는 authority 쪽 Spawned()에서 Networked 값으로 반영된다.
    /// 로컬 Instantiate에서는 즉시 로컬 상태에 반영된다.
    /// </summary>
    public void InitializeSpawnedState(bool isFake, float realEscapeTime)
    {
        _hasPendingSpawnInit = true;
        _pendingStartsAsFake = isFake;
        _pendingEscapeTime = Mathf.Max(0f, realEscapeTime);

        startsAsFake = isFake;
        _localIsFake = isFake;
        _localEscapeTimer = isFake ? 0f : _pendingEscapeTime;
        itemName = DefaultItemName;
        metadata["ingredientID"] = DefaultIngredientId;
    }

    private void OnValidate()
    {
        defaultEscapeTime = Mathf.Max(0.1f, defaultEscapeTime);
    }

    public override void Spawned()
    {
        base.Spawned();

        _queuedChopRequest = false;
        _isDespawning = false;
        itemName = DefaultItemName;
        metadata["ingredientID"] = DefaultIngredientId;

        if (HasStateAuthority)
        {
            bool finalIsFake = _hasPendingSpawnInit ? _pendingStartsAsFake : startsAsFake;
            float finalEscapeTime = finalIsFake ? 0f : (_hasPendingSpawnInit ? _pendingEscapeTime : defaultEscapeTime);

            IsFake = finalIsFake;
            EscapeTimerValue = finalEscapeTime;
        }
        else if (IsNetworkReady)
        {
            _localIsFake = IsFakeNetworked;
            _localEscapeTimer = EscapeTimerNetworked;
        }

        if (!IsNetworkReady && _hasPendingSpawnInit)
        {
            _localIsFake = _pendingStartsAsFake;
            _localEscapeTimer = _pendingStartsAsFake ? 0f : _pendingEscapeTime;
        }
        else if (!IsNetworkReady)
        {
            _localIsFake = startsAsFake;
            _localEscapeTimer = startsAsFake ? 0f : defaultEscapeTime;
        }
    }

    public override void Render()
    {
        base.Render();

        if (!IsNetworkReady)
            return;

        if (_localIsFake != IsFakeNetworked)
            _localIsFake = IsFakeNetworked;

        if (!Mathf.Approximately(_localEscapeTimer, EscapeTimerNetworked))
            _localEscapeTimer = EscapeTimerNetworked;
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessSimulationStep(Time.fixedDeltaTime);
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsNetworkReady)
            return;

        if (HasStateAuthority)
        {
            float deltaTime = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
            ProcessSimulationStep(deltaTime);
        }

        if (Object != null && Object.IsValid)
            base.FixedUpdateNetwork();
    }

    private void ProcessSimulationStep(float deltaTime)
    {
        if (_isDespawning)
            return;

        if (_queuedChopRequest)
        {
            _queuedChopRequest = false;

            if (IsNetworkReady)
                ResolveKnifeAuthority();
            else
                ResolveKnifeLocal();

            if (_isDespawning)
                return;
        }

        // 진짜 물고기만 일정 시간 후 사라짐
        if (!IsFake)
        {
            EscapeTimerValue -= deltaTime;
            if (EscapeTimerValue <= 0f)
            {
                if (IsNetworkReady)
                    VanishAndDestroyAuthority();
                else
                    VanishAndDestroyLocal();
            }
        }
    }

    public override void Chop(CuttingStation board)
    {
        if (!CanChop)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                RPC_RequestChop();
                return;
            }

            _queuedChopRequest = true;
            return;
        }

        _queuedChopRequest = true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestChop()
    {
        if (!CanChop)
            return;

        _queuedChopRequest = true;
    }

    private void ResolveKnifeAuthority()
    {
        if (_isDespawning)
            return;

        if (IsFake)
        {
            VanishAndDestroyAuthority();
            return;
        }

        SpawnPreparedAndDestroyAuthority();
    }

    private void ResolveKnifeLocal()
    {
        if (_isDespawning)
            return;

        if (IsFake)
        {
            VanishAndDestroyLocal();
            return;
        }

        SpawnPreparedAndDestroyLocal();
    }

    private void SpawnPreparedAndDestroyAuthority()
    {
        if (preparedFishPrefab == null)
        {
            Debug.LogWarning("[PhantomFishTargetItem] preparedFishPrefab이 비어 있습니다.");
            return;
        }

        _isDespawning = true;
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        List<string> seqCopy = new List<string>(cookingSeq);
        Dictionary<string, string> metaCopy = new Dictionary<string, string>(metadata);

        string logTag = KnifeLabel;
        if (!string.IsNullOrEmpty(logTag))
        {
            seqCopy.Add(logTag);
            metaCopy["cutMethod"] = logTag;
        }

        Runner.Spawn(preparedFishPrefab, pos, rot,
            onBeforeSpawned: (_, obj) =>
            {
                if (obj == null)
                    return;

                PickableItem item = obj.GetComponent<PickableItem>();
                if (item == null)
                    return;

                item.cookingSeq = new List<string>(seqCopy);
                foreach (var kv in metaCopy)
                    item.metadata[kv.Key] = kv.Value;
            });

        RPC_PlayVanishEffect(pos);

        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }

    private void SpawnPreparedAndDestroyLocal()
    {
        if (preparedFishPrefab == null)
        {
            Debug.LogWarning("[PhantomFishTargetItem] preparedFishPrefab이 비어 있습니다.");
            return;
        }

        _isDespawning = true;
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        GameObject go = Instantiate(preparedFishPrefab.gameObject, pos, rot);

        PickableItem item = go.GetComponent<PickableItem>();
        if (item != null)
        {
            item.cookingSeq = new List<string>(cookingSeq);
            foreach (var kv in metadata)
                item.metadata[kv.Key] = kv.Value;

            string logTag = KnifeLabel;
            if (!string.IsNullOrEmpty(logTag))
            {
                item.cookingSeq.Add(logTag);
                item.metadata["cutMethod"] = logTag;
            }
        }

        PlayLocalVanishEffect(pos);
        Destroy(gameObject);
    }

    private void VanishAndDestroyAuthority()
    {
        if (_isDespawning)
            return;

        _isDespawning = true;
        Vector3 pos = transform.position;
        RPC_PlayVanishEffect(pos);

        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }

    private void VanishAndDestroyLocal()
    {
        if (_isDespawning)
            return;

        _isDespawning = true;
        PlayLocalVanishEffect(transform.position);
        Destroy(gameObject);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayVanishEffect(Vector3 pos)
    {
        PlayLocalVanishEffect(pos);
    }

    private void PlayLocalVanishEffect(Vector3 pos)
    {
        if (vanishEffectPrefab == null)
            return;

        GameObject fx = Instantiate(vanishEffectPrefab, pos, Quaternion.identity);
        Destroy(fx, 2f);
    }
}