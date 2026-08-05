using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Order", menuName = "Order System/Order")]
public class OrderData : ScriptableObject
{
    [TextArea(3, 10)]
    public string orderText;     // 주문 상세 내용 (텍스트)

    [Header("레시피 목록")]
    public List<RecipeData> Recipes;

    [Header("손님 정보")]
    public GuestDataSO guestData;  // 이 주문에 해당하는 손님 데이터

    [Header("채점 및 정답 로직")]
    public RecipeRequirementSO recipeRequirement; // [추가] 이 주문의 정답지!!

    [Header("재료 목록")]
    public List<IngredientData> Ingredients;
}