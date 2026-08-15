/// <summary>
/// 기본 드래곤 알.
/// 불 위에서 색이 변하다가 화구에서 내려오면 노랑/초록/빨강 알 프리팹으로 고정됩니다.
/// 기본 알 상태에서는 절단할 수 없습니다.
/// </summary>
public class DragonEggItem : DragonEggCookingBaseItem
{
    protected override string IngredientId => "드래곤 알";
    protected override string RawItemName => "기본 드래곤 알";
    protected override string ChangingItemName => "변하는 드래곤 알";
}
