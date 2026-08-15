using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

public class RoundSelectionUI : MonoBehaviour
{
    [Header("Dialog Panel")]
    [SerializeField] private GameObject readyDialogPanel;
    [SerializeField] private TMP_Text stageTitleText;
    
    [Header("Role Visuals")]
    [SerializeField] private Image recipeRoleBorder;
    [SerializeField] private Image cookingRoleBorder;
    [SerializeField] private TMP_Text readyButtonText;

    [Header("Loading UI")]
    [SerializeField] private GameObject loadingPanel;

    [Header("Buttons")]
    [SerializeField] private GameObject backButton; 
    [SerializeField] private GameObject mapElements; // [추가] 스테이지 버튼들이 모여있는 부모 오브젝트
    [SerializeField] private Sprite unlockedSprite; // [신규] 해금된 기본 버튼 스프라이트
    [SerializeField] private Sprite lockedSprite;   // [신규] 잠긴 자물쇠 버튼 스프라이트

    [Header("Debug Mode")]
    [SerializeField] private bool unlockAllRoundsForDebug = false; // [신규] 전 스테이지 강제 해금 디버그 옵션

    private bool _warnedButtonMissing = false; // 경고 중복 출력 방지용 필드
    private int currentMinCleared = 0;        // [신규] 방 안의 모든 플레이어 중 최저 클리어 라운드 캐시

    void OnEnable()
    {
        if (readyDialogPanel != null) readyDialogPanel.SetActive(false);
        if (loadingPanel != null) loadingPanel.SetActive(false);
        if (mapElements != null) mapElements.SetActive(true);

        // [추가] 라운드 선택창(StageSelection)에 진입할 때 준비 상태를 즉시 초기화합니다.
        // 로비에서의 '준비 완료' 상태가 남아있어 발생하는 UI 깜빡임을 방지합니다.
        LobbyPlayer myData = LobbyPlayer.Local;
        if (myData != null)
        {
            myData.SetReady(false);
            Debug.Log("🔄 [RoundUI] 라운드 선택 진입 - 준비 상태를 초기화했습니다.");
        }

        SystemManager.SubscribeWhenReady(HandleGlobalMetaStateChanged);
        LobbyPlayer.OnAnyStateChanged -= OnLobbyPlayerStateChanged;
        LobbyPlayer.OnAnyStateChanged += OnLobbyPlayerStateChanged;

        // [수정] 데이터 동기화 타이밍을 고려하여 코루틴으로 초기 갱신
        StopAllCoroutines();
        StartCoroutine(InitialRefreshRoutine());

        UpdateHostSpecificUI();
        
        // [신규] 계정 전적을 기반으로 라운드가 열렸는지 체크하여 버튼들을 잠급니다.
        RefreshRoundButtons();
    }

    /// <summary>
    /// [핵심] 방 안의 모든 플레이어 중 가장 낮은 전적을 기준으로 접근 가능한 라운드 버튼들을 갱신합니다.
    /// </summary>
    private void RefreshRoundButtons()
    {
        if (mapElements == null) return;

        // 1. 방 안의 모든 플레이어 중 가장 낮은 클리어 기록(최하향 평준화)을 찾습니다.
        int minCleared = int.MaxValue;
        var runner = NetworkLauncher.Instance?.Runner;
        
        if (runner != null)
        {
            foreach (var pRef in runner.ActivePlayers)
            {
                var lp = LobbyPlayer.Get(pRef);
                if (lp != null)
                {
                    if (lp.MaxClearedRound < minCleared) minCleared = lp.MaxClearedRound;
                }
            }
        }

        // 아무도 없거나 측정 불가 시 기본값 0 (1라운드 해금)
        // [디버그 전용] 디버그 모드 활성화 시 모두 해금된 것으로 간주 (최저 전적 12로 강제 설정)
        if (unlockAllRoundsForDebug) minCleared = 12;

        currentMinCleared = minCleared; // [신규] 캐시 필드 갱신
        int maxAvailable = minCleared + 1;
        Debug.Log($"🗺️ [RoundUI] 모든 플레이어 중 최저 클리어: {minCleared} -> 해금 범위: 1~{maxAvailable}");

        // 2. mapElements 밑의 모든 버튼 탐색
        Button[] buttons = mapElements.GetComponentsInChildren<Button>(true);
        
        for (int i = 0; i < buttons.Length; i++)
        {
            int roundNum = i + 1; // 1, 2, 3...
            bool isUnlocked = roundNum <= maxAvailable;

            // [수정] 유니티의 자동 어두워짐(Disabled Color)을 피하기 위해 interactable은 항상 true 유지
            // 대신 자물쇠 아이콘과 OnClickRound 가드 로직으로 잠금을 처리합니다.
            buttons[i].interactable = true;

            // [신규] 이미지와 텍스트를 해금 상태에 따라 교체
            Image btnImg = buttons[i].GetComponent<Image>();
            if (btnImg != null)
            {
                btnImg.sprite = isUnlocked ? unlockedSprite : lockedSprite;
            }
            // 2. 스테이지 텍스트 숨기기/보이기 (꺼진 오브젝트도 찾기 위해 true 추가)
            TMP_Text btnTxt = buttons[i].GetComponentInChildren<TMP_Text>(true);
            if (btnTxt != null)
            {
                btnTxt.gameObject.SetActive(isUnlocked);
                // [신규] 텍스트가 비어있거나 꼬여있을 수 있으므로 직접 써주기
                if (isUnlocked) btnTxt.text = $"Stage {roundNum}";
            }
        }
    }

    private System.Collections.IEnumerator InitialRefreshRoutine()
    {
        while (LobbyPlayer.Local == null) yield return null;
        
        // 처음 2초간은 네트워크 지연이 있을 수 있으므로 주기적으로 체크
        for (int i = 0; i < 5; i++)
        {
            OnLobbyPlayerStateChanged();
            yield return new WaitForSeconds(0.4f);
        }
    }

    void OnDisable()
    {
        SystemManager.Unsubscribe(HandleGlobalMetaStateChanged);
        LobbyPlayer.OnAnyStateChanged -= OnLobbyPlayerStateChanged;
    }

    private void OnLobbyPlayerStateChanged()
    {
        if (readyDialogPanel != null && readyDialogPanel.activeSelf)
            CheckNetworkedReadyState();

        // [신규] 플레이어의 전적(MaxClearedRound)이 바뀌면 라운드 버튼도 실시간으로 다시 켜야 합니다.
        RefreshRoundButtons();
    }

    /// <summary>
    /// [핵심] 전역 메타 상태에 따라 준비창 내부 다이얼로그를 제어합니다.
    /// 패널 ON/OFF는 MainMenuUI.HandleGlobalMetaStateChanged가 담당합니다.
    /// </summary>
    private void HandleGlobalMetaStateChanged(MetaState newState)
    {
        Debug.Log($"🗺️ [RoundUI] 메타 상태 변화 감지 -> {newState}");

        // [수정] StageSelection 단계가 아닐 때는 확실하게 mapElements 패널을 끕니다. 
        // (다시하기 확인창 등에서 배경으로 비치는 현상 방지)
        if (mapElements != null)
        {
            mapElements.SetActive(newState == MetaState.StageSelection);
        }

        switch (newState)
        {
            case MetaState.StageSelection:
                if (readyDialogPanel.activeSelf) readyDialogPanel.SetActive(false);
                break;

            case MetaState.ReadyConfirmation:
                if (SystemManager.Instance != null)
                    OpenReadyDialogLocal(SystemManager.Instance.SelectedStage);
                break;

            case MetaState.OrderDialogue:
            case MetaState.Cooking:
            case MetaState.Feedback:
            case MetaState.Result:
                if (readyDialogPanel.activeSelf) readyDialogPanel.SetActive(false);
                break;

            default:
                // [추가] 그 외 모든 단계(Lobby, None 등)에서는 확인 다이얼로그를 닫습니다.
                if (readyDialogPanel != null && readyDialogPanel.activeSelf)
                    readyDialogPanel.SetActive(false);
                break;
        }

        UpdateHostSpecificUI();
    }

    private void UpdateHostSpecificUI()
    {
        if (NetworkLauncher.Instance == null || NetworkLauncher.Instance.Runner == null) return;
        
        if (backButton != null)
        {
            backButton.SetActive(NetworkLauncher.Instance.Runner.IsSharedModeMasterClient);
        }
    }

    public void OnClickRound(int roundNumber)
    {
        // [수정] 디버그 모드가 아니고 클릭한 라운드가 모든 플레이어 중 최저 실력을 기준으로 아직 해금되지 않았다면 무시합니다.
        if (!unlockAllRoundsForDebug && roundNumber > currentMinCleared + 1)
        {
            Debug.LogWarning($"🚫 [RoundUI] {roundNumber} 라운드는 아직 팀원 중 누군가에게 잠겨 있습니다.");
            return;
        }

        // [수정] 방장만 라운드를 선택할 수 있도록 제한합니다. 참가자는 클릭해도 아무 반응이 없어야 합니다.
        if (NetworkLauncher.Instance.Runner.IsSharedModeMasterClient)
        {
            if (SystemManager.Instance != null)
            {
                SystemManager.Instance.SelectedStage = roundNumber;
                SystemManager.Instance.ChangeMetaState(MetaState.ReadyConfirmation);
            }
        }
        else
        {
            Debug.Log("🚫 [RoundUI] 방장이 아니므로 라운드 선택 권한이 없습니다.");
        }
    }

    private void OpenReadyDialogLocal(int roundNumber)
    {
        if (stageTitleText != null) stageTitleText.text = "STAGE " + roundNumber;
        readyDialogPanel.SetActive(true);
        if (mapElements != null) mapElements.SetActive(false);

        LobbyPlayer myData = LobbyPlayer.Local;
        if (myData != null) myData.SetReady(false);

        // [추가] 다이얼로그가 열릴 때 즉시 버튼 텍스트를 초기화합니다.
        if (readyButtonText != null) readyButtonText.text = "준비";
        
        // 시각적 상태 즉시 갱신
        CheckNetworkedReadyState();
    }

    public void OnClickReady()
    {
        LobbyPlayer myData = LobbyPlayer.Local;
        if (myData != null)
        {
            myData.SetReady(!myData.IsReady);
            Debug.Log($"👤 [RoundUI] 준비 상태 토글: {myData.IsReady}");
        }
    }

    private void CheckNetworkedReadyState()
    {
        NetworkRunner runner = NetworkLauncher.Instance?.Runner;
        if (runner == null || !runner.IsRunning) return;

        bool allReady = true;
        int playerCount = 0;

        // 매 프레임 체크 전 테두리 초기화
        if (recipeRoleBorder != null) recipeRoleBorder.gameObject.SetActive(false);
        if (cookingRoleBorder != null) cookingRoleBorder.gameObject.SetActive(false);

        // 현재 단계가 확인 단계가 아니면 그리지 않음
        bool isReadyPhaseActive = SystemManager.Instance != null && SystemManager.Instance.CurrentMetaState == MetaState.ReadyConfirmation;
        if (!isReadyPhaseActive) return;

        foreach (var playerRef in runner.ActivePlayers)
        {
            var lp = GetLobbyPlayer(playerRef);
            if (lp == null) continue;

            playerCount++;
            
            // [수정] 중앙 라운드 상태가 '준비 다이얼로그(ReadyConfirmation)'일 때만 인식합니다.
            bool isReady = lp.IsReady && isReadyPhaseActive;
            int role = lp.SelectedRole;

            if (role == 1 && recipeRoleBorder != null) recipeRoleBorder.gameObject.SetActive(isReady);
            if (role == 2 && cookingRoleBorder != null) cookingRoleBorder.gameObject.SetActive(isReady);

            if (!isReady) allReady = false;
        }

        // [시각적 피드백 강화]
        if (readyButtonText != null)
        {
            if (playerCount < 2)
            {
                readyButtonText.text = "다른 플레이어 대기 중... (" + playerCount + "/2)";
                allReady = false; 
            }
            else
            {
                // 플레이어가 2명 이상이면, 준비 상태가 모두 끝났더라도 버튼 텍스트는 본인의 상태를 반영합니다.
                LobbyPlayer myData = LobbyPlayer.Local;
                if (myData != null)
                    readyButtonText.text = myData.IsReady ? "준비 취소" : "준비";
            }
        }

        if (playerCount >= 2 && allReady)
        {
            if (runner.IsSharedModeMasterClient && !IsInvoking("StartGame"))
            {
                Invoke("StartGame", 0.5f); // 1.2초는 너무 길어서 0.5초로 단축
            }
        }
    }

    private void StartGame()
    {
        Debug.Log("🚀 [RoundUI] 모든 준비 완료. StartGame() 진입.");
        if (NetworkLauncher.Instance != null)
        {
            Debug.Log("🚀 [RoundUI] NetworkLauncher.Instance.StartGame() 호출");
            NetworkLauncher.Instance.StartGame();
        }
    }


    private LobbyPlayer GetLobbyPlayer(PlayerRef player) => LobbyPlayer.Get(player);

    public void OnClickCancel()
    {
        if (SystemManager.Instance == null) return;

        // [추가] 뒤로가기 시 자신의 준비 상태를 즉시 해제합니다.
        LobbyPlayer myData = LobbyPlayer.Local;
        if (myData != null) myData.SetReady(false);

        // 방장이면 즉시 상태 변경, 참가자면 RPC 요청
        if (SystemManager.Instance.HasStateAuthority)
        {
            SystemManager.Instance.ChangeMetaState(MetaState.StageSelection);
        }
        else
        {
            if (myData != null)
            {
                myData.RPC_RequestLobbyMenuState(1); // 1: StageSelection
                Debug.Log("📢 [RoundUI] 참가자가 취소(뒤로가기)를 요청했습니다.");
            }
        }
    }

    public void OnClickBack()
    {
        if (SystemManager.Instance == null) return;

        // [추가] 로비 복귀 시 자신의 준비 상태를 즉시 해제합니다.
        LobbyPlayer myData = LobbyPlayer.Local;
        if (myData != null) myData.SetReady(false);

        // 방장이면 즉시 상태 변경, 참가자면 RPC 요청
        if (SystemManager.Instance.HasStateAuthority)
        {
            SystemManager.Instance.ChangeMetaState(MetaState.Lobby);
        }
        else
        {
            if (myData != null)
            {
                myData.RPC_RequestLobbyMenuState(0); // 0: Lobby 
                Debug.Log("📢 [RoundUI] 참가자가 로비 복귀를 요청했습니다.");
            }
        }
    }
}
