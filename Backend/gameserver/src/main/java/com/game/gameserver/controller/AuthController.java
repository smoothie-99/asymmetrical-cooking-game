package com.game.gameserver.controller;

import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import com.game.gameserver.dto.LoginRequest;
import com.game.gameserver.dto.LoginResponse;
import com.game.gameserver.dto.RefreshTokenRequest;
import com.game.gameserver.dto.SignupRequest;
import com.game.gameserver.dto.EmailVerificationRequest;
import com.game.gameserver.dto.TokenResponse;
import com.game.gameserver.dto.NicknameCheckRequest;
import com.game.gameserver.service.AuthService;

import jakarta.validation.Valid;
import jakarta.validation.constraints.Email;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;
import jakarta.validation.constraints.NotBlank;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.validation.annotation.Validated;

@RestController
@RequestMapping("/api/auth")
@RequiredArgsConstructor
@Slf4j
@Validated
public class AuthController {

    private final AuthService authService;

    @PostMapping("/signup")
    public ResponseEntity<String> signup(@Valid @RequestBody SignupRequest request) {
        log.info("회원가입 요청 수신");
        String message = authService.signup(
                request.getLoginId(),
                request.getPassword(),
                request.getPasswordConfirm(),
                request.getEmail(),
                request.getNickname());
        return ResponseEntity.ok(message);
    }

    @PostMapping("/login")
    public ResponseEntity<LoginResponse> login(@Valid @RequestBody LoginRequest request) {
        LoginResponse response = authService.login(
                request.getLoginId(),
                request.getPassword());
        return ResponseEntity.ok(response);
    }

    @PostMapping("/refresh")
    public ResponseEntity<TokenResponse> refresh(@Valid @RequestBody RefreshTokenRequest request) {
        return ResponseEntity.ok(authService.refresh(request.refreshToken()));
    }

    @PostMapping("/logout")
    public ResponseEntity<Void> logout(@Valid @RequestBody RefreshTokenRequest request) {
        authService.logout(request.refreshToken());
        return ResponseEntity.noContent().build();
    }

    @GetMapping("/is-verified")
    public ResponseEntity<Boolean> isVerified(
            @RequestParam @NotBlank @Email @Size(max = 100) String email) {
        boolean verified = authService.isEmailVerified(email);
        return ResponseEntity.ok(verified);
    }

    @GetMapping("/check-id")
    public ResponseEntity<Boolean> checkId(
            @RequestParam
            @Pattern(regexp = "^(?=.*[A-Za-z])(?=.*\\d)[A-Za-z\\d]{6,10}$")
            String loginId) {
        boolean isDuplicated = authService.isLoginIdDuplicated(loginId);
        return ResponseEntity.ok(isDuplicated);
    }

    @PostMapping("/check-nickname")
    public ResponseEntity<String> checkNickname(@Valid @RequestBody NicknameCheckRequest request) {
        String nickname = request.nickname();
        boolean isDuplicated = authService.isNicknameDuplicated(nickname);

        if (isDuplicated) {
            throw new IllegalStateException("이미 존재하는 닉네임입니다.");
        }
        return ResponseEntity.ok("사용 가능한 닉네임입니다.");
    }

    @GetMapping("/verify-email")
    public ResponseEntity<String> verifyEmail(
            @RequestParam @NotBlank @Size(max = 128) String token) {
        authService.verifyEmail(token);

        // 인증 성공 후 사용자에게 보여줄 간단한 HTML 결과 페이지
        String htmlResponse = "<html>" +
                "<head><meta charset='UTF-8'></head>" +
                "<body style='text-align: center; padding-top: 50px; font-family: sans-serif;'>" +
                "<h1>이메일 인증 완료! 🎉</h1>" +
                "<p>이제 게임으로 돌아가서 회원가입을 완료해주세요.</p>" +
                "<button onclick='window.close()' style='padding: 10px 20px; font-size: 16px; cursor: pointer;'>창 닫기</button>"
                +
                "</body>" +
                "</html>";

        return ResponseEntity.ok()
                .header(HttpHeaders.CONTENT_TYPE, MediaType.TEXT_HTML_VALUE)
                .body(htmlResponse);
    }

    @PostMapping("/send-verification")
    public ResponseEntity<String> sendVerification(@Valid @RequestBody EmailVerificationRequest request) {
        authService.sendEmailVerification(request.email());
        return ResponseEntity.ok("인증 메일이 발송되었습니다.");
    }
}
