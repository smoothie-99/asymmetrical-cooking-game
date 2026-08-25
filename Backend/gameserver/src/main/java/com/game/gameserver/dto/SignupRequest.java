package com.game.gameserver.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;
import jakarta.validation.constraints.Email;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;

@Getter
@NoArgsConstructor
public class SignupRequest {

    @NotBlank
    @Size(min = 3, max = 10)
    @Pattern(regexp = "^[a-zA-Z0-9]+$", message = "아이디는 영문자와 숫자만 사용할 수 있습니다.")
    private String loginId;

    @NotBlank
    @Size(min = 8, max = 72)
    private String password;

    @NotBlank
    private String passwordConfirm;

    @NotBlank
    @Email
    @Size(max = 100)
    private String email;

    @NotBlank
    @Size(max = 10)
    private String nickname;

}
