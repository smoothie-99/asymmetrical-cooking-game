using UnityEngine;
using Fusion;

public class CookingMasterCamera : NetworkBehaviour
{
    public float mouseSensitivity = 5.0f; 
    public Transform playerBody;
    public Vector3 firstPersonLocalPos = new Vector3(0, 1.5f, 0); 
    public Vector3 thirdPersonOffset = new Vector3(0, 3.5f, 3.5f); 
    public float cameraTransitionSpeed = 5f;
    public LayerMask obstacleLayer = ~0; 

    [Header("Arms Camera")]
    [SerializeField] private Camera armsCamera;

    private float xRotation = 0f;
    private CookingMasterMovement movement;
    private float sleepTimer = 0f; // [추가] 수면 카메라 시점 전환 지연 타이머
    private Camera camComponent;
    private AudioListener audioListener;

    private void OnEnable()
    {
        // [추가] 환경설정 변경 이벤트 구독
        SettingsPanel.OnSensitivityUpdated -= UpdateSensitivity;
        SettingsPanel.OnSensitivityUpdated += UpdateSensitivity;
    }

    private void OnDisable()
    {
        SettingsPanel.OnSensitivityUpdated -= UpdateSensitivity;
    }

    private void UpdateSensitivity(float newSensitivity)
    {
        // [추가] 본인 클라이언트에만 적용되도록 체크
        if (HasStateAuthority)
        {
            mouseSensitivity = newSensitivity;
            Debug.Log($"🖱️ [Camera] 마우스 감도 실시간 갱신: {mouseSensitivity}");
        }
    }

    public override void Spawned()
    {
        movement = GetComponentInParent<CookingMasterMovement>();
        camComponent = GetComponent<Camera>();
        audioListener = GetComponent<AudioListener>();

        // [추가] 초기값 로드
        mouseSensitivity = PlayerPrefs.GetFloat("MouseSensitivity", mouseSensitivity);

        if (playerBody == null && transform.parent != null)
        {
            playerBody = transform.parent;
        }

        if (HasStateAuthority)
        {
            // 내가 주방장인 경우: 화면을 직접 봐야 하므로 카메라와 오디오 리스너 활성화
            if (camComponent != null) camComponent.enabled = true;
            if (armsCamera != null) armsCamera.enabled = true;

            if (audioListener != null)
            {
                audioListener.enabled = true;
                
                // [안전 장치] 씬에 있는 다른 모든 오디오 리스너를 찾아 끕니다.
                AudioListener[] allListeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
                foreach (var listener in allListeners)
                {
                    if (listener != audioListener) listener.enabled = false;
                }
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log("✅ [Camera] 로컬 요리사용 카메라/리스너 활성화 및 타 리스너 정리 완료");
        }
        else
        {
            // 타인 클라이언트(2P 레시피 마스터 등)인 경우: 
            // 요리사의 화면이 내 전체 화면을 가리지 않도록 카메라를 끕니다.
            // (추후 수정구를 위해 RenderTexture를 사용하는 경우는 여기서 예외 처리를 할 수 있습니다.)
            if (camComponent != null) camComponent.enabled = false;
            if (armsCamera != null) armsCamera.enabled = false;
            if (audioListener != null) audioListener.enabled = false;
            
            Debug.Log("🚫 [Camera] 타인 캐릭터이므로 카메라/리스너 비활성화");
        }
    }

    void Start()
    {
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.nearClipPlane = 0.3f;

        // 샌드박스용: 네트워크 런너가 없으면 1인칭 카메라 자동 활성화
        bool isSandbox = (Runner == null || !Runner.IsRunning);
        if (isSandbox)
        {
            camComponent = GetComponent<Camera>();
            audioListener = GetComponent<AudioListener>();
            if (camComponent != null) camComponent.enabled = true;
            if (armsCamera != null) armsCamera.enabled = true;
            if (audioListener != null) audioListener.enabled = true;

            // [수정] 단일 씬 구조에서 메인메뉴와 구분하기 위해
            // Cook 단계일 때만 커서를 잠급니다.
            bool isCookPhase = SystemManager.Instance != null
                && SystemManager.Instance.CurrentMetaState == MetaState.Cooking;
            if (isCookPhase)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    void LateUpdate() 
    {
        if (Object != null && HasStateAuthority == false) return;
        if (playerBody == null) return;

        // [수정] Cook 단계이면서 'UI 메뉴가 닫혀있고 일시정지 상태가 아닐 때만' 커서를 잠그고 화면을 회전시킵니다.
        bool isCookPhase = SystemManager.Instance != null
            && SystemManager.Instance.CurrentMetaState == MetaState.Cooking;
        
        // 일시정지 상태 확인 (Proxy 참조)
        bool isPaused = GlobalNetworkState.Instance != null && GlobalNetworkState.Instance.IsPaused;
        bool isMenuOpen = InGameMenuUI.IsMenuOpen;

        // [수정] Cook 단계이면서 'UI 메뉴가 닫혀있고 일시정지 상태가 아닐 때' Cursor를 잠급니다.
        // 하지만 실제 회전은 'Cooking' 상태일 때만 수행합니다 (Ready 상태에서는 화면 고정).
        bool isControlPhase = GamePlayManager.Instance != null && 
                             (GamePlayManager.Instance.CurrentCookingState == CookingState.Ready || 
                              GamePlayManager.Instance.CurrentCookingState == CookingState.Cooking);
        
        bool isActualCooking = GamePlayManager.Instance != null && GamePlayManager.Instance.CurrentCookingState == CookingState.Cooking;

        if (isCookPhase && isControlPhase && !isMenuOpen && !isPaused)
        {
            // 게임(또는 카운트다운) 중일 때는 커서 잠금
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (movement == null) movement = GetComponentInParent<CookingMasterMovement>();
            bool isSleeping = movement != null && (bool)movement.NetworkedIsSleeping;

            // [핵심] 실제 마우스 회전 처리는 '요리 시작(Cooking)' 이후에만 수행합니다 (수면 중 차단).
            if (isActualCooking && !isSleeping)
            {
                float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
                float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

                xRotation -= mouseY;
                xRotation = Mathf.Clamp(xRotation, -90f, 90f);
                
                if (movement != null && movement.Object != null)
                {
                    movement.NetworkedXRotation = xRotation;
                }

                Vector3 currentRotation = playerBody.eulerAngles;
                currentRotation.y += mouseX;
                playerBody.eulerAngles = currentRotation;
                
                if (movement != null && movement.Object != null)
                {
                    movement.NetworkedYRotation = currentRotation.y;
                }
            }
        }
        else
        {
            // 메뉴가 열려있거나 일시정지 중이거나 Cook 단계가 아니면 마우스가 자유로워야 함
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Debug.Log($"🔓 [Camera] 마우스 해제 (Menu:{isMenuOpen}, Pause:{isPaused}, Meta:{SystemManager.Instance.CurrentMetaState})");
            }
        }

        if (movement == null) movement = GetComponentInParent<CookingMasterMovement>();
        bool isSleepingCheck = movement != null && (bool)movement.NetworkedIsSleeping;

        if (isSleepingCheck)
        {
            sleepTimer += Time.deltaTime;

            if (sleepTimer >= 0.3f)
            {
                // 탑뷰 로직 - 1초 지연 후 설정
                transform.localPosition = new Vector3(0f, 4.0f, -0.5f);
                transform.localRotation = Quaternion.Euler(75f, 0f, 0f);
            }
            else
            {
                // 1초 도달 전엔 기존 1인칭 유지
                transform.localPosition = firstPersonLocalPos;
                transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            }
        }
        else
        {
            sleepTimer = 0f; // 수면 해제 시 타이머 초기화

            // 1인칭 위치 복귀 (즉시)
            transform.localPosition = firstPersonLocalPos;
            transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }
    }
}