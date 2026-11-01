package com.game.gameserver.dto;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Pattern;

public record NicknameCheckRequest(
        @NotBlank(message = "닉네임을 입력해주세요.")
        @Pattern(
                regexp = "^(?!\\s)(?!.*\\s$).{2,10}$",
                message = "닉네임은 앞뒤 공백 없이 2~10자여야 합니다.")
        String nickname) {
}
