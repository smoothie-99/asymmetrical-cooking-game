using UnityEngine;

public class KnifeItem : PickableItem, ITool
{
    public string ToolType => "Knife";

    void Start()
    {
        if (string.IsNullOrEmpty(itemName) || itemName == "Object")
        {
            itemName = "칼";
        }
    }
}
