using UnityEngine;

/// <summary>
/// 익은 파스타.
/// - 기본 상태가 이미 익은 상태
/// - 여기서 30초 더 가열되면 타버림
/// </summary>
public class CookedPastaItem : PastaCookableBaseItem
{
    protected override string RawItemName => "익은 파스타";
    protected override string CookedItemName => "익은 파스타";
    protected override string BurnedItemName => "타버린 파스타";

    protected override string RawIngredientId => "익은 파스타";
    protected override string CookedIngredientId => "익은 파스타";
    protected override string BurnedIngredientId => "타버린 파스타";

    protected override CookState DefaultSpawnCookState => CookState.Cooked;

    protected override float GetDefaultSpawnCookProgress()
    {
        return CurrentCookSeconds;
    }
}