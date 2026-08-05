using UnityEngine;
using Fusion;

/// <summary>
/// 귀마개.
/// 양손 중 한 곳에라도 들고 있으면
/// 만드라고라 손질 시 발생하는 15초 청력 마비 디버프를 무효화합니다.
///
/// [판정 방식]
/// MandragoraItem.PlayerHasEarplugs() 가 GetComponentsInChildren 으로
/// 이 아이템의 타입명("EarplugsItem") 또는 itemName("귀마개")을 검사합니다.
/// </summary>
public class EarplugsItem : PickableItem, IWearable
{
    private void Start()
    {
        itemName = "귀마개";
    }

    public override void Spawned()
    {
        itemName = "귀마개";
    }
}
