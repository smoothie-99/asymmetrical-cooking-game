using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class InGameMenuUI : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject escMenuPanel;      // ESC 메뉴
    [SerializeField] private GameObject pausePanel;        // 일시정지 패널
    [SerializeField] private GameObject settingsPanel;     // 환경설정 패널
    [SerializeField] private GameObject exitWaitingPanel;   // [추가] 통합 이동 안내 패널

    [Header("Dynamic Menu Elements")]
    [SerializeField] private GameObject pauseButtonObj;    // 일시정지 버튼 오브젝트
    [SerializeField] private TMP_Text exitWaitingText;       // [추가] 안내 텍스트

    [Header("Restart Confirmation UI")]
    [SerializeField] private GameObject restartReadyPanel; // 다시하기 확인 다이얼로그
    [SerializeField] private TMP_Text restartStatusText;    
    [SerializeField] private Image restartRecipeBorder;    
    [SerializeField] private Image restartCookingBorder;    
    [SerializeField] private Button restartReadyButton;    
    [SerializeField] private Button restartCancelButton;   

    [Header("Settings UI")]
    [SerializeField] private SettingsPanel settingsPanelComponent;

    [Header("Local Confirm Dialog")]
    [SerializeField] private GameObject localConfirmPanel;
    [SerializeField] private TMP_Text localConfirmText;
    private System.Action _onConfirmAction;

    public static InGameMenuUI Instance { get; private set; } 
    public static bool IsMenuOpen { get; private set; } = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        IsMenuOpen = false;
        
        if (restartReadyButton != null) restartReadyButton.onClick.AddListener(OnClickRestartReady);
        if (restartCancelButton != null) restartCancelButton.onClick.AddListener(OnClickRestartCancel);
    }

    private void OnEnable()
    {
        SystemManager.SubscribeWhenReady(HandleGlobalMetaStateChanged);
        LobbyPlayer.OnAnyStateChanged -= HandleLobbyPlayerStateChanged;
        LobbyPlayer.OnAnyStateChanged += HandleLobbyPlayerStateChanged;
        
        GlobalNetworkState.OnPauseStateChanged -= SyncPauseState;
        GlobalNetworkState.OnPauseStateChanged += SyncPauseState;
    }

    private void OnDisable()
    {
        SystemManager.Unsubscribe(HandleGlobalMetaStateChanged);
        LobbyPlayer.OnAnyStateChanged -= HandleLobbyPlayerStateChanged;
        GlobalNetworkState.OnPauseStateChanged -= SyncPauseState;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (localConfirmPanel != null && localConfirmPanel.activeSelf) OnClickCancelConfirm();
            else if (settingsPanel.activeSelf) OnClickCancel();
            else if (restartReadyPanel.activeSelf) OnClickRestartCancel();
            else if (pausePanel.activeSelf) { /* Ignore */ }
            else ToggleESCMenu();
        }
    }

    private void HandleGlobalMetaStateChanged(MetaState newState)
    {
        if (newState == MetaState.OrderDialogue || newState == MetaState.StageSelection)
        {
            if (pausePanel != null) pausePanel.SetActive(false);
            if (restartReadyPanel != null) restartReadyPanel.SetActive(false);
            if (escMenuPanel != null) escMenuPanel.SetActive(false);
            if (exitWaitingPanel != null) exitWaitingPanel.SetActive(false); 
            
            if (localConfirmPanel != null)
            {
                localConfirmPanel.SetActive(false);
                _onConfirmAction = null;
            }

            IsMenuOpen = false;
        }

        if (escMenuPanel != null && newState == MetaState.ReadyConfirmation) escMenuPanel.SetActive(false);
        UpdateCursorState(newState);
    }

    private void HandleLobbyPlayerStateChanged()
    {
        var runner = NetworkLauncher.Instance?.Runner;
        if (runner != null && runner.IsRunning)
        {
            bool anyoneVoting = false;
            foreach (var p in runner.ActivePlayers)
            {
                var lp = LobbyPlayer.Get(p);
                if (lp != null && lp.IsRestartVoting) { anyoneVoting = true; break; }
            }

            MetaState current = SystemManager.Instance != null ? SystemManager.Instance.CurrentMetaState : MetaState.None;
            bool canOpen = (current == MetaState.OrderDialogue || current == MetaState.Cooking || 
                            current == MetaState.Feedback || current == MetaState.Result);

            if (anyoneVoting && !restartReadyPanel.activeSelf && canOpen) OpenRestartPanel();
            else if (!anyoneVoting && restartReadyPanel.activeSelf) CloseRestartPanel();    
        }

        if (restartReadyPanel != null && restartReadyPanel.activeSelf)
        {
            CheckNetworkedReadyState();
        }
    }

    private void CheckNetworkedReadyState()
    {
        var runner = NetworkLauncher.Instance?.Runner;
        if (runner == null || !runner.IsRunning) return;

        bool allReady = true;
        int playerCount = 0;

        if (restartRecipeBorder != null) restartRecipeBorder.gameObject.SetActive(false);
        if (restartCookingBorder != null) restartCookingBorder.gameObject.SetActive(false);

        foreach (var pRef in runner.ActivePlayers)
        {
            var lp = LobbyPlayer.Get(pRef);
            if (lp == null) continue;
            playerCount++;

            int role = lp.SelectedRole;
            bool ready = lp.IsReady;

            if (ready)
            {
                if (role == 1 && restartRecipeBorder != null) restartRecipeBorder.gameObject.SetActive(true);
                if (role == 2 && restartCookingBorder != null) restartCookingBorder.gameObject.SetActive(true);
            }
            
            if (!ready) allReady = false;
        }

        if (restartStatusText != null)
        {
            int stage = (GlobalNetworkState.Instance != null) ? GlobalNetworkState.Instance.SelectedStage : 1;
            restartStatusText.text = "STAGE " + stage;
        }

        if (restartReadyButton != null)
        {
            var btnText = restartReadyButton.GetComponentInChildren<TMP_Text>();
            if (btnText != null)
            {
                LobbyPlayer local = LobbyPlayer.Local;
                btnText.text = (local != null && local.IsReady) ? "준비 취소" : "준비";
            }
        }

        if (playerCount >= 2 && allReady && runner.IsSharedModeMasterClient)
        {
            if (!IsInvoking("FinalizeRestart")) Invoke("FinalizeRestart", 1.0f);
        }
    }

    private void FinalizeRestart()
    {
        if (LobbyPlayer.Local != null) LobbyPlayer.Local.RPC_GlobalFinalizeRestart();
        LobbyPlayer.Local?.RPC_RequestLobbyMenuState(3); // 3: OrderDialogue
    }
    
    private float _lastClickTime = 0f;

    public void OnClickRestartReady()
    {
        if (Time.time - _lastClickTime < 0.5f) return;
        _lastClickTime = Time.time;

        if (LobbyPlayer.Local != null)
        {
            bool nextReady = !LobbyPlayer.Local.IsReady;
            LobbyPlayer.Local.SetReady(nextReady);
            CheckNetworkedReadyState();
        }
    }

    public void OpenRestartPanel()
    {
        if (restartReadyPanel != null)
        {
            if (LobbyPlayer.Local != null) LobbyPlayer.Local.SetReady(false);
            restartReadyPanel.SetActive(true);
            IsMenuOpen = true;
            CheckNetworkedReadyState();
            UpdateCursorState(SystemManager.Instance != null ? SystemManager.Instance.CurrentMetaState : MetaState.Cooking);
        }
    }

    public void CloseRestartPanel()
    {
        if (restartReadyPanel != null)
        {
            restartReadyPanel.SetActive(false);
            LobbyPlayer.Local?.RPC_SetRestartVoting(false);
            bool isPaused = GlobalNetworkState.Instance != null && GlobalNetworkState.Instance.IsPaused;
            if (!isPaused) IsMenuOpen = false;
            UpdateCursorState(SystemManager.Instance != null ? SystemManager.Instance.CurrentMetaState : MetaState.Cooking);
        }
    }

    public void OnClickRestartCancel()
    {
        LobbyPlayer.Local?.RPC_GlobalCancelRestart();
    }

    public void ToggleESCMenu()
    {
        IsMenuOpen = !IsMenuOpen;
        if (escMenuPanel != null)
        {
            escMenuPanel.SetActive(IsMenuOpen);
            if (IsMenuOpen && pauseButtonObj != null)
            {
                MetaState current = SystemManager.Instance != null ? SystemManager.Instance.CurrentMetaState : MetaState.None;
                pauseButtonObj.SetActive(current == MetaState.Cooking);
            }
        }
        if (SystemManager.Instance != null) UpdateCursorState(SystemManager.Instance.CurrentMetaState);
    }

    private void UpdateCursorState(MetaState state)
    {
        if (GlobalNetworkState.Instance != null && GlobalNetworkState.Instance.IsLocalRecipeMaster())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // [중요] 확인 창이 떠있을 때도 커서가 열려야 합니다.
        bool isAnyMenuOpen = (escMenuPanel != null && escMenuPanel.activeSelf) || 
                            (pausePanel != null && pausePanel.activeSelf) || 
                            (settingsPanel != null && settingsPanel.activeSelf) ||
                            (restartReadyPanel != null && restartReadyPanel.activeSelf) ||
                            (localConfirmPanel != null && localConfirmPanel.activeSelf) || // 공용 확인창 체크
                            (exitWaitingPanel != null && exitWaitingPanel.activeSelf) || 
                            (GlobalNetworkState.Instance != null && GlobalNetworkState.Instance.IsPaused);
        
        if (isAnyMenuOpen || state != MetaState.Cooking)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void SyncPauseState(bool isPaused)
    {
        if (pausePanel != null) pausePanel.SetActive(isPaused);
        if (!isPaused && localConfirmPanel != null && localConfirmPanel.activeSelf) OnClickCancelConfirm();
        if (!isPaused && (escMenuPanel == null || !escMenuPanel.activeSelf)) IsMenuOpen = false;
        if (SystemManager.Instance != null) UpdateCursorState(SystemManager.Instance.CurrentMetaState);
    }

    public void OnClickPause()
    {
        if (GlobalNetworkState.Instance != null)
        {
            if (GlobalNetworkState.Instance.Object.HasStateAuthority) GlobalNetworkState.Instance.SetPauseInternal(true);
            else GlobalNetworkState.Instance.RPC_RequestPause(true);
        }
        if (escMenuPanel != null) escMenuPanel.SetActive(false);
        IsMenuOpen = true; 
    }

    public void OnClickResumeProgress() => OnClickResume();

    public void OnClickResume()
    {
        if (GlobalNetworkState.Instance != null)
        {
            if (GlobalNetworkState.Instance.Object.HasStateAuthority) GlobalNetworkState.Instance.SetPauseInternal(false);
            else GlobalNetworkState.Instance.RPC_RequestPause(false);
        }
        if (IsMenuOpen)
        {
            IsMenuOpen = false;
            if (escMenuPanel != null) escMenuPanel.SetActive(false);
            if (SystemManager.Instance != null) UpdateCursorState(SystemManager.Instance.CurrentMetaState);
        }
    }

    public void OnClickSettings() => settingsPanel.SetActive(true);
    public void OnClickApply() { settingsPanelComponent?.Apply(); settingsPanel.SetActive(false); ToggleESCMenu(); }
    public void OnClickCancel() { settingsPanelComponent?.Cancel(); settingsPanel.SetActive(false); ToggleESCMenu(); }
    
    // --- [공용] 확인 창 API ---
    public void OpenConfirmDialog(string message, System.Action action)
    {
        if (localConfirmPanel != null)
        {
            if (localConfirmText != null) localConfirmText.text = message;
            _onConfirmAction = action;
            localConfirmPanel.SetActive(true);
            localConfirmPanel.transform.SetAsLastSibling();
            UpdateCursorState(SystemManager.Instance != null ? SystemManager.Instance.CurrentMetaState : MetaState.Cooking);
        }
    }

    public void OnClickConfirm()
    {
        _onConfirmAction?.Invoke();
        _onConfirmAction = null;
        if (localConfirmPanel != null) localConfirmPanel.SetActive(false);
        if (SystemManager.Instance != null) UpdateCursorState(SystemManager.Instance.CurrentMetaState);
    }

    public void OnClickCancelConfirm()
    {
        _onConfirmAction = null;
        if (localConfirmPanel != null) localConfirmPanel.SetActive(false);
        if (SystemManager.Instance != null) UpdateCursorState(SystemManager.Instance.CurrentMetaState);
    }

    public void OnClickToMain() => OpenConfirmDialog("정말 메인 메뉴로\n이동하시겠습니까?", PerformToMain);

    private void PerformToMain()
    {
        if (escMenuPanel != null) escMenuPanel.SetActive(false);
        if (restartReadyPanel != null) restartReadyPanel.SetActive(false);
        IsMenuOpen = false;

        if (exitWaitingPanel != null)
        {
            exitWaitingPanel.SetActive(true);
            if (exitWaitingText != null) exitWaitingText.text = "메인 메뉴로 이동 중입니다...";
        }

        if (LobbyPlayer.Local != null) LobbyPlayer.Local.RPC_RequestExitAll();
        else NetworkLauncher.Instance?.LeaveSession();
    }
    
    public void OnClickRestart() => OpenConfirmDialog("정말 이번 라운드를\n다시 시작하시겠습니까?", PerformRestart);

    private void PerformRestart()
    {
        MetaState current = SystemManager.Instance != null ? SystemManager.Instance.CurrentMetaState : MetaState.None;
        if (current == MetaState.Cooking && GlobalNetworkState.Instance != null)
        {
            if (GlobalNetworkState.Instance.Object.HasStateAuthority) GlobalNetworkState.Instance.SetPauseInternal(true);
            else GlobalNetworkState.Instance.RPC_RequestPause(true);
        }
        LobbyPlayer.Local?.RPC_SetRestartVoting(true);
        OpenRestartPanel();
        if (escMenuPanel != null) escMenuPanel.SetActive(false);
    }

    public void OnClickRoundSelection() => OpenConfirmDialog("정말 라운드 선택 창으로\n이동하시겠습니까?", PerformRoundSelection);

    private void PerformRoundSelection()
    {
        if (escMenuPanel != null) escMenuPanel.SetActive(false);
        if (restartReadyPanel != null) restartReadyPanel.SetActive(false);
        IsMenuOpen = false;
        if (LobbyPlayer.Local != null) LobbyPlayer.Local.RPC_RequestGlobalRoundSelect();
        else NetworkLauncher.Instance?.LeaveSession();
    }

    public void ShowMoveNotification(string message = "라운드 선택 창으로 이동 중입니다...", bool autoHide = true)
    {
        if (exitWaitingPanel != null)
        {
            StopAllCoroutines();
            StartCoroutine(MoveNotificationRoutine(message, autoHide));
        }
    }

    private System.Collections.IEnumerator MoveNotificationRoutine(string message, bool autoHide)
    {
        if (exitWaitingPanel != null)
        {
            exitWaitingPanel.SetActive(true);
            exitWaitingPanel.transform.SetAsLastSibling(); 
        }
        if (exitWaitingText != null) exitWaitingText.text = message;
        yield return new WaitForSecondsRealtime(1.0f);
        if (autoHide && exitWaitingPanel != null) exitWaitingPanel.SetActive(false);
    }
}
