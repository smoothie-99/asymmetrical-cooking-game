using UnityEngine;
using Interactions;

/// <summary>
/// SupplyStation이 실제로 손에 쥐여줄 프리팹을 동적으로 바꾸고 싶을 때 사용하는 인터페이스.
/// 예: 미믹 상자 프록시가 금/은/동 상자 중 하나를 랜덤 선택.
/// </summary>
public interface ISupplyDispenseResolver
{
    /// <summary>
    /// SupplyStation이 실제로 Spawn / Instantiate 해야 하는 PickableItem 프리팹을 반환한다.
    /// null을 반환하면 원본 프리팹을 그대로 사용한다.
    /// </summary>
    PickableItem ResolveDispensePrefab();
}
