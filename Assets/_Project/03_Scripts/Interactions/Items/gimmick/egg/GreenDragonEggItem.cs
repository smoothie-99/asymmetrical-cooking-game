/// <summary>
/// 초록색 드래곤 알.
/// 칼로 자르면 드래곤 프라이로 변합니다.
/// 결과물은 Guideline Config의 resultPrefabs에 설정합니다.
/// </summary>
public class GreenDragonEggItem : DragonEggCuttableLockedStateBaseItem
{
    protected override string DisplayItemName => "초록색 드래곤 알";
    protected override DragonEggState DisplayEggState => DragonEggState.Green;
}
