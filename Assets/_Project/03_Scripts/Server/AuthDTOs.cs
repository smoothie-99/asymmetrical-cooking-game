using JetBrains.Annotations;
using System;

[Serializable]
public class SignupRequest
{
    public string loginId;
    public string password;
    public string passwordConfirm;
    public string email;
    public string nickname;
}

[Serializable]
public class CheckIdRequest
{
    public string loginId;
}

[Serializable]
public class CheckNicknameRequest
{
    public string nickname;
}

[Serializable]
public class LoginRequest
{
    public string loginId;
    public string password;
}

[Serializable]
public class LoginResponse
{
    public string accessToken;
    public string refreshToken;
    public string nickname;
    public int[] stageResults; // [신규] 스테이지 클리어 결과 (2: Perfect, 1: Clear, 0: Failed)
}

[Serializable]
public class GameResultRequest
{
    public int stage;
    public int achievementLevel;
}

[Serializable]
public class RefreshTokenRequest
{
    public string refreshToken;
}

[Serializable]
public class TokenResponse
{
    public string accessToken;
    public string refreshToken;
}


