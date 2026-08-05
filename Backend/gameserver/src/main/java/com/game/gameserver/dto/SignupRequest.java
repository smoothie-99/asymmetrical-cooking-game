package com.game.gameserver.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;

@Getter
@NoArgsConstructor
public class SignupRequest {

    private String loginId;
    private String password;
    private String passwordConfirm;
    private String email;
    private String nickname;

}
