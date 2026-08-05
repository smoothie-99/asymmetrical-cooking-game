using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

public class RoomLobbyUI : MonoBehaviour
{
    public enum PlayerRole { None = 0, RecipeMaster = 1, CookingMaster = 2 }

    [Header("Room Info")]
    [SerializeField] private TMP_Text roomCodeText;

    [Header("Player Slots - Visuals")]
    [SerializeField] private Image recipeBorder;
    [SerializeField] private Image recipeDarken;      // 추가: 선택 불가능할 때 어두운 배경
    
    [Header("Player Slots - Visuals")]
    [SerializeField] private Image cookingBorder;
    [SerializeField] private Image cookingDarken;     // 추가: 선택 불가능할 때 어두운 배경

    [Header("Lobby State UI")]
    [SerializeField] private TMP_Text actionButtonText;
    [SerializeField] private Button actionButton;

    private void OnEnable()
    {
        LobbyPlayer.OnAnyStateChanged -= UpdateLobbyNetworkState;
        LobbyPlayer.OnAnyStateChanged += UpdateLobbyNetworkState;

        // [추가] 플레이어가 들어오거나 나갔을 때 즉시 UI를 갱신하도록 구독합니다.
        NetworkLauncher.OnPlayerJoinedOrLeft -= UpdateLobbyNetworkState;
        NetworkLauncher.OnPlayerJoinedOrLeft += UpdateLobbyNetworkState;

        // [추가] 방장이 바뀌거나 메타 상태가 강제로 전이(리셋)될 때 UI를 갱신합니다.
        SystemManager.SubscribeWhenReady(HandleMetaStateChanged);

        // [수정] 데이터 동기화 타이밍을 고려하여 코루틴으로 초기 갱신
        StopAllCoroutines();
        StartCoroutine(InitialRefreshRoutine());
    }

    private void HandleMetaStateChanged(MetaState state) => UpdateLobbyNetworkState();

    private System.Collections.IEnumerator InitialRefreshRoutine()
    {
        // 1. 내 로컬 플레이어 데이터가 들어올 때까지 대기
        while (LobbyPlayer.Local == null) yield return null;
        
        // 2. 초기 네트워크 동기화 지연을 고려하여 잠시 동안 주기적으로 갱신 (약 2초간)
        for (int i = 0; i < 5; i++)
        {
            UpdateLobbyNetworkState();
            yield return new WaitForSeconds(0.4f);
        }
    }

    private void OnDisable()
    {
        LobbyPlayer.OnAnyStateChanged -= UpdateLobbyNetworkState;
        NetworkLauncher.OnPlayerJoinedOrLeft -= UpdateLobbyNetworkState;

        // [추가] 구독 해제
        SystemManager.Unsubscribe(HandleMetaStateChanged);
    }

    public void OnClickExit()
    {
        // 네트워크 종료 및 패널 이동
        if (NetworkLauncher.Instance != null && NetworkLauncher.Instance.Runner != null)
        {
            NetworkLauncher.Instance.Runner.Shutdown();
        }

        MainMenuUI menu = GetComponentInParent<MainMenuUI>();
        if (menu != null)
        {
            menu.OnCloseLobby();
        }
    }

    private void UpdateLobbyNetworkState()
    {
        // 0. 초기화 (데이터가 없으면 무조건 끔)
        if (recipeBorder) recipeBorder.gameObject.SetActive(false);
        if (cookingBorder) cookingBorder.gameObject.SetActive(false);
        if (recipeDarken) recipeDarken.gameObject.SetActive(false);
        if (cookingDarken) cookingDarken.gameObject.SetActive(false);

        NetworkRunner runner = NetworkLauncher.Instance?.Runner;
        if (runner == null || !runner.IsRunning) return;

        LobbyPlayer myData = LobbyPlayer.Local;
        if (myData == null) return;

        bool iAmHost = runner.IsSharedModeMasterClient;
        bool otherHasRecipe = false;
        bool otherHasCook = false;
        bool otherIsReady = false;
        int playerCount = 0;

        foreach (var playerObj in runner.ActivePlayers)
        {
            var lp = LobbyPlayer.Get(playerObj);
            if (lp == null) continue;

            playerCount++;
            if (lp == myData) continue; // 자신은 건너뜀

            if (lp.SelectedRole == 1) otherHasRecipe = true;
            if (lp.SelectedRole == 2) otherHasCook = true;
            if (lp.IsReady) otherIsReady = true; // 한 명이라도 준비 완료면 true
        }

        // [추가/수정] 방장이 되었을 때의 예외 처리
        if (iAmHost && myData.IsReady)
        {
            // 방장은 '준비' 상태가 아니라 '시작' 권한을 가집니다. 
            // 호스트 위임 시 이전의 '준비 완료' 상태가 남아 역할 변경을 막는 것을 방지하기 위해 강제로 해제합니다.
            myData.SetReady(false);
            Debug.Log("👑 [Lobby] 방장 권한 위임 확인 - 준비 상태를 초기화합니다.");
        }

        // 1. 포지션 배경 처리 (다른 플레이어가 고른 건 어둡게)
        if (recipeDarken) recipeDarken.gameObject.SetActive(otherHasRecipe);
        if (cookingDarken) cookingDarken.gameObject.SetActive(otherHasCook);

        // 2. 테두리 처리 (내가 고른 것)
        if (recipeBorder) recipeBorder.gameObject.SetActive(myData.SelectedRole == 1);
        if (cookingBorder) cookingBorder.gameObject.SetActive(myData.SelectedRole == 2);

        // 3. 버튼 로직
        if (iAmHost)
        {
            // 방장: 모든 플레이어가 들어왔고, 내가 포지션을 골랐으며, 상대방이 준비 완료했을 때만 '게임 시작' 활성화
            actionButtonText.text = "게임 시작";
            
            // [추가] 방장은 준비 상태여서는 안 되므로 강제로 해제합니다. (UI 불일치 방지)
            if (myData.IsReady) myData.SetReady(false);

            // [수정] 혼자 있을 때는 '게임 시작' 버튼을 비활성화합니다.
            bool canStart = (playerCount >= 2 && myData.SelectedRole != 0 && otherIsReady);
            actionButton.interactable = canStart;
        }
        else
        {
            // 팀원: 포지션을 골랐을 때만 '준비' 버튼 활성화
            actionButton.interactable = (myData.SelectedRole != 0);
            // [수정] "준비 완료!" -> "준비 취소" (사용자 편의성 향상)
            actionButtonText.text = myData.IsReady ? "준비 취소" : "준비하기";
        }
    }

    public void OnClickActionButton()
    {
        NetworkRunner runner = NetworkLauncher.Instance?.Runner;
        if (runner == null) return;

        LobbyPlayer myData = LobbyPlayer.Local;
        if (myData == null) return;

        // [추가] 방장인지 다시 한번 체크합니다. (서버/클라이언트 동기화 오차 대비)
        bool strictlyIsHost = runner.IsSharedModeMasterClient;

        if (strictlyIsHost)
        {
            // [추가/수정] 혼자 있을 때는 버튼이 눌리더라도 넘어가지 않도록 방어 로직을 둡니다.
            int activePlayerCount = 0;
            foreach (var p in runner.ActivePlayers) if (LobbyPlayer.Get(p) != null) activePlayerCount++;

            if (activePlayerCount < 2)
            {
                Debug.LogWarning("🚫 [Lobby] 혼자서는 게임을 시작할 수 없습니다. 다른 플레이어를 기다려주세요.");
                return;
            }

            // 방장: 전역 메타 상태를 StageSelection으로 바꿉니다.
            if (SystemManager.Instance != null)
            {
                SystemManager.Instance.ChangeMetaState(MetaState.StageSelection);
                Debug.Log("🌐 [Lobby] 방장이 라운드 선택창 진입을 요청했습니다.");
            }
        }
        else
        {
            // 팀원: 준비 상태 토글
            myData.SetReady(!myData.IsReady);
        }
    }

    // [alias] 기존 UI 연결용
    public void OnClickReady() => OnClickActionButton();

    public void OnClickSelectRole(int roleIndex)
    {
        LobbyPlayer myData = LobbyPlayer.Local;
        if (myData == null) return;

        // [수정] 방장은 자신의 준비 상태에 상관없이 역할을 바꿀 수 있도록 허용합니다.
        // 일반 클라이언트만 준비 완료(IsReady) 상태에서 변경이 금지됩니다.
        bool iAmHost = NetworkLauncher.Instance?.Runner?.IsSharedModeMasterClient ?? false;
        if (myData.IsReady && !iAmHost) return; 

        // 이미 선택된 걸 다시 누르면 취소(0), 아니면 새로 선택
        int nextRole = (myData.SelectedRole == roleIndex) ? 0 : roleIndex;

        // 다른 플레이어가 이미 고른 거면 무시 (하지만 취소는 가능해야 함)
        if (nextRole != 0 && IsRoleTaken(nextRole)) return;

        myData.SetRole(nextRole);
        // 전역 변수에도 저장 (씬 전환용)
        NetworkLauncher.SelectedJob = (NetworkLauncher.PlayerJob)nextRole;
    }


    private LobbyPlayer GetLobbyPlayer(PlayerRef player) => LobbyPlayer.Get(player);

    private bool IsRoleTaken(int roleIndex)
    {
        var runner = NetworkLauncher.Instance?.Runner;
        foreach (var p in runner.ActivePlayers)
        {
            var lp = GetLobbyPlayer(p);
            if (lp != null && !lp.Object.HasStateAuthority && lp.SelectedRole == roleIndex) return true;
        }
        return false;
    }
}
