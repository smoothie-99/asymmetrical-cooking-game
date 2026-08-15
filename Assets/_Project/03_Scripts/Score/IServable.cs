using System.Collections.Generic;

/// <summary>
/// ServingStation 에 제출할 수 있는 요리 컨테이너 인터페이스.
/// PlateItem, SoupBowlItem 이 구현합니다.
/// </summary>
public interface IServable
{
    IReadOnlyList<PlatedIngredient> PlatedIngredients { get; }
    bool IsEmpty { get; }
    string DishType { get; }
    void ClearDish();
}
