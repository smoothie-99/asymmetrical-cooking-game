/// <summary>
/// 도구(칼 등)로 절단 가능한 아이템 인터페이스.
/// 아이템 프리팹의 자식 오브젝트에 CutGuidelineVisual을 배치하고,
/// 도구 타입별로 어떤 가이드라인을 반환할지 구현합니다.
/// </summary>
public interface ICuttable
{
    /// <summary>특정 도구로 처리 가능한 가이드라인 목록을 반환합니다.</summary>
    CutGuidelineVisual[] GetGuidelineVisuals(string toolType);

    /// <summary>해당 도구 타입의 가이드라인을 표시합니다.</summary>
    void ShowGuidelines(string toolType);

    /// <summary>모든 가이드라인을 숨깁니다.</summary>
    void HideGuidelines();

    /// <summary>스크린 중심과 가장 가까운 가이드라인을 하이라이트합니다.</summary>
    void UpdateGuidelineHighlight();

    /// <summary>현재 선택된 가이드라인 인덱스. 없으면 -1.</summary>
    int SelectedGuidelineIndex { get; }
}
