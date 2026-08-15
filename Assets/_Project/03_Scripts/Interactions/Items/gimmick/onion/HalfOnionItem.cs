/// <summary>
/// 반쪽 회복 양파.
/// 칼로 자르면 GuidelineConfig에 설정된 다져진 회복 양파들로 분리됩니다.
/// 방치 시(손에 들지 않고, 가열 이력 없음) 회복 양파로 복원됩니다.
/// </summary>
public class HalfOnionItem : OnionCuttableBaseItem
{
    protected override string RawItemName => "반쪽 회복 양파";
    protected override string CookedItemName => "반쪽 회복 양파";
    protected override string BurnedItemName => "타버린 반쪽 회복 양파";
    protected override string IngredientId => "회복 양파";

    protected override float DefaultCookTime => 60f;
    protected override float DefaultBurnTime => 90f;
    protected override float DefaultRestoreTime => 10f;
    protected override bool SupportsRestore => true;
}