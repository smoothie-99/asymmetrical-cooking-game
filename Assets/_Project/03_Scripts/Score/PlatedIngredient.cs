using System;
using System.Collections.Generic;

/// <summary>
/// 접시 또는 냄비에 담긴 재료 기록.
/// </summary>
[Serializable]
public struct PlatedIngredient
{
    public string    ingredientID;
    public string    itemClassName;
    public CookState cookState;
    public List<string> cookingSeq; // [긴급 복구] 누락된 필드 추가
    public string    cutMethod;
    public float     cookTime; // [추가] 조리 시간 기록

    public PlatedIngredient(string ingredientID, CookState cookState,
        List<string> cookingSeq = null, string cutMethod = "", string itemClassName = "", float cookTime = 0f)
    {
        this.ingredientID  = ingredientID;
        this.itemClassName = itemClassName ?? "";
        this.cookState     = cookState;
        this.cookingSeq    = cookingSeq ?? new List<string>();
        this.cutMethod     = cutMethod ?? "";
        this.cookTime   = cookTime;
    }
}
