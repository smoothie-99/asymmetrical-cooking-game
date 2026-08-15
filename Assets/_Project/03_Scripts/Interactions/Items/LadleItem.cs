using UnityEngine;

/// <summary>
/// 국자 아이템. PotStation에서 [E]키로 젓기를 할 수 있는 도구.
/// </summary>
public class LadleItem : PickableItem, ITool
{
    public string ToolType => "Ladle";

    void Start()
    {
        if (string.IsNullOrEmpty(itemName) || itemName == "Object")
            itemName = "국자";
    }
}
