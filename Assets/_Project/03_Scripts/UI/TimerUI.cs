using UnityEngine;
using TMPro;

/// <summary>
/// GamePlayManager의 남은 시간을 화면에 실시간으로 표시하는 UI 스크립트
/// </summary>
public class TimerUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI timerText; // 화면 상단에 띄울 텍스트 컴포넌트

    void Update()
    {
        if (GamePlayManager.Instance == null) return;

        CookingState currentState = GamePlayManager.Instance.CurrentCookingState;
        
        // [자동 표시 제어] 요리 세션이 준비 중이거나 요리 중일 때만 타이머를 보여줍니다.
        bool shouldShow = (currentState == CookingState.Ready || currentState == CookingState.Cooking);
        
        if (timerText != null)
        {
            if (timerText.gameObject.activeSelf != shouldShow)
            {
                timerText.gameObject.SetActive(shouldShow);
            }
        }

        // 보이지 않는 상태라면 이후 로직(텍스트 업데이트)은 수행하지 않습니다.
        if (!shouldShow) return;

        // 남은 시간을 가져옵니다.
        float timeLeft = GamePlayManager.Instance.GetRemainingTime();
        
        // [수정] 준비 단계(Ready)일 때는 타이머가 시작 시간(초기화된 시간)에 멈춰있는 것처럼 보이게 합니다.
        if (currentState == CookingState.Ready)
        {
            timeLeft = GamePlayManager.Instance.cookDuration;
        }

        // 시간을 분(Min)과 초(Sec)로 계산
        int minutes = Mathf.FloorToInt(timeLeft / 60f);
        int seconds = Mathf.FloorToInt(timeLeft % 60f);

        // 00:00 포맷으로 텍스트 업데이트
        if (timerText != null)
        {
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            
            // 시간이 10초 이하남았을 때 빨간색 깜빡임
            if (timeLeft <= 10f && timeLeft > 0f)
            {
                timerText.color = Mathf.PingPong(Time.time * 2f, 1f) > 0.5f ? Color.red : Color.white;
            }
            else
            {
                timerText.color = Color.white;
            }
        }
    }
}
