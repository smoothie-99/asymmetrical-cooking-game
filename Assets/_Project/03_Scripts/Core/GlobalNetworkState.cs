using UnityEngine;
using Fusion;
using System;

/// <summary>
/// [Fusion 정식 등록 매니저] 
/// SystemManager(로컬)를 대신하여 네트워크 상에서 상태를 동기화하는 실제 '등록증' 있는 객체입니다.
/// </summary>
public class GlobalNetworkState : NetworkBehaviour
{
    public static GlobalNetworkState Instance { get; private set; }

    /// <summary>
    /// 일시정지 상태가 바뀔 때 방송하는 이벤트. UI 등 구독자가 스스로 반응합니다.
    /// </summary>
    public static event Action<bool> OnPauseStateChanged;

    [Header("System State (Meta)")]
    [Networked, OnChangedRender(nameof(OnMetaStateChanged))]
    public MetaState CurrentMetaState { get; set; }

    [Networked]
    public int SelectedStage { get; set; }

    [Header("GamePlay State (Cooking)")]
    [Networked, OnChangedRender(nameof(OnCookingStateChanged))]
    public CookingState CurrentCookingState { get; set; }

    [Networked]
    public TickTimer CookTimer { get; set; }

    [Networked]
    public NetworkBool IsSuccess { get; set; }

    [Networked]
    public NetworkString<_256> FeedbackMessage { get; set; }

    [Networked]
    public float SavedRemainingTime { get; set; }

    [Networked]
    public CookingOutcome ResultOutcome { get; set; } // [추가] 채점 결과 (Perfect, Clear, Fail)

    [Networked]
    public FeedbackType ResultFeedbackType { get; set; } // [추가] 채점 사유 (어떤 실수가 있었는가)

    [Networked, OnChangedRender(nameof(OnPausedChanged))]
    public NetworkBool IsPaused { get; set; }

    public override void Spawned()
    {
        Instance = this;
        Debug.Log("🌐 [GlobalNetworkState] 네트워크 스폰 완료! 이제 정식으로 명령을 보낼 수 있습니다.");

        if (HasStateAuthority)
            FeedbackMessage = "";

        // SystemManager에게 자신의 존재를 알립니다.
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.LinkNetworkProxy(this);
        }

        // [추가] 이미 진행 중인 CookingState가 있다면 비방장에게도 즉시 갱신해줍니다.
        if (!Object.HasStateAuthority && CurrentCookingState != CookingState.None)
        {
            if (GamePlayManager.Instance != null)
            {
                GamePlayManager.Instance.SyncFromProxy(CurrentCookingState);
                Debug.Log($"🌐 [GlobalNetworkState] 기존 CookingState({CurrentCookingState}) 동기화 완료.");
            }
        }

        // 방장인 경우 초기화 (혹은 방장이 나간 후 권한을 위임받은 경우)
        if (this.Object != null && this.Object.HasStateAuthority)
        {
            if (CurrentMetaState == MetaState.None
                || CurrentMetaState == MetaState.Feedback
                || CurrentMetaState == MetaState.Result
                || CurrentMetaState == MetaState.StageSelection
                || CurrentMetaState == MetaState.ReadyConfirmation)
                CurrentMetaState = MetaState.Lobby;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // [방장 전용] 네트워크 변수(CurrentCookingState, CookTimer, IsPaused) 갱신은 오직 권한자만 수행합니다.
        if (this.Object == null || !this.Object.HasStateAuthority) return;

        // [추가] 라운드 리셋(OrderDialogue) 시 일시정지 강제 해제
        if (CurrentMetaState == MetaState.OrderDialogue && IsPaused)
        {
            IsPaused = false;
            Debug.Log("🔄 [GlobalNetworkState] 라운드 리셋 감지 -> 일시정지 네트워크 상태를 해제합니다.");
        }

        if (IsPaused) return;

        // 요리 세션 타이머 만료 체크
        if (CookTimer.IsRunning && CookTimer.Expired(Runner))
        {
            HandleTimerExpired();
        }
    }

    private void HandleTimerExpired()
    {
        switch (CurrentCookingState)
        {
            case CookingState.Ready:
                // 3, 2, 1 카운트다운 종료 -> 실제 요리 시작
                if (GamePlayManager.Instance != null)
                {
                    GamePlayManager.Instance.SetCookingState(CookingState.Cooking);
                }
                break;

            case CookingState.Cooking:
                // 요리 시간 종료 -> 타임 오버 처리
                if (GamePlayManager.Instance != null)
                {
                    GamePlayManager.Instance.FinishCookingSession(false, "이런, 시간이 다 됐군!");
                }
                break;
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestMetaStateChange(MetaState newState)
    {
        if (this.Object != null && this.Object.IsValid && IsAllowedMetaTransition(CurrentMetaState, newState))
        {
            CurrentMetaState = newState;
            Debug.Log($"🌐 [GlobalNetworkState] RPC processed: MetaState -> {newState}");
        }
    }

    /// <summary>
    /// [추가] 클라이언트(비방장)가 요리 완성 판정을 방장에게 위임 제출합니다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_FinishCookingSession(
        bool success,
        string message,
        CookingOutcome outcome,
        FeedbackType feedbackType,
        RpcInfo info = default)
    {
        LobbyPlayer requester = LobbyPlayer.Get(info.Source);
        bool isCookingMaster = requester != null
            && requester.SelectedRole == (int)NetworkLauncher.PlayerJob.CookingMaster;

        if (this.Object != null && this.Object.IsValid
            && CurrentMetaState == MetaState.Cooking
            && isCookingMaster)
        {
            if (GamePlayManager.Instance != null)
            {
                GamePlayManager.Instance.FinishCookingSession(success, message, outcome, feedbackType);
            }
        }
    }

    /// <summary>
    /// [추가] 네트워크 상태를 로비로 완전히 초기화합니다. (방장 위임/이탈 시용)
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ResetToLobby()
    {
        if (this.Object != null && this.Object.IsValid)
        {
            CurrentMetaState = MetaState.Lobby;
            SelectedStage = 0;
            Debug.Log("🌐 [GlobalNetworkState] RPC: 로비 상태 및 스테이지 선택이 초기화되었습니다.");
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestPause(bool pause)
    {
        if (CurrentMetaState == MetaState.Cooking)
            SetPauseInternal(pause);
    }

    private static bool IsAllowedMetaTransition(MetaState current, MetaState next)
    {
        if (!Enum.IsDefined(typeof(MetaState), next)) return false;
        if (current == next) return true;

        return current switch
        {
            MetaState.None => next == MetaState.Lobby,
            MetaState.Lobby => next == MetaState.StageSelection,
            MetaState.StageSelection => next == MetaState.Lobby || next == MetaState.ReadyConfirmation,
            MetaState.ReadyConfirmation => next == MetaState.Lobby
                || next == MetaState.StageSelection
                || next == MetaState.OrderDialogue,
            MetaState.OrderDialogue => next == MetaState.Cooking || next == MetaState.Lobby,
            MetaState.Cooking => next == MetaState.Feedback || next == MetaState.Lobby,
            MetaState.Feedback => next == MetaState.Result || next == MetaState.Lobby,
            MetaState.Result => next == MetaState.Lobby
                || next == MetaState.StageSelection
                || next == MetaState.ReadyConfirmation
                || next == MetaState.OrderDialogue,
            _ => false
        };
    }

    /// <summary>
    /// [핵심] 일시정지 상태를 변경하고 시간을 캡처하거나 복원합니다.
    /// 방장 권한이 있는 경우에만 실제로 값을 변경합니다.
    /// </summary>
    public void SetPauseInternal(bool pause)
    {
        if (this.Object == null || !this.Object.IsValid || !this.Object.HasStateAuthority) return;

        // [중요] 상태가 실제로 바뀔 때만 실행
        if (IsPaused == pause) return;

        if (pause)
        {
            // 1. 일시정지 시: 현재 남은 시간을 캡처해서 네트워크 변수에 저장
            if (GamePlayManager.Instance != null)
            {
                SavedRemainingTime = GamePlayManager.Instance.GetRemainingTime();
            }
        }
        else
        {
            // 2. 재개 시: 저장된 남은 시간으로 타이머를 새로 생성
            if (GamePlayManager.Instance != null)
            {
                GamePlayManager.Instance.ResumeCookingSession(SavedRemainingTime);
            }
        }

        IsPaused = pause;
        Debug.Log($"🌐 [GlobalNetworkState] 일시정지 상태 변경: {pause} (남은 시간: {SavedRemainingTime}초)");
    }

    private void OnMetaStateChanged()
    {
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.SyncFromProxy(CurrentMetaState);
        }
    }

    private void OnCookingStateChanged()
    {
        if (GamePlayManager.Instance != null)
        {
            GamePlayManager.Instance.SyncFromProxy(CurrentCookingState);
        }
    }

    private void OnPausedChanged()
    {
        OnPauseStateChanged?.Invoke(IsPaused);
    }

    /// <summary>
    /// 로컬 플레이어가 레시피 마스터인지 확인합니다.
    /// </summary>
    public bool IsLocalRecipeMaster()
    {
        // NetworkLauncher나 LobbyPlayer를 통해 역학 확인
        return NetworkLauncher.SelectedJob == NetworkLauncher.PlayerJob.RecipeMaster;
    }
}
