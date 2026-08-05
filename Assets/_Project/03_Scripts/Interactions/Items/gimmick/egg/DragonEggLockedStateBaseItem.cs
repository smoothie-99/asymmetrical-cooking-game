using UnityEngine;

/// <summary>
/// 고정된 색상의 드래곤 알 공통 베이스.
/// - 기본 알 / 빨간색 알은 이 베이스만으로 사용
/// - 초록/노랑은 Cuttable 베이스를 상속
/// </summary>
public abstract class DragonEggLockedStateBaseItem : DragonEggBaseItem
{
    protected override string IngredientId => "드래곤 알";
    public override bool CanChop => false;

    // 자식 클래스에서 반드시 구현
    protected abstract override string DisplayItemName { get; }
    protected abstract override DragonEggState DisplayEggState { get; }
}
