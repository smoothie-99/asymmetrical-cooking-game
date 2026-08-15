using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class LoadingUI : MonoBehaviour
{
    public static LoadingUI Instance { get; set; } // [수정] 외부에서 수동 할당 가능하게 set 추가

    [Header("UI References")]
    [SerializeField] private RectTransform loadingIcon;
    [SerializeField] private GameObject exitButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject opaqueBackground; // [추가] 강제 종료 시에만 켤 불투명 회색 배경

    [Header("Settings")]
    [SerializeField] private float rotationSpeed = 200f;
    [SerializeField] private float timeoutLimit = 15f;

    private float timer = 0f;
    private bool isTimeoutReached = false;
    private bool isReconnectionMode = false;
    private bool isForceExitMode = false; // [추가] MVP용 강제 종료 모드
    private float forceExitTimer = 0f;    // [추가] 자동 퇴장 타이머
    private MetaState stateBeforePause = MetaState.None;

    private void Awake()
    {
        // [수정] 이미 인스턴스가 있더라도 비활성 상태였다면 갱신합니다.
        Instance = this;
    }

    private void OnEnable()
    {
        // 초기화
        timer = 0f;
        forceExitTimer = 0f;
        isTimeoutReached = false;
        
        if (exitButton != null) exitButton.SetActive(false);
        
        // [추가] 모드에 따라 불투명 배경 가시성 제어
        if (opaqueBackground != null) opaqueBackground.SetActive(isForceExitMode);

        if (isForceExitMode)
        {
            statusText.text = "상대방의 연결이 끊겨 게임을 종료합니다.\n3초 후 메인으로 이동합니다...";
            Debug.Log("🚫 [Loading] MVP 강제 종료 모드로 진입합니다.");
        }
        else if (isReconnectionMode)
        {
            statusText.text = "상대방의 연결이 끊겼습니다. 재접속을 기다리는 중...";
        }
        else
        {
            // [수정] 만약 외부(MainMenuUI 등)에서 이미 특정 텍스트를 넣어두었다면 덮어쓰지 않습니다.
            if (statusText.text == "" || statusText.text == "New Text" || statusText.text == "Status..." || statusText.text == "Status Text")
            {
                statusText.text = "다른 플레이어를 기다리는 중...";
            }
        }
        
        // [수정] 강제 종료 모드일 때는 전역 상태를 바꾸지 않습니다. (메인 메뉴 UI가 튀어나와서 글자를 가리는 현상 방지)
        if (SystemManager.Instance != null && !isForceExitMode)
        {
            stateBeforePause = SystemManager.Instance.CurrentMetaState;
            SystemManager.Instance.ChangeMetaState(MetaState.Lobby);
        }
    }

    private void Update()
    {
        // 1. 로딩 아이콘 회전 연출
        if (loadingIcon != null)
        {
            loadingIcon.Rotate(Vector3.back * rotationSpeed * Time.deltaTime);
        }

        // 2. 강제 종료 카운트다운 (MVP용)
        if (isForceExitMode)
        {
            forceExitTimer += Time.deltaTime;
            if (forceExitTimer >= 3f) // 3초 뒤 자동 퇴장
            {
                isForceExitMode = false;
                OnClickExitToMenu();
                return;
            }
        }

        // 3. 타임아웃 체크 (일반 로딩/재접속용)
        if (!isTimeoutReached && !isForceExitMode)
        {
            timer += Time.deltaTime;
            if (timer >= timeoutLimit)
            {
                OnTimeout();
            }
        }
    }

    private void OnTimeout()
    {
        isTimeoutReached = true;
        
        if (exitButton != null) exitButton.SetActive(true); 

        if (statusText != null) 
        {
            statusText.text = isReconnectionMode 
                ? "상대방이 돌아오지 않습니다. 메인으로 돌아갈까요?" 
                : "상대방의 연결을 기다리는 중입니다...";
        }
    }

    // [추가] MVP용: 플레이어 이탈 시 3초 후 자동 퇴장 호출
    public void ShowPlayerLeftAndExit()
    {
        isForceExitMode = true;
        isReconnectionMode = false;

        // [추가] 방해가 되는 인게임 UI(레시피 시스템 등)를 찾아 숨깁니다.
        var recipeSystem = GameObject.Find("P2_Recipe_System") ?? GameObject.Find("P2_Recipe_System(Clone)");
        if (recipeSystem != null) recipeSystem.SetActive(false);

        // [추가] 내 캔버스의 Sorting Order를 최상단(999)으로 올려서 어떤 UI보다 앞에 오게 합니다.
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            parentCanvas.sortingOrder = 999;
            Debug.Log($"🔝 [Loading] Canvas Sorting Order를 {parentCanvas.sortingOrder}로 변경하여 최상단으로 올립니다.");
        }

        gameObject.SetActive(true);
    }

    // [추가] 재접속 성공 시 호출
    public void OnReconnected()
    {
        if (!isReconnectionMode) return;

        Debug.Log("🔗 [Loading] 상대방 재접속 성공! 게임을 재개합니다.");
        
        // 이전 상태로 복구
        if (SystemManager.Instance != null && stateBeforePause != MetaState.None)
        {
            SystemManager.Instance.ChangeMetaState(stateBeforePause);
        }

        isReconnectionMode = false;
        gameObject.SetActive(false);
    }

    // 모두 준비되었을 때 호출 (기존 입장용)
    public void OnAllPlayersReady()
    {
        if (isReconnectionMode) return; // 재접속 모드면 무시

        // 게임 상태를 Dialogue로 넘겨서 게임 시작!
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.ChangeMetaState(MetaState.OrderDialogue);
        }

        Debug.Log("모든 인원 준비 완료! 로딩 패널을 닫고 게임을 시작합니다.");
        gameObject.SetActive(false);
    }

    // 나가기 버튼에 연결할 함수
    public void OnClickExitToMenu()
    {
        Debug.Log("메인 메뉴로 퇴장합니다.");
        if (NetworkLauncher.Instance != null)
        {
            // [수정] 강제 종료 모드일 때는 추가 안내창(ExitWaitingPanel)을 띄우지 않습니다.
            NetworkLauncher.Instance.LeaveSession(!isForceExitMode);
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        }
    }
}
