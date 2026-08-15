using System.Collections.Generic;
using UnityEngine;

public class OrderManager : MonoBehaviour
{
    public static OrderManager Instance;

    public List<OrderData> allOrders;
    public OrderData currentOrder;

    public List<OrderUIHandler> uiHandlers = new List<OrderUIHandler>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnEnable()
    {
        // [수정] 태어날 때 이미 상태가 결정되어 있어도 놓치지 않도록 SubscribeWhenReady를 사용합니다.
        SystemManager.SubscribeWhenReady(HandleMetaPhaseEntered);
    }

    private void OnDisable()
    {
        SystemManager.Unsubscribe(HandleMetaPhaseEntered);
    }

    private void HandleMetaPhaseEntered(MetaState state)
    {
        // [핵심] 주문 단계(OrderDialogue)로 진입할 때마다 라운드 데이터를 갱신합니다.
        if (state == MetaState.OrderDialogue)
        {
            StartNewRound();
        }
    }

    public void StartNewRound()
    {
        if (allOrders.Count > 0)
        {
            // [수정] 랜덤이나 인덱스 증가 대신 현재 선택된 스테이지 번호를 인덱스로 사용합니다.
            int stageIndex = (SystemManager.Instance != null) ? SystemManager.Instance.SelectedStage - 1 : 0;
            
            // 인덱스 안전장치 (데이터가 충분하지 않았을 때 첫 번째 데이터를 출력)
            if (stageIndex < 0 || stageIndex >= allOrders.Count)
            {
                Debug.LogWarning($"⚠️ [OrderManager] 스테이지 {stageIndex + 1}에 해당하는 주문 데이터가 부족하여 기본 주문을 결정합니다.");
                stageIndex = 0;
            }

            // 현재 스테이지에 맞는 주문을 가져옴
            currentOrder = allOrders[stageIndex];
            Debug.Log($"🎯 [OrderManager] 스테이지 {stageIndex + 1} 주문 확정: {currentOrder.orderText}");

            // 2. UI 업데이트 방송
            foreach (var handler in uiHandlers)
            {
                if (handler != null) handler.UpdateOrderUI(currentOrder);
            }

            // 3. EvaluationUI 새로고침 (데이터가 바뀌었으니 UI도 갱신!)
            EvaluationUI evalUI = FindObjectOfType<EvaluationUI>();
            if (evalUI != null)
            {
                evalUI.RefreshUI();
            }
        }
    }
}