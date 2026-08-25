package com.game.gameserver.dto;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

public record RefreshTokenRequest(
        @NotBlank(message = "Refresh Token을 입력해주세요.")
        @Size(max = 4096, message = "Refresh Token이 너무 깁니다.")
        String refreshToken) {
}
