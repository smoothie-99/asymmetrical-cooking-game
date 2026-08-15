using UnityEngine;
using Fusion;
using System;

/// <summary>
/// [In-Game State] 요리 단계 내부의 상세 흐름을 정의합니다.
/// </summary>
public enum CookingState
{
    None,
    Ready,      // 요리 시작 전 카운트다운 (3, 2, 1!)
    Cooking,    // 실제 요리 진행 중 (타이머 작동)
    Submit      // 요리 완성 및 제출 판정
}

/// <summary>
/// 실제 플레이 중인 '요리 세션'의 시간과 판정을 담당하는 현장 지휘관.
/// </summary>
public class GamePlayManager : MonoBehaviour 
{
    public static GamePlayManager Instance { get; private set; }

    [Header("Cooking Settings")]
    public float readyDuration = 4f;  // [수정] 3, 2, 1, START! 를 위해 4초로 변경
    public float cookDuration = 120f; // 기본 요리 시간
    
    // Proxy 안전 접근용 속성
    private GlobalNetworkState Proxy => GlobalNetworkState.Instance;
    private bool IsProxyValid => Proxy != null && Proxy.Object != null && Proxy.Object.IsValid;

    public CookingState CurrentCookingState => IsProxyValid ? Proxy.CurrentCookingState : CookingState.None;
    public bool IsSuccess => IsProxyValid ? (bool)Proxy.IsSuccess : false;
    public string FeedbackMessage => IsProxyValid ? Proxy.FeedbackMessage.ToString() : "";

    public event Action<CookingState> OnCookingPhaseEntered;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void OnEnable()
    {
        if (SystemManager.Instance != null)
            SystemManager.Instance.OnMetaPhaseEntered += SyncWithMetaState;
    }

    private void OnDisable()
    {
        if (SystemManager.Instance != null)
            SystemManager.Instance.OnMetaPhaseEntered -= SyncWithMetaState;
    }

    /// <summary>
    /// 전역 메타 상태에 따라 요리 세션의 시작/초기화를 결정합니다.
    /// </summary>
    private void SyncWithMetaState(MetaState metaState)
    {
        Debug.Log($"🕒 [GamePlayManager] MetaState 변경 감지: {metaState}");

        switch (metaState)
        {
            case MetaState.Cooking:
                // [방장 권한] 요리 단계 진입 시, 아직 시작 전(None)이라면 세션을 시작합니다.
                if (SystemManager.Instance.HasStateAuthority && CurrentCookingState == CookingState.None) 
                {
                    StartCookingSession();
                }
                break;

            case MetaState.StageSelection:
            case MetaState.OrderDialogue:
            case MetaState.Lobby:
                // 게임 준비 단계나 로비로 돌아갈 때는 요리 상태를 초기화합니다.
                if (SystemManager.Instance.HasStateAuthority && IsProxyValid)
                {
                    Proxy.CurrentCookingState = CookingState.None;
                    Debug.Log("🔄 [GamePlayManager] 요리 세션을 초기화했습니다.");
                }
                break;
        }
    }

    private void StartCookingSession()
    {
        if (!IsProxyValid) return;

        Debug.Log("🔥 [GamePlayManager] 새로운 요리 세션 시작 (Ready 단계 진입)");
        Proxy.IsSuccess = false;
        Proxy.FeedbackMessage = "";
        Proxy.ResultOutcome = CookingOutcome.Fail;
        Proxy.ResultFeedbackType = (FeedbackType)(-1);
        
        SetCookingState(CookingState.Ready);
    }

    /// <summary>
    /// [핵심] 요리 내부 상태를 변경하고 필요한 초기화(타이머 등)를 수행합니다.
    /// </summary>
    public void SetCookingState(CookingState newState)
    {
        if (!IsProxyValid || !Proxy.Object.HasStateAuthority) return;

        Proxy.CurrentCookingState = newState;
        
        float duration = 0f;
        if (newState == CookingState.Ready) duration = readyDuration; 
        else if (newState == CookingState.Cooking) duration = cookDuration;

        if (duration > 0)
        {
            Proxy.CookTimer = TickTimer.CreateFromSeconds(Proxy.Runner, duration);
            Proxy.SavedRemainingTime = duration; 
            Debug.Log($"🕒 [GamePlayManager] 상태 전환: {newState} (시간 설정: {duration}초)");
        }
    }

    /// <summary>
    /// 일시정지 해제 시 멈췄던 지점부터 타이머를 재개합니다.
    /// </summary>
    public void ResumeCookingSession(float remainingTime)
    {
        if (IsProxyValid && Proxy.Object.HasStateAuthority)
        {
            Proxy.CookTimer = TickTimer.CreateFromSeconds(Proxy.Runner, remainingTime);
            Debug.Log($"🕒 [GamePlayManager] 타이머 재개: {remainingTime}초 남음");
        }
    }

    /// <summary>
    /// 네트워크 데이터(Proxy)의 변화를 로컬 시스템에 알립니다. (모든 클라이언트 공통)
    /// </summary>
    public void SyncFromProxy(CookingState newState)
    {
        OnCookingPhaseEntered?.Invoke(newState);
        Debug.Log($"🧼 [GamePlayManager] Cooking Phase 동기화 -> {newState}");
    }

    /// <summary>
    /// 요리 세션을 종료하고 결과를 전역 시스템에 보고합니다.
    /// [수정] 성공 등급(outcome)과 감점 사유(feedbackType)를 함께 보고합니다.
    /// </summary>
    public void FinishCookingSession(bool success, string message, CookingOutcome outcome = CookingOutcome.Fail, FeedbackType feedbackType = (FeedbackType)(-1))
    {
        if (!IsProxyValid) return;

        // 권한이 없으면 방장(MasterClient)에게 위임 처리합니다. (비방장 쿠킹마스터가 음식 제출 시 진척을 잃지 않게 함)
        if (!Proxy.Object.HasStateAuthority)
        {
            Proxy.RPC_FinishCookingSession(success, message, outcome, feedbackType);
            return;
        }

        Proxy.IsSuccess = success;
        Proxy.FeedbackMessage = message;
        Proxy.ResultOutcome = outcome;
        Proxy.ResultFeedbackType = feedbackType;

        SetCookingState(CookingState.Submit);

        // [가이드 준수] SystemManager를 직접 호출하여 지시하지 않습니다. 
        // 상태 변화(OnCookingPhaseEntered)를 통해 각 시스템이 스스로 반응하게 합니다.
        
        Debug.Log($"🏁 [GamePlayManager] 요리 세션 상태 전환: Submit (결과 저장 완료)");
    }

    /// <summary>
    /// 현재 남은 시간을 반환합니다. (UI 표시 및 일시정지 저장용)
    /// </summary>
    public float GetRemainingTime()
    {
        if (!IsProxyValid) return 0f;

        // 일시정지 중이면 멈춰둔 시점의 시간을 반환
        if (Proxy.IsPaused) return Proxy.SavedRemainingTime;

        if (Proxy.CookTimer.IsRunning)
            return Proxy.CookTimer.RemainingTime(Proxy.Runner) ?? 0f;
            
        return 0f;
    }

    // 하위 호황성을 위한 기존 명칭 유지
    [System.Obsolete("Use SetCookingState instead")]
    public void ChangeCookingState(CookingState newState) => SetCookingState(newState);
}
