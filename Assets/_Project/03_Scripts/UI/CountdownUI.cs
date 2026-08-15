using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// 요리 시작 전 3, 2, 1, START! 카운트다운 UI (가벼운 이벤트 방식)
/// </summary>
public class CountdownUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("배경과 텍스트를 모두 포함한 '부모 패널'을 여기에 넣어주세요.")]
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private TextMeshProUGUI countdownText;

    [Header("Display Settings")]
    [SerializeField] private float fontSizeNormal = 200f;
    [SerializeField] private float fontSizeStart = 150f;

    private void Awake()
    {
        // 씬 시작 시 카운트다운 창을 확실히 꺼줍니다.
        if (countdownPanel != null) countdownPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (GamePlayManager.Instance != null)
        {
            GamePlayManager.Instance.OnCookingPhaseEntered -= HandlePhaseChanged;
            GamePlayManager.Instance.OnCookingPhaseEntered += HandlePhaseChanged;
            
            // 현재 상태가 이미 Ready라면 즉시 실행
            HandlePhaseChanged(GamePlayManager.Instance.CurrentCookingState);
        }
    }

    private void OnDisable()
    {
        if (GamePlayManager.Instance != null)
        {
            GamePlayManager.Instance.OnCookingPhaseEntered -= HandlePhaseChanged;
        }
    }

    private void HandlePhaseChanged(CookingState state)
    {
        if (state == CookingState.Ready)
        {
            if (countdownPanel != null) countdownPanel.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(CountdownRoutine());
        }
        else
        {
            // Ready 상태가 아니면 무조건 패널을 비활성화합니다.
            if (countdownPanel != null) countdownPanel.SetActive(false);
            StopAllCoroutines();
        }
    }

    private IEnumerator CountdownRoutine()
    {
        while (GamePlayManager.Instance != null && GamePlayManager.Instance.CurrentCookingState == CookingState.Ready)
        {
            float timeLeft = GamePlayManager.Instance.GetRemainingTime();
            
            if (timeLeft > 1.0f)
            {
                int displayValue = Mathf.CeilToInt(timeLeft - 1.0f);
                if (countdownText != null)
                {
                    countdownText.text = displayValue.ToString();
                    countdownText.fontSize = fontSizeNormal;
                    countdownText.color = Color.white;
                }
            }
            else if (timeLeft > 0f)
            {
                if (countdownText != null)
                {
                    countdownText.text = "START!";
                    countdownText.fontSize = fontSizeStart;
                    countdownText.color = Color.yellow;
                }
            }
            yield return null;
        }

        // 카운트다운이 종료되면 패널을 끕니다.
        if (countdownPanel != null) countdownPanel.SetActive(false);
    }
}
