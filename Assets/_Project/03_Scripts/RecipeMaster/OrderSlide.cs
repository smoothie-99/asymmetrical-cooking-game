using DG.Tweening;
using UnityEngine;
using UnityEngine.Audio;

public class OrderSlide : MonoBehaviour
{
    [SerializeField] private AudioSource orderAudioSource; // UI 전용 오디오 소스
    [SerializeField] private AudioClip orderToggleSound;

    private RectTransform rectTransform;
    private bool isOpen = false;

    [Header("슬라이드 설정")]
    public float openX = 300f;   // 완전히 펼쳐졌을 때의 X 위치
    public float closedX = 20f;  // 살짝만 보일 때의 X 위치
    public float duration = 0.5f; // 이동 시간

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        // 시작할 때 닫힌 위치로 초기화
        rectTransform.anchoredPosition = new Vector2(closedX, rectTransform.anchoredPosition.y);
    }

    // 매 프레임 키보드 입력 체크!
    void Update()
    {
        // 숫자 1번 키를 누르면 슬라이드 토글
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            ToggleBookmark();
        }
    }

    public void ToggleBookmark()
    {
        PlayToggleSound();

        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
            Debug.LogError("RectTransform이 없어서 다시 가져옵니다!");
        }

        // 중복 실행 방지를 위해 이전 트윈 중단
        rectTransform.DOKill();

        Debug.Log($"움직임 시작! 목표 위치: {(isOpen ? closedX : openX)} / 현재 위치: {rectTransform.anchoredPosition.x}");

        if (isOpen)
        {
            rectTransform.DOAnchorPosX(closedX, duration).SetEase(Ease.InQuad)
                .OnComplete(() => Debug.Log("닫기 완료!"));
        }
        else
        {
            rectTransform.DOAnchorPosX(openX, duration).SetEase(Ease.OutCubic)
                .OnComplete(() => Debug.Log("열기 완료!"));
        }

        isOpen = !isOpen;
    }

    private void PlayToggleSound()
    {
        if (orderAudioSource != null && orderToggleSound != null)
        {
            orderAudioSource.PlayOneShot(orderToggleSound);
        }
    }
}