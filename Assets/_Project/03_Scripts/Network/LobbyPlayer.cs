using Fusion;
using UnityEngine;
using System;

public class LobbyPlayer : NetworkBehaviour
{
    // [Networked]가 붙으면 모든 플레이어의 화면에서 똑같이 보입니다.
    [Networked, OnChangedRender(nameof(OnReadyChanged))]
    public NetworkBool IsReady { get; set; }

    [Networked, OnChangedRender(nameof(OnAnyStateChangedLog))]
    public NetworkBool IsRestartVoting { get; set; } // 추가: 다시하기 투표 패널 활성화 여부

    [Networked, OnChangedRender(nameof(OnAnyStateChangedLog))]
    public int MaxClearedRound { get; set; } // [신규] 해당 플레이어의 최고 클리어 라운드

    [Networked, OnChangedRender(nameof(OnRoleChanged))]
    public int SelectedRole { get; set; } // 0: None, 1: Recipe, 2: Cook

    // [제거] LobbyMenuState, TargetStage는 GamePlayManager로 통합되었습니다.

    /// <summary>
    /// 어느 플레이어든 IsReady 또는 SelectedRole이 바뀌면 방송합니다.
    /// UI는 이 이벤트를 구독하여 Update 폴링 없이 반응합니다.
    /// </summary>
    public static event Action OnAnyStateChanged;

    public static LobbyPlayer Local { get; private set; }

    /// <summary>
    /// 특정 PlayerRef에 해당하는 LobbyPlayer를 반환합니다.
    /// </summary>
    public static LobbyPlayer Get(PlayerRef player)
    {
        if (NetworkLauncher.Instance?.Runner == null) return null;
        foreach (var obj in NetworkLauncher.Instance.Runner.GetAllNetworkObjects())
        {
            if (obj.TryGetComponent<LobbyPlayer>(out var lp) && obj.InputAuthority == player)
                return lp;
        }
        return null;
    }

    /// <summary>
    /// PlayerId가 가장 낮은 플레이어(방장)의 LobbyPlayer를 반환합니다.
    /// </summary>
    public static LobbyPlayer Host
    {
        get
        {
            var runner = NetworkLauncher.Instance?.Runner;
            if (runner == null || !runner.IsRunning) return null;
            PlayerRef hostRef = default;
            int minId = int.MaxValue;
            foreach (var p in runner.ActivePlayers)
            {
                if (p.PlayerId < minId) { minId = p.PlayerId; hostRef = p; }
            }
            return Get(hostRef);
        }
    }

    private void OnReadyChanged() => OnAnyStateChanged?.Invoke();
    private void OnRoleChanged()  => OnAnyStateChanged?.Invoke();
    
    private void OnAnyStateChangedLog() => OnAnyStateChanged?.Invoke();

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            Local = this;
            Debug.Log($"👤 [LobbyPlayer] 로컬 플레이어 인스턴스가 설정되었습니다. (ID: {Object.InputAuthority.PlayerId})");
        }

        if (Object.HasStateAuthority)
        {
            IsReady = false;
            IsRestartVoting = false; // [추가] 방 입장 혹은 스폰 시 투표 상태를 초기화합니다.
            
            // [수정] SelectedRole = 0; <- 스폰 시 강제 리셋을 방지하여 역할 정보를 보존합니다.
            
            // 로컬 플레이어라면 이전에 선택했던 역할을 복구하여 동기화합니다.
            if (Object.HasInputAuthority)
            {
                SelectedRole = (int)NetworkLauncher.SelectedJob;
                Debug.Log($"👤 [LobbyPlayer] 로컬 역할({NetworkLauncher.SelectedJob})을 네트워크 데이터에 복구했습니다.");

                // [신규] 내 계정의 전적을 네트워크에 동기화합니다.
                AuthManager.SyncProgressionToNetwork();
            }
        }

        // [추가] 전역 메타 상태 변화 구독
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.OnMetaPhaseEntered += HandleMetaStateChanged;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.OnMetaPhaseEntered -= HandleMetaStateChanged;
        }
    }

    private void OnDestroy()
    {
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.OnMetaPhaseEntered -= HandleMetaStateChanged;
        }
    }

    private void HandleMetaStateChanged(MetaState newState)
    {
        // [핵심 수정] 라운드 선택, 로티 뿐만 아니라 '요리 시작(OrderDialogue)' 단계로 넘어갈 때도 준비 상태를 초기화합니다.
        // 이를 통해 게임 플레이 중이나 결과창에서 다시하기를 누를 때 이전의 준비 상태가 남아있지 않게 합니다.
        if (newState == MetaState.StageSelection || newState == MetaState.Lobby || 
            newState == MetaState.ReadyConfirmation || newState == MetaState.OrderDialogue)
        {
            if (Object.HasStateAuthority)
            {
                if (IsReady) IsReady = false;
                if (IsRestartVoting) IsRestartVoting = false; // [추가] 라운드 선택이나 메뉴 이동 시 투표 중임을 알리는 패널도 동기화 초기화
                
                Debug.Log($"🔄 [LobbyPlayer] 단계 진입({newState})에 따라 준비/투표 상태를 초기화합니다.");
            }
        }
    }

    // [신규] 클리어 라운드 데이터 동기화 요청
    public void SetMaxClearedRound(int round)
    {
        if (Object.HasStateAuthority)
        {
            MaxClearedRound = round;
        }
        else
        {
            RPC_SetMaxClearedRound(round);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_SetMaxClearedRound(int round)
    {
        MaxClearedRound = round;
    }

    // 포지션 선택 요청 (권한이 있는 나 자신만 호출 가능)
    public void SetRole(int role)
    {
        if (Object.HasStateAuthority)
        {
            SelectedRole = role;
        }
    }

    // 준비 상태 변경 요청
    public void SetReady(bool ready)
    {
        if (Object.HasStateAuthority)
        {
            IsReady = ready;
        }
        else
        {
            RPC_SetReady(ready);
        }
    }

    // [수정] 누가 누르든 상관없이, 이 신호를 받은 모든 플레이어는 자신의 상태를 리셋합니다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_GlobalCancelRestart()
    {
        // 신호를 받은 모든 기기에서, 자신의 로컬 플레이어 객체를 찾아 상태를 끕니다.
        if (Local != null && Local.Object.IsValid)
        {
            // 권한 기반으로 네트워크 변수를 초기화합니다.
            Local.SetReady(false);
            Local.RPC_SetRestartVoting(false);
            Debug.Log($"🚫 [LobbyPlayer] 전역 취소 신호 수신 - 내 로컬 상태를 리셋합니다.");
        }
    }

    // [추가] 다시하기 창 가시성을 동기화하는 RPC
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetRestartVoting(bool voting)
    {
        IsRestartVoting = voting;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(bool ready)
    {
        IsReady = ready;
    }

    // [추가] 모든 플레이어의 화면에서 메인 메뉴 종료 시퀀스를 시작하게 하는 RPC
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RequestExitAll()
    {
        Debug.Log("📢 [RPC] ExitGame 요청 수신 - 모든 플레이어의 종료 시퀀스를 시작합니다.");
        
        // [추가] 모든 플레이어의 화면에 안내창 텍스트를 "메인 메뉴로 이동 중..."으로 갱신합니다.
        if (InGameMenuUI.Instance != null)
        {
            // [수정] 메인 메뉴 이동 시에는 씬이 로드될 때까지 창이 유지되도록 autoHide를 false로 보냅니다.
            InGameMenuUI.Instance.ShowMoveNotification("메인 메뉴로 이동 중입니다...", false);
        }

        if (NetworkLauncher.Instance != null)
        {
            // [중요] 안내창은 이미 앞에서 띄웠으므로, LeaveSession의 기본 팝업 노출은 끕니다(false).
            NetworkLauncher.Instance.LeaveSession(false);
        }
    }

    // [추가] 방장이 '다시하기'를 최종 승인했을 때 모두에게 초기화를 명령하는 RPC
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_GlobalFinalizeRestart()
    {
        if (Local != null && Local.Object.IsValid)
        {
            // 권한 기반으로 네트워크 변수를 초기화합니다.
            Local.SetReady(false);
            Local.RPC_SetRestartVoting(false);
            
            // [리팩토링 지침 준수] 씬 검색(FindFirstObjectByType) 대신 명시적 싱글톤인 Instance로 즉시 접근합니다.
            if (InGameMenuUI.Instance != null) InGameMenuUI.Instance.CloseRestartPanel();

            Debug.Log("🎉 [LobbyPlayer] 전역 다시하기 최종 신호 수신 - 모든 상태를 초기화하고 창을 닫습니다.");
        }
    }

    // [추가] 방장에게 특정 스테이지 선택을 요청하는 RPC
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestStageSelection(int stageNumber)
    {
        if (SystemManager.Instance == null) return;
        
        SystemManager.Instance.SelectedStage = stageNumber;
        SystemManager.Instance.ChangeMetaState(MetaState.ReadyConfirmation);
        
        Debug.Log($"📢 [RPC] 방장이 클라이언트의 요청으로 스테이지를 {stageNumber}로 설정하고 ReadyConfirmation 단계로 진입했습니다.");
    }

    // [추가] 방장에게 메뉴 상태 변경을 요청하는 함수 (참가자가 호출해도 방장이 실행함)
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestLobbyMenuState(int newState)
    {
        // 이 코드는 방장의 화면에서 실행됩니다.
        if (SystemManager.Instance == null) return;

        switch (newState)
        {
            case 0: // 로비 복귀
                SystemManager.Instance.ChangeMetaState(MetaState.Lobby);
                break;
            case 1: // 라운드 선택창
                SystemManager.Instance.ChangeMetaState(MetaState.StageSelection);
                break;
            case 2: // 준비/다시하기 다이얼로그
                SystemManager.Instance.ChangeMetaState(MetaState.ReadyConfirmation);
                break;
            case 3: // 인게임 시작 (주문 대화부터)
                // [추가] 방장이 게임을 시작/재시작할 때, 모든 플레이어의 상태를 완전히 초기화합니다.
                if (SystemManager.Instance != null && SystemManager.Instance.HasStateAuthority)
                {
                    var runner = NetworkLauncher.Instance?.Runner;
                    if (runner != null)
                    {
                        foreach (var pRef in runner.ActivePlayers)
                        {
                            var lp = Get(pRef);
                            if (lp != null)
                            {
                                lp.IsReady = false;
                                lp.IsRestartVoting = false;
                            }
                        }
                    }

                    // [중요] 일시정지 상태였다면 이를 해제하여 참가자들의 화면을 풀어줍니다.
                    if (GlobalNetworkState.Instance != null)
                    {
                        GlobalNetworkState.Instance.SetPauseInternal(false);
                    }

                    SystemManager.Instance.ChangeMetaState(MetaState.OrderDialogue);
                    Debug.Log("🚀 [LobbyPlayer] 모든 상태 초기화 및 일시정지 해제 후 주문창으로 이동합니다.");
                }
                break;
            case 4: // 인게임 복귀 (요리 중)
                SystemManager.Instance.ChangeMetaState(MetaState.Cooking);
                break;
        }

        Debug.Log($"📢 [RPC] 방장이 SystemManager를 통해 메타 상태를 {SystemManager.Instance.CurrentMetaState}로 변경했습니다. (요청된 ID: {newState})");
    }

    // [추가] 모든 플레이어에게 라운드 선택 이동 안내창을 띄우고 실제로 이동을 수행합니다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RequestGlobalRoundSelect()
    {
        Debug.Log("📢 [RPC] RoundSelect 이동 요청 수신 - 모든 플레이어의 화면에 안내창을 띄웁니다.");
        
        // 1. 모든 클라이언트의 화면에 안내창 노출
        if (InGameMenuUI.Instance != null)
        {
            // [수정] 라운드 선택 시에도 전역 상태가 바뀔 때까지 안내창을 유지합니다 (autoHide = false).
            InGameMenuUI.Instance.ShowMoveNotification("라운드 선택 창으로 이동 중입니다...", false);
        }

        // 2. 방장(State Authority)이라면 1초 대기 후 실제로 씬/상태를 변경합니다.
        if (Object.HasStateAuthority)
        {
            // [지연 처리] 1초 안내창을 충분히 보여준 뒤 이동합니다.
            Invoke("PerformRoundSelectTransition", 1.0f);
        }
    }

    private void PerformRoundSelectTransition()
    {
        if (SystemManager.Instance != null)
        {
            // [중요] 일시정지 상태였다면 이를 해제하여 참가자들의 화면을 풀어줍니다.
            if (GlobalNetworkState.Instance != null && Object.HasStateAuthority)
            {
                GlobalNetworkState.Instance.SetPauseInternal(false);
            }

            // 방장 권한으로 모든 클라이언트의 메타 상태를 '라운드 선택'으로 강제 전환
            SystemManager.Instance.ChangeMetaState(MetaState.StageSelection);
            
            Debug.Log("🚀 [LobbyPlayer] 1초 경과 - 방장이 전역 메타 상태를 StageSelection으로 변경했습니다.");
        }
    }
}
