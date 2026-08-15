using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 완성된 요리(PlateItem 또는 SoupBowlItem)를 제출하고 채점하는 서빙 스테이션.
///
/// [조작법]
/// E키 (Primary)   : 손에 든 요리를 올려두기 / 올려둔 요리 다시 집기
/// F키 (Secondary) : 올려둔 요리를 채점 제출
/// </summary>
public class ServingStation : NetworkBehaviour, IInteractable
{
    [Header("Recipe Settings")]
    [Tooltip("이 스테이션이 채점할 레시피 정답지")]
    [SerializeField] private RecipeRequirementSO recipeRequirement;
    [Tooltip("채점 피드백을 내놓을 손님 데이터")]
    [SerializeField] private GuestDataSO guestData;

    [Header("Station Settings")]
    [Tooltip("요리가 놓일 위치 Transform (없으면 이 오브젝트 기준)")]
    [SerializeField] private Transform placementPoint;
    [Tooltip("요리 높이 오프셋 (m)")]
    [SerializeField] private float heightOffset = 0.1f;

    public void SetRecipeRequirement(RecipeRequirementSO r) => recipeRequirement = r;
    public void SetGuestData(GuestDataSO g)                 => guestData = g;

    // PlateItem 과 SoupBowlItem 을 모두 PickableItem 으로 보관
    private PickableItem _currentDish;
    public PickableItem CurrentDish => _currentDish;

    private IServable CurrentServable => _currentDish as IServable;
    private bool IsNetworkReady => Object != null && Object.IsValid;

    private void Awake()
    {
        if (placementPoint == null)
            placementPoint = transform;
    }

    // ── 물리 트리거: IServable 이 굴러들어오면 자동 거치 ──────────────
    private void OnTriggerEnter(Collider other)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (_currentDish != null) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item == null || item.IsPhysicallyHeld) return;
        if (item is not IServable) return;

        PlaceOnStation(item);
    }

    private void PlaceHeldOnStation(PickableItem held, CookingMasterHandsManager player)
    {
        Vector3 pos = placementPoint.position + Vector3.up * heightOffset;
        held.SetHeldLayer(false);
        held.LocalDrop(pos);
        if (held.Object != null && held.Object.IsValid)
            held.Rpc_Drop(pos);
        player.ClearActiveHandSlot();
        PlaceOnStation(held);
    }

    private void PlaceOnStation(PickableItem item)
    {
        _currentDish = item;
        Vector3 pos = placementPoint.position + Vector3.up * heightOffset;
        if (IsNetworkReady) item.Rpc_PutOnStation(Object.Id, pos);
        else                item.LocalPutOnStation(pos);
        item.transform.rotation = Quaternion.identity;
    }

    // ── IInteractable ──────────────────────────────────────────────

    // 접시가 외부(PickableItem.Interact 등)에서 집혀도 스테이션 참조를 자동 정리
    private void SyncDishState()
    {
        if (_currentDish != null && !_currentDish.IsOnStation)
            _currentDish = null;
    }

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type) => false;

    public void Interact(CookingMasterHandsManager player, InteractionType type) { }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        SyncDishState();
        if (type != InteractionType.Primary) return "";
        if (_currentDish == null)
            return "[서빙 테이블]\n[Q] 요리를 던져서 올려두세요";
        return "[서빙 테이블]\n요리가 올려져 있습니다";
    }

    // ── 채점 ───────────────────────────────────────────────────────

    /// <summary>외부(BellInteractable 등)에서 제출을 트리거할 때 호출합니다.</summary>
    public void Submit()
    {
        if (IsNetworkReady) Rpc_Submit();
        else                EvaluateAndReport();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_Submit() => EvaluateAndReport();

    private void EvaluateAndReport()
    {
        IServable servable = CurrentServable;
        if (servable == null) return;

        // [핵심] 현재 활성화된 주문서에서 정답지와 손님 데이터를 가져옵니다. 
        // (만약 주문이 없으면 인스펙터에 등록된 기본값을 보조적으로 사용합니다.)
        var currentOrder = OrderManager.Instance != null ? OrderManager.Instance.currentOrder : null;
        
        RecipeRequirementSO req = (currentOrder != null && currentOrder.recipeRequirement != null) 
                                  ? currentOrder.recipeRequirement : recipeRequirement;
        GuestDataSO guest = (currentOrder != null && currentOrder.guestData != null) 
                                  ? currentOrder.guestData : guestData;

        if (req == null || guest == null)
        {
            Debug.LogError("❌ [ServingStation] 정답지(req) 또는 손님데이터(guest)가 없어서 채점을 진행할 수 없습니다.");
            return;
        }

        DishRecord dish = BuildDishRecord(servable);
        LogDishRecord(dish);
        CookingResult result = ScoringEngine.Evaluate(dish, req, guest);

        if (result == null)
        {
            Debug.LogWarning("[ServingStation] 채점 결과가 null입니다.");
            return;
        }

        bool success = result.outcome != CookingOutcome.Fail;
        Debug.Log($"[ServingStation] 채점 완료 — 결과: {result.outcome} / 피드백: {result.feedbackMessage}");

        if (GamePlayManager.Instance != null)
            GamePlayManager.Instance.FinishCookingSession(success, result.feedbackMessage, result.outcome, result.primaryFeedbackType);

        DespawnDish();
    }

    private void DespawnDish()
    {
        if (_currentDish == null) return;

        PickableItem dish = _currentDish;
        _currentDish = null;

        (dish as IServable)?.ClearDish();

        if (IsNetworkReady && dish.Object != null && dish.Object.IsValid)
            Runner.Despawn(dish.Object);
        else
            Destroy(dish.gameObject);
    }

    // ── DishRecord 변환 ────────────────────────────────────────────

    private void LogDishRecord(DishRecord dish)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[ServingStation] ══════ 제출된 DishRecord ══════");
        sb.AppendLine($"  dishType : {dish.dishType}");
        sb.AppendLine($"  재료 수  : {dish.ingredients.Count}개");
        sb.AppendLine($"  ──────────────────────────────────────");
        for (int i = 0; i < dish.ingredients.Count; i++)
        {
            IngredientRecord r = dish.ingredients[i];
            sb.AppendLine($"  [{i}] itemType   : {r.itemType}");
            sb.AppendLine($"       cookState  : {r.cookState}");
            sb.AppendLine($"       cookingTime: {r.cookingTime:F1}s");
            sb.AppendLine($"       cutMethod  : '{r.cutMethod}'");
            sb.AppendLine($"       cookingSeq : [{string.Join(" → ", r.cookingSeq)}]");
            if (i < dish.ingredients.Count - 1)
                sb.AppendLine($"  ──────────────────────────────────────");
        }
        sb.AppendLine($"══════════════════════════════════════════");
        Debug.Log(sb.ToString());
    }

    private DishRecord BuildDishRecord(IServable servable)
    {
        DishRecord dish = new DishRecord { dishType = servable.DishType };

        foreach (PlatedIngredient plated in servable.PlatedIngredients)
        {
            dish.ingredients.Add(new IngredientRecord
            {
                itemType   = plated.ingredientID,
                cookState  = plated.cookState.ToString(),
                cookingSeq = plated.cookingSeq ?? new System.Collections.Generic.List<string>(),
                cutMethod  = plated.cutMethod ?? "",
                cookingTime = plated.cookTime, // [추가]
            });
        }

        return dish;
    }
}
