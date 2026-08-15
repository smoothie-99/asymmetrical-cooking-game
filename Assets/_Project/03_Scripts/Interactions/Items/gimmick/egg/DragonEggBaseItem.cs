using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Interactions;

public enum DragonEggState
{
    Raw,
    Yellow,
    Green,
    Red
}

/// <summary>
/// 드래곤 알 계열 공통 베이스.
/// - 이름 / 색상 적용
/// - metadata / cookingSeq 전달
/// - 네트워크/로컬 공용 치환 스폰 헬퍼
/// </summary>
public abstract class DragonEggBaseItem : PickableItem
{
    [Header("Visual Settings")]
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private Color rawColor = Color.white;
    [SerializeField] private Color yellowColor = Color.yellow;
    [SerializeField] private Color greenColor = Color.green;
    [SerializeField] private Color redColor = Color.red;

    protected abstract string IngredientId { get; }
    protected abstract string DisplayItemName { get; }
    protected abstract DragonEggState DisplayEggState { get; }

    protected virtual void Start()
    {
        RefreshVisualState();
    }

    public override void Spawned()
    {
        RefreshVisualState();
    }

    protected void RefreshVisualState()
    {
        itemName = DisplayItemName;
        metadata["ingredientID"] = IngredientId;
        UpdateVisuals();
    }

    protected virtual void UpdateVisuals()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<MeshRenderer>(true);

        if (targetRenderer == null)
            return;

        switch (DisplayEggState)
        {
            case DragonEggState.Raw:
                targetRenderer.material.color = rawColor;
                break;
            case DragonEggState.Yellow:
                targetRenderer.material.color = yellowColor;
                break;
            case DragonEggState.Green:
                targetRenderer.material.color = greenColor;
                break;
            case DragonEggState.Red:
                targetRenderer.material.color = redColor;
                break;
        }
    }

    protected void CopyCookingDataTo(PickableItem target, bool addCutStep)
    {
        if (target == null)
            return;

        target.cookingSeq = new List<string>(cookingSeq);

        foreach (KeyValuePair<string, string> pair in metadata)
            target.metadata[pair.Key] = pair.Value;

        if (addCutStep && !target.cookingSeq.Contains("절단"))
            target.cookingSeq.Add("절단");
    }

    protected void SpawnReplacementAndDespawnSelf(NetworkObject prefab, bool addCutStep)
    {
        if (prefab == null)
        {
            Debug.LogError($"[DragonEggBaseItem] {name} replacement prefab is null.");
            return;
        }

        Vector3 spawnPos = transform.position;
        Quaternion spawnRot = transform.rotation;

        if (IsNetworkReady && Runner != null && Object != null && Object.IsValid)
        {
            List<string> seqCopy = new List<string>(cookingSeq);
            Dictionary<string, string> metaCopy = new Dictionary<string, string>(metadata);

            Runner.Spawn(prefab, spawnPos, spawnRot,
                onBeforeSpawned: (runner, obj) =>
                {
                    PickableItem spawnedItem = obj.GetComponent<PickableItem>();
                    if (spawnedItem == null)
                        return;

                    spawnedItem.cookingSeq = new List<string>(seqCopy);
                    foreach (KeyValuePair<string, string> pair in metaCopy)
                        spawnedItem.metadata[pair.Key] = pair.Value;

                    if (addCutStep && !spawnedItem.cookingSeq.Contains("절단"))
                        spawnedItem.cookingSeq.Add("절단");
                });

            Runner.Despawn(Object);
            return;
        }

        NetworkObject spawned = UnityEngine.Object.Instantiate(prefab, spawnPos, spawnRot);
        if (spawned != null)
        {
            PickableItem spawnedItem = spawned.GetComponent<PickableItem>();
            CopyCookingDataTo(spawnedItem, addCutStep);
        }

        Destroy(gameObject);
    }
}
