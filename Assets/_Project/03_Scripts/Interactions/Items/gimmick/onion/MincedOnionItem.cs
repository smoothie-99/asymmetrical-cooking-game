/// <summary>
/// 다져진 회복 양파.
/// 더 이상 자를 수 없으며, 방치 시(손에 들지 않고, 가열 이력 없음)
/// 반쪽 회복 양파로 복원됩니다.
/// </summary>
public class MincedOnionItem : OnionBaseItem
{
    protected override string RawItemName => "다져진 회복 양파";
    protected override string CookedItemName => "다져진 회복 양파";
    protected override string BurnedItemName => "타버린 다져진 회복 양파";
    protected override string IngredientId => "회복 양파";

    protected override float DefaultCookTime => 30f;
    protected override float DefaultBurnTime => 60f;
    protected override float DefaultRestoreTime => 10f;
    protected override bool SupportsRestore => true;
}