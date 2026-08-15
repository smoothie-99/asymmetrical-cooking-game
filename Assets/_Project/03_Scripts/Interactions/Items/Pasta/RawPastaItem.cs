using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 생 파스타.
/// - 30초 가열 시 익은 파스타 프리팹으로 교체
/// - 누적 가열량을 익은 파스타로 그대로 넘김
/// </summary>
public class RawPastaItem : PastaCookableBaseItem
{
    [Header("Result")]
    [SerializeField] private NetworkObject cookedPastaPrefab;

    private bool _isReplacing;

    protected override string RawItemName => "생 파스타";
    protected override string CookedItemName => "익은 파스타";
    protected override string BurnedItemName => "타버린 파스타";

    protected override string RawIngredientId => "생 파스타";
    protected override string CookedIngredientId => "익은 파스타";
    protected override string BurnedIngredientId => "타버린 파스타";

    public override void Spawned()
    {
        _isReplacing = false;
        base.Spawned();
    }

    protected override void OnCookProgressProcessed()
    {
        if (_isReplacing)
            return;

        if (CookProgress < CurrentCookSeconds)
            return;

        ReplaceWithCookedPrefab();
    }

    private void ReplaceWithCookedPrefab()
    {
        if (cookedPastaPrefab == null)
        {
            Debug.LogWarning("[RawPastaItem] cookedPastaPrefab이 비어 있습니다.");
            return;
        }

        if (IsNetworkReady)
            ReplaceAuthority();
        else
            ReplaceLocal();
    }

    private void ReplaceAuthority()
    {
        if (_isReplacing)
            return;

        if (Runner == null)
            return;

        _isReplacing = true;

        Vector3 spawnPos = transform.position;
        Quaternion spawnRot = transform.rotation;

        List<string> seqCopy = new List<string>(cookingSeq);
        Dictionary<string, string> metaCopy = new Dictionary<string, string>(metadata);

        CookState inheritedState = CookProgress >= CurrentBurnSeconds ? CookState.Burned : CookState.Cooked;
        float inheritedProgress = Mathf.Max(CookProgress, CurrentCookSeconds);

        Runner.Spawn(cookedPastaPrefab, spawnPos, spawnRot,
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

                CookedPastaItem cooked = obj.GetComponent<CookedPastaItem>();
                if (cooked != null)
                    cooked.ApplyInheritedState(inheritedState, inheritedProgress);
            });

        if (Object != null && Object.IsValid)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }

    private void ReplaceLocal()
    {
        if (_isReplacing)
            return;

        _isReplacing = true;

        Vector3 spawnPos = transform.position;
        Quaternion spawnRot = transform.rotation;

        GameObject go = Instantiate(cookedPastaPrefab.gameObject, spawnPos, spawnRot);

        PickableItem item = go.GetComponent<PickableItem>();
        if (item != null)
        {
            item.cookingSeq = new List<string>(cookingSeq);
            foreach (var kv in metadata)
                item.metadata[kv.Key] = kv.Value;
        }

        CookedPastaItem cooked = go.GetComponent<CookedPastaItem>();
        if (cooked != null)
        {
            CookState inheritedState = CookProgress >= CurrentBurnSeconds ? CookState.Burned : CookState.Cooked;
            float inheritedProgress = Mathf.Max(CookProgress, CurrentCookSeconds);
            cooked.ApplyInheritedState(inheritedState, inheritedProgress);
        }

        Destroy(gameObject);
    }
}