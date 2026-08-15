/// <summary>
/// 원형 회복 양파.
/// 칼로 자르면 GuidelineConfig에 설정된 반쪽 회복 양파들로 분리됩니다.
/// </summary>
public class WholeOnionItem : OnionCuttableBaseItem
{
    protected override string RawItemName => "회복 양파";
    protected override string CookedItemName => "회복 양파";
    protected override string BurnedItemName => "타버린 회복 양파";
    protected override string IngredientId => "회복 양파";
    protected override float DefaultBurnTime => 30f;
}