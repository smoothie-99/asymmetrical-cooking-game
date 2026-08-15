using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class NetworkLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    public enum PlayerJob { None = 0, RecipeMaster = 1, CookingMaster = 2 }
    public static PlayerJob SelectedJob = PlayerJob.None;
    
    public static NetworkLauncher Instance { get; private set; }
    public NetworkRunner Runner => _runner;

    public static System.Action OnPlayerJoinedOrLeft;
    private NetworkRunner _runner;
    private bool _isLeavingSession = false; // [추가] 내가 현재 방을 나가는 중인지 기록
    private Photon.Voice.Unity.Recorder _voiceRecorder; // [추가] 푸시투톡 제어용 레코더
    private System.Collections.Generic.List<Photon.Voice.Unity.Speaker> _activeSpeakers = new System.Collections.Generic.List<Photon.Voice.Unity.Speaker>(); // [추가] 실시간 자막/UI용 스피커 링크

    [Header("Voice Settings")]
    public bool showVoiceDebugUI = true; // [추가] 화면에 말하기 UI 표시 여부



    [Header("Lobby Settings")]
    public NetworkObject lobbyPlayerPrefab; 
    public NetworkObject globalNetworkStatePrefab; // 추가: 정식 네트워크 대리인 프리팹

    [Header("Exit Settings")]
    public GameObject exitWaitingPanel; // 새롭게 만들 '메인으로 이동 중' 안내창

    private void Awake()
    {
        // 씬 내에서 Launcher를 찾을 수 있게 인스턴스만 할당합니다.
        // 불사신(DontDestroyOnLoad) 설정은 씬 리셋을 방해하므로 제거합니다.
        Instance = this;
    }

    public async Task<bool> JoinOrCreateRoom(string roomName, bool isCreating = false)
    {
        // [추가] 로컬에서 기억하던 직업 정보를 리셋합니다.
        SelectedJob = PlayerJob.None;

        // 1. 기존 런너가 있다면 확실히 정리
        if (_runner != null)
        {
            Debug.Log("🌐 [Launcher] 기존 세션 종료 중...");
            await _runner.Shutdown();
            if (_runner != null && _runner.gameObject != this.gameObject) 
            {
                Destroy(_runner.gameObject);
            }
            _runner = null;
        }

        // 2. 새 런너를 전용 오브젝트로 생성 (안정성 강화)
        GameObject runnerObj = new GameObject("FusionRunner_" + roomName);
        runnerObj.transform.SetParent(this.transform);
        _runner = runnerObj.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;
        
        // [추가] 3. Photon Voice 클라이언트 세팅
        // runnerObj에 Voice 클라이언트와 Recorder를 부착하고, AppSettings 연동 설정을 켭니다.
        var voiceClient = runnerObj.AddComponent<Photon.Voice.Fusion.FusionVoiceClient>();
        _voiceRecorder = runnerObj.AddComponent<Photon.Voice.Unity.Recorder>();
        
        // Recorder 설정 (클릭/누를 때만 전송하도록 기본값 false)
        _voiceRecorder.TransmitEnabled = false;
        // voiceClient 설정
        voiceClient.PrimaryRecorder = _voiceRecorder;
        voiceClient.UseFusionAppSettings = true; // Fusion의 AppId 등 공유

        // [추가] 4. 음성 연결 상태 모니터링 로그
        voiceClient.SpeakerLinked += (speaker) => {
            if (!_activeSpeakers.Contains(speaker))
            {
                _activeSpeakers.Add(speaker);
                // 상대방이 나가거나 보이스 해제 시 목록에서 제거
                speaker.OnRemoteVoiceRemoveAction += (s) => {
                    _activeSpeakers.Remove(speaker);
                };
            }
            Debug.Log($"🎙️ [Voice] 상대방 스피커 연결됨! 소리 출력 위치: {speaker.gameObject.name}");
        };

        // 또는 연결 상태 변화 로그
        if (voiceClient.Client != null)
        {
            voiceClient.Client.StateChanged += (fromState, toState) => {
                if (toState == Photon.Realtime.ClientState.Joined)
                {
                    Debug.Log("🎙️ [Voice] 음성 서버 방 입장 성공!");
                }
            };
        }

        // 콜백 등록
        _runner.AddCallbacks(this);
        _runner.AddCallbacks(voiceClient); // [추가] 보이스 클라이언트가 방 입장 이벤트를 받게 하려면 콜백 직접 등록이 필요!

        Debug.Log($"🌐 [Launcher] 세션 접속 시도 ({(isCreating ? "생성" : "참가")}): {roomName}");

        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            PlayerCount = isCreating ? 2 : (int?)null, // ★ 생성 시 2명 인원 제한
            EnableClientSessionCreation = isCreating, // ★ 참가 시 방 자동 생성 차단
            SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (result.Ok)
        {
            Debug.Log($"✅ [Launcher] {(isCreating ? "방 생성 및" : "")} 접속 성공! (방: {roomName})");
            return true;
        }
        else
        {
            // [수정] 방이 없거나 꽉 찬 경우 등 '예상 가능한 실패'는 에러(Error) 대신 경고(Warning)로 처리합니다.
            if (result.ShutdownReason == ShutdownReason.GameNotFound || result.ShutdownReason == ShutdownReason.GameIsFull)
            {
                Debug.LogWarning($"⚠️ [Launcher] 접속 실패 (무시 가능): {result.ShutdownReason}");
            }
            else
            {
                Debug.LogError($"❌ [Launcher] 접속 실패: {result.ShutdownReason}");
            }
            if (_runner != null) Destroy(_runner.gameObject);
            _runner = null;
            return false;
        }
    }

    // --- UI 버튼 연동용 편의 메서드 ---
    public void CreateRoom(string roomName) => _ = JoinOrCreateRoom(roomName, true);
    public void JoinRoom(string roomName) => _ = JoinOrCreateRoom(roomName, false);

    public void StartGame()
    {
        Debug.Log($"🚀 [Launcher] StartGame() 호출됨. (Host: {_runner?.IsSharedModeMasterClient})");
        if (_runner != null && _runner.IsSharedModeMasterClient)
        {
            if (SystemManager.Instance != null)
            {
                SystemManager.Instance.ChangeMetaState(MetaState.OrderDialogue);
                Debug.Log("🚀 [Launcher] SystemManager.Instance.ChangeMetaState(OrderDialogue) 완료");
            }
            else
            {
                Debug.LogError("❌ [Launcher] SystemManager.Instance가 존재하지 않습니다!");
            }
        }
    }

    /// <summary>
    /// 세션을 종료하고 메인 메뉴(초기 상태)로 돌아갑니다.
    /// @param showWaitingPanel: '메인으로 이동 중...' 안내창을 띄울지 여부
    /// </summary>
    public async void LeaveSession(bool showWaitingPanel = true)
    {
        _isLeavingSession = true; // [추가] 이제부터 나가는 중이므로 다른 사람의 이탈 경고를 무시합니다.

        // 1. 안내창 노출 여부를 결정합니다.
        if (showWaitingPanel)
        {
            if (exitWaitingPanel == null)
            {
                var canvas = GameObject.Find("Canvas_Evaluation_1");
                if (canvas != null)
                {
                    Transform found = canvas.transform.Find("ExitWaitingPanel");
                    if (found != null) exitWaitingPanel = found.gameObject;
                }
                if (exitWaitingPanel == null) exitWaitingPanel = GameObject.Find("ExitWaitingPanel");
            }

            if (exitWaitingPanel != null)
            {
                exitWaitingPanel.SetActive(true);
                exitWaitingPanel.transform.SetAsLastSibling();

                // [추가] 패널이 켜질 때, 안내 문구도 기본값으로 갱신해 줍니다.
                // InGameMenuUI 싱글톤이 있다면 텍스트 오브젝트를 찾아 "메인 메뉴로 이동 중..."을 적습니다.
                if (InGameMenuUI.Instance != null)
                {
                    var textMesh = exitWaitingPanel.GetComponentInChildren<TMPro.TMP_Text>();
                    if (textMesh != null) textMesh.text = "메인 메뉴로 이동 중입니다...";
                }
            }
        }
        else
        {
            // 강제 종료 모드 등에서는 기존 안내창이 있다면 오히려 끕니다.
            if (exitWaitingPanel != null) exitWaitingPanel.SetActive(false);
        }

        if (_runner != null)
        {
            Debug.Log("🌐 [Launcher] 세션 종료 요청... RPC 전송을 위해 잠시 대기합니다.");
            // [중요] RPC가 상대방에게 도달할 수 있도록 0.5초 정도 대기 시간을 늘립니다.
            await Task.Delay(500);
            
            if (_runner != null) await _runner.Shutdown();
        }
        else
        {
            ReloadMainScene();
        }
    }

    private bool isReloading = false;
    private void ReloadMainScene()
    {
        if (isReloading) return;
        isReloading = true;
        
        StopAllCoroutines();
        StartCoroutine(ReloadRoutine());
    }

    private System.Collections.IEnumerator ReloadRoutine()
    {
        // 1. 안내창을 정해진 시간(1초) 동안 보여줍니다.
        Debug.Log("🏠 [Launcher] 메인 메뉴 복귀 대기 중 (1.0초)...");
        yield return new WaitForSecondsRealtime(1.0f);

        // 2. 커서 잠금 해제
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 3. 씬 새로고침 (DontDestroyOnLoad가 없으므로 모든 시스템이 새로 태어납니다)
        Debug.Log("🏠 [Launcher] 씬을 새로 로드하여 모든 시스템을 리셋합니다.");
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        
        isReloading = false;
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player == runner.LocalPlayer && lobbyPlayerPrefab != null)
        {
            runner.Spawn(lobbyPlayerPrefab, Vector3.zero, Quaternion.identity, player);

            // [추가] 방장이라면 게임의 정식 네트워크 대리인(GlobalNetworkState)을 소환합니다.
            if (runner.IsSharedModeMasterClient && globalNetworkStatePrefab != null)
            {
                runner.Spawn(globalNetworkStatePrefab, Vector3.zero, Quaternion.identity);
                Debug.Log("🌐 [Launcher] 방장이 GlobalNetworkState를 정식으로 소환했습니다.");
            }
        }
        else if (player != runner.LocalPlayer)
        {
            // [추가] 다른 플레이어가 들어왔을 때, 만약 재접속 대기 중이었다면 해제합니다.
            if (LoadingUI.Instance != null && LoadingUI.Instance.gameObject.activeInHierarchy)
            {
                LoadingUI.Instance.OnReconnected();
            }
        }
        
        // [추가] 플레이어 입장 시 UI 갱신을 위해 이벤트를 발생시킵니다.
        OnPlayerJoinedOrLeft?.Invoke();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) 
    {
        // 1. 퇴장한 플레이어의 네트워크 캐릭터(LobbyPlayer)를 즉시 제거합니다.
        LobbyPlayer lp = LobbyPlayer.Get(player);
        if (lp != null && lp.Object != null && lp.Object.IsValid)
        {
            runner.Despawn(lp.Object);
        }

        // 2. [Refactored] 메타 상태 및 UI 흐름 제어는 SystemManager에게 위임합니다.
        // [수정] 내가 나가는 중(_isLeavingSession)이라면 상대방의 이탈 로직을 타지 않습니다.
        if (SystemManager.Instance != null && player != runner.LocalPlayer && !_isLeavingSession)
        {
            SystemManager.Instance.HandleOtherPlayerLeft(runner, player);
        }

        // 3. UI 갱신을 위해 이벤트를 알립니다.
        OnPlayerJoinedOrLeft?.Invoke();
    }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) 
    { 
        Debug.Log($"🌐 [Launcher] OnShutdown 완료 (사유: {shutdownReason})");
        
        // 1. 종료 전의 메타 상태를 확인합니다.
        MetaState lastState = MetaState.None;
        if (SystemManager.Instance != null)
        {
            lastState = SystemManager.Instance.LastMetaState; // [수정] Proxy 유무와 상관없는 마지막 상태 사용
            SystemManager.Instance.ResetLocalState();
        }

        _runner = null;

        // [핵심 로직 수정]
        // 1. 인게임 핵심 단계(OrderDialogue 이상)에서 종료되었거나,
        // 2. 비정상적인 오류(Ok가 아님)로 종료되었을 때 씬을 리셋합니다.
        // 단, '방이 없음(GameNotFound)'이나 '방이 꽉 참(GameIsFull)' 같은 단순 접속 실패는 씬을 리셋하지 않습니다.
        bool isBenignError = shutdownReason == ShutdownReason.GameNotFound || shutdownReason == ShutdownReason.GameIsFull;

        if ((lastState >= MetaState.OrderDialogue || shutdownReason != ShutdownReason.Ok) && !isBenignError)
        {
            Debug.Log($"🏠 [Launcher] 핵심 단계({lastState}) 또는 크리티컬 오류({shutdownReason})이므로 씬을 리셋합니다.");
            ReloadMainScene();
        }
        else
        {
            Debug.Log($"🏠 [Launcher] 단순 실패 또는 메뉴 단계 종료({shutdownReason}). 씬 리셋 없이 대기합니다.");
        }
    }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    private void Update()
    {
        if (_voiceRecorder != null)
        {
            // [수정] V 키를 누를 때마다 음성 전송 상태를 반전 (토글 방식)
            if (Input.GetKeyDown(KeyCode.V))
            {
                _voiceRecorder.TransmitEnabled = !_voiceRecorder.TransmitEnabled;
                Debug.Log($"🎙️ [Voice] 마이크 {(_voiceRecorder.TransmitEnabled ? "켜짐" : "꺼짐")}");
            }
        }
    }

    // [추가] 실시간 음성 상단 UI 출력
    private void OnGUI()
    {
        if (!showVoiceDebugUI) return;

        // 화면 왼쪽 상단에 반투명 박스 및 텍스트 그리기
        GUILayout.BeginArea(new Rect(10, 10, 300, 150), GUI.skin.box);
        GUILayout.Label("🎙️ [Photon Voice Debug]");

        if (_voiceRecorder != null && _voiceRecorder.TransmitEnabled && _voiceRecorder.IsCurrentlyTransmitting)
        {
            GUI.color = Color.green;
            GUILayout.Label("● 나: [말하는 중] (마이크 켜짐)");
        }
        else if (_voiceRecorder != null && _voiceRecorder.TransmitEnabled)
        {
            GUI.color = Color.yellow;
            GUILayout.Label("○ 나: [대기 중] (마이크 켜짐)");
        }
        else
        {
            GUI.color = Color.gray;
            GUILayout.Label("○ 나: [음소거] (V 누르면 토글)");
        }

        GUI.color = Color.cyan;
        foreach (var speaker in _activeSpeakers)
        {
            if (speaker != null && speaker.IsPlaying)
            {
                GUILayout.Label($"● {speaker.gameObject.name}: [말하는 중]");
            }
        }
        
        // 색상 초기화
        GUI.color = Color.white;
        GUILayout.EndArea();
    }
}

