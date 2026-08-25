/// <summary>
/// 인증 토큰을 현재 프로세스 메모리에서만 관리합니다.
/// PlayerPrefs는 평문 파일이므로 인증 토큰 저장 용도로 사용하지 않습니다.
/// </summary>
public static class AuthSession
{
    public static string AccessToken { get; private set; }
    public static string RefreshToken { get; private set; }

    public static bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public static void SetTokens(string accessToken, string refreshToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
    }

    public static void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
    }
}
