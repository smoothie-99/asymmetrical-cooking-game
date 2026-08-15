using System;
using System.Collections.Generic;
using UnityEngine;

// 1. 실제 요리 데이터 레코드 (쿠킹마스터 -> 채점기)
// 쿠킹마스터로부터 이 구조로 요리 데이터를 전달받습니다.
[Serializable]
public class DishRecord
{
    public string dishType; // 요리 종류 (예: Salad, SoupDish)
    public List<IngredientRecord> ingredients = new List<IngredientRecord>(); // 담긴 순차 리스트
}

[Serializable]
public class IngredientRecord
{
    public string itemType;      // 아이템 아이디 (MandragoraItem 등)
    public string category;      // [추가] 재료 분류 (Main, Sub 등)
    public string containerType; // 그릇 종류 (Plate, Bowl 등)
    public List<IngredientRecord> contents = new List<IngredientRecord>(); // 용기 안의 내용물 (재귀)

    public string cookState;             // 조리 상태 (Raw, Cooked, Burned 등)
    public List<string> cookingSeq = new List<string>(); // ["Washed", "Shredded"] 등 조리 순서
    public float cookingTime;           // 총 조리 시간 (초)
    public string cutMethod;             // 절단 방법 ("포썰기", "깍뚝썰기" 등, 없으면 빈 문자열)
    public string flavor;                // 풍미(색상 등)
}

public enum CookingOutcome
{
    PerfectClear,
    Clear,
    Fail
}

// 2. 최종 채점 결과 데이터 (채점기 -> UI/네트워크)
[Serializable]
public class CookingResult
{
    public CookingOutcome outcome;      // 결과 (Perfect, Clear, Fail)
    public string feedbackMessage;     // 손님의 대사
    public FeedbackType primaryFeedbackType; // [추가] 가장 우선순위가 높은 감점/실패 사유
    public List<string> deductionReasons = new List<string>(); // 사유 리스트 (디버깅용)
}
