using UnityEngine;
using Fusion;
using System;
using System.Collections;

/// <summary>
/// [Meta State] 게임의 전체적인 흐름을 정의합니다. 
/// 인게임 요리 단계는 'Cooking' 페이즈 내부에 위임됩니다.
/// </summary>
public enum MetaState
{
    None,
    Lobby,              // 방 생성/참가 및 역할 선택
    StageSelection,     // 맵 선택
    ReadyConfirmation,  // 모든 인원 확인
    OrderDialogue,      // 손님 등장 및 주문 (인게임 진입 전)
    Cooking,            // [임무 위임] GamePlayManager가 주도권을 가짐
    Feedback,           // 요리 평가 대사 출력
    Result              // 최종 점수 및 메뉴 이동
}

/// <summary>
/// 게임의 전체 라이프사이클(메타 흐름)을 총괄하는 최상위 관리자
/// </summary>
public class SystemManager : MonoBehaviour // NetworkBehaviour 대신 MonoBehaviour 사용
{
    public static SystemManager Instance { get; private set; }

    [Header("Debug")]
    public bool useTestMap = true;
    public bool enableAutoLaunch = false;
    public int debugRole = 2; // 1: RecipeMaster, 2: CookingMaster
    public string debugRoomCode = "DEBUG";
    public float hostStartDelay = 2.0f;

    [Header("System Prefabs")]
    public GameObject mapSystemPrefab;
    public GameObject uiSystemPrefab;
    public GameObject networkSystemPrefab;
    public GameObject audioSystemPrefab;
    public GameObject gamePlaySystemPrefab;

    [Header("Network State Proxy")]
    private GlobalNetworkState _proxy;
    
    // [추가] Proxy가 파괴되어도 마지막으로 확인된 상태를 기억하기 위해 사용합니다. (종료 시 씬 리셋 판단용)
    public MetaState LastMetaState { get; private set; } = MetaState.None;

    // 외부에서 기존처럼 접근할 수 있도록 래퍼 속성 제공
    // [수정] 프록시가 없으면 기본적으로 'None'을 반환하여 메인 메뉴 UI가 뜨도록 합니다.
    public MetaState CurrentMetaState => (_proxy != null && _proxy.Object != null && _proxy.Object.IsValid) ? _proxy.CurrentMetaState : MetaState.None;

    public int SelectedStage 
    { 
        get => _proxy != null ? _proxy.SelectedStage : 0;
        set { if (_proxy != null && _proxy.Object.HasStateAuthority) _proxy.SelectedStage = value; }
    }

    // 다른 스크립트들이 권한 체크를 할 수 있도록 제공
    public bool HasStateAuthority => _proxy != null && _proxy.Object != null && _proxy.Object.HasStateAuthority;

    public event Action<MetaState> OnMetaPhaseEntered;

    /// <summary>
    /// Instance가 준비되면 handler를 OnMetaPhaseEntered에 구독하고 현재 상태를 즉시 한 번 전달합니다.
    /// Instance가 아직 없으면 씬에서 MonoBehaviour를 통해 코루틴을 돌릴 수 없으므로,
    /// 호출부에서 OnEnable/코루틴 패턴 대신 이 메서드 하나로 대체합니다.
    /// </summary>
    public static void SubscribeWhenReady(Action<MetaState> handler)
    {
        if (Instance != null)
        {
            Instance.OnMetaPhaseEntered -= handler;
            Instance.OnMetaPhaseEntered += handler;
            handler(Instance.CurrentMetaState);
        }
        // Instance가 null이면 Awake보다 먼저 호출된 것 — 이 프로젝트 구조상 발생하지 않음
    }

    /// <summary>
    /// OnMetaPhaseEntered 구독을 해제합니다.
    /// </summary>
    public static void Unsubscribe(Action<MetaState> handler)
    {
        if (Instance != null)
            Instance.OnMetaPhaseEntered -= handler;
    }

    private void Awake()
    {
        // 단일 씬 방식에서의 씬 재로드는 '모든 것의 리셋'을 의미하므로
        // DontDestroyOnLoad를 제거하여 씬 로드 시 모든 시스템이 깨끗이 파괴되게 합니다.
        Instance = this;
        InitializeSystems();
    }

    private void Start()
    {
        if (enableAutoLaunch)
            StartCoroutine(AutoLaunchRoutine());

        // [가이드 준수] 전역 매니저로서 실무 시스템의 상태 변화를 관찰(Subscribe)합니다.
        if (GamePlayManager.Instance != null)
        {
            GamePlayManager.Instance.OnCookingPhaseEntered += HandleCookingPhaseChanged;
        }
    }

    private void HandleCookingPhaseChanged(CookingState newState)
    {
        // [방장 전용 로직] 요리가 제출(Submit) 상태가 되면 메타 스테이트를 '평가(Feedback)'로 자동 전환합니다.
        // MonoBehaviour이므로 대리인(_proxy)을 통해 권한을 확인합니다.
        if (_proxy != null && _proxy.Object != null && _proxy.Object.HasStateAuthority && newState == CookingState.Submit)
        {
            Debug.Log("📣 [SystemManager] 요리 제출 감지 -> 메타 상태를 Feedback으로 전환합니다.");
            ChangeMetaState(MetaState.Feedback);
        }
    }

    private void OnDestroy()
    {
        if (GamePlayManager.Instance != null)
            GamePlayManager.Instance.OnCookingPhaseEntered -= HandleCookingPhaseChanged;
    }

    private IEnumerator AutoLaunchRoutine()
    {
        Debug.Log($"🚀 [Debug] {debugRoomCode}번 방으로 자동 접속 시도 중... (역할: {debugRole})");

        while (NetworkLauncher.Instance == null) yield return null;

        // [수정] 디버그 모드에서는 방이 없을 때 자동 생성될 수 있도록 true를 전달합니다.
        var joinTask = NetworkLauncher.Instance.JoinOrCreateRoom(debugRoomCode, true);
        yield return new WaitUntil(() => joinTask.IsCompleted);

        if (!joinTask.Result)
        {
            Debug.LogError("❌ [Debug] 방 접속에 실패했습니다.");
            yield break;
        }

        while (LobbyPlayer.Local == null) yield return null;

        LobbyPlayer.Local.SetRole(debugRole);
        LobbyPlayer.Local.SetReady(true);
        Debug.Log($"✅ [Debug] 역할({debugRole}) 설정 및 준비 완료.");

        if (NetworkLauncher.Instance.Runner.IsSharedModeMasterClient)
        {
            Debug.Log($"⏳ [Debug] {hostStartDelay}초 대기 후 게임 시작...");
            yield return new WaitForSeconds(hostStartDelay);

            if (NetworkLauncher.Instance.Runner.IsSharedModeMasterClient)
            {
                // [추가] 디버그 모드(AutoLaunch) 시 1스테이지를 기본으로 설정합니다.
                useTestMap = false;
                SelectedStage = 12;
                NetworkLauncher.Instance.StartGame();
            }
        }
    }

    private void InitializeSystems()
    {
        // 모든 하위 실무 시스템(UI, 오디오, 맵, 게임플레이 등)을 로컬 오브젝트로 생성
        // Architecture_guide.md의 "Container Pattern"에 따라 각 시스템은 독립적인 공간을 가집니다.
        if (uiSystemPrefab) Instantiate(uiSystemPrefab, transform);
        if (audioSystemPrefab) Instantiate(audioSystemPrefab, transform);
        if (networkSystemPrefab) Instantiate(networkSystemPrefab, transform);
        if (mapSystemPrefab) Instantiate(mapSystemPrefab, transform);
        if (gamePlaySystemPrefab) Instantiate(gamePlaySystemPrefab, transform);
    }

    // Proxy가 스폰되면 연결합니다.
    public void LinkNetworkProxy(GlobalNetworkState proxy)
    {
        _proxy = proxy;
        Debug.Log("🧬 [SystemManager] 정식 네트워크 대리인(Proxy)과 연결되었습니다.");
        
        SyncFromProxy(_proxy.CurrentMetaState);
    }

    // [추가] 네트워크 종료 시 호출하여 연결을 끊습니다.
    public void ResetLocalState()
    {
        _proxy = null;
    }

    public void ChangeMetaState(MetaState newState)
    {
        if (_proxy == null || !_proxy.Object.IsValid)
        {
            Debug.LogWarning($"⚠️ [SystemManager] 아직 네트워크 대리인이 준비되지 않았습니다. (요청: {newState})");
            return;
        }

        if (_proxy.Object.HasStateAuthority)
        {
            _proxy.CurrentMetaState = newState;
            Debug.Log($"🌐 [SystemManager] Global Phase -> {newState} (Direct via Proxy)");
        }
        else
        {
            Debug.Log($"🌐 [SystemManager] Global Phase -> {newState} (Requesting via RPC on Proxy)");
            _proxy.RPC_RequestMetaStateChange(newState);
        }
    }

    // Proxy에서 상태 변화가 감지되면 실행
    public void SyncFromProxy(MetaState newState)
    {
        Debug.Log($"📣 [SystemManager] Sync from Proxy: {newState}");
        LastMetaState = newState; // 마지막 확인된 유효 상태를 기록합니다.
        OnMetaPhaseEntered?.Invoke(newState);
    }

    // [추가] 네트워크와 상관없이 로컬 상태를 강제로 바꾸고 이벤트를 알립니다. (종료 시 사용)
    public void ForceLocalMetaState(MetaState newState)
    {
        Debug.Log($"🏠 [SystemManager] Local Phase Forced -> {newState}");
        OnMetaPhaseEntered?.Invoke(newState);
    }

    #region [Refactoring] Player Disconnection Handling

    /// <summary>
    /// 다른 플레이어가 이탈했을 때 현재 상태에 따라 흐름을 결정합니다.
    /// </summary>
    public void HandleOtherPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // 1. 인게임 핵심 단계 (주문 대화 ~ 결과창)일 때 -> 메인으로 동반 퇴장
        if (LastMetaState >= MetaState.OrderDialogue && LastMetaState <= MetaState.Result)
        {
            Debug.LogWarning("⚠️ [System] 인게임 도중 플레이어 이탈 감지. 종료 시퀀스를 시작합니다.");
            
            var loadingUI = LoadingUI.Instance ?? FindFirstObjectByType<LoadingUI>(FindObjectsInactive.Include);
            if (loadingUI != null)
                loadingUI.ShowPlayerLeftAndExit();
            else
                NetworkLauncher.Instance?.LeaveSession();
        }
        // 2. 준비 단계 (라운드 선택, 준비창)일 때 -> 로비로 안전 복귀
        else if (LastMetaState == MetaState.StageSelection || LastMetaState == MetaState.ReadyConfirmation)
        {
            Debug.Log($"👥 [System] 준비 단계({LastMetaState})에서 인원 부족. 로비 복귀 시퀀스를 시작합니다.");
            
            // 로컬 UI 즉시 전환
            ForceLocalMetaState(MetaState.Lobby);

            // [수정] 방장 권한이 넘어오는 타이밍을 기다리기 위해, 체크 없이 루틴을 한 번 더 실행합니다.
            StopAllCoroutines();
            StartCoroutine(ResetNetworkStateRoutine(runner));
        }
    }

    private IEnumerator ResetNetworkStateRoutine(NetworkRunner runner)
    {
        Debug.Log("🔄 [System] 네트워크 상태 초기화 루틴 시작...");
        
        // [중요] 퓨전 내부의 마스터 클라이언트 위임 처리를 위해 아주 잠깐 대기합니다. (Race Condition 방지)
        yield return new WaitForSeconds(0.2f);

        GlobalNetworkState proxy = GlobalNetworkState.Instance;
        
        // Proxy가 없다면 새로 소환 (새로 소환된 객체는 Lobby상태로 시작)
        if (proxy == null || !proxy.Object.IsValid)
        {
            var prefab = NetworkLauncher.Instance?.globalNetworkStatePrefab;
            if (prefab != null)
                runner.Spawn(prefab, Vector3.zero, Quaternion.identity);
            yield break;
        }

        // 권한 획득 대기 (최대 2초)
        if (!proxy.Object.HasStateAuthority)
        {
            Debug.Log("🔄 [System] Proxy에 대한 StateAuthority 획득을 시도합니다.");
            proxy.Object.RequestStateAuthority();
            
            float timeout = 2.0f;
            while (timeout > 0 && !proxy.Object.HasStateAuthority)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        // 상태 교정
        if (proxy.Object.HasStateAuthority)
        {
            proxy.CurrentMetaState = MetaState.Lobby;
            proxy.SelectedStage = 0;
            Debug.Log("✅ [System] 네트워크 상태 초기화 완료 (권한 획득 후 직접 수정)");
        }
        else
        {
            proxy.RPC_ResetToLobby();
            Debug.LogWarning("⚠️ [System] 권한 획득 지연으로 RPC 초기화 신호를 전송했습니다.");
        }

        // [추가] 중요: 권한 위임 및 상태 리셋 작업이 완료되었음을 UI에 알립니다. (방장 위임 시 버튼 갱신용)
        ForceLocalMetaState(MetaState.Lobby);
    }

    #endregion
}
