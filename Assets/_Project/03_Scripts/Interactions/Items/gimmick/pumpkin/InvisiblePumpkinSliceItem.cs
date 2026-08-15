
/// <summary>
/// 손질된 투명 호박.
/// - 더 이상 순간이동하지 않음
/// - 일반 재료처럼 조리만 진행됨
/// </summary>
public class InvisiblePumpkinSliceItem : PumpkinCookableBaseItem
{
    protected override string RawItemName => "손질된 투명 호박";
    protected override string CookedItemName => "익은 손질된 투명 호박";
    protected override string BurnedItemName => "타버린 손질된 투명 호박";
    protected override string IngredientId => "투명 호박";

    public override bool CanChop => false;
}
