package com.game.gameserver.dto;

public record LoginResponse(
        String accessToken,
        String refreshToken,
        String nickname,
        int[] stageResults) {
}
