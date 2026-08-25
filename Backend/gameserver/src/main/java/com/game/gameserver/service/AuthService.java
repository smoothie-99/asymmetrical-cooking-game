package com.game.gameserver.service;

import java.time.LocalDateTime;
import java.util.List;
import java.util.Locale;
import java.util.UUID;

import org.springframework.security.authentication.BadCredentialsException;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.game.gameserver.dto.LoginResponse;
import com.game.gameserver.dto.TokenResponse;
import com.game.gameserver.entity.EmailVerification;
import com.game.gameserver.entity.UserDish;
import com.game.gameserver.entity.Users;
import com.game.gameserver.exception.ApiException;
import com.game.gameserver.repository.DishRepository;
import com.game.gameserver.repository.EmailVerificationRepository;
import com.game.gameserver.repository.UserDishRepository;
import com.game.gameserver.repository.UserRepository;
import com.game.gameserver.util.JwtUtil;
import com.game.gameserver.util.TokenHashUtil;

import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;

@Service
@RequiredArgsConstructor
@Slf4j
public class AuthService {

    private final JwtUtil jwtUtil;
    private final UserRepository userRepository;
    private final UserDishRepository userDishRepository;
    private final DishRepository dishRepository;
    private final PasswordEncoder passwordEncoder;
    private final EmailVerificationRepository emailVerificationRepository;
    private final EmailService emailService;

    @Transactional
    public String signup(String loginId, String password, String passwordConfirm, String email, String nickname) {
        String normalizedLoginId = loginId.trim();
        String normalizedEmail = normalizeEmail(email);
        String normalizedNickname = nickname.trim();

        EmailVerification verification = emailVerificationRepository.findByEmail(normalizedEmail)
                .orElseThrow(() -> ApiException.badRequest("EMAIL_NOT_VERIFIED", "이메일 인증 기록이 없습니다."));
        
        if (!verification.isVerified()) {
            throw ApiException.badRequest("EMAIL_NOT_VERIFIED", "이메일 인증이 완료되지 않았습니다.");
        }

        if (!password.equals(passwordConfirm)) {
            throw ApiException.badRequest("PASSWORD_MISMATCH", "비밀번호가 일치하지 않습니다.");
        }

        if (userRepository.existsByLoginId(normalizedLoginId)) {
            throw ApiException.conflict("DUPLICATE_LOGIN_ID", "이미 존재하는 아이디입니다.");
        }
        if (userRepository.existsByEmail(normalizedEmail)) {
            throw ApiException.conflict("DUPLICATE_EMAIL", "이미 존재하는 이메일입니다.");
        }
        if (userRepository.existsByNickname(normalizedNickname)) {
            throw ApiException.conflict("DUPLICATE_NICKNAME", "이미 존재하는 닉네임입니다.");
        }

        String encodedPassword = passwordEncoder.encode(password);

        Users user = Users.builder()
                .loginId(normalizedLoginId)
                .password(encodedPassword)
                .email(normalizedEmail)
                .nickname(normalizedNickname)
                .isEmailVerified(true)
                .clearProgressLevel(1)
                .build();

        userRepository.save(user);

        // 인증 기록 삭제 (가입 완료되었으므로)
        emailVerificationRepository.delete(verification);

        return "회원가입이 완료되었습니다 🎉";
    }

    public boolean isEmailVerified(String email) {
        return emailVerificationRepository.findByEmail(normalizeEmail(email))
                .map(EmailVerification::isVerified)
                .orElse(false);
    }

    @Transactional
    public LoginResponse login(String loginId, String password) {
        Users user = userRepository.findByLoginId(loginId.trim())
                .orElseThrow(() -> new BadCredentialsException("아이디 또는 비밀번호가 일치하지 않습니다."));

        if (!passwordEncoder.matches(password, user.getPassword())) {
            throw new BadCredentialsException("아이디 또는 비밀번호가 일치하지 않습니다.");
        }

        String accessToken = jwtUtil.createAccessToken(user.getLoginId());
        String refreshToken = jwtUtil.createRefreshToken(user.getLoginId());
        user.login(TokenHashUtil.sha256(refreshToken));

        return LoginResponse.builder()
                .accessToken(accessToken)
                .refreshToken(refreshToken)
                .nickname(user.getNickname())
                .stageResults(buildStageResults(user))
                .build();
    }

    @Transactional
    public TokenResponse refresh(String refreshToken) {
        if (!jwtUtil.validateRefreshToken(refreshToken)) {
            throw ApiException.unauthorized("INVALID_REFRESH_TOKEN", "유효하지 않은 refresh token입니다.");
        }

        String loginId = jwtUtil.getLoginId(refreshToken);
        Users user = userRepository.findByLoginId(loginId)
                .orElseThrow(() -> ApiException.unauthorized("INVALID_REFRESH_TOKEN", "유효하지 않은 refresh token입니다."));

        if (!TokenHashUtil.matches(refreshToken, user.getRefreshToken())) {
            throw ApiException.unauthorized("INVALID_REFRESH_TOKEN", "이미 사용되었거나 폐기된 refresh token입니다.");
        }

        String newAccessToken = jwtUtil.createAccessToken(loginId);
        String newRefreshToken = jwtUtil.createRefreshToken(loginId);
        user.rotateRefreshToken(TokenHashUtil.sha256(newRefreshToken));

        return TokenResponse.builder()
                .accessToken(newAccessToken)
                .refreshToken(newRefreshToken)
                .build();
    }

    @Transactional
    public void verifyEmail(String token) {
        EmailVerification verification = emailVerificationRepository.findByToken(token)
                .orElseThrow(() -> ApiException.badRequest("INVALID_EMAIL_TOKEN", "유효하지 않거나 만료된 인증 토큰입니다."));

        if (verification.isExpired()) {
            throw ApiException.badRequest("EXPIRED_EMAIL_TOKEN", "인증 토큰이 만료되었습니다. 다시 시도해주세요.");
        }

        verification.verify();
    }

    public boolean isLoginIdDuplicated(String loginId) {
        return userRepository.existsByLoginId(loginId.trim());
    }

    public boolean isNicknameDuplicated(String nickname) {
        return userRepository.existsByNickname(nickname.trim());
    }

    @Transactional
    public void sendEmailVerification(String email) {
        String normalizedEmail = normalizeEmail(email);
        if (userRepository.existsByEmail(normalizedEmail)) {
            throw ApiException.conflict("DUPLICATE_EMAIL", "이미 가입된 이메일입니다.");
        }

        LocalDateTime now = LocalDateTime.now();
        String token = UUID.randomUUID().toString();

        EmailVerification verification = emailVerificationRepository.findByEmail(normalizedEmail)
                .map(existing -> {
                    if (!existing.canResend(now)) {
                        throw ApiException.tooManyRequests("EMAIL_RATE_LIMIT", "인증 메일은 1분 후 다시 요청할 수 있습니다.");
                    }
                    existing.renew(token, now, now.plusMinutes(5));
                    return existing;
                })
                .orElseGet(() -> EmailVerification.builder()
                        .email(normalizedEmail)
                        .token(token)
                        .lastSentAt(now)
                        .expirationTime(now.plusMinutes(5))
                        .build());

        emailVerificationRepository.save(verification);
        emailService.sendVerificationEmail(normalizedEmail, token);
    }

    private int[] buildStageResults(Users user) {
        int maxStage = dishRepository.findMaxStage();
        int[] stageResults = new int[maxStage];
        List<UserDish> records = userDishRepository.findCollectionByUser(user);
        for (UserDish record : records) {
            int stageIndex = record.getDish().getStage() - 1;
            if (stageIndex >= 0 && stageIndex < stageResults.length) {
                stageResults[stageIndex] = Math.max(stageResults[stageIndex], record.getAchievementLevel());
            }
        }
        return stageResults;
    }

    private String normalizeEmail(String email) {
        return email.trim().toLowerCase(Locale.ROOT);
    }
}
