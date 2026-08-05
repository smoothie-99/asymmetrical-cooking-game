using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Recipe", menuName = "Recipe System/Recipe")]
public class RecipeData : ScriptableObject
{
    public string recipeName;      // 요리 이름
    public Sprite recipeIcon;      // 요리 완성 사진

    [TextArea(3, 10)]
    public string description;     // 요리 설명 (이게 있어야죠!)

    [Header("주재료 목록")]
    public List<IngredientData> mainIngredients; // 재료 데이터들

    [Header("부재료 목록")]
    public List<IngredientData> subIngredients;
}