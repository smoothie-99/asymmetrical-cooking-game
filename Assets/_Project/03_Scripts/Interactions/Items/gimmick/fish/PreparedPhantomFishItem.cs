/// <summary>
/// 실제 손질된 환영 물고기.
/// - 이제부터만 굽기/태우기 가능
/// </summary>
public class PreparedPhantomFishItem : PreparedPhantomFishBaseItem
{
    protected override string RawItemName => "손질된 환영 물고기";
    protected override string CookedItemName => "익은 손질된 환영 물고기";
    protected override string BurnedItemName => "타버린 손질된 환영 물고기";
    protected override string IngredientId => "환영 물고기";

    protected override float DefaultCookSeconds => 90f;
    protected override float DefaultBurnSeconds => 120f;
}
