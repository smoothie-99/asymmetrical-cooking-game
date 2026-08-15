using UnityEngine;

/// <summary>
/// 기름 베이컨 조각.
/// - 더 이상 자를 수 없음
/// - 하지만 OilBaconItem을 상속하므로 HandsManager의
///   "기름 베이컨 계열" 강제 드롭 / 추가 픽업 금지 규칙을 그대로 탐
/// </summary>
public class OilBaconPieceItem : OilBaconItem
{
    protected override string RawItemName => "기름 베이컨 조각";
    protected override string CookedItemName => "기름 베이컨 조각 구이";
    protected override string BurnedItemName => "타버린 베이컨 조각";
    protected override string IngredientId => "기름 베이컨";
    protected override bool SupportsCutting => false;
}