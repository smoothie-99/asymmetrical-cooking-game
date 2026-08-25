package com.game.gameserver.dto;

import lombok.Builder;
import lombok.Getter;

@Getter
@Builder
public class LoginResponse {

    private String accessToken;
    private String refreshToken;
    private String nickname;
    private int[] stageResults;
}
