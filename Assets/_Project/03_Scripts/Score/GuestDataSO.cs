using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 12명의 손님 정보를 관리하는 ScriptableObject입니다.
/// </summary>
[CreateAssetMenu(fileName = "GuestData_", menuName = "Cooking/Guest Data")]
public class GuestDataSO : ScriptableObject
{
    [Header("손님 기본 정보")]
    public string guestName;
    public string guestTitle;
    public Sprite guestIcon;

    [Header("손님 표정 (상황별 5종)")]
    public Sprite portraitOrder;   // 주문 시
    public Sprite portraitHappy;   // 퍼펙트 클리어 (Perfect)
    public Sprite portraitNormal;  // 일반 성공 (Clear)
    public Sprite portraitAngry;   // 실패 (Fail)
    public Sprite portraitSpecial; // 특수 기믹 사유로 인한 감점/실패 시

    [Header("피드백 템플릿 (9가지 우선순위 사유별)")]
    public List<GuestFeedbackTemplate> feedbackTemplates = new List<GuestFeedbackTemplate>();

    [Header("최고/최악 상황 기본 피드백")]
    public string perfectFeedback = "오, 이건 최고의 요리야! 완벽하군!";
    public string failedFeedback = "이런 요리는 처음 보는군... 실망이야.";
}

[System.Serializable]
public class GuestFeedbackTemplate
{
    public FeedbackType feedbackType; // 감점 사유
    public string targetItemType;     // [추가] 특정 아이템을 지정. 비워두면 공용 대사.
    public List<string> feedbackMessages = new List<string>(); // 해당 상황에서 나갈 문장들
}

public enum FeedbackType
{
    EmptyDish,         // 1. 빈 접시를 냈을 때
    SpecialCondition,  // 2. 특수 효과 (동그란 재료 비선호 등)
    WrongDish,         // 3. 다른 요리를 냈을 때
    WrongMain,         // 4. 주재료 자리에 다른 재료 사용
    Burned,            // 5. 재료가 탐
    MissingIngredient,  // 6. 필수 재료가 빠짐
    ExtraIngredient,   // 7. 정답 외 다른 재료가 추가로 들어감
    WrongCookState,    // 8. 재료 익힘이 잘못됨
    WrongCutting,      // 9. 재료 손질(썰기 등)이 잘못됨
    WrongOrder,        // 10. 담는 순서가 잘못됨
    WrongSequence,    // 11. 조리 순서가 잘못됨 (예: 가열 후 썰기 vs 썰기 후 가열)
}
