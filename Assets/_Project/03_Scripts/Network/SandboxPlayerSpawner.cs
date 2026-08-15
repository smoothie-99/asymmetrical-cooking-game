using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 샌드박스/테스트용 플레이어 스포너.
/// 프로덕션용 PlayerSpawner의 복잡한 조건(라운드 상태, 특정 씬 제약 등) 없이 
/// 런너가 시작되면 즉시 로컬 플레이어를 소환합니다.
/// </summary>
public class SandboxPlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Prefabs")]
    public NetworkObject playerPrefab;
    
    [Header("Spawn Settings")]
    public Transform spawnPoint;

    private void Start()
    {
        // 씬에 이미 런너가 있다면 콜백 등록
        NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null)
        {
            runner.AddCallbacks(this);
            if (runner.IsRunning)
            {
                SpawnPlayer(runner, runner.LocalPlayer);
            }
        }
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player == runner.LocalPlayer)
        {
            SpawnPlayer(runner, player);
        }
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        // 이미 캐릭터가 있는지 확인
        foreach (var obj in runner.GetAllNetworkObjects())
        {
            if (obj.InputAuthority == player && obj.name.Contains(playerPrefab.name))
            {
                return;
            }
        }

        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        pos += new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 0, UnityEngine.Random.Range(-0.5f, 0.5f));
        
        runner.Spawn(playerPrefab, pos, Quaternion.identity, player);
        
        // 씬의 다른 모든 카메라들 끄기 (리스너 충돌 방지)
        GameObject[] anyCams = GameObject.FindGameObjectsWithTag("MainCamera");
        foreach (var cam in anyCams)
        {
            if (cam.transform.parent == null) cam.SetActive(false);
        }
    }

    // --- 필수 콜백 구현 (비워둠) ---
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
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
