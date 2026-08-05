using UnityEngine;

[CreateAssetMenu(fileName = "New Ingredient", menuName = "Recipe System/Ingredient")]
public class IngredientData : ScriptableObject
{
    public string ingredientName;
    public Sprite icon;
    public string category;
    [TextArea(3, 10)]
    public string description;
}