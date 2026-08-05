package com.game.gameserver.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;

@Getter
@NoArgsConstructor
public class ProfileUpdateRequest {
    private String nickname;
    private Integer clearProgressLevel;

}
