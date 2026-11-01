using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;

public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance;

    [Header("Backend API")]
    [SerializeField] private string apiBaseUrl = "http://localhost:8080/api";
    [SerializeField] private int requestTimeoutSeconds = 10;

    private string AuthBaseUrl => $"{apiBaseUrl.TrimEnd('/')}/auth";
    private string GameBaseUrl => $"{apiBaseUrl.TrimEnd('/')}/game";

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

    private void Awake()
    {
        Instance = this;

        string environmentUrl = Environment.GetEnvironmentVariable("COOKING_GAME_API_URL");
        if (!string.IsNullOrWhiteSpace(environmentUrl))
        {
            apiBaseUrl = environmentUrl;
        }
    }

    private void ConfigureRequest(UnityWebRequest request)
    {
        request.timeout = Mathf.Max(1, requestTimeoutSeconds);
        request.SetRequestHeader("Accept", "application/json");
    }

    private static string GetErrorMessage(UnityWebRequest request, string fallback)
    {
        if (request?.downloadHandler == null || string.IsNullOrWhiteSpace(request.downloadHandler.text))
            return fallback;

        try
        {
            ApiErrorResponse error = JsonUtility.FromJson<ApiErrorResponse>(request.downloadHandler.text);
            return string.IsNullOrWhiteSpace(error?.message) ? fallback : error.message;
        }
        catch
        {
            return fallback;
        }
    }

    // 회원가입 요청
    public IEnumerator Register(SignupRequest data, System.Action<bool, string> callback)
    {
        string json = JsonUtility.ToJson(data);
        using (UnityWebRequest request = new UnityWebRequest($"{AuthBaseUrl}/signup", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            ConfigureRequest(request);

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
                callback?.Invoke(false, GetErrorMessage(request, "회원가입 요청에 실패했습니다."));
            }
        }
    }

    // 로그인 요청
    public IEnumerator Login(LoginRequest data, System.Action<bool, LoginResponse> callback)
    {
        string json = JsonUtility.ToJson(data);
        using (UnityWebRequest request = new UnityWebRequest($"{AuthBaseUrl}/login", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            ConfigureRequest(request);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
                if (response == null || string.IsNullOrWhiteSpace(response.accessToken)
                        || string.IsNullOrWhiteSpace(response.refreshToken))
                {
                    AuthSession.Clear();
                    callback?.Invoke(false, null);
                    yield break;
                }

                // [신규] 스테이지 해금 및 전적 정보를 로컬에 동기화합니다.
                InitializeProgression(response);

                AuthSession.SetTokens(response.accessToken, response.refreshToken);
                callback?.Invoke(true, response);
            }
            else
            {
                AuthSession.Clear();
                callback?.Invoke(false, null);
            }
        }
    }

    // 아이디 중복 확인 요청
    public IEnumerator CheckID(string loginId, System.Action<bool, string> callback)
    {
        string url = $"{AuthBaseUrl}/check-id?loginId={UnityWebRequest.EscapeURL(loginId)}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            ConfigureRequest(request);

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
        string url = $"{AuthBaseUrl}/is-verified?email={UnityWebRequest.EscapeURL(email)}";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            ConfigureRequest(request);
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

        using (UnityWebRequest request = new UnityWebRequest($"{AuthBaseUrl}/check-nickname", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            ConfigureRequest(request);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                callback?.Invoke(true, "사용 가능한 닉네임입니다.");
            }
            else
            {
                callback?.Invoke(false, GetErrorMessage(request, "이미 존재하는 닉네임입니다."));
            }
        }
    }

    // 이메일 인증 메일 발송 요청
    public IEnumerator SendVerificationEmail(string email, System.Action<bool, string> callback)
    {
        string json = JsonUtility.ToJson(new EmailVerificationRequest { email = email });

        using (UnityWebRequest request = new UnityWebRequest($"{AuthBaseUrl}/send-verification", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            ConfigureRequest(request);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[SendVerificationEmail] 성공: {request.downloadHandler.text}");
                callback?.Invoke(true, request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"[SendVerificationEmail] 실패: {request.error}");
                callback?.Invoke(false, GetErrorMessage(request, "메일 발송에 실패했습니다."));
            }
        }
    }

    // 스테이지 결과 서버 전송 (회원 전용)
    public IEnumerator UpdateStageRecord(int stageNumber, CookingOutcome outcome, System.Action<bool> callback = null)
    {
        if (stageNumber < 1 || stageNumber > 12)
        {
            Debug.LogError($"[Auth] 잘못된 스테이지 번호: {stageNumber}");
            callback?.Invoke(false);
            yield break;
        }

        int resultScore = (outcome == CookingOutcome.PerfectClear) ? 2 : (outcome == CookingOutcome.Clear ? 1 : 0);

        if (!AuthSession.IsAuthenticated)
        {
            UpdateLocalStageRecord(stageNumber, resultScore);
            Debug.Log($"[Auth] 게스트 모드입니다. {stageNumber}단계 결과를 현재 세션에만 저장합니다.");
            callback?.Invoke(true);
            yield break;
        }

        string json = JsonUtility.ToJson(new GameResultRequest
        {
            stage = stageNumber,
            achievementLevel = resultScore
        });

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using (UnityWebRequest request = new UnityWebRequest($"{GameBaseUrl}/result", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + AuthSession.AccessToken);
                ConfigureRequest(request);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    UpdateLocalStageRecord(stageNumber, resultScore);
                    Debug.Log("[Auth] 서버 전적 저장 성공");
                    callback?.Invoke(true);
                    yield break;
                }

                if (request.responseCode != 401 || attempt > 0)
                {
                    Debug.LogWarning($"[Auth] 서버 전적 저장 실패: {GetErrorMessage(request, request.error)}");
                    callback?.Invoke(false);
                    yield break;
                }
            }

            bool refreshed = false;
            yield return RefreshTokens(success => refreshed = success);
            if (!refreshed)
            {
                callback?.Invoke(false);
                yield break;
            }
        }
    }

    private static void UpdateLocalStageRecord(int stageNumber, int resultScore)
    {
        if (StageRecords == null) StageRecords = new int[12];
        else if (StageRecords.Length < 12)
        {
            int[] expanded = new int[12];
            Array.Copy(StageRecords, expanded, StageRecords.Length);
            StageRecords = expanded;
        }

        if (StageRecords[stageNumber - 1] < resultScore)
        {
            StageRecords[stageNumber - 1] = resultScore;
        }
        SyncProgressionToNetwork();
    }

    private IEnumerator RefreshTokens(System.Action<bool> callback)
    {
        if (string.IsNullOrWhiteSpace(AuthSession.RefreshToken))
        {
            AuthSession.Clear();
            callback?.Invoke(false);
            yield break;
        }

        string json = JsonUtility.ToJson(new RefreshTokenRequest
        {
            refreshToken = AuthSession.RefreshToken
        });

        using (UnityWebRequest request = new UnityWebRequest($"{AuthBaseUrl}/refresh", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            ConfigureRequest(request);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                TokenResponse response = JsonUtility.FromJson<TokenResponse>(request.downloadHandler.text);
                if (response != null && !string.IsNullOrWhiteSpace(response.accessToken)
                        && !string.IsNullOrWhiteSpace(response.refreshToken))
                {
                    AuthSession.SetTokens(response.accessToken, response.refreshToken);
                    callback?.Invoke(true);
                    yield break;
                }
            }

            Debug.LogWarning($"[Auth] 토큰 갱신 실패: {GetErrorMessage(request, request.error)}");
            AuthSession.Clear();
            callback?.Invoke(false);
        }
    }

    public IEnumerator Logout(System.Action callback = null)
    {
        if (!string.IsNullOrWhiteSpace(AuthSession.RefreshToken))
        {
            string json = JsonUtility.ToJson(new RefreshTokenRequest
            {
                refreshToken = AuthSession.RefreshToken
            });

            using (UnityWebRequest request = new UnityWebRequest($"{AuthBaseUrl}/logout", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ConfigureRequest(request);
                yield return request.SendWebRequest();
            }
        }

        AuthSession.Clear();
        InitializeProgression(null);
        callback?.Invoke();
    }
}
