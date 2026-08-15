using UnityEngine;

public class ScreenMover2 : MonoBehaviour
{
    [Header("회전 설정")]
    [SerializeField] private float rotationSpeed = 100f; // 회전 속도
    [SerializeField] private float maxRotationAngle = 60f; // 중심에서 한쪽으로 회전할 최대 각도 (총 범위는 120도)

    private float currentAngle = 0f; // 현재 회전 각도 (0은 정중앙)
    private RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    void Update()
    {
        // 1. 키보드 입력 받기 (A, D 또는 좌우 방향키)
        float input = Input.GetAxisRaw("Horizontal"); // 왼쪽: -1, 오른쪽: 1, 안 누름: 0

        // 2. 입력에 따라 목표 각도 변화 계산
        if (input != 0)
        {
            // 입력 방향으로 속도를 곱해 각도를 변화시킴
            currentAngle += input * rotationSpeed * Time.deltaTime;

            // 3. 회전 범위 제한 (Clamping)
            // 중심(0도)을 기준으로 -maxRotationAngle ~ +maxRotationAngle 사이로 제한
            currentAngle = Mathf.Clamp(currentAngle, -maxRotationAngle, maxRotationAngle);
        }

        // 4. 실제 UI에 회전 적용
        // UI는 Z축을 기준으로 회전해야 부채꼴 모양이 됨
        rectTransform.localRotation = Quaternion.Euler(0, 0, currentAngle);
    }
}
