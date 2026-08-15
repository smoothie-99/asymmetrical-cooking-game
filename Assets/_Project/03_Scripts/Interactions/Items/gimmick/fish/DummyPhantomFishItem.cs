using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 시작용 더미 환영 물고기.
/// - 첫 칼질 시 자신은 사라지고
/// - 진짜 환영 물고기 1개 + 가짜 환영 물고기 여러 개로 흩어진다.
/// - 분열 이펙트는 잘린 원래 위치에서만 나오며, 진짜 위치에는 따로 표시하지 않는다.
/// </summary>
public class DummyPhantomFishItem : PhantomFishKnifeTargetBaseItem
{
    [Header("Scatter")]
    [SerializeField] private NetworkObject realFishPrefab;
    [SerializeField] private NetworkObject fakeFishPrefab;
    [SerializeField, Min(0)] private int fakeCount = 4;
    [SerializeField, Min(0.1f)] private float scatterRadius = 3f;
    [SerializeField, Min(0.1f)] private float escapeTime = 10f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private GameObject vanishEffectPrefab;

    private bool _queuedChopRequest;
    private bool _isDespawning;

    protected override string DefaultItemName => "환영 물고기";
    protected override string DefaultIngredientId => "환영 물고기";
    protected override string KnifeLabel => "환영 가르기";
    protected override bool CanKnifeProcess => !_isDespawning && realFishPrefab != null && fakeFishPrefab != null;

    protected override void Start()
    {
        if (groundLayer == 0)
            groundLayer = LayerMask.GetMask("Default", "Interactable");

        base.Start();
    }

    private void OnValidate()
    {
        fakeCount = Mathf.Max(0, fakeCount);
        scatterRadius = Mathf.Max(0.1f, scatterRadius);
        escapeTime = Mathf.Max(0.1f, escapeTime);
    }

    public override void Spawned()
    {
        base.Spawned();
        _queuedChopRequest = false;
        _isDespawning = false;
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;

        if (_queuedChopRequest)
        {
            _queuedChopRequest = false;
            ScatterAndDestroyLocal();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsNetworkReady)
            return;

        if (HasStateAuthority && _queuedChopRequest)
        {
            _queuedChopRequest = false;
            ScatterAndDestroyAuthority();
        }

        if (Object != null && Object.IsValid)
            base.FixedUpdateNetwork();
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

    private void ScatterAndDestroyAuthority()
    {
        if (_isDespawning)
            return;

        if (realFishPrefab == null || fakeFishPrefab == null)
        {
            Debug.LogWarning("[DummyPhantomFishItem] realFishPrefab 또는 fakeFishPrefab이 비어 있습니다.");
            return;
        }

        _isDespawning = true;
        Vector3 origin = transform.position;

        // 진짜 1마리
        Vector3 realPos = GetScatterPosition(origin);
        SpawnTargetAuthority(realFishPrefab, realPos, false);

        // 가짜 여러 마리
        for (int i = 0; i < fakeCount; i++)
        {
            Vector3 fakePos = GetScatterPosition(origin);
            SpawnTargetAuthority(fakeFishPrefab, fakePos, true);
        }

        // 분열 이펙트는 원래 잘린 자리에서만 표시.
        RPC_PlayVanishEffect(origin);

        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }

    private void ScatterAndDestroyLocal()
    {
        if (_isDespawning)
            return;

        if (realFishPrefab == null || fakeFishPrefab == null)
        {
            Debug.LogWarning("[DummyPhantomFishItem] realFishPrefab 또는 fakeFishPrefab이 비어 있습니다.");
            return;
        }

        _isDespawning = true;
        Vector3 origin = transform.position;

        Vector3 realPos = GetScatterPosition(origin);
        SpawnTargetLocal(realFishPrefab, realPos, false);

        for (int i = 0; i < fakeCount; i++)
        {
            Vector3 fakePos = GetScatterPosition(origin);
            SpawnTargetLocal(fakeFishPrefab, fakePos, true);
        }

        // 로컬도 동일하게 잘린 위치에만 이펙트.
        PlayLocalVanishEffect(origin);
        Destroy(gameObject);
    }

    private void SpawnTargetAuthority(NetworkObject prefab, Vector3 position, bool isFake)
    {
        if (Runner == null || prefab == null)
            return;

        List<string> seqCopy = new List<string>(cookingSeq);
        Dictionary<string, string> metaCopy = new Dictionary<string, string>(metadata);
        
        string logTag = KnifeLabel;
        if (!string.IsNullOrEmpty(logTag))
        {
            seqCopy.Add(logTag);
            metaCopy["cutMethod"] = logTag;
        }
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        Runner.Spawn(prefab, position, rotation,
            onBeforeSpawned: (_, obj) =>
            {
                if (obj == null)
                    return;

                PickableItem item = obj.GetComponent<PickableItem>();
                if (item != null)
                {
                    item.cookingSeq = new List<string>(seqCopy);
                    foreach (var kv in metaCopy)
                        item.metadata[kv.Key] = kv.Value;
                }

                PhantomFishTargetItem target = obj.GetComponent<PhantomFishTargetItem>();
                if (target != null)
                    target.InitializeSpawnedState(isFake, escapeTime);
            });
    }

    private void SpawnTargetLocal(NetworkObject prefab, Vector3 position, bool isFake)
    {
        if (prefab == null)
            return;

        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject go = Instantiate(prefab.gameObject, position, rotation);

        PickableItem item = go.GetComponent<PickableItem>();
        if (item != null)
        {
            item.cookingSeq = new List<string>(cookingSeq);
            string logTag = KnifeLabel;
            if (!string.IsNullOrEmpty(logTag))
            {
                item.cookingSeq.Add(logTag);
                item.metadata["cutMethod"] = logTag;
            }

            foreach (var kv in metadata)
                item.metadata[kv.Key] = kv.Value;
        }

        PhantomFishTargetItem target = go.GetComponent<PhantomFishTargetItem>();
        if (target != null)
            target.InitializeSpawnedState(isFake, escapeTime);
    }

    private Vector3 GetScatterPosition(Vector3 origin)
    {
        Vector2 circle = Random.insideUnitCircle * scatterRadius;
        Vector3 rayStart = origin + new Vector3(circle.x, 5f, circle.y);

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 15f, groundLayer, QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * 0.2f;

        return origin + new Vector3(circle.x, 0.2f, circle.y);
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