package com.game.gameserver.dto;

import jakarta.validation.constraints.Pattern;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Getter
@NoArgsConstructor
public class ProfileUpdateRequest {
    @Pattern(
            regexp = "^(?!\\s)(?!.*\\s$).{2,10}$",
            message = "닉네임은 앞뒤 공백 없이 2~10자여야 합니다.")
    private String nickname;

}
