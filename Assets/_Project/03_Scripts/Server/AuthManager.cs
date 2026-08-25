using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;

public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance;
    private string apiBaseUrl = "https://j14d107.p.ssafy.io/api"; // 서버 주소

    // [신규] 로그인 유저의 스테이지 전적 (2: Perfect, 1: Clear, 0: Failed / null이면 게스트/비로그인)
    public static int[] StageRecords { get; private set; } = null;

    /// <summary>
    /// [핵심] 현재 기록 중 '성공(1 이상)'한 가장 높은 라운드 번호를 반환합니다. 
    /// </summary>
    public static int GetMaxClearedRound()
    {
        if (StageRecords == null || StageRecords.Length == 0) return 0; // 아무 기록 없으면 0라운드 클리어

        int maxCleared = 0;
        for (int i = 0; i < StageRecords.Length; i++)
        {
            if (StageRecords[i] >= 1)
            {
                int roundNum = i + 1;
                if (roundNum > maxCleared) maxCleared = roundNum;
            }
        }
        return maxCleared;
    }

    /// <summary>
    /// [신규] 로컬 전적 데이터를 네트워트(LobbyPlayer) 상의 내 프로필에 동기화합니다.
    /// </summary>
    public static void SyncProgressionToNetwork()
    {
        if (LobbyPlayer.Local != null)
        {
            int maxCleared = GetMaxClearedRound();
            LobbyPlayer.Local.SetMaxClearedRound(maxCleared);
            Debug.Log($"📡 [Auth] 로컬 전적({maxCleared})을 네트워크 플레이어에 동기화했습니다.");
        }
    }

    /// <summary>
    /// [신규] 로그인 결과물(DTO)을 받아 로컬 전적 데이터를 갱신합니다.
    /// </summary>
    public static void InitializeProgression(LoginResponse response)
    {
        // [신규] 게스트 상태에서 메인 메뉴 등으로 돌아왔을 때 기록이 초기화되는 것을 방지
        if (response == null && StageRecords != null && StageRecords.Length > 0)
        {
            Debug.Log("🔄 [Auth] 기존 게스트 전적 데이터를 유지합니다.");
            SyncProgressionToNetwork();
            return;
        }

        if (response != null && response.stageResults != null)
        {
            StageRecords = response.stageResults;
            Debug.Log($"📊 [Auth] 전적 동기화 완료! 현재 숙련도: {GetMaxClearedRound()}");
        }
        else
        {
            // 게스트로 새로 진입하거나 로그아웃 시 초기화
            StageRecords = new int[0];
            Debug.Log("🥚 [Auth] 새로운 게스트 계정으로 전적을 초기화합니다.");
        }

        // 현재 이미 방에 들어와 있다면 즉시 동기화
        SyncProgressionToNetwork();
    }

    private void Awake() => Instance = this;

    // 회원가입 요청
    public IEnumerator Register(SignupRequest data, System.Action<bool, string> callback)
    {
        string json = JsonUtility.ToJson(data);
        using (UnityWebRequest request = new UnityWebRequest($"{apiBaseUrl}/auth/signup", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            Debug.Log($"[Register] 응답 수신: {request.downloadHandler.text}");

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("[Register] 회원가입 성공!");
                callback?.Invoke(true, request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"[Register] 에러 발생: {request.error}");
                Debug.LogError($"[Register] 상세 에러: {request.downloadHandler.text}");
                callback?.Invoke(false, request.downloadHandler.text);
            }
        }
    }

    // 로그인 요청
    public IEnumerator Login(LoginRequest data, System.Action<bool, LoginResponse> callback)
    {
        string json = JsonUtility.ToJson(data);
        using (UnityWebRequest request = new UnityWebRequest($"{apiBaseUrl}/auth/login", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
                
                // [신규] 스테이지 해금 및 전적 정보를 로컬에 동기화합니다.
                InitializeProgression(response);

                // JWT 토큰 저장!
                PlayerPrefs.SetString("AccessToken", response.accessToken);
                PlayerPrefs.SetString("RefreshToken", response.refreshToken);
                callback?.Invoke(true, response);
            }
            else
            {
                callback?.Invoke(false, null);
            }
        }
    }

    // 아이디 중복 확인 요청
    public IEnumerator CheckID(string loginId, System.Action<bool, string> callback)
    {
        string url = $"{apiBaseUrl}/auth/check-id?loginId={UnityWebRequest.EscapeURL(loginId)}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            Debug.Log($"[CheckID] 서버 응답: {request.downloadHandler.text}");

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text.Trim().ToLower();

                // [수정] true면 중복(사용불가), false면 중복아님(사용가능)
                if (responseText == "true") 
                {
                    callback?.Invoke(false, "이미 존재하는 아이디입니다.");
                }
                else 
                {
                    callback?.Invoke(true, "사용 가능한 아이디입니다.");
                }
            }
            else
            {
                Debug.LogError($"[CheckID] 통신 에러: {request.error}");
                callback?.Invoke(false, "서버 연결에 실패했습니다.");
            }
        }
    }

    // [수정] 이메일 인증 확인 (이메일 기준)
    public IEnumerator CheckEmailVerified(string email, System.Action<bool> callback)
    {
        string url = $"{apiBaseUrl}/auth/is-verified?email={UnityWebRequest.EscapeURL(email)}";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                bool isVerified = request.downloadHandler.text.ToLower() == "true";
                callback?.Invoke(isVerified);
            }
            else
            {
                callback?.Invoke(false);
            }
        }
    }

    // 닉네임 중복 확인 요청
    public IEnumerator CheckNickname(string nickname, System.Action<bool, string> callback)
    {
        // 닉네임 중복 확인은 POST 방식 (기존 규격 유지)
        string json = JsonUtility.ToJson(new CheckNicknameRequest { nickname = nickname });

        using (UnityWebRequest request = new UnityWebRequest($"{apiBaseUrl}/auth/check-nickname", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                callback?.Invoke(true, "사용 가능한 닉네임입니다.");
            }
            else
            {
                string errorMsg = request.downloadHandler.text;
                callback?.Invoke(false, string.IsNullOrEmpty(errorMsg) ? "이미 존재하는 닉네임입니다." : errorMsg);
            }
        }
    }

    // 이메일 인증 메일 발송 요청
    public IEnumerator SendVerificationEmail(string email, System.Action<bool, string> callback)
    {
        string url = $"{apiBaseUrl}/auth/send-verification?email={UnityWebRequest.EscapeURL(email)}";
        
        using (UnityWebRequest request = UnityWebRequest.PostWwwForm(url, ""))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[SendVerificationEmail] 성공: {request.downloadHandler.text}");
                callback?.Invoke(true, request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"[SendVerificationEmail] 실패: {request.error}");
                callback?.Invoke(false, "메일 발송에 실패했습니다.");
            }
        }
    }

    // 스테이지 결과 서버 전송 (회원 전용)
    public IEnumerator UpdateStageRecord(int stageNumber, CookingOutcome outcome, System.Action<bool> callback = null)
    {
        // 결과 점수화 (2: Perfect, 1: Clear, 0: Failed)
        int resultScore = (outcome == CookingOutcome.PerfectClear) ? 2 : (outcome == CookingOutcome.Clear ? 1 : 0);

        // [핵심] 서버 성공 여부와 무관하게 로컬 메모리는 무조건 먼저 갱신! (세션 유지)
        if (StageRecords == null) StageRecords = new int[stageNumber];
        else if (StageRecords.Length < stageNumber)
        {
            int[] newArr = new int[stageNumber];
            Array.Copy(StageRecords, newArr, StageRecords.Length);
            StageRecords = newArr;
        }

        if (StageRecords[stageNumber - 1] < resultScore) 
        {
            StageRecords[stageNumber - 1] = resultScore;
            Debug.Log($"💾 [Auth] 로컬 전적 {stageNumber}단계 갱신: {resultScore}");
        }

        // 즉시 네트워크 동기화 (팀원들에게 알림)
        SyncProgressionToNetwork();

        string accessToken = PlayerPrefs.GetString("AccessToken", "");
        
        // 게스트나 비로그인 상태면 여기서 종료 (이미 로컬 갱신됨)
        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.Log($"[Auth] 게스트 모드입니다. {stageNumber}단계 결과를 로컬에만 저장하고 종료합니다.");
            callback?.Invoke(true);
            yield break;
        }

        string json = JsonUtility.ToJson(new GameResultRequest
        {
            stage = stageNumber,
            achievementLevel = resultScore
        });

        yield return SendGameResult(json, true, callback);
    }

    private IEnumerator SendGameResult(string json, bool allowRefresh, System.Action<bool> callback)
    {
        string accessToken = PlayerPrefs.GetString("AccessToken", "");
        using (UnityWebRequest request = new UnityWebRequest($"{apiBaseUrl}/game/result", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Auth] 서버 전적 백업 성공!");
                callback?.Invoke(true);
            }
            else if (request.responseCode == 401 && allowRefresh)
            {
                bool refreshed = false;
                yield return RefreshAccessToken(success => refreshed = success);
                if (refreshed)
                {
                    yield return SendGameResult(json, false, callback);
                }
                else
                {
                    callback?.Invoke(false);
                }
            }
            else
            {
                Debug.LogWarning($"[Auth] 서버 전적 저장 실패({request.responseCode}): {request.downloadHandler.text}");
                callback?.Invoke(false);
            }
        }
    }

    private IEnumerator RefreshAccessToken(System.Action<bool> callback)
    {
        string refreshToken = PlayerPrefs.GetString("RefreshToken", "");
        if (string.IsNullOrEmpty(refreshToken))
        {
            callback?.Invoke(false);
            yield break;
        }

        string json = JsonUtility.ToJson(new RefreshTokenRequest { refreshToken = refreshToken });
        using (UnityWebRequest request = new UnityWebRequest($"{apiBaseUrl}/auth/refresh", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                TokenResponse response = JsonUtility.FromJson<TokenResponse>(request.downloadHandler.text);
                PlayerPrefs.SetString("AccessToken", response.accessToken);
                PlayerPrefs.SetString("RefreshToken", response.refreshToken);
                callback?.Invoke(true);
            }
            else
            {
                PlayerPrefs.DeleteKey("AccessToken");
                PlayerPrefs.DeleteKey("RefreshToken");
                callback?.Invoke(false);
            }
        }
    }
}
