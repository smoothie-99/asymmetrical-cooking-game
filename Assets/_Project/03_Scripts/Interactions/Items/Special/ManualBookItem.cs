using UnityEngine;

public class ManualBookItem : PickableItem, ITool
{
    public string ToolType => "ManualBook";

    void Start()
    {
        if (string.IsNullOrEmpty(itemName) || itemName == "Object")
        {
            itemName = "조리 매뉴얼";
        }
    }
}