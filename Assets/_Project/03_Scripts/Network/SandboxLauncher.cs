using Fusion;
using UnityEngine;

/// <summary>
/// 씬 시작 시 자동으로 Fusion 세션을 시작해주는 테스트용 런처.
/// </summary>
[RequireComponent(typeof(NetworkRunner))]
public class SandboxLauncher : MonoBehaviour
{
    public GameMode gameMode = GameMode.AutoHostOrClient;
    public string sessionName = "SandboxSession";

    private async void Start()
    {
        NetworkRunner runner = GetComponent<NetworkRunner>();
        runner.ProvideInput = true;

        await runner.StartGame(new StartGameArgs()
        {
            GameMode = gameMode,
            SessionName = sessionName,
            SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
    }
}
