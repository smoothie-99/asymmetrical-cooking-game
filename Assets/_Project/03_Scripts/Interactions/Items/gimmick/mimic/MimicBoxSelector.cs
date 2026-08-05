using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class MimicBoxSelector : MonoBehaviour
{
    [SerializeField] private NetworkObject bronzeBoxPrefab;
    [SerializeField] private NetworkObject silverBoxPrefab;
    [SerializeField] private NetworkObject goldBoxPrefab;

    public NetworkObject GetRandomBoxPrefab()
    {
        List<NetworkObject> candidates = new List<NetworkObject>(3);

        if (bronzeBoxPrefab != null) candidates.Add(bronzeBoxPrefab);
        if (silverBoxPrefab != null) candidates.Add(silverBoxPrefab);
        if (goldBoxPrefab != null) candidates.Add(goldBoxPrefab);

        if (candidates.Count == 0)
        {
            Debug.LogError("[MimicBoxSelector] 연결된 미믹 상자 프리팹이 없습니다.");
            return null;
        }

        int index = Random.Range(0, candidates.Count);
        return candidates[index];
    }
}