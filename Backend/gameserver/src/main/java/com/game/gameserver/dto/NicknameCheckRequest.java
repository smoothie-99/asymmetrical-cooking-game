package com.game.gameserver.dto;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Getter
@NoArgsConstructor
public class NicknameCheckRequest {

    @NotBlank
    @Size(max = 10)
    private String nickname;
}
