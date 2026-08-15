using UnityEngine;

public class ScreenMover : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 500f; // 이동 속도
    private RectTransform rectTransform;

    void Awake()
    {
        // UI 요소의 위치를 제어하기 위해 RectTransform을 가져옴
        rectTransform = GetComponent<RectTransform>();
    }

    void Update()
    {
        // 1. 키보드 입력 받기 (WASD 또는 방향키)
        float horizontal = Input.GetAxisRaw("Horizontal"); // 좌우 (-1, 0, 1)
        float vertical = Input.GetAxisRaw("Vertical");     // 상하 (-1, 0, 1)

        // 2. 이동 방향 설정
        Vector2 moveDirection = new Vector2(horizontal, vertical).normalized;

        // 3. UI의 anchoredPosition 변경 (델타타임을 곱해 프레임 독립적 이동)
        if (moveDirection.sqrMagnitude > 0)
        {
            rectTransform.anchoredPosition += moveDirection * moveSpeed * Time.deltaTime;

            ClampToParent();
        }
    }

    void ClampToParent()
    {
        // 현재 위치를 제한 범위 내로 고정
        Vector2 clampedPos = rectTransform.anchoredPosition;
        clampedPos.x = Mathf.Clamp(clampedPos.x, -500f, 500f);
        clampedPos.y = Mathf.Clamp(clampedPos.y, -400f, 400f);

        rectTransform.anchoredPosition = clampedPos;
    }
}