using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Collections;

[System.Serializable]
public class ServerData
{
    public string message;
    public string status;
}

public class ServerConnection : MonoBehaviour
{
    [Header("서버 설정")]
    // 나중에 배포할 때는 j14d107.p.ssafy.io 로 변경
    public string url = "https://nonembryonic-sprucely-juliane.ngrok-free.dev/api/game/start"; 

    [Header("이동할 씬 이름")]
    public string targetSceneName = "00_Main_Game";

    void Start()
    {
        Debug.Log("🚀 게임이 실행되었습니다. 서버 연동을 시작합니다!");
        StartCoroutine(CallGameStart());
    }

    IEnumerator CallGameStart()
    {
        // using을 써서 통신이 끝나면 자동으로 메모리를 정리해!
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;
                Debug.Log("🎉 서버 대답: " + jsonResponse);

                ServerData data = JsonUtility.FromJson<ServerData>(jsonResponse);

                if (data.status == "success")
                {
                    Debug.Log("✅ 인증 성공! " + targetSceneName + " 씬으로 이동합니다.");
                    SceneManager.LoadScene(targetSceneName);
                }
                else
                {
                    Debug.LogWarning("🤔 서버 fail: " + data.status);
                }
            }
            else
            {
                Debug.LogError("😭 서버 연결 실패: " + request.error);
            }
        }
    }
}

