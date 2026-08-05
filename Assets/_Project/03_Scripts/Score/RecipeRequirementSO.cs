using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스테이지별 '정답' 요리 데이터를 정의하는 ScriptableObject입니다.
/// </summary>
[CreateAssetMenu(fileName = "RecipeRequirement_", menuName = "Cooking/Recipe Requirement")]
public class RecipeRequirementSO : ScriptableObject
{
    [Header("기본 요리 속성 (DishType 불일치 시 FAIL)")]
    public string dishType; // 기대하는 요리 분류 (Salad, SoupDish 등)
    public string containerType; // 기대하는 그릇 종류 (Plate, Bowl 등)

    [Header("담는 순서 조건 (ItemType 목록)")]
    public List<string> expectedPlacedOrder = new List<string>();

    [Header("특수 효과 발동 식재료 (발견 시 FAIL)")]
    public List<string> specialConditionItems = new List<string>();

    [Header("특수 효과 발동 요리 종류 (발견 시 FAIL)")]
    public List<string> specialConditionDishTypes = new List<string>();

    [Header("재료별 정밀 요구사항")]
    public List<IngredientRequirement> ingredientRequirements = new List<IngredientRequirement>();
}

[System.Serializable]
public class IngredientRequirement
{
    public string itemType; // 아이템 아이디 (예: MandragoraItem)
    
    [Header("기대하는 익힘 상태 및 조리 시간 (불일치 시 FAIL)")]
    public string targetCookState = "Cooked"; 
    public bool allowRawAndCooked = false; // 추가: Raw와 Cooked 상태를 모두 허용할 때 사용
    public float minCookingTime;
    public float maxCookingTime;

    [Header("절단 방법 조건 (불일치 시 PERFECT 실패)")]
    public bool checkCutMethod;
    public string requiredCutMethod;

    [Header("풍미 조건 (불일치 시 PERFECT 실패)")]
    public bool checkFlavor;
    public string targetFlavor;

    [Header("조리 순서 조건 (불일치 시 PERFECT 실패)")]
    public bool checkCookingSequence;
    public List<string> expectedCookingSequence = new List<string>();
    public List<string> optionalCookingSequence = new List<string>(); // [추가] 순서 상관없이 허용되는 추가 조리 단계들
    public bool strictOrderCheck = true; // true면 순서와 개수 일치 체크, false면 포함 여부와 허용 범위 체크

    [Header("핵심 재료 여부 (오답 시 FAIL 전용)")]
    public bool isMain;
}
