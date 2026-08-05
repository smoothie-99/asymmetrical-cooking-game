using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 요리 데이터를 레시피 정답지와 비교하여 [Perfect / Clear / Fail] 결과를 도출하고
/// 10단계 우선순위에 따라 최적의 피드백 대사를 선택합니다.
/// </summary>
public static class ScoringEngine
{
    private struct ErrorInfo
    {
        public FeedbackType Type;
        public string ItemType;
    }

    public static CookingResult Evaluate(DishRecord dish, RecipeRequirementSO requirement, GuestDataSO guest)
    {
        if (dish == null || requirement == null || guest == null)
            return null;

        CookingResult result = new CookingResult();
        List<ErrorInfo> errors = new List<ErrorInfo>(); 
        var allActualIngredients = GetFlattenedIngredients(dish.ingredients);

        // --- PHASE 1: 오류 탐지 (10단계 우선순위 기반) ---

        // 1. 빈 그릇 체크
        if (allActualIngredients.Count == 0)
        {
            errors.Add(new ErrorInfo { Type = FeedbackType.EmptyDish, ItemType = "" });
            return FinalizeResult(CookingOutcome.Fail, errors, guest);
        }

        // 2. 특수 효과 체크 (동그란 재료 등) -> 위반 시 즉시 FAIL
        bool hasSpecialConditionViolated = false;
        foreach (var actual in allActualIngredients)
        {
            if (requirement.specialConditionItems != null && requirement.specialConditionItems.Contains(actual.itemType))
            {
                errors.Add(new ErrorInfo { Type = FeedbackType.SpecialCondition, ItemType = actual.itemType });
                hasSpecialConditionViolated = true;
                break; 
            }
        }

        // 3-1. 특수 효과 요리 종류 확인 (샐러드 지뢰 등) -> 위반 시 FAIL
        if (requirement.specialConditionDishTypes != null && requirement.specialConditionDishTypes.Contains(dish.dishType))
        {
            errors.Add(new ErrorInfo { Type = FeedbackType.SpecialCondition, ItemType = "" });
        }

        // 3-2. 요리 종류 확인 -> 불일치 시 FAIL
        if (string.IsNullOrEmpty(dish.dishType) || (dish.dishType != requirement.dishType))
        {
            errors.Add(new ErrorInfo { Type = FeedbackType.WrongDish, ItemType = "" });
        }

        // 5. 전역 탄 음식 체크 -> 어떤 재료라도 타면 즉시 FAIL
        bool anyBurnedFound = false;
        foreach (var actual in allActualIngredients)
        {
            if (actual.cookState == "Burned")
            {
                errors.Add(new ErrorInfo { Type = FeedbackType.Burned, ItemType = actual.itemType });
                anyBurnedFound = true;
            }
        }

        // 6, 8, 9. 개별 필수 재료 체크
        bool hasMissingEssential = false;
        bool hasWrongCookState = false;
        bool hasWrongCutting = false;
        bool hasWrongSequence = false;

        foreach (var req in requirement.ingredientRequirements)
        {
            var matched = allActualIngredients.FirstOrDefault(i => i.itemType == req.itemType);
            
            // [누락 체크]
            if (matched == null)
            {
                errors.Add(new ErrorInfo { Type = FeedbackType.MissingIngredient, ItemType = req.itemType });
                hasMissingEssential = true;
                continue;
            }

            // [익힘 및 시간 체크]
            bool stateMatch = matched.cookState.ToString() == req.targetCookState;
            
            // [추가] allowRawAndCooked 옵션: 태우지만 않았으면(Raw/Cooked) 통과
            if (req.allowRawAndCooked)
            {
                string s = matched.cookState.ToString();
                stateMatch = (s == "Raw" || s == "Cooked");
            }

            bool timeInRange = matched.cookingTime >= req.minCookingTime && matched.cookingTime <= req.maxCookingTime;
            if (!stateMatch || !timeInRange)
            {
                errors.Add(new ErrorInfo { Type = FeedbackType.WrongCookState, ItemType = req.itemType });
                hasWrongCookState = true;
            }

            // [절단 방법 체크]
            if (req.checkCutMethod && !string.IsNullOrEmpty(req.requiredCutMethod) && matched.cutMethod != req.requiredCutMethod)
            {
                errors.Add(new ErrorInfo { Type = FeedbackType.WrongCutting, ItemType = req.itemType });
                hasWrongCutting = true;
            }

            // [조리 순서 체크]
            if (req.checkCookingSequence && req.expectedCookingSequence != null && req.expectedCookingSequence.Count > 0)
            {
                bool seqMatch = true;
                
                if (req.strictOrderCheck)
                {
                    // [엄격 모드] 순서와 개수가 정답 리스트와 100% 일치해야 함
                    if (matched.cookingSeq.Count != req.expectedCookingSequence.Count)
                    {
                        seqMatch = false;
                    }
                    else
                    {
                        for (int s = 0; s < req.expectedCookingSequence.Count; s++)
                        {
                            if (matched.cookingSeq[s] != req.expectedCookingSequence[s])
                            {
                                seqMatch = false;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // [유연 모드] 
                    // 1. 필수 단계 포함 확인
                    foreach (var expectedStep in req.expectedCookingSequence)
                    {
                        if (!matched.cookingSeq.Contains(expectedStep))
                        {
                            seqMatch = false;
                            break;
                        }
                    }

                    // 2. 허용되지 않은 추가 작업(세척 등 레시피 외 행위)이 있는지 확인
                    if (seqMatch)
                    {
                        foreach (var actualStep in matched.cookingSeq)
                        {
                            bool isMandatory = req.expectedCookingSequence.Contains(actualStep);
                            bool isOptional = req.optionalCookingSequence != null && req.optionalCookingSequence.Contains(actualStep);
                            
                            if (!isMandatory && !isOptional)
                            {
                                seqMatch = false; // 허용되지 않은 작업이 발견됨
                                break;
                            }
                        }
                    }
                }

                if (!seqMatch)
                {
                    errors.Add(new ErrorInfo { Type = FeedbackType.WrongSequence, ItemType = req.itemType });
                    hasWrongSequence = true;
                }
            }
        }

        // 4, 7. 불필요한 재료 탐지
        bool hasExtraIngredient = false;
        bool hasWrongMainIngredient = false;

        foreach (var actual in allActualIngredients)
        {
            bool isRequired = requirement.ingredientRequirements.Any(r => r.itemType == actual.itemType);
            if (!isRequired)
            {
                if (actual.category == "Main")
                {
                    errors.Add(new ErrorInfo { Type = FeedbackType.WrongMain, ItemType = actual.itemType });
                    hasWrongMainIngredient = true;
                }
                else
                {
                    errors.Add(new ErrorInfo { Type = FeedbackType.ExtraIngredient, ItemType = actual.itemType });
                    hasExtraIngredient = true;
                }
            }
        }

        // 10. 담는 순서 체크 -> 틀려도 CLEAR는 가능하나 PERFECT는 불가능
        bool hasWrongOrder = false;
        if (requirement.expectedPlacedOrder != null && requirement.expectedPlacedOrder.Count > 0)
        {
            List<string> actualOrderTypes = allActualIngredients.Select(i => i.itemType).ToList();
            for (int i = 0; i < requirement.expectedPlacedOrder.Count; i++)
            {
                if (actualOrderTypes.Count <= i || actualOrderTypes[i] != requirement.expectedPlacedOrder[i])
                {
                    errors.Add(new ErrorInfo { Type = FeedbackType.WrongOrder, ItemType = requirement.expectedPlacedOrder[i] });
                    hasWrongOrder = true;
                    break;
                }
            }
        }

        // --- PHASE 2: 결과 결정 ---
        CookingOutcome outcome = CookingOutcome.Fail;

        // 치명적 실패 사유: 필수 누락, 요리 틀림, 특수조건 위반, 재료 탐 (익힘 상태 오류는 이제 비치명적으로 분류)
        bool isCriticalFail = hasMissingEssential || (dish.dishType != requirement.dishType) || hasSpecialConditionViolated || anyBurnedFound;

        if (!isCriticalFail)
        {
            // 완벽합격 조건: 합격권 + 불필요 재료 없음 + 오답 주재료 없음 + 손질 정확 + 순서 정확 + 조리 순서 정확
            bool isPerfect = !hasExtraIngredient && !hasWrongMainIngredient && !hasWrongCutting && !hasWrongOrder && !hasWrongSequence && !hasWrongCookState;
            outcome = isPerfect ? CookingOutcome.PerfectClear : CookingOutcome.Clear;
        }

        return FinalizeResult(outcome, errors, guest);
    }

    private static CookingResult FinalizeResult(CookingOutcome outcome, List<ErrorInfo> errors, GuestDataSO guest)
    {
        CookingResult result = new CookingResult();
        result.outcome = outcome;

        // [추가] 가장 우선순위가 높은 에러 타입을 찾아 결과에 담아줍니다.
        var primaryError = errors.OrderBy(e => GetErrorPriority(e.Type)).FirstOrDefault();
        // 에러가 없으면 기본값(없음)이지만, Perfect일 때는 사실상 무의미하므로 기본 enum값 사용
        result.primaryFeedbackType = (errors.Count > 0) ? primaryError.Type : (FeedbackType)(-1); 

        result.feedbackMessage = GenerateFeedback(errors, outcome, guest);
        return result;
    }

    private static List<IngredientRecord> GetFlattenedIngredients(List<IngredientRecord> root)
    {
        List<IngredientRecord> list = new List<IngredientRecord>();
        foreach (var r in root)
        {
            if (!string.IsNullOrEmpty(r.itemType)) list.Add(r);
            if (r.contents != null && r.contents.Count > 0)
                list.AddRange(GetFlattenedIngredients(r.contents));
        }
        return list;
    }

    private static string GenerateFeedback(List<ErrorInfo> errors, CookingOutcome outcome, GuestDataSO guest)
    {
        if (outcome == CookingOutcome.PerfectClear && errors.Count == 0) return guest.perfectFeedback;
        
        var primaryError = errors.OrderBy(e => GetErrorPriority(e.Type)).FirstOrDefault();
        if (primaryError.Type == (FeedbackType)(-1) && errors.Count > 0) primaryError = errors[0];
        else if (errors.Count == 0) return guest.perfectFeedback;

        var template = guest.feedbackTemplates.FirstOrDefault(t => 
            t.feedbackType == primaryError.Type && t.targetItemType == primaryError.ItemType);
        
        if (template == null)
            template = guest.feedbackTemplates.FirstOrDefault(t => 
                t.feedbackType == primaryError.Type && string.IsNullOrEmpty(t.targetItemType));

        if (template != null && template.feedbackMessages != null && template.feedbackMessages.Count > 0)
        {
            return template.feedbackMessages[UnityEngine.Random.Range(0, template.feedbackMessages.Count)];
        }

        return outcome == CookingOutcome.Fail ? guest.failedFeedback : "음, 괜찮군.";
    }

    private static int GetErrorPriority(FeedbackType type)
    {
        switch (type)
        {
            case FeedbackType.EmptyDish: return 0;
            case FeedbackType.SpecialCondition: return 1;
            case FeedbackType.WrongDish: return 2;
            case FeedbackType.WrongMain: return 3;
            case FeedbackType.Burned: return 4;
            case FeedbackType.MissingIngredient: return 5;
            case FeedbackType.ExtraIngredient: return 6;
            case FeedbackType.WrongCookState: return 7;
            case FeedbackType.WrongCutting: return 8;
            case FeedbackType.WrongOrder: return 9;
            case FeedbackType.WrongSequence: return 10; // 11번째 우선순위
            default: return 11;
        }
    }
}
