using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// SupplyStation에 넣기 위한 "가상 미믹 상자" 프리팹.
/// - PickableItem이라 SupplyStation ingredients 배열에 넣을 수 있음
/// - 자기 자신은 실제 지급용이 아니라, 지급 직전에 금/은/동 실제 상자 프리팹 중 하나를 랜덤 선택하는 프록시 역할
/// - 프리팹에 일반 미믹 상자 외형을 붙여두면 SupplyStation 떠다니는 비주얼에도 그대로 보임
/// </summary>
public class MimicBoxSupplyItem : PickableItem, ISupplyDispenseResolver
{
    [Header("Real Mimic Box Prefabs")]
    [SerializeField] private PickableItem bronzeBoxPrefab;
    [SerializeField] private PickableItem silverBoxPrefab;
    [SerializeField] private PickableItem goldBoxPrefab;

    [Header("Optional Weighted Random")]
    [SerializeField, Min(0f)] private float bronzeWeight = 1f;
    [SerializeField, Min(0f)] private float silverWeight = 1f;
    [SerializeField, Min(0f)] private float goldWeight = 1f;

    private void Start()
    {
        itemName = "미믹 상자";
        metadata["ingredientID"] = "미믹 상자";
    }

    public PickableItem ResolveDispensePrefab()
    {
        var entries = new List<(PickableItem prefab, float weight)>(3);

        if (bronzeBoxPrefab != null && bronzeWeight > 0f)
            entries.Add((bronzeBoxPrefab, bronzeWeight));

        if (silverBoxPrefab != null && silverWeight > 0f)
            entries.Add((silverBoxPrefab, silverWeight));

        if (goldBoxPrefab != null && goldWeight > 0f)
            entries.Add((goldBoxPrefab, goldWeight));

        if (entries.Count == 0)
        {
            Debug.LogError("[MimicBoxSupplyItem] 실제 미믹 상자 프리팹이 하나도 연결되지 않았습니다.");
            return null;
        }

        float totalWeight = 0f;
        for (int i = 0; i < entries.Count; i++)
            totalWeight += entries[i].weight;

        if (totalWeight <= 0f)
            return entries[0].prefab;

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < entries.Count; i++)
        {
            cumulative += entries[i].weight;
            if (roll <= cumulative)
                return entries[i].prefab;
        }

        return entries[entries.Count - 1].prefab;
    }

    public override bool CanChop => false;

    public override void Chop(CuttingStation board)
    {
        // SupplyStation 프록시 전용 프리팹이라 실제 플레이 중에는 손질되지 않게 둔다.
    }
}
