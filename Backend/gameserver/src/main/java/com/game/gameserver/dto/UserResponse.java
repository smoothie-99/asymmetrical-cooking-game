package com.game.gameserver.dto;

import lombok.Builder;
import lombok.Getter;

@Getter
@Builder
public class UserResponse {

    private String loginId;
    private String email;
    private String nickname;
    private int clearProgressLevel;
    private boolean isEmailVerified;
    
}
