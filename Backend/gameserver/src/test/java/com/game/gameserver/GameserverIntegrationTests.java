package com.game.gameserver;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.security.authentication.BadCredentialsException;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;

import com.game.gameserver.dto.LoginResponse;
import com.game.gameserver.dto.TokenResponse;
import com.game.gameserver.entity.UserDish;
import com.game.gameserver.entity.Users;
import com.game.gameserver.repository.EmailVerificationRepository;
import com.game.gameserver.repository.RoomPlayerRepository;
import com.game.gameserver.repository.UserDishRepository;
import com.game.gameserver.repository.UserRepository;
import com.game.gameserver.service.AuthService;
import com.game.gameserver.service.UserService;
import com.game.gameserver.util.JwtUtil;

@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
class GameserverIntegrationTests {

    private static final String LOGIN_ID = "player01";
    private static final String PASSWORD = "Password1!";

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private JwtUtil jwtUtil;

    @Autowired
    private AuthService authService;

    @Autowired
    private UserService userService;

    @Autowired
    private UserRepository userRepository;

    @Autowired
    private UserDishRepository userDishRepository;

    @Autowired
    private RoomPlayerRepository roomPlayerRepository;

    @Autowired
    private EmailVerificationRepository emailVerificationRepository;

    @Autowired
    private PasswordEncoder passwordEncoder;

    private ExecutorService executor;

    @BeforeEach
    void setUp() {
        roomPlayerRepository.deleteAllInBatch();
        userDishRepository.deleteAllInBatch();
        userRepository.deleteAllInBatch();
        emailVerificationRepository.deleteAllInBatch();
        createUser();
    }

    @AfterEach
    void tearDown() throws InterruptedException {
        if (executor != null) {
            executor.shutdownNow();
            executor.awaitTermination(5, TimeUnit.SECONDS);
        }
    }

    @Test
    void publicEndpointDoesNotRequireAuthentication() throws Exception {
        mockMvc.perform(get("/api/auth/check-id").param("loginId", "other01"))
                .andExpect(status().isOk());
    }

    @Test
    void protectedEndpointRejectsMissingToken() throws Exception {
        mockMvc.perform(get("/api/users/me"))
                .andExpect(status().isUnauthorized());
    }

    @Test
    void refreshTokenCannotAuthenticateProtectedEndpoint() throws Exception {
        String refreshToken = jwtUtil.createRefreshToken(LOGIN_ID);

        mockMvc.perform(get("/api/users/me")
                        .header("Authorization", "Bearer " + refreshToken))
                .andExpect(status().isUnauthorized());
    }

    @Test
    void accessTokenCanAuthenticateProtectedEndpoint() throws Exception {
        String accessToken = jwtUtil.createAccessToken(LOGIN_ID);

        mockMvc.perform(get("/api/users/me")
                        .header("Authorization", "Bearer " + accessToken))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.loginId").value(LOGIN_ID));
    }

    @Test
    void accessTokenCannotBeUsedAtRefreshEndpoint() throws Exception {
        String accessToken = jwtUtil.createAccessToken(LOGIN_ID);

        mockMvc.perform(post("/api/auth/refresh")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"refreshToken\":\"" + accessToken + "\"}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("AUTHENTICATION_FAILED"));
    }

    @Test
    void refreshTokenIsRotatedAndCannotBeReusedAfterRefreshOrLogout() {
        LoginResponse login = authService.login(LOGIN_ID, PASSWORD);

        TokenResponse rotated = authService.refresh(login.refreshToken());
        assertThat(rotated.accessToken()).isNotBlank();
        assertThat(rotated.refreshToken()).isNotEqualTo(login.refreshToken());
        assertThatThrownBy(() -> authService.refresh(login.refreshToken()))
                .isInstanceOf(BadCredentialsException.class);

        authService.logout(rotated.refreshToken());
        assertThatThrownBy(() -> authService.refresh(rotated.refreshToken()))
                .isInstanceOf(BadCredentialsException.class);
    }

    @Test
    void invalidAndLockedStagesAreRejected() {
        assertThatThrownBy(() -> userService.saveDishResult(LOGIN_ID, 0, 1))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> userService.saveDishResult(LOGIN_ID, 13, 1))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> userService.saveDishResult(LOGIN_ID, 2, 1))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("해금되지 않은");
    }

    @Test
    void failedStageDoesNotAdvanceProgress() {
        userService.saveDishResult(LOGIN_ID, 1, 0);

        Users user = userRepository.findByLoginId(LOGIN_ID).orElseThrow();
        assertThat(user.getClearProgressLevel()).isEqualTo(1);
    }

    @Test
    void successAdvancesOnceAndLowerResultCannotOverwriteBestResult() {
        userService.saveDishResult(LOGIN_ID, 1, 1);
        userService.saveDishResult(LOGIN_ID, 1, 2);
        userService.saveDishResult(LOGIN_ID, 1, 0);

        Users user = userRepository.findByLoginId(LOGIN_ID).orElseThrow();
        List<UserDish> records = userDishRepository.findByUser(user);
        assertThat(user.getClearProgressLevel()).isEqualTo(2);
        assertThat(records).hasSize(1);
        assertThat(records.get(0).getAchievementLevel()).isEqualTo(2);
    }

    @Test
    void oneHundredConcurrentResultsKeepSingleBestRecord() throws Exception {
        int requestCount = 100;
        executor = Executors.newFixedThreadPool(20);
        CountDownLatch start = new CountDownLatch(1);
        List<Future<?>> futures = new ArrayList<>();

        for (int i = 0; i < requestCount; i++) {
            int achievementLevel = i % 3;
            futures.add(executor.submit(() -> {
                start.await();
                userService.saveDishResult(LOGIN_ID, 1, achievementLevel);
                return null;
            }));
        }

        start.countDown();
        for (Future<?> future : futures) {
            future.get(30, TimeUnit.SECONDS);
        }

        Users user = userRepository.findByLoginId(LOGIN_ID).orElseThrow();
        List<UserDish> records = userDishRepository.findByUser(user);
        assertThat(records).hasSize(1);
        assertThat(records.get(0).getAchievementLevel()).isEqualTo(2);
        assertThat(user.getClearProgressLevel()).isEqualTo(2);
    }

    private void createUser() {
        userRepository.saveAndFlush(Users.builder()
                .loginId(LOGIN_ID)
                .password(passwordEncoder.encode(PASSWORD))
                .email("player01@example.com")
                .nickname("플레이어01")
                .isEmailVerified(true)
                .clearProgressLevel(1)
                .build());
    }
}
