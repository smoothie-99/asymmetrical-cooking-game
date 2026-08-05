package com.game.gameserver.controller;

import java.util.Map;

import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.http.HttpStatus;

import com.game.gameserver.dto.LoginRequest;
import com.game.gameserver.dto.SignupRequest;
import com.game.gameserver.service.AuthService;

import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;

@RestController
@RequestMapping("/api/auth")
@RequiredArgsConstructor
@Slf4j
public class AuthController {

    private final AuthService authService;

    @PostMapping("/signup")
    public ResponseEntity<String> signup(@RequestBody SignupRequest request) {
        log.info("회원가입 요청 수신: loginId={}, email={}", request.getLoginId(), request.getEmail());
        String message = authService.signup(
                request.getLoginId(),
                request.getPassword(),
                request.getPasswordConfirm(),
                request.getEmail(),
                request.getNickname());
        return ResponseEntity.ok(message);
    }

    @PostMapping("/login")
    public ResponseEntity<Map<String, String>> login(@RequestBody LoginRequest request) {
        Map<String, String> response = authService.login(
                request.getLoginId(),
                request.getPassword());
        return ResponseEntity.ok(response);
    }

    @GetMapping("/is-verified")
    public ResponseEntity<Boolean> isVerified(@RequestParam String email) {
        boolean verified = authService.isEmailVerified(email);
        return ResponseEntity.ok(verified);
    }

    @GetMapping("/check-id")
    public ResponseEntity<Boolean> checkId(@RequestParam String loginId) {
        log.info("Checking loginId duplication: {}", loginId);
        boolean isDuplicated = authService.isLoginIdDuplicated(loginId);
        log.info("Check result for loginId {}: {}", loginId, isDuplicated);
        return ResponseEntity.ok(isDuplicated);
    }

    @PostMapping("/check-nickname")
    public ResponseEntity<?> checkNickname(@RequestBody Map<String, String> request) {
        String nickname = request.get("nickname");
        log.info("Checking nickname duplication: {}", nickname);
        boolean isDuplicated = authService.isNicknameDuplicated(nickname);
        log.info("Check result for nickname {}: {}", nickname, isDuplicated);

        if (isDuplicated) {
            return ResponseEntity.status(HttpStatus.CONFLICT).body("이미 존재하는 닉네임입니다.");
        }
        return ResponseEntity.ok("사용 가능한 닉네임입니다.");
    }

    @GetMapping("/verify-email")
    public ResponseEntity<String> verifyEmail(@RequestParam String token) {
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
    public ResponseEntity<String> sendVerification(@RequestParam String email) {
        authService.sendEmailVerification(email);
        return ResponseEntity.ok("인증 메일이 발송되었습니다.");
    }
}
