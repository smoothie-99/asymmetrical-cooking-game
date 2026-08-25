package com.game.gameserver.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;
import jakarta.validation.constraints.Size;

@Getter
@NoArgsConstructor
public class ProfileUpdateRequest {
    @Size(min = 1, max = 10)
    private String nickname;

}
