package com.game.gameserver.service;

import java.time.LocalDateTime;
import java.util.Arrays;
import java.util.Locale;
import java.util.UUID;

import org.springframework.security.authentication.BadCredentialsException;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;

import com.game.gameserver.dto.LoginResponse;
import com.game.gameserver.dto.TokenResponse;
import com.game.gameserver.entity.EmailVerification;
import com.game.gameserver.entity.UserDish;
import com.game.gameserver.entity.Users;
import com.game.gameserver.repository.EmailVerificationRepository;
import com.game.gameserver.repository.UserDishRepository;
import com.game.gameserver.repository.UserRepository;
import com.game.gameserver.util.JwtUtil;
import com.game.gameserver.util.TokenHashUtil;

import jakarta.transaction.Transactional;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;

@Service
@RequiredArgsConstructor
@Slf4j
public class AuthService {

    private static final int STAGE_COUNT = 12;
    private static final long VERIFICATION_RESEND_SECONDS = 60;

    private final JwtUtil jwtUtil;
    private final UserRepository userRepository;
    private final PasswordEncoder passwordEncoder;
    private final EmailVerificationRepository emailVerificationRepository;
    private final UserDishRepository userDishRepository;
    private final EmailService emailService;

    @Transactional
    public String signup(String loginId, String password, String passwordConfirm, String email, String nickname) {
        log.info("회원가입 처리 시작");
        String normalizedEmail = normalizeEmail(email);
        
        // [추가] 이메일 인증 여부 확인
        EmailVerification verification = emailVerificationRepository.findByEmail(normalizedEmail)
                .orElseThrow(() -> new IllegalArgumentException("이메일 인증 기록이 없습니다."));
        
        if (!verification.isVerified() || verification.isExpired()) {
            throw new IllegalArgumentException("이메일 인증이 완료되지 않았습니다.");
        }

        if (!password.equals(passwordConfirm)) {
            throw new IllegalArgumentException("비밀번호가 일치하지 않습니다.");
        }

        if (userRepository.existsByLoginId(loginId)) {
            throw new IllegalStateException("이미 존재하는 아이디입니다.");
        }
        if (userRepository.existsByEmailIgnoreCase(normalizedEmail)) {
            throw new IllegalStateException("이미 존재하는 이메일입니다.");
        }
        if (userRepository.existsByNickname(nickname)) {
            throw new IllegalStateException("이미 존재하는 닉네임입니다.");
        }

        String encodedPassword = passwordEncoder.encode(password);

        Users user = Users.builder()
                .loginId(loginId)
                .password(encodedPassword)
                .email(normalizedEmail)
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
        return emailVerificationRepository.findByEmail(normalizeEmail(email))
                .map(verification -> verification.isVerified() && !verification.isExpired())
                .orElse(false);
    }

    // 2. 로그인 로직 추가
    @Transactional
    public LoginResponse login(String loginId, String password) {
        Users user = userRepository.findByLoginId(loginId).orElse(null);

        if (user == null || !passwordEncoder.matches(password, user.getPassword())) {
            throw new BadCredentialsException("아이디 또는 비밀번호가 일치하지 않습니다.");
        }

        // 토큰 생성
        String accessToken = jwtUtil.createAccessToken(user.getLoginId());
        String refreshToken = jwtUtil.createRefreshToken(user.getLoginId());
        user.setRefreshTokenHash(TokenHashUtil.sha256(refreshToken));
        user.setLastLoginTime(LocalDateTime.now());

        int[] stageResults = new int[STAGE_COUNT];
        Arrays.fill(stageResults, 0);
        for (UserDish userDish : userDishRepository.findByUser(user)) {
            int stageIndex = userDish.getDish().getStage() - 1;
            if (stageIndex >= 0 && stageIndex < STAGE_COUNT) {
                stageResults[stageIndex] = userDish.getAchievementLevel();
            }
        }

        return new LoginResponse(accessToken, refreshToken, user.getNickname(), stageResults);
    }

    @Transactional
    public TokenResponse refresh(String refreshToken) {
        if (!jwtUtil.validateRefreshToken(refreshToken)) {
            throw new BadCredentialsException("유효하지 않은 Refresh Token입니다.");
        }

        String loginId = jwtUtil.getLoginId(refreshToken);
        Users user = userRepository.findByLoginIdForUpdate(loginId)
                .orElseThrow(() -> new BadCredentialsException("유효하지 않은 Refresh Token입니다."));

        String storedHash = user.getRefreshTokenHash();
        if (storedHash == null || !storedHash.equals(TokenHashUtil.sha256(refreshToken))) {
            throw new BadCredentialsException("만료되었거나 폐기된 Refresh Token입니다.");
        }

        String newAccessToken = jwtUtil.createAccessToken(loginId);
        String newRefreshToken = jwtUtil.createRefreshToken(loginId);
        user.setRefreshTokenHash(TokenHashUtil.sha256(newRefreshToken));

        return new TokenResponse(newAccessToken, newRefreshToken);
    }

    @Transactional
    public void logout(String refreshToken) {
        if (!jwtUtil.validateRefreshToken(refreshToken)) return;

        String loginId = jwtUtil.getLoginId(refreshToken);
        userRepository.findByLoginId(loginId).ifPresent(user -> {
            if (TokenHashUtil.sha256(refreshToken).equals(user.getRefreshTokenHash())) {
                user.setRefreshTokenHash(null);
            }
        });
    }

    @Transactional
    public void verifyEmail(String token) {
        EmailVerification verification = emailVerificationRepository.findByTokenHash(TokenHashUtil.sha256(token))
                .orElseThrow(() -> new IllegalArgumentException("유효하지 않거나 만료된 인증 토큰입니다."));

        if (verification.isExpired()) {
            throw new IllegalArgumentException("인증 토큰이 만료되었습니다. 다시 시도해주세요.");
        }

        verification.verify();
        emailVerificationRepository.save(verification);
    }

    public boolean isLoginIdDuplicated(String loginId) {
        boolean exists = userRepository.existsByLoginId(loginId);
        return exists;
    }

    public boolean isNicknameDuplicated(String nickname) {
        boolean exists = userRepository.existsByNickname(nickname);
        return exists;
    }

    @Transactional
    public void sendEmailVerification(String email) {
        log.info("이메일 인증 메일 발송 요청");
        String normalizedEmail = normalizeEmail(email);

        emailVerificationRepository.findByEmail(normalizedEmail).ifPresent(existing -> {
            if (existing.getCreatedAt() != null
                    && existing.getCreatedAt().isAfter(LocalDateTime.now().minusSeconds(VERIFICATION_RESEND_SECONDS))) {
                throw new IllegalArgumentException("인증 메일은 1분 후 다시 요청할 수 있습니다.");
            }
        });
        
        String token = UUID.randomUUID().toString();
        
        emailVerificationRepository.deleteByEmail(normalizedEmail);

        EmailVerification verification = EmailVerification.builder()
                .email(normalizedEmail)
                .tokenHash(TokenHashUtil.sha256(token))
                .expirationTime(LocalDateTime.now().plusMinutes(5))
                .build();

        emailVerificationRepository.save(verification);
        emailService.sendVerificationEmail(normalizedEmail, token);
    }

    private String normalizeEmail(String email) {
        return email.trim().toLowerCase(Locale.ROOT);
    }
}
