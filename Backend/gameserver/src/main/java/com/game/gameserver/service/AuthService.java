package com.game.gameserver.service;

import java.time.LocalDateTime;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.stereotype.Service;

import com.game.gameserver.entity.EmailVerification;
import com.game.gameserver.entity.Users;
import com.game.gameserver.repository.EmailVerificationRepository;
import com.game.gameserver.repository.UserRepository;
import com.game.gameserver.util.JwtUtil;

import jakarta.transaction.Transactional;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;

@Service
@RequiredArgsConstructor
@Slf4j
public class AuthService {

    private final JwtUtil jwtUtil;
    private final UserRepository userRepository;
    private final BCryptPasswordEncoder passwordEncoder;
    private final EmailVerificationRepository emailVerificationRepository;
    private final EmailService emailService;

    @Transactional
    public String signup(String loginId, String password, String passwordConfirm, String email, String nickname) {
        log.info("회원가입 비즈니스 로직 시작: loginId={}, email={}", loginId, email);
        
        // [추가] 이메일 인증 여부 확인
        EmailVerification verification = emailVerificationRepository.findByEmail(email)
                .orElseThrow(() -> new IllegalArgumentException("이메일 인증 기록이 없습니다."));
        
        if (!verification.isVerified()) {
            throw new IllegalArgumentException("이메일 인증이 완료되지 않았습니다.");
        }

        if (loginId.length() < 3) {
            throw new IllegalArgumentException("아이디는 3글자 이상이어야 합니다.");
        }

        if (!loginId.matches("[a-zA-Z0-9]+$")) {
            throw new IllegalArgumentException("아이디는 영문자와 숫자를 사용해야 합니다.");
        }

        if (!password.equals(passwordConfirm)) {
            throw new IllegalArgumentException("비밀번호가 일치하지 않습니다.");
        }

        if (userRepository.existsByLoginId(loginId)) {
            throw new IllegalArgumentException("이미 존재하는 아이디입니다.");
        }
        if (userRepository.existsByEmail(email)) {
            throw new IllegalArgumentException("이미 존재하는 이메일입니다.");
        }
        if (userRepository.existsByNickname(nickname)) {
            throw new IllegalArgumentException("이미 존재하는 닉네임입니다.");
        }

        String encodedPassword = passwordEncoder.encode(password);

        Users user = Users.builder()
                .loginId(loginId)
                .password(encodedPassword)
                .email(email)
                .nickname(nickname)
                .isEmailVerified(true) // 이미 인증됨!
                .clearProgressLevel(1)
                .build();

        userRepository.save(user);

        // 인증 기록 삭제 (가입 완료되었으므로)
        emailVerificationRepository.delete(verification);

        return "회원가입이 완료되었습니다 🎉";
    }

    public boolean isEmailVerified(String email) {
        return emailVerificationRepository.findByEmail(email)
                .map(EmailVerification::isVerified)
                .orElse(false);
    }

    // 2. 로그인 로직 추가
    @Transactional
    public Map<String, String> login(String loginId, String password) {
        // [수정] 아이디 확인
        Users user = userRepository.findByLoginId(loginId)
                .orElseThrow(() -> new IllegalArgumentException("해당하는 아이디가 없습니다."));

        // [수정] 비밀번호 확인
        if (!passwordEncoder.matches(password, user.getPassword())) {
            throw new IllegalArgumentException("아이디 또는 비밀번호가 일치하지 않습니다.");
        }

        // 토큰 생성
        String accessToken = jwtUtil.createAccessToken(user.getLoginId());
        String refreshToken = jwtUtil.createRefreshToken(user.getLoginId());

        // 결과 리턴
        Map<String, String> response = new HashMap<>();
        response.put("accessToken", accessToken);
        response.put("refreshToken", refreshToken);
        response.put("nickname", user.getNickname());

        return response;
    }

    @Transactional
    public void verifyEmail(String token) {
        EmailVerification verification = emailVerificationRepository.findByToken(token)
                .orElseThrow(() -> new IllegalArgumentException("유효하지 않거나 만료된 인증 토큰입니다."));

        if (verification.isExpired()) {
            throw new IllegalArgumentException("인증 토큰이 만료되었습니다. 다시 시도해주세요.");
        }

        verification.verify();
        emailVerificationRepository.save(verification);
    }

    public boolean isLoginIdDuplicated(String loginId) {
        log.info("Checking if loginId exists: {}", loginId);
        boolean exists = userRepository.existsByLoginId(loginId);
        log.info("Result for loginId {}: {}", loginId, exists);
        return exists;
    }

    public boolean isNicknameDuplicated(String nickname) {
        log.info("Checking if nickname exists: {}", nickname);
        boolean exists = userRepository.existsByNickname(nickname);
        log.info("Result for nickname {}: {}", nickname, exists);
        return exists;
    }

    @Transactional
    public void sendEmailVerification(String email) {
        log.info("이메일 인증 메일 발송 요청: {}", email);
        
        String token = UUID.randomUUID().toString();
        
        emailVerificationRepository.deleteByEmail(email);

        EmailVerification verification = EmailVerification.builder()
                .email(email)
                .token(token)
                .expirationTime(LocalDateTime.now().plusMinutes(5))
                .build();

        emailVerificationRepository.save(verification);
        emailService.sendVerificationEmail(email, token);
    }
}
