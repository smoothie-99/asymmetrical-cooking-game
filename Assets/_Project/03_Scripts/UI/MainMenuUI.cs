using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine.EventSystems;

public class MainMenuUI : MonoBehaviour
{
    [Header("Login Inputs")] // 로그인 전용 입력창
    [SerializeField] private TMP_InputField loginIDInput;       // 로그인용 아이디 입력창
    [SerializeField] private TMP_InputField loginPasswordInput; // 로그인용 비밀번호 입력창

    [Header("Warning Modal")] // 경고창
    [SerializeField] private GameObject warningDialogPanel; // 경고창 패널 오브젝트
    [SerializeField] private TMP_Text warningText;          // 경고 문구 텍스트

    [Header("Panels")]
    [SerializeField] private GameObject titlePanel;          // 타이틀 화면 (Touch to Start)
    [SerializeField] private GameObject loginPanel;          // 로그인/회원가입 부모 패널
    [SerializeField] private GameObject loginSelectionPanel; // [신규] 로그인/회원가입/게스트 선택 화면
    [SerializeField] private GameObject loginFormPanel;      // [신규] 실제 ID/PW 입력 화면
    [SerializeField] private GameObject signupFormPanel;     // [신규] 회원가입 입력 화면
    [SerializeField] private TMP_InputField signupIDInput;    // [신규] 회원가입 아이디 입력창
    [SerializeField] private TMP_Text idValidationText;      // [신규] 아이디 검증 결과 안내 텍스트
    [SerializeField] private TMP_InputField signupNicknameInput; // [신규] 회원가입 닉네임 입력창
    [SerializeField] private TMP_Text nicknameValidationText; // [신규] 닉네임 검증 결과 안내 텍스트
    [SerializeField] private TMP_InputField signupPasswordInput; // [신규] 비밀번호 입력창
    [SerializeField] private TMP_InputField signupPasswordConfirmInput; // [신규] 비밀번호 확인 입력창
    [SerializeField] private TMP_Text passwordValidationText; // [신규] 비밀번호 검증 안내 텍스트
    [SerializeField] private TMP_Text passwordConfirmValidationText; // [신규] 비밀번호 일치 확인 안내 텍스트
    [SerializeField] private TMP_InputField signupEmailInput;    // [신규] 회원가입 이메일 입력창
    [SerializeField] private TMP_Text emailCheckText;           // [신규] 이메일 인증 상태 안내 텍스트
    private bool isEmailVerified = false;                      // [신규] 이메일 인증 완료 여부
    private bool isVerificationStarted = false;                // [신규] 이메일 인증 메일 발송 여부
    private Coroutine emailVerificationCoroutine;              // [신규] 이메일 인증 체크 코루틴
    private bool isIDChecked = false;                          // [신규] 아이디 중복 확인 여부
    private bool isNicknameChecked = false;                    // [신규] 닉네임 중복 확인 여부
    [SerializeField] private GameObject signupSuccessDialogPanel;               // [신규] 회원가입 성공 안내 다이얼로그
    [SerializeField] private TMP_Text signupSuccessText;                       // [신규] 회원가입 성공 메시지 텍스트
    [SerializeField] private Button signupConfirmButton;        // [신규] 회원가입 확인 버튼
    [SerializeField] private GameObject mainPanel;           // 기존 메인 버튼들이 있는 패널
    [SerializeField] private GameObject settingsPanel;       // 환경설정 패널
    [SerializeField] private GameObject roomSelectionPanel;  // [생성/입장] 선택 패널
    [SerializeField] private GameObject joinRoomPanel;       // 방 코드 입력 다이얼로그 패널
    [SerializeField] private GameObject roomLobbyPanel;      // [팀 구성] 로비 패널
    [SerializeField] public GameObject roundSelectionPanel; // [라운드 선택] 패널 추가
    [SerializeField] private GameObject menuBackground;      // 메인 통합 배경 (흰색/이미지)
    [SerializeField] private GameObject decoratedElements;   // 제목, 캐릭터 등 장식 요소들
    [SerializeField] private GameObject loadingPanel;        // 로딩 패널 추가
    [SerializeField] private TMP_Text loadingStatusText;     // 로딩 상태 텍스트
    [SerializeField] private GameObject myInfoPanel;        // [신규] 내 정보 패널 (MyinfoPanel)
    [SerializeField] private Button tutorialButton;        // [신규] 메인 화면의 튜토리얼 시작 버튼

    [Header("Tutorial System")]
    [SerializeField] private GameObject tutorialPanel;       // 튜토리얼 메인 패널
    [SerializeField] private GameObject tutorialExitDialog;  // ESC 종료 확인 다이얼로그
    [SerializeField] private Image tutorialImageDisplay;     // 이미지가 출력될 UI Image
    [SerializeField] private System.Collections.Generic.List<Sprite> tutorialSprites; // 튜토리얼 이미지 명단
    private int _currentTutorialPage = 0;
    private bool _isTutorialActive = false;

    [Header("Top Level Folders (Single Scene)")]    
    [SerializeField] private GameObject mainMenuFolder;      // "MainMenu" 폴더 오브젝트
    [SerializeField] private GameObject cookFolder;          // "Cook" 폴더 오브젝트
    [SerializeField] private GameObject p1Sandbox;           // "P1_Chef_Sandbox" 오브젝트
    [SerializeField] private GameObject p2Sandbox;           // "P2_Recipe_Sandbox" 오브젝트

    [Header("Join Room UI")]
    [SerializeField] private TMP_InputField roomCodeInputField;
    [SerializeField] private TMP_Text joinRoomErrorText;  // 에러 메시지 텍스트 추가

    [Header("Lobby UI")]
    [SerializeField] private TMP_Text lobbyRoomCodeText;

    [Header("Settings UI")]
    [SerializeField] private SettingsPanel settingsPanelComponent;

    private string currentRoomCode = "";
    public static string CurrentSessionUserID { get; private set; } = null; // [신규] 현재 실행 중인 세션의 로그인 정보
    public static string CurrentSessionNickname { get; private set; } = null; // [신규] 현재 세션 로그인 닉네임

    void Start()
    {
        // 시작할 때는 마우스를 풀어줍니다.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 시작 시 패널 구성을 결정합니다.
        if (!string.IsNullOrEmpty(CurrentSessionUserID))
        {
            // [추가] 이미 이번 세션에서 로그인되어 있다면 타이틀/로그인을 건너뛰고 바로 메인으로 이동
            if (titlePanel != null) titlePanel.SetActive(false);
            if (loginPanel != null) loginPanel.SetActive(false);
            if (mainPanel != null) mainPanel.SetActive(true);
            if (decoratedElements != null) decoratedElements.SetActive(true);
        }
        else
        {
            // 초기 패널 상태 설정 (일반적인 시작)
            if (titlePanel != null) titlePanel.SetActive(true);
            if (loginPanel != null) loginPanel.SetActive(false);
            if (mainPanel != null) mainPanel.SetActive(false);
        }

        // 서브 패널 초기화
        if (loginSelectionPanel != null) loginSelectionPanel.SetActive(true);
        if (loginFormPanel != null) loginFormPanel.SetActive(false);
        if (signupFormPanel != null) signupFormPanel.SetActive(false);
        if (signupSuccessDialogPanel != null) signupSuccessDialogPanel.SetActive(false);

        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (menuBackground != null) menuBackground.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(true);
        if (loadingPanel != null) loadingPanel.SetActive(false);
        if (myInfoPanel != null) myInfoPanel.SetActive(false); // [신규] 초기 비활성화

        // [추가] 저장된 아이디 불러오기 (아이디 기억하기)
        if (loginIDInput != null && PlayerPrefs.HasKey("SavedLoginID"))
        {
            loginIDInput.text = PlayerPrefs.GetString("SavedLoginID");
            // 아이디가 이미 입력되어 있다면 비밀번호 칸에 바로 포커스를 줍니다.
            if (loginPasswordInput != null) loginPasswordInput.Select();
        }

        // [아이디 설정] 6~10자 제한 및 검증 연결
        if (signupIDInput != null)
        {
            signupIDInput.onValidateInput = ValidateIDInput;
            signupIDInput.onValueChanged.AddListener(OnIDValueChanged);
            signupIDInput.characterLimit = 10;
        }

        // [비밀번호 설정] 8~20자 제한 및 실시간 일치 확인 이벤트 연결
        if (signupPasswordInput != null)
        {
            signupPasswordInput.onValidateInput = ValidatePasswordInput;
            signupPasswordInput.characterLimit = 20;
            signupPasswordInput.onValueChanged.AddListener((_) => ResetPasswordValidationText());
        }

        if (signupPasswordConfirmInput != null)
        {
            signupPasswordConfirmInput.onValidateInput = ValidatePasswordInput;
            signupPasswordConfirmInput.characterLimit = 20;
            signupPasswordConfirmInput.onValueChanged.AddListener(OnPasswordConfirmValueChanged);
        }

        if (signupEmailInput != null)
        {
            signupEmailInput.onValueChanged.AddListener((newEmail) =>
            {
                // 인증메일이 한 번이라도 발송된 이후에만 안내 메시지가 출력되어야 함!
                if (isVerificationStarted)
                {
                    isEmailVerified = false; 
                    isVerificationStarted = false; // 상태 리셋
                    emailCheckText.text = "<color=red>이메일이 변경되었습니다. 다시 인증해주세요.</color>";
                    
                    if (emailVerificationCoroutine != null)
                    {
                        StopCoroutine(emailVerificationCoroutine);
                        emailVerificationCoroutine = null;
                    }
                }
            });
        }

        if (signupNicknameInput != null)
        {
            signupNicknameInput.characterLimit = 10;
            signupNicknameInput.onValueChanged.AddListener(OnNicknameValueChanged);
        }

        // [로그인 설정] 한글 입력 방지 및 글자수 제한 추가
        if (loginIDInput != null)
        {
            loginIDInput.onValidateInput = ValidateIDInput; // 한글 차단 및 소문자 자동 변환
            loginIDInput.characterLimit = 10;
            // [추가] IME 한글 버그 방지 - 실시간 텍스트 정화
            loginIDInput.onValueChanged.AddListener((val) => SanitizeInputField(loginIDInput, @"[^a-z0-9]"));
        }

        if (loginPasswordInput != null)
        {
            loginPasswordInput.onValidateInput = ValidatePasswordInput; // 한글/공백 차단
            loginPasswordInput.characterLimit = 20;
            // [추가] IME 한글 버그 방지 - 실시간 텍스트 정화
            loginPasswordInput.onValueChanged.AddListener((val) => SanitizeInputField(loginPasswordInput, @"[^a-zA-Z0-9!@#$%^&*()_+{}:""<>?|\[\]\\;',./`~-]"));
        }

        // [회원가입 설정] IME 한글 버그 방지 추가
        if (signupIDInput != null)
        {
            signupIDInput.onValueChanged.AddListener((val) => SanitizeInputField(signupIDInput, @"[^a-z0-9]"));
        }
        if (signupPasswordInput != null)
        {
            signupPasswordInput.onValueChanged.AddListener((val) => SanitizeInputField(signupPasswordInput, @"[^a-zA-Z0-9!@#$%^&*()_+{}:""<>?|\[\]\\;',./`~-]"));
        }
        if (signupPasswordConfirmInput != null)
        {
            signupPasswordConfirmInput.onValueChanged.AddListener((val) => SanitizeInputField(signupPasswordConfirmInput, @"[^a-zA-Z0-9!@#$%^&*()_+{}:""<>?|\[\]\\;',./`~-]"));
        }

        // [방 코드 입력] 엔터키(Submit) 이벤트 연결 및 숫자 6자리 제한
        if (roomCodeInputField != null)
        {
            roomCodeInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
            roomCodeInputField.characterLimit = 6;
            roomCodeInputField.onSubmit.AddListener((_) => OnClickConfirmJoin());
        }

        // [튜토리얼 설정] 버튼 클릭 리스너 연결
        if (tutorialButton != null)
        {
            tutorialButton.onClick.AddListener(OpenTutorial);
        }
    }

    /// <summary>
    /// [신규] 입력창에서 허용되지 않는 문자(한글 등)를 실시간으로 제거합니다.
    /// </summary>
    private void SanitizeInputField(TMP_InputField inputField, string pattern)
    {
        string currentText = inputField.text;
        string sanitizedText = Regex.Replace(currentText, pattern, "");

        if (currentText != sanitizedText)
        {
            inputField.text = sanitizedText;
            // 커서 위치가 맨 앞으로 튀지 않도록 보정
            inputField.caretPosition = inputField.text.Length;
        }
    }

    private void OnEnable()
    {
        SystemManager.SubscribeWhenReady(HandleGlobalMetaStateChanged);
    }

    private void OnDisable()
    {
        SystemManager.Unsubscribe(HandleGlobalMetaStateChanged);
    }


    private void Update()
    {
        // 환경설정 창이 열려있을 때 ESC를 누르면 '취소' 로직을 실행합니다.
        if (settingsPanel != null && settingsPanel.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            OnClickCancel();
        }

        // [추가] 방 코드 입력창 열려있을 때 ESC 누르면 닫기
        if (joinRoomPanel != null && joinRoomPanel.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            OnClickBackFromJoin();
        }

        // [보안] 비밀번호 복사/붙여넣기/잘라내기(Ctrl+C,V,X) 원천 차단
        bool isSensitiveFieldFocused = (signupPasswordInput != null && signupPasswordInput.isFocused) || 
                                      (signupPasswordConfirmInput != null && signupPasswordConfirmInput.isFocused) ||
                                      (loginPasswordInput != null && loginPasswordInput.isFocused);

        if (Keyboard.current != null)
        {
            // 비밀번호 필드에 포커스가 있고 Ctrl 키를 누르고 있다면 필드를 읽기 전용으로 만들어 수정을 막습니다.
            if (isSensitiveFieldFocused && Keyboard.current.ctrlKey.isPressed)
            {
                if (signupPasswordInput != null) signupPasswordInput.readOnly = true;
                if (signupPasswordConfirmInput != null) signupPasswordConfirmInput.readOnly = true;
                if (loginPasswordInput != null) loginPasswordInput.readOnly = true;

                if (Keyboard.current.cKey.wasPressedThisFrame || 
                    Keyboard.current.vKey.wasPressedThisFrame || 
                    Keyboard.current.xKey.wasPressedThisFrame)
                {
                    Debug.LogWarning("⚠️ 보안상 비밀번호 필드에서는 복사/붙여넣기를 사용할 수 없습니다.");
                }
            }
            else
            {
                // Ctrl을 뗐거나 포커스가 없으면 다시 입력 가능 상태로 돌려줍니다.
                if (signupPasswordInput != null && signupPasswordInput.readOnly) signupPasswordInput.readOnly = false;
                if (signupPasswordConfirmInput != null && signupPasswordConfirmInput.readOnly) signupPasswordConfirmInput.readOnly = false;
                if (loginPasswordInput != null && loginPasswordInput.readOnly) loginPasswordInput.readOnly = false;
            }
        }
        // [리팩토링] 이제 Update에서 매 프레임 체크하지 않습니다.
        // SyncLobbyStateAcrossFolders(); // 제거됨

        // [추가] Tab / Enter 키 네비게이션
        HandleKeyboardNavigation();

        // [신규] 아이디/비밀번호 필드 포커스 시 IME 비활성화 (한글 입력 방지 및 8개 별표 버그 해결)
        UpdateIMEStatus();
    }

    private void UpdateIMEStatus()
    {
        // 1. 한글 입력이 차단되어야 하는 민감한 필드들 (ID, 비밀번호, 이메일, 방 코드 등)
        bool isAnySensitiveFieldFocused = (loginIDInput != null && loginIDInput.isFocused) ||
                                         (loginPasswordInput != null && loginPasswordInput.isFocused) ||
                                         (signupIDInput != null && signupIDInput.isFocused) ||
                                         (signupPasswordInput != null && signupPasswordInput.isFocused) ||
                                         (signupPasswordConfirmInput != null && signupPasswordConfirmInput.isFocused) ||
                                         (signupEmailInput != null && signupEmailInput.isFocused) ||
                                         (roomCodeInputField != null && roomCodeInputField.isFocused);

        // 2. 한글 입력이 명시적으로 허용되어야 하는 필드 (닉네임)
        bool isNicknameFieldFocused = (signupNicknameInput != null && signupNicknameInput.isFocused);

        if (isAnySensitiveFieldFocused)
        {
            // 입력창에 포커스가 있으면 IME를 꺼버려서 영문만 입력되게 함 (8개 별표 버그 방지)
            Input.imeCompositionMode = IMECompositionMode.Off;
        }
        else if (isNicknameFieldFocused)
        {
            // 닉네임 입력 시에는 IME를 활성화하여 한글 입력을 가능케 함
            Input.imeCompositionMode = IMECompositionMode.On;
        }
        else
        {
            // 포커스가 없으면 다시 원래대로 돌려둠 (Auto)
            Input.imeCompositionMode = IMECompositionMode.Auto;
        }
    }

    private void HandleKeyboardNavigation()
    {
        if (Keyboard.current == null) return;

        // 1. Tab 키: 다음 입력창으로 이동
        if (Keyboard.current.tabKey.wasPressedThisFrame)
        {
            NavigateToNextField();
        }

        // 2. Enter 키: 확인/로그인 실행
        if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
        {
            SubmitCurrentForm();
        }

        // 3. ESC 키: 뒤로 가기 / 취소
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HandleEscapeKey();
        }
    }

    private void HandleEscapeKey()
    {
        // [1순위] 경고창 닫기
        if (warningDialogPanel != null && warningDialogPanel.activeSelf)
        {
            OnClickCloseWarning();
            return;
        }
        
        // [신규] 내 정보 패널 닫기 (우선순위 높음)
        if (myInfoPanel != null && myInfoPanel.activeSelf)
        {
            OnClickCloseMyInfo();
            return;
        }

        // [2순위] 게임 코드 입력창 닫기
        if (joinRoomPanel != null && joinRoomPanel.activeSelf)
        {
            OnClickBackFromJoin();
            return;
        }

        // [3순위] 로그인 양식 -> 로그인 선택 화면으로
        if (loginFormPanel != null && loginFormPanel.activeSelf)
        {
            OnBackToLoginSelection();
            return;
        }

        // [4순위] 방 선택 화면 -> 타이틀/메인 화면으로
        if (roomSelectionPanel != null && roomSelectionPanel.activeSelf)
        {
            OnClickBackToMain();
            return;
        }

        // [5순위] 튜토리얼 종료 확인 다이얼로그 (튜토리얼 중 ESC 입력 시)
        if (_isTutorialActive)
        {
            if (tutorialExitDialog != null && tutorialExitDialog.activeSelf)
            {
                ToggleExitDialog(false); // 이미 다이얼로그가 떠 있으면 닫기 (취소)
            }
            else
            {
                ToggleExitDialog(true); // 다이얼로그 띄우기
            }
            return;
        }
    }

    private void NavigateToNextField()
    {
        GameObject current = EventSystem.current.currentSelectedGameObject;
        if (current == null) return;

        // 현재 선택된게 InputField 인지 Button 인지 확인
        Selectable currentSelectable = current.GetComponent<Selectable>();
        if (currentSelectable == null) return;

        Selectable nextSelectable = null;

        // 로그인 화면일 때
        if (loginFormPanel.activeSelf)
        {
            if (currentSelectable == loginIDInput) nextSelectable = loginPasswordInput;
            else if (currentSelectable == loginPasswordInput) nextSelectable = loginIDInput;
        }
        // 회원가입 화면일 때
        else if (signupFormPanel.activeSelf)
        {
            // [수정] 요청된 순서: 아이디-비밀번호-비확-이메일-닉네임-가입하기
            if (currentSelectable == signupIDInput) nextSelectable = signupPasswordInput;
            else if (currentSelectable == signupPasswordInput) nextSelectable = signupPasswordConfirmInput;
            else if (currentSelectable == signupPasswordConfirmInput) nextSelectable = signupEmailInput;
            else if (currentSelectable == signupEmailInput) nextSelectable = signupNicknameInput;
            else if (currentSelectable == signupNicknameInput) nextSelectable = signupConfirmButton;
            else if (currentSelectable == signupConfirmButton) nextSelectable = signupIDInput;
        }

        if (nextSelectable != null)
        {
            nextSelectable.Select();
            // 만약 InputField 라면 입력 대기 상태로 만듦
            if (nextSelectable is TMP_InputField inputField)
            {
                inputField.ActivateInputField();
            }
        }
    }

    private void SubmitCurrentForm()
    {
        if (loginFormPanel.activeSelf)
        {
            OnClickConfirmLogin();
        }
        else if (warningDialogPanel.activeSelf)
        {
            OnClickCloseWarning();
        }
        else if (signupSuccessDialogPanel.activeSelf)
        {
            OnClickConfirmSignupSuccess();
        }
        // [5순위] 튜토리얼 종료 다이얼로그 활성화 시 엔터로 확인 처리
        else if (tutorialExitDialog != null && tutorialExitDialog.activeSelf)
        {
            CloseTutorial();
        }
    }

    // ─────────────────────────────────────────
    // Tutorial System Methods
    // ─────────────────────────────────────────

    public void OpenTutorial()
    {
        if (tutorialSprites == null || tutorialSprites.Count == 0)
        {
            Debug.LogWarning("⚠️ [MainMenuUI] 등록된 튜토리얼 이미지가 없습니다.");
            return;
        }

        _isTutorialActive = true;
        _currentTutorialPage = 0;
        UpdateTutorialUI();

        if (mainPanel != null) mainPanel.SetActive(false);
        if (tutorialPanel != null) tutorialPanel.SetActive(true);
        if (tutorialExitDialog != null) tutorialExitDialog.SetActive(false);
        if (decoratedElements != null) decoratedElements.SetActive(false);
    }

    public void OnClickNextTutorialPage()
    {
        // 종료 확인 다이얼로그가 떠 있다면 클릭을 무시합니다.
        if (tutorialExitDialog != null && tutorialExitDialog.activeSelf)
        {
            return;
        }

        _currentTutorialPage++;
        if (_currentTutorialPage >= tutorialSprites.Count)
        {
            // 마지막 페이지를 넘기면 즉시 종료 확인 다이얼로그를 띄우거나 바로 종료합니다.
            // 여기선 바로 종료 처리를 하겠습니다.
            CloseTutorial();
        }
        else
        {
            UpdateTutorialUI();
        }
    }

    private void UpdateTutorialUI()
    {
        if (tutorialImageDisplay != null && _currentTutorialPage < tutorialSprites.Count)
        {
            tutorialImageDisplay.sprite = tutorialSprites[_currentTutorialPage];
        }
    }

    public void ToggleExitDialog(bool show)
    {
        if (tutorialExitDialog != null)
        {
            tutorialExitDialog.SetActive(show);
        }
    }

    public void CloseTutorial()
    {
        _isTutorialActive = false;
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
        if (tutorialExitDialog != null) tutorialExitDialog.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(true);
    }

    // [추가] 외부(RoundSelectionUI 등)에서 현재 인게임 폴더가 주도권을 가지고 있는지 확인하기 위함
    public bool IsInGameFolderActive => cookFolder != null && cookFolder.activeSelf;

    /// <summary>
    /// [핵심] SystemManager에서 방송하는 전역 메타 상태에 따라 UI 폴더를 제어합니다.
    /// </summary>
    private void HandleGlobalMetaStateChanged(MetaState newState)
    {
        Debug.Log($"🖥️ [MainMenuUI] HandleGlobalMetaStateChanged called with: {newState}");

        switch (newState)
        {
            case MetaState.Lobby:
                if (cookFolder != null && cookFolder.activeSelf)
                    OnReturnToLobby();
                else
                {
                    // 맵 선택창에서 뒤로 돌아올 때 패널을 바로잡습니다.
                    if (roundSelectionPanel != null) roundSelectionPanel.SetActive(false);
                    if (roomLobbyPanel != null) roomLobbyPanel.SetActive(true);
                }
                break;

            case MetaState.StageSelection:
                if (cookFolder != null && cookFolder.activeSelf)
                    OnReturnToLobby();
                OpenRoundSelection();
                break;

            case MetaState.ReadyConfirmation:
                if (cookFolder != null && cookFolder.activeSelf)
                    OnReturnToLobby();
                if (roundSelectionPanel != null && !roundSelectionPanel.activeSelf)
                    OpenRoundSelection();
                break;

            case MetaState.OrderDialogue:
            case MetaState.Cooking:
                OnStartGameSingleScene();

                // [추가] 인게임 진입 시 요리 BGM 재생
                if (SoundManager.Instance != null) SoundManager.Instance.PlayCookingBGM();
                break;
        }
    }

    private void OnReturnToLobby()
    {
        if (mainMenuFolder == null || cookFolder == null) return;

        MetaState currentState = SystemManager.Instance != null ? SystemManager.Instance.CurrentMetaState : MetaState.None;

        bool isActuallyRestarting = (currentState == MetaState.ReadyConfirmation);

        if (isActuallyRestarting)
        {
            // [다시하기] 결과창(배경) + 준비창(오버레이)
            mainMenuFolder.SetActive(true);
            cookFolder.SetActive(true);
            if (menuBackground != null) menuBackground.SetActive(false);

            // 라운드 선택 패널을 명시적으로 열어주어야 내부의 다이얼로그 로직이 작동합니다.
            OpenRoundSelection();
        }
        else
        {
            // [일반적 로비 복귀] 결과창 완전 종료
            mainMenuFolder.SetActive(true);
            cookFolder.SetActive(false);
            if (menuBackground != null) menuBackground.SetActive(true);

            // [추가] 로비로 돌아왔으므로 로비 BGM 재생
            if (SoundManager.Instance != null) SoundManager.Instance.PlayLobbyBGM();
        }

        if (decoratedElements != null) decoratedElements.SetActive(false);
        Debug.Log($"🏠 [MainMenuUI] 폴더 정리 완료 (다시하기 오버레이: {isActuallyRestarting})");
    }

    // --- [신규] 타이틀 및 로그인 액션 ---

    public void OnClickAnywhereOnTitle()
    {
        if (titlePanel != null) titlePanel.SetActive(false);
        if (loginPanel != null) loginPanel.SetActive(true);

        // 선택 화면을 먼저 띄웁니다.
        if (loginSelectionPanel != null) loginSelectionPanel.SetActive(true);
        if (loginFormPanel != null) loginFormPanel.SetActive(false);
        if (signupFormPanel != null) signupFormPanel.SetActive(false);
    }

    public void OnOpenLoginForm()
    {
        ClearAllInputs(); // [추가] 열 때 깨끗하게 청소!
        if (loginSelectionPanel != null) loginSelectionPanel.SetActive(false);
        if (loginFormPanel != null)
        {
            loginFormPanel.SetActive(true);
            // [추가] 첫 번째 필드에 바로 포커스!
            if (loginIDInput != null)
            {
                loginIDInput.Select();
                loginIDInput.ActivateInputField();
            }
        }

        // [추가] 로그인 입력창이 뜨면 장식 요소들을 숨깁니다.
        if (decoratedElements != null) decoratedElements.SetActive(false);
    }

    public void OnOpenSignupForm()
    {
        ClearAllInputs(); // [추가] 열 때 깨끗하게 청소!
        if (loginSelectionPanel != null) loginSelectionPanel.SetActive(false);
        if (signupFormPanel != null)
        {
            signupFormPanel.SetActive(true);
            // [추가] 첫 번째 필드에 바로 포커스!
            if (signupIDInput != null)
            {
                signupIDInput.Select();
                signupIDInput.ActivateInputField();
            }
        }

        // [추가] 회원가입창이 뜨면 장식 요소들을 숨깁니다.
        if (decoratedElements != null) decoratedElements.SetActive(false);

        // [추가] 진입 시 이전 경고 문구 청소
        if (idValidationText != null) idValidationText.text = "";
        if (passwordValidationText != null) passwordValidationText.text = "";
        if (passwordConfirmValidationText != null) passwordConfirmValidationText.text = "";
        if (nicknameValidationText != null) nicknameValidationText.text = "";
        if (emailCheckText != null) emailCheckText.text = "";
        isEmailVerified = false;
        isVerificationStarted = false;
        if (emailVerificationCoroutine != null)
        {
            StopCoroutine(emailVerificationCoroutine);
            emailVerificationCoroutine = null;
        }
        isIDChecked = false;
        isNicknameChecked = false;
    }

    public void OnBackToLoginSelection()
    {
        ClearAllInputs(); // [전원 청소]
        if (loginFormPanel != null) loginFormPanel.SetActive(false);
        if (signupFormPanel != null) signupFormPanel.SetActive(false);
        if (loginSelectionPanel != null) loginSelectionPanel.SetActive(true);

        // [추가] 다시 선택창으로 돌아오면 장식 요소들을 다시 보여줍니다.
        if (decoratedElements != null) decoratedElements.SetActive(true);

        // [추가] 돌아올 때 검증 문구 초기화
        if (idValidationText != null) idValidationText.text = "";
        if (passwordValidationText != null) passwordValidationText.text = "";
        if (passwordConfirmValidationText != null) passwordConfirmValidationText.text = "";
        if (nicknameValidationText != null) nicknameValidationText.text = "";
        if (emailCheckText != null) emailCheckText.text = "";
        isEmailVerified = false;
        isVerificationStarted = false;
        if (emailVerificationCoroutine != null)
        {
            StopCoroutine(emailVerificationCoroutine);
            emailVerificationCoroutine = null;
        }
        isIDChecked = false;
        isNicknameChecked = false;
    }

    /// <summary>
    /// [신규] 모든 입력 필드를 깨끗하게 비웁니다.
    /// </summary>
    private void ClearAllInputs()
    {
        // 경고창도 확실하게 끕니다.
        OnClickCloseWarning();

        // 로그인 창 필드
        if (loginIDInput != null) loginIDInput.text = "";
        if (loginPasswordInput != null) loginPasswordInput.text = "";

        // 회원가입 창 필드
        if (signupIDInput != null) signupIDInput.text = "";
        if (signupPasswordInput != null) signupPasswordInput.text = "";
        if (signupPasswordConfirmInput != null) signupPasswordConfirmInput.text = "";
        if (signupNicknameInput != null) signupNicknameInput.text = "";
        if (signupEmailInput != null) signupEmailInput.text = "";
    }

    public void OnClickConfirmLogin()
    {
        string id = loginIDInput != null ? loginIDInput.text.Trim() : "";
        string pw = loginPasswordInput != null ? loginPasswordInput.text.Trim() : "";

        // 입력창이 비었는지 먼저 체크! 
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pw))
        {
            ShowWarningModal("아이디와 비밀번호를 모두 입력해주세요.");
            return;
        }

        LoginRequest data = new LoginRequest
        {
            loginId = id,
            password = pw
        };

        // 서버에 진짜로 물어보기! (이게 핵심!)
        StartCoroutine(AuthManager.Instance.Login(data, (success, response) => {
            if (success)
            {
                // [수정] 현재 세션 로그인 정보 기록 (씬 리셋 되어도 유지됨)
                CurrentSessionUserID = id;
                CurrentSessionNickname = response.nickname; // [추가] 닉네임 저장
                
                // [추가] 성공 시 아이디 저장 (아이디 기억하기 - PlayerPrefs)
                PlayerPrefs.SetString("SavedLoginID", id);
                PlayerPrefs.Save();

                // 서버가 "맞아, 가입된 사람이야!"라고 할 때만 입장!
                ClearAllInputs(); // [추가] 입장 전 모든 기밀 정보 파기!
                loginPanel.SetActive(false);
                mainPanel.SetActive(true);
                decoratedElements.SetActive(true);
            }
            else
            {
                ShowWarningModal("아이디 또는 비밀번호가 틀렸습니다.");
                // 비번 틀림, 아이디 없음, 혹은 "이메일 미인증"일 때!
                Debug.LogError("로그인 실패!");
            }
        }));
    }

    // 경고창을 띄워주는 친절한 함수
    private void ShowWarningModal(string message)
    {
        if (warningDialogPanel != null)
        {
            warningDialogPanel.SetActive(true);
            if (warningText != null) warningText.text = message;
        }
    }

    // 경고창의 [확인] 버튼을 눌렀을 때 닫는 함수
    public void OnClickCloseWarning()
    {
        if (warningDialogPanel != null) warningDialogPanel.SetActive(false);
    }

    public void OnClickConfirmSignup()
    {
        bool hasError = false;

        // 1. 아이디 확인
        string id = signupIDInput != null ? signupIDInput.text : "";

        if (string.IsNullOrWhiteSpace(id))
        {
            SetValidationText(idValidationText, "<color=red>아이디를 입력해주세요.</color>");
            hasError = true;
        }
        // DB와 동일하게 영문+숫자 필수 포함, 6~10자
        else if (!Regex.IsMatch(id, @"^(?=.*[a-zA-Z])(?=.*[0-9])[a-zA-Z0-9]{6,10}$"))
        {
            SetValidationText(idValidationText, "<color=red>아이디는 영문과 숫자를 포함해 6~10자여야 합니다.</color>");
            hasError = true;
        }
        else if (!isIDChecked)
        {
            SetValidationText(idValidationText, "<color=red>중복 확인이 필요합니다.</color>");
            hasError = true;
        }

        // 2. 닉네임 확인
        string nickname = signupNicknameInput != null ? signupNicknameInput.text : "";
        if (string.IsNullOrEmpty(nickname))
        {
            if (nicknameValidationText != null) nicknameValidationText.text = "<color=red>닉네임을 입력해주세요.</color>";
            hasError = true;
        }
        else if (nickname.Length < 2 || nickname.Length > 10)
        {
            if (nicknameValidationText != null) nicknameValidationText.text = "<color=red>닉네임은 2~10자 이내여야 합니다.</color>";
            hasError = true;
        }
        else if (nickname.StartsWith(" ") || nickname.EndsWith(" "))
        {
            if (nicknameValidationText != null) nicknameValidationText.text = "<color=red>닉네임의 맨 앞과 맨 뒤에는 공백을 사용할 수 없습니다.</color>";
            hasError = true;
        }
        else if (!isNicknameChecked)
        {
            if (nicknameValidationText != null) nicknameValidationText.text = "<color=red>중복 확인이 필요합니다.</color>";
            hasError = true;
        }

        // 3. 비밀번호 확인
        string pw = signupPasswordInput != null ? signupPasswordInput.text : "";
        string pwConfirm = signupPasswordConfirmInput != null ? signupPasswordConfirmInput.text : "";

        // 영문+숫자+특수문자 필수 포함, 8~20자 
        string pwPattern = @"^(?=.*[a-zA-Z])(?=.*[0-9])(?=.*[!@#$%^&*()_+{}:""<>?|\[\]\\;',./`~-]).{8,20}$";

        if (string.IsNullOrWhiteSpace(pw))
        {
            SetValidationText(passwordValidationText, "<color=red>비밀번호를 입력해주세요.</color>");
            hasError = true;
        }
        else if (pw == id)
        {
            SetValidationText(passwordValidationText, "<color=red>아이디와 비밀번호는 같을 수 없습니다.</color>");
            hasError = true;
        }
        else if (!Regex.IsMatch(pw, pwPattern))
        {
            SetValidationText(passwordValidationText, "<color=red>영문, 숫자, 특수문자를 모두 포함해 8~20자여야 합니다.</color>");
            hasError = true;
        }
        // 비밀번호 일치 확인
        else if (pw != pwConfirm)
        {
            SetValidationText(passwordConfirmValidationText, "<color=red>비밀번호가 일치하지 않습니다.</color>");
            hasError = true;
        }



        // 4. 이메일 인증 확인
        if (!isEmailVerified)
        {
            if (emailCheckText != null) emailCheckText.text = "<color=red>이메일 인증이 필요합니다.</color>";
            hasError = true;
        }

        if (hasError)
        {
            Debug.Log("⚠️ [MainMenuUI] 회원가입 검증 실패");
            return;
        }

        Debug.Log("🔘 [MainMenuUI] 모든 검증 통과! 서버로 가입 요청을 보냅니다.");

        // 1. 서버로 보낼 선물 상자(데이터) 만들기
        SignupRequest data = new SignupRequest
        {
            loginId = signupIDInput.text,
            password = signupPasswordInput.text,
            passwordConfirm = signupPasswordConfirmInput.text,
            email = signupEmailInput.text,
            nickname = signupNicknameInput.text
        };

        // 2. AuthManager 팀장님 출동!
        StartCoroutine(AuthManager.Instance.Register(data, (success, message) => {
            if (success)
            {
                // ✅ 서버 가입 성공! (메일 발송됨)
                if (signupSuccessDialogPanel != null)
                {
                    signupSuccessDialogPanel.SetActive(true);
                    if (signupSuccessText != null)
                        signupSuccessText.text = "회원가입이 완료되었습니다!";
                }
            }
            else
            {
                // ❌ 서버 가입 실패! (중복 아이디가 생겼거나 서버 에러 등)
                // 여기에 "아이디 비번 틀렸다"는 로그인용 문구 대신, 서버가 준 진짜 이유(message)를 띄워줘!
                ShowWarningModal($"회원가입에 실패했어요: {message}");
                Debug.LogError($"회원가입 실패 사유: {message}");
            }
        }));
    }

    private void SetValidationText(TMP_Text textComponent, string message)
    {
        if (textComponent != null) textComponent.text = message;
    }

    public void OnClickConfirmSignupSuccess()
    {
        if (signupSuccessDialogPanel != null) signupSuccessDialogPanel.SetActive(false);
        OnBackToLoginSelection();
    }

    // [임시 기능] 입력 중인 대문자를 소문자로 즉시 바꾸고, 그 이외의 특수문자/한글 등은 차단합니다.
    private char ValidateIDInput(string text, int charIndex, char addedChar)
    {
        // 1. 영어 대문자면 소문자로 바꿈
        if (addedChar >= 'A' && addedChar <= 'Z')
        {
            return char.ToLower(addedChar);
        }

        // 2. 영어 소문자나 숫자만 허용
        if ((addedChar >= 'a' && addedChar <= 'z') || (addedChar >= '0' && addedChar <= '9'))
        {
            return addedChar;
        }

        // 3. 그 외 글자(한글, 특수문자, 공백 등)는 아예 무시(\0)
        return '\0';
    }

    private void OnIDValueChanged(string value)
    {
        // 실시간 안내 문구 초기화 (입력 중에는 경고를 지움)
        if (idValidationText != null) idValidationText.text = "";
        isIDChecked = false;
    }

    private void OnNicknameValueChanged(string value)
    {
        if (nicknameValidationText != null) nicknameValidationText.text = "";
        isNicknameChecked = false;
    }

    public void OnClickCheckIDDuplicate()
    {
        if (signupIDInput == null || idValidationText == null) return;

        string id = signupIDInput.text;

        // 1. 글자 수 검사 (6~10자)
        if (!Regex.IsMatch(id, @"^(?=.*[a-zA-Z])(?=.*[0-9])[a-zA-Z0-9]{6,10}$"))
        {
            StopAllCoroutines(); // 기존에 돌던 메시지 지우기 예약 취소
            StartCoroutine(ShowTemporaryMessage(idValidationText, "<color=red>영문과 숫자를 포함해 6~10자로 입력해주세요!</color>"));
            isIDChecked = false;
            return;
        }

        // 2. 규칙에 맞는다면 이제 서버에 진짜 중복인지 물어보기!
        idValidationText.text = "확인 중... ";
        StartCoroutine(AuthManager.Instance.CheckID(id, (success, message) => {
            if (success)
            {
                idValidationText.text = "<color=green>사용 가능한 아이디입니다!</color>";
                isIDChecked = true;
            }
            else
            {
                idValidationText.text = "<color=red>이미 사용 중인 아이디입니다.</color>";
                isIDChecked = false;
            }
        }));

        // 3. 서버 중복 체크 (임시 로직)
        Debug.Log($"🔍 [MainMenuUI] 아이디 중복 체크 시도: {id}");

    }

    public void OnClickCheckNicknameDuplicate()
    {
        if (signupNicknameInput == null || nicknameValidationText == null) return;

        string nickname = signupNicknameInput.text;

        // 1. 글자 수 검사 (2~10자)
        if (string.IsNullOrEmpty(nickname) || nickname.Length < 2 || nickname.Length > 10)
        {
            nicknameValidationText.text = "<color=red>닉네임은 2~10자 이내여야 합니다.</color>";
            return;
        }

        // 2. 공백 검사
        if (nickname.StartsWith(" ") || nickname.EndsWith(" "))
        {
            nicknameValidationText.text = "<color=red>닉네임의 맨 앞과 맨 뒤에는 공백을 사용할 수 없습니다.</color>";
            return;
        }

        // 3. 서버 중복 체크
        nicknameValidationText.text = "확인 중...";
        StartCoroutine(AuthManager.Instance.CheckNickname(nickname, (success, message) => {
            if (success)
            {
                nicknameValidationText.text = "<color=green>사용 가능한 닉네임입니다!</color>";
                isNicknameChecked = true;
            }
            else
            {
                nicknameValidationText.text = $"<color=red>{message}</color>";
                isNicknameChecked = false;
            }
        }));
    }

    public void OnClickFindAccount()
    {
        // [신규] 아이디/비밀번호 찾기 버튼 클릭 시 로그를 남깁니다.
        Debug.Log("🔍 [MainMenuUI] 아이디/비밀번호 찾기 버튼 클릭됨 (기능 구현 예정)");
    }

    public void OnClickGuestLogin()
    {
        AuthSession.Clear();
        // [추가] 게스트 모드 로그인 정보 기록 (세션 한정)
        CurrentSessionUserID = "Guest_" + UnityEngine.Random.Range(1000, 9999).ToString();
        Debug.Log($"👤 [MainMenuUI] 게스트 로그인 시도: {CurrentSessionUserID}");

        // [신규] 게스트는 전적이 없으므로 초기화하여 1라운드만 열리게 합니다.
        AuthManager.InitializeProgression(null);

        if (loginPanel != null) loginPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(true);
        
        // 입력 필드 청소
        ClearAllInputs();
    }

    public void OnClickLogout()
    {
        void CompleteLogout()
        {
            CurrentSessionUserID = null;
            CurrentSessionNickname = null;
            AuthSession.Clear();
            AuthManager.InitializeProgression(null);
            UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        }

        if (AuthManager.Instance != null)
            StartCoroutine(AuthManager.Instance.Logout(CompleteLogout));
        else
            CompleteLogout();
    }

    // [신규] 비밀번호 입력 필터 (한글/공백 차단)
    private char ValidatePasswordInput(string text, int charIndex, char addedChar)
    {
        // 1. 영어(대소문자), 숫자, 특수문자만 허용
        // 특수문자 범위: 33(!) ~ 47(/), 58(:) ~ 64(@), 91([) ~ 96(`), 123({) ~ 126(~)
        if ((addedChar >= 'a' && addedChar <= 'z') ||
            (addedChar >= 'A' && addedChar <= 'Z') ||
            (addedChar >= '0' && addedChar <= '9') ||
            (addedChar >= 33 && addedChar <= 47) ||
            (addedChar >= 58 && addedChar <= 64) ||
            (addedChar >= 91 && addedChar <= 96) ||
            (addedChar >= 123 && addedChar <= 126))
        {
            return addedChar;
        }

        // 2. 그 외(한글, 공백 등) 차단
        return '\0';
    }

    private void ResetPasswordValidationText()
    {
        if (passwordValidationText != null) passwordValidationText.text = "";
    }

    // [신규] 비밀번호 확인 칸 실시간 일치 검사
    private void OnPasswordConfirmValueChanged(string value)
    {
        if (signupPasswordConfirmInput == null || string.IsNullOrEmpty(value))
        {
            if (passwordConfirmValidationText != null) passwordConfirmValidationText.text = "";
            return;
        }

        if (signupPasswordInput.text == value)
        {
            if (passwordConfirmValidationText != null) passwordConfirmValidationText.text = "<color=green>비밀번호 확인이 완료되었습니다.</color>";
        }
        else
        {
            if (passwordConfirmValidationText != null) passwordConfirmValidationText.text = "<color=red>비밀번호가 일치하지 않습니다.</color>";
        }
    }

    public void OnClickSendVerificationEmail()
    {
        if (signupEmailInput == null || emailCheckText == null) return;

        string email = signupEmailInput.text;
        isEmailVerified = false;

        // 이메일 형식 검사 (간단한 체크: @와 . 포함 여부)
        if (string.IsNullOrEmpty(email) || !email.Contains("@") || !email.Contains("."))
        {
            emailCheckText.text = "<color=red>올바른 이메일 형식을 입력해주세요.</color>";
            return;
        }

        emailCheckText.text = "인증메일이 발송되었습니다...";

        // [수정] 실제 서버에 메일 발송 요청!
        StartCoroutine(AuthManager.Instance.SendVerificationEmail(email, (success, msg) => {
            if (success)
            {
                emailCheckText.text = "<color=green>인증메일이 발송되었습니다. 이메일을 확인해주세요.</color>";
                isVerificationStarted = true; // 인증 시작됨!
                
                // 3초마다 서버에 물어보기 시작!
                if (emailVerificationCoroutine != null) StopCoroutine(emailVerificationCoroutine);
                emailVerificationCoroutine = StartCoroutine(CheckVerificationLoop());
            }
            else
            {
                emailCheckText.text = $"<color=red>{msg}</color>";
            }
        }));
    }

    private IEnumerator CheckVerificationLoop()
    {
        while (!isEmailVerified)
        {
            // 3초마다 서버에 물어보기!
            yield return new WaitForSeconds(3.0f);
            StartCoroutine(AuthManager.Instance.CheckEmailVerified(signupEmailInput.text, (verified) => {
                if (verified)
                {
                    isEmailVerified = true;
                    emailCheckText.text = "<color=green>인증이 완료되었습니다.</color>";
                }
            }));
        }
    }

    // 메시지를 띄우고 일정 시간 뒤에 지워주는 마법의 코루틴
    private IEnumerator ShowTemporaryMessage(TMP_Text textComponent, string message, float delay = 3.0f)
    {
        if (textComponent == null) yield break;

        textComponent.text = message; // 메시지 표시
        yield return new WaitForSeconds(delay); // 3초 기다리기
        textComponent.text = ""; // 메시지 지우기 🪄
    }

    // --- 버튼 액션 ---

    public void OnClickStart()
    {
        if (mainPanel) mainPanel.SetActive(false);
        if (roomSelectionPanel) roomSelectionPanel.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(false);
    }

    // [방 생성] 네트워킹 로직 포함
    public async void OnClickCreateRoom()
    {
        currentRoomCode = Random.Range(100000, 1000000).ToString();

        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (loadingStatusText != null) loadingStatusText.text = "방을 만드는 중입니다...";

        try
        {
            if (NetworkLauncher.Instance != null)
            {
                // [수정] true를 인자로 전달하여 '새 방 생성(인원2명)'으로 작동하게 합니다.
                bool success = await NetworkLauncher.Instance.JoinOrCreateRoom(currentRoomCode, true);
                Debug.Log($"🌐 [Lobby] JoinOrCreateRoom 완료. 결과: {success}");

                if (success)
                {
                    UpdateLobbyUI();
                    if (roomSelectionPanel) roomSelectionPanel.SetActive(false);
                    if (roomLobbyPanel) roomLobbyPanel.SetActive(true);
                    if (menuBackground != null) menuBackground.SetActive(false);
                }
            }
        }
        finally
        {
            if (loadingPanel != null) loadingPanel.SetActive(false);
        }
    }

    public void OnClickJoinRoom()
    {
        if (roomSelectionPanel) roomSelectionPanel.SetActive(false);
        if (joinRoomPanel) joinRoomPanel.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(false);

        if (roomCodeInputField != null) 
        {
            roomCodeInputField.text = "";
            // [추가] 바로 입력을 시작할 수 있도록 포커스 활성화
            roomCodeInputField.Select();
            roomCodeInputField.ActivateInputField();
        }
        if (joinRoomErrorText != null) joinRoomErrorText.gameObject.SetActive(false);
    }

    // [방 접속] 네트워킹 로직 포함
    public async void OnClickConfirmJoin()
    {
        string code = roomCodeInputField != null ? roomCodeInputField.text : "";
        if (!Regex.IsMatch(code, @"^\d{6}$"))
        {
            if (joinRoomErrorText != null)
            {
                joinRoomErrorText.text = "방 코드는 숫자 6자리입니다.";
                joinRoomErrorText.gameObject.SetActive(true);
            }
            return;
        }

        currentRoomCode = code;
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (loadingStatusText != null) loadingStatusText.text = $"{currentRoomCode}번 방에 접속 중입니다...";

        try
        {
            if (NetworkLauncher.Instance != null)
            {
                bool success = await NetworkLauncher.Instance.JoinOrCreateRoom(currentRoomCode);
                Debug.Log($"🌐 [Join] 접속 시도 완료. 결과: {success}");

                if (success)
                {
                    UpdateLobbyUI();
                    if (joinRoomPanel) joinRoomPanel.SetActive(false);
                    if (roomLobbyPanel) roomLobbyPanel.SetActive(true);
                    if (menuBackground != null) menuBackground.SetActive(false);
                }
                else
                {
                    if (joinRoomErrorText != null) joinRoomErrorText.gameObject.SetActive(true);
                }
            }
        }
        finally
        {
            if (loadingPanel != null) loadingPanel.SetActive(false);
        }
    }


    public void OnClickBackFromJoin()
    {
        if (joinRoomPanel) joinRoomPanel.SetActive(false);
        if (roomSelectionPanel) roomSelectionPanel.SetActive(true);
    }

    public void OnClickBackToMain()
    {
        if (roomSelectionPanel) roomSelectionPanel.SetActive(false);
        if (mainPanel) mainPanel.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(true);
    }

    public void OnClickSettings()
    {
        if (mainPanel) mainPanel.SetActive(false);
        if (settingsPanel) settingsPanel.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(false);
    }

    public void OnClickApply()
    {
        settingsPanelComponent?.Apply();
        OnCloseSettings();
    }

    public void OnClickCancel()
    {
        settingsPanelComponent?.Cancel();
        OnCloseSettings();
    }

    public void OpenRoundSelection()
    {
        if (roomLobbyPanel) roomLobbyPanel.SetActive(false);
        if (roundSelectionPanel) roundSelectionPanel.SetActive(true);
    }

    public void OnCloseRoundSelection()
    {
        if (roundSelectionPanel) roundSelectionPanel.SetActive(false);
        if (roomLobbyPanel) roomLobbyPanel.SetActive(true);
    }

    public void OnCloseLobby()
    {
        if (roomLobbyPanel) roomLobbyPanel.SetActive(false);
        if (roomSelectionPanel) roomSelectionPanel.SetActive(true);
        if (menuBackground != null) menuBackground.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(false);
    }

    public void OnStartGameSingleScene()
    {
        // 1. UI 폴더 교체
        if (mainMenuFolder != null) mainMenuFolder.SetActive(false);
        if (cookFolder != null) cookFolder.SetActive(true);

        // 2. 개별 패널들도 확실히 끕니다 (메인메뉴 폴더 외부에 있을 경우 대비)
        if (roomSelectionPanel != null) roomSelectionPanel.SetActive(false);
        if (joinRoomPanel != null) joinRoomPanel.SetActive(false);
        if (roomLobbyPanel != null) roomLobbyPanel.SetActive(false);
        if (roundSelectionPanel != null) roundSelectionPanel.SetActive(false);

        // 3. 만약 배경이나 데코 소품이 켜져있다면 그것도 정리합니다.
        if (menuBackground != null) menuBackground.SetActive(false);
        if (decoratedElements != null) decoratedElements.SetActive(false);

        Debug.Log($"🚀 [MainMenuUI] OnStartGameSingleScene() - Folder Swap: MainMenu({mainMenuFolder?.activeSelf}) -> Cook({cookFolder?.activeSelf})");

        // 4. [중요] 역할(Role)에 맞는 샌드박스 환경을 켭니다.
        // [수정] 로컬 PlayerPrefs 대신 네트워크 상의 내 역할을 읽어옵니다.
        int myRole = LobbyPlayer.Local?.SelectedRole ?? 2;

        Debug.Log($"🎭 [MainMenuUI] 내 네트워크 역할({myRole})에 맞는 환경을 활성화합니다.");

        if (myRole == 1) // 레시피 마스터
        {
            if (p2Sandbox != null) p2Sandbox.SetActive(true);
            if (p1Sandbox != null) p1Sandbox.SetActive(false);
        }
        else // 요리사 (기본값)
        {
            if (p1Sandbox != null) p1Sandbox.SetActive(true);
            if (p2Sandbox != null) p2Sandbox.SetActive(false);
        }

        // 5. [중요] 새로 켜진 Cook 폴더 내부의 UI들이 현재 상태를 즉시 반영하도록 합니다.
        if (cookFolder != null)
        {
            EvaluationUI evalUI = cookFolder.GetComponentInChildren<EvaluationUI>(true);
            if (evalUI != null)
            {
                evalUI.RefreshUI();
                Debug.Log("🎨 [MainMenuUI] EvaluationUI를 새로고침했습니다.");
            }
        }
    }

    public void OnCloseSettings()
    {
        if (settingsPanel) settingsPanel.SetActive(false);
        if (mainPanel) mainPanel.SetActive(true);
        if (decoratedElements != null) decoratedElements.SetActive(true);
    }

    public void OnClickQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }

    // [신규] MainPanel 에 있는 MyinfoButton 클릭 시 호출하여 정보를 열고 갱신합니다.
    public void OnClickOpenMyInfo()
    {
        if (myInfoPanel != null)
        {
            myInfoPanel.SetActive(true);
            
            // [추가] 열릴 때 정보를 새로고침합니다.
            ProfileUI profileUI = myInfoPanel.GetComponent<ProfileUI>();
            if (profileUI != null) profileUI.RefreshProfile();
        }
    }

    // [신규] MyinfoPanel 에 있는 내 닫기 버튼을 클릭할 때 사용합니다.
    public void OnClickCloseMyInfo()
    {
        if (myInfoPanel != null) myInfoPanel.SetActive(false);
    }

    private void UpdateLobbyUI()
    {
        if (lobbyRoomCodeText != null)
        {
            lobbyRoomCodeText.text = "방 코드 : " + currentRoomCode;
        }
    }

}
