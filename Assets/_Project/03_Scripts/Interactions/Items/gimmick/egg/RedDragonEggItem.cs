/// <summary>
/// 빨간색 드래곤 알.
/// 더 이상 절단할 수 없습니다.
/// </summary>
public class RedDragonEggItem : DragonEggLockedStateBaseItem
{
    protected override string DisplayItemName => "빨간색 드래곤 알";
    protected override DragonEggState DisplayEggState => DragonEggState.Red;
}
