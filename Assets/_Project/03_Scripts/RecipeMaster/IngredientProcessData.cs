using UnityEngine;

[CreateAssetMenu(fileName = "New Process", menuName = "Recipe System/Ingredient Process")]
public class IngredientProcessData : ScriptableObject
{
    public IngredientData ingredient;

    public IngredientState startState;
    public IngredientState resultState;

    public float minTime;
    public float maxTime;

    [TextArea(3, 10)]
    public string description;
}