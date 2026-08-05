
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 가상 미믹 상자.
/// 실제로는 이 프리팹이 생성되면 금/은/동 미믹 상자 중 하나를 랜덤으로 골라
/// 같은 위치에 스폰하고 자신은 즉시 사라진다.
///
/// 이 프리팹을 디스펜서/스폰 테이블 등에 연결해 두면 된다.
/// </summary>
public class MimicBoxItem : PickableItem
{
    [Header("Variant Prefabs")]
    [SerializeField] private NetworkObject bronzeBoxPrefab;
    [SerializeField] private NetworkObject silverBoxPrefab;
    [SerializeField] private NetworkObject goldBoxPrefab;

    private bool _revealed;

    private void Start()
    {
        itemName = "미믹 상자";
        metadata["ingredientID"] = "미믹 상자";

        // 퓨전이 아닌 일반 씬 오브젝트로 테스트할 때만 로컬 치환
        if ((Object == null || !Object.IsValid) && !_revealed)
            RevealLocal();
    }

    public override void Spawned()
    {
        if (HasStateAuthority && !_revealed)
            RevealAuthority();
    }

    public override bool CanChop => false;

    // ── IInteractable override (집기만 허용) ──────────────────────

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary)
            return base.CanInteract(player, type);
        return false;
    }

    public override void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary)
            base.Interact(player, type);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary && CanInteract(player, type))
            return $"[{itemName}]\n[E] 집기";
        return "";
    }

    // ── 도구/접시 충돌 시 뱉어냄 ─────────────────────────────────

    protected override void OnTriggerEnter(Collider other)
    {
        if (IsNetworkReady && !HasStateAuthority) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item == null || item == this || item.IsPhysicallyHeld) return;

        if (item is ITool)
        {
            SpitOut(item);
            return;
        }

        if (item is IServable servable)
        {
            if (!servable.IsEmpty)
                servable.ClearDish();
            SpitOut(item);
        }
    }

    private void SpitOut(PickableItem item)
    {
        Vector3 dir = item.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
        dir = (dir.normalized + Vector3.up * 0.5f).normalized;

        Vector3 spitPos = transform.position + dir * 0.5f;
        Vector3 velocity = dir * 5f;
        Vector3 angularVelocity = new Vector3(
            Random.Range(-3f, 3f),
            Random.Range(-3f, 3f),
            Random.Range(-3f, 3f));

        item.SetHeldLayer(false);
        item.LocalThrow(spitPos, velocity, angularVelocity);
        if (item.Object != null && item.Object.IsValid)
            item.Rpc_Throw(spitPos, velocity, angularVelocity);
    }

    // ── 랜덤 변형 스폰 ───────────────────────────────────────────

    private void RevealAuthority()
    {
        if (_revealed) return;

        NetworkObject selectedPrefab = PickRandomVariantPrefab();
        if (selectedPrefab == null)
        {
            Debug.LogError("[MimicBoxItem] 금/은/동 미믹 상자 프리팹이 하나도 연결되지 않았습니다.");
            return;
        }

        _revealed = true;

        List<string> seqCopy = new(cookingSeq);
        Dictionary<string, string> metaCopy = new(metadata);

        if (Runner != null)
        {
            Runner.Spawn(selectedPrefab, transform.position, transform.rotation,
                onBeforeSpawned: (_, obj) =>
                {
                    PickableItem item = obj.GetComponent<PickableItem>();
                    if (item == null) return;
                    item.cookingSeq = seqCopy;
                    foreach (var kv in metaCopy)
                        item.metadata[kv.Key] = kv.Value;
                });
        }

        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }

    private void RevealLocal()
    {
        if (_revealed) return;

        NetworkObject selectedPrefab = PickRandomVariantPrefab();
        if (selectedPrefab == null)
        {
            Debug.LogError("[MimicBoxItem] 금/은/동 미믹 상자 프리팹이 하나도 연결되지 않았습니다.");
            return;
        }

        _revealed = true;

        GameObject spawned = Instantiate(selectedPrefab.gameObject, transform.position, transform.rotation);
        PickableItem item = spawned.TryGetComponent(out PickableItem pi) ? pi : null;
        if (item != null)
        {
            item.cookingSeq = new List<string>(cookingSeq);
            foreach (var kv in metadata)
                item.metadata[kv.Key] = kv.Value;
        }

        Destroy(gameObject);
    }

    private NetworkObject PickRandomVariantPrefab()
    {
        List<NetworkObject> candidates = new List<NetworkObject>(3);
        if (bronzeBoxPrefab != null) candidates.Add(bronzeBoxPrefab);
        if (silverBoxPrefab != null) candidates.Add(silverBoxPrefab);
        if (goldBoxPrefab != null) candidates.Add(goldBoxPrefab);
        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }
}
