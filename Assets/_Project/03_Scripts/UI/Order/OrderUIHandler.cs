using UnityEngine;
using TMPro;

public class OrderUIHandler : MonoBehaviour
{
    [Header("연결할 UI")]
    public TMP_Text displayText;

    // 활성화될 때마다 실행 (Start보다 먼저, 그리고 켜질 때마다 실행됨)
    void OnEnable()
    {
        if (OrderManager.Instance != null)
        {
            // 리스트에 나를 추가 (중복 방지 체크)
            if (!OrderManager.Instance.uiHandlers.Contains(this))
            {
                OrderManager.Instance.uiHandlers.Add(this);
            }

            // 켜지자마자 현재 진행 중인 주문이 있다면 바로 표시
            if (OrderManager.Instance.currentOrder != null)
            {
                UpdateOrderUI(OrderManager.Instance.currentOrder);
            }
        }
    }

    void OnDisable()
    {
        if (OrderManager.Instance != null)
        {
            // 리스트에서 제거
            OrderManager.Instance.uiHandlers.Remove(this);
        }
    }

    public void UpdateOrderUI(OrderData newOrder)
    {
        if (newOrder != null && displayText != null)
        {
            displayText.text = newOrder.orderText;
        }
    }
}