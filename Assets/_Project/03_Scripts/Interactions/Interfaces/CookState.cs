using UnityEngine;

public enum CookState
{
    Raw,            // 날것
    NotCooked,      // 덜 익음
    Cooked,         // 알맞게 익음
    TooCooked,      // 너무 익음
    Burned          // 탐
}

public static class CookStateExtensions
{
    public static string ToKoreanString(this CookState state)
    {
        switch (state)
        {
            case CookState.Raw: return "날것";
            case CookState.NotCooked: return "덜 익음";
            case CookState.Cooked: return "알맞게 익음";
            case CookState.TooCooked: return "너무 익음";
            case CookState.Burned: return "탐";
            default: return "알 수 없음";
        }
    }
}
