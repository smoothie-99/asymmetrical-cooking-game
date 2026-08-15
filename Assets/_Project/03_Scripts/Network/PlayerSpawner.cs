using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 스폰 및 생성 로직 전담 스크립트
/// </summary>
public class PlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Prefabs")]
    public NetworkObject cookingMasterPrefab; // 요리사 프리팹
    public NetworkObject recipeMasterPrefab;  // 레시피 마스터 프리팹 (추후 사용)


    private bool _hasSpawned = false; // 중복 소환 방지용 로컬 플래그

    private void Start()
    {
        // 씬 로드 시 현재 러너에 콜백 등록
        if (NetworkLauncher.Instance != null && NetworkLauncher.Instance.Runner != null)
        {
            NetworkLauncher.Instance.Runner.AddCallbacks(this);
            Debug.Log("👤 [Spawner] Runner에 콜백 등록 완료.");
        }

        // 전역 메타 상태 구독 (다시하기 시 스폰 플래그 리셋용)
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.OnMetaPhaseEntered += HandleMetaStateChanged;
        }

        // [수정] GamePlayManager가 없으면 생성될 때까지 대기 후 구독합니다.
        if (GamePlayManager.Instance != null)
        {
            SubscribeToGamePlayManager();
        }
        else
        {
            StartCoroutine(WaitForGamePlayManager());
        }
    }

    private IEnumerator WaitForGamePlayManager()
    {
        yield return new WaitUntil(() => GamePlayManager.Instance != null);
        SubscribeToGamePlayManager();
    }

    private void SubscribeToGamePlayManager()
    {
        if (GamePlayManager.Instance == null) return;

        GamePlayManager.Instance.OnCookingPhaseEntered += OnPhaseEntered;

        if (GamePlayManager.Instance.CurrentCookingState == CookingState.Cooking || 
            GamePlayManager.Instance.CurrentCookingState == CookingState.Ready)
        {
            Debug.Log($"🍳 [Spawner] 초기화 시점에 이미 {GamePlayManager.Instance.CurrentCookingState} 상태 - 소환을 시도합니다.");
            TrySpawnLocalPlayer();
        }
    }

    private void OnDestroy()
    {
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.OnMetaPhaseEntered -= HandleMetaStateChanged;
        }

        if (GamePlayManager.Instance != null)
        {
            GamePlayManager.Instance.OnCookingPhaseEntered -= OnPhaseEntered;
        }
    }

    private void HandleMetaStateChanged(MetaState newState)
    {
        // [핵심] 주문창(OrderDialogue)이나 로비, 혹은 게임 종료/준비 확인 단계로 진입하면 캐릭터를 제거합니다.
        // 이를 통해 '다시하기' 시 카운트다운 동안 기존 캐릭터가 남아있는 현상을 방지합니다.
        if (newState == MetaState.OrderDialogue || newState == MetaState.Lobby || 
            newState == MetaState.ReadyConfirmation || newState == MetaState.Result ||
            newState == MetaState.StageSelection) // [추가] 라운드 선택 시에도 기존 캐릭터를 제거합니다.
        {
            if (_hasSpawned)
            {
                Debug.Log($"🔄 [Spawner] 상태 변경({newState})에 따라 기존 플레이어 제거를 수행합니다.");
                _hasSpawned = false;
                DespawnLocalPlayer();
            }
        }
    }

    private void DespawnLocalPlayer()
    {
        var runner = NetworkLauncher.Instance?.Runner;
        if (runner == null || !runner.IsRunning) return;

        // [중요] 모든 네트워크 오브젝트 중 내가 주인인 캐릭터를 찾아 확실히 제거합니다.
        foreach (var obj in runner.GetAllNetworkObjects())
        {
            if (obj.InputAuthority == runner.LocalPlayer)
            {
                string objName = obj.name.ToLower();
                bool isTarget = objName.Contains("master") || objName.Contains("chef");

                if (isTarget)
                {
                    if (obj.HasStateAuthority)
                    {
                        runner.Despawn(obj);
                        Debug.Log($"👤 [Spawner] [Despawn] {obj.name} 제거 성공.");
                    }
                    else
                    {
                        // 클라이언트 모드 등에서 직접 권한이 없는 경우를 위한 추가 로그
                        Debug.LogWarning($"⚠️ [Spawner] {obj.name}을 찾았으나 상태 권한(StateAuthority)이 없어 직접 제거할 수 없습니다.");
                    }
                }
            }
        }
    }

    private void OnPhaseEntered(CookingState state)
    {
        // [수정] Cooking 뿐만 아니라 Ready(카운트다운) 단계에서도 플레이어를 소환합니다.
        // 이를 통해 인게임 카메라 시점에서 카운트다운을 지켜볼 수 있게 합니다.
        if (state == CookingState.Ready || state == CookingState.Cooking)
        {
            Debug.Log($"🍳 [Spawner] 요리 세션 단계 진입({state})! 플레이어 소환을 시도합니다.");
            TrySpawnLocalPlayer();
        }
    }

    /// <summary>
    /// 로컬 플레이어 소환을 통합 관리하는 함수
    /// </summary>
    private void TrySpawnLocalPlayer()
    {
        if (NetworkLauncher.Instance == null || NetworkLauncher.Instance.Runner == null) return;
        
        var runner = NetworkLauncher.Instance.Runner;
        if (runner.IsRunning)
        {
            // [수정] 바로 스폰하지 않고, 맵이 완성될 때까지 기다리는 코루틴 실행!
            StartCoroutine(WaitAndSpawnPlayer(runner, runner.LocalPlayer));
        }
    }

    // 🌟 [추가] 맵이 완벽하게 다 그려질 때까지 기다렸다가 스폰하는 코루틴
    private System.Collections.IEnumerator WaitAndSpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        MapGenerator mapGen = null;

        // 1. MapGenerator가 씬에 나타날 때까지 대기
        while (mapGen == null)
        {
            mapGen = FindAnyObjectByType<MapGenerator>();
            yield return null;
        }

        Debug.Log("⏳ [Spawner] 맵 데이터를 받는 중... 대기합니다.");

        // 2. 맵 데이터 동기화가 끝나고 타일 생성이 완료될 때까지 대기
        while (!mapGen.IsMapReady)
        {
            yield return null;
        }

        Debug.Log("✅ [Spawner] 맵 생성 완료 확인! 드디어 캐릭터를 스폰합니다!");

        // 3. 맵이 준비되었으니 안전하게 스폰!
        SpawnPlayer(runner, player);
    }

    public void OnSceneLoadDone(NetworkRunner runner) 
    {
        Debug.Log("👤 [Spawner] 씬 로드 완료. 주문창을 대기하며 스폰을 미룹니다.");
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null || !runner.IsRunning) return;

        // [추가] 이미 소환했다면 더 이상 진행하지 않음 (로컬 검사)
        if (_hasSpawned) return;

        // P4의 단일 씬 구조에 맞게 씬 이름을 확인합니다.
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (currentSceneName != "00_Main_Game")
        {
            Debug.Log($"👤 [Spawner] 현재 씬이 {currentSceneName}이므로 스폰을 대기합니다.");
            return;
        }

        // 이미 내 캐릭터가 있는지 확인 (네트워크 검사 - 안전망)
        foreach (var obj in runner.GetAllNetworkObjects())
        {
            if (obj.InputAuthority == player && 
                (obj.name.Contains("CookingMaster") || obj.name.Contains("Cooking_Master") || 
                 obj.name.Contains("RecipeMaster") || obj.name.Contains("Recipe_Master")))
            {
                Debug.Log("👤 [Spawner] 이미 네트워크상에 캐릭터가 존재하여 스폰을 건너뜁니다.");
                _hasSpawned = true; // 이미 존재하므로 소환된 것으로 간주
                return;
            }
        }

        NetworkObject prefabToSpawn = GetPlayerPrefab(runner, player);
        if (prefabToSpawn == null) return;

        // [추가] 실제 소환 직전에 플래그를 먼저 세워 중복 진입 차단 (Optimistic Lock)
        _hasSpawned = true;

        Vector3 spawnPos = GetSpawnPosition();

        // 조리대 방향을 바라보도록 회전값 계산 (MapGenerator가 있을 때만)
        Quaternion spawnRot = Quaternion.identity;
        MapGenerator mapGen = FindAnyObjectByType<MapGenerator>();
        if (mapGen != null && mapGen.IsMapReady)
        {
            Vector2Int tilePos = new Vector2Int(Mathf.RoundToInt(spawnPos.x), Mathf.RoundToInt(spawnPos.z));
            spawnRot = mapGen.GetLookRotation(tilePos);
        }

        var spawned = runner.Spawn(prefabToSpawn, spawnPos, spawnRot, player);

        if (spawned != null)
        {
            Debug.Log($"✅ [Spawner] {prefabToSpawn.name} 스폰 성공! (ID: {player.PlayerId})");
            
            // 씬의 기본 카메라(주로 'Main Camera'라는 이름의 루트 오브젝트)를 찾아 비활성화
            // 단순히 Tag로 찾으면 이미 스폰된 플레이어의 카메라를 건드릴 위험이 있으므로 이름과 구조로 찾습니다.
            GameObject sceneMainCam = GameObject.Find("Main Camera");
            if (sceneMainCam != null && sceneMainCam.transform.parent == null)
            {
                sceneMainCam.SetActive(false);
                Debug.Log("🎥 [Spawner] 씬의 기본 카메라를 찾아 비활성화했습니다.");
            }
        }
    }

    private NetworkObject GetPlayerPrefab(NetworkRunner runner, PlayerRef player)
    {
        // [수정] 네트워크 동기화된 LobbyPlayer를 1순위로 확인합니다.
        var lp = LobbyPlayer.Get(player);
        int roleInt = (lp != null) ? lp.SelectedRole : 0;

        if (lp != null)
        {
            Debug.Log($"👤 [Spawner] LobbyPlayer.SelectedRole: {roleInt} (Player: {player.PlayerId})");
        }

        // LobbyPlayer에 정보가 없거나 0인 경우에만 로컬 static 변수 fallback
        if (roleInt == 0 && player == runner.LocalPlayer)
        {
            roleInt = (int)NetworkLauncher.SelectedJob;
            Debug.Log($"👤 [Spawner] SelectedJob Fallback 값: {NetworkLauncher.SelectedJob} → roleInt: {roleInt}");
        }

        // 최종 확인
        Debug.Log($"👤 [Spawner] 최종 roleInt: {roleInt} → {(roleInt == 1 ? "RecipeMaster" : roleInt == 2 ? "CookingMaster" : "없음")}");
        
        if (roleInt == 1) return recipeMasterPrefab;
        if (roleInt == 2) return cookingMasterPrefab;
        
        Debug.LogWarning($"⚠️ [Spawner] roleInt={roleInt} 역할 없음 - 스폰 취소");
        return null;
    }


    private Vector3 GetSpawnPosition()
    {
        Vector3 pos = Vector3.zero;

        // 프로시저럴 맵이 있으면 맵의 빈 타일 위치 사용
        MapGenerator mapGen = FindAnyObjectByType<MapGenerator>();
        if (mapGen != null && mapGen.IsMapReady)
        {
            return mapGen.GetRandomSpawnPosition();
        }

        else
        {
            GameObject found = GameObject.Find("SpawnPoint");
            if (found != null) pos = found.transform.position;
        }

        // 겹침 방지를 위해 약간의 랜덤 오프셋
        pos += new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 0, UnityEngine.Random.Range(-0.5f, 0.5f));
        return pos;
    }

    // --- 나머지 콜백 필수 구현 (비워둠) ---
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) 
    {
        // [핵심 수정] 단일 씬 구조에서는 씬 이름만으로는 부족합니다.
        // 반드시 Cook 단계일 때만 소환해야 메인메뉴에서 커서가 잠기지 않습니다.
        bool isCookPhase = GamePlayManager.Instance != null 
            && GamePlayManager.Instance.CurrentCookingState == CookingState.Cooking;

        if (player == runner.LocalPlayer && runner.IsRunning && isCookPhase)
        {
            SpawnPlayer(runner, player);
        }
    }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
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
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
