package com.game.gameserver;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import com.game.gameserver.util.JwtUtil;

class JwtUtilTests {

    private JwtUtil jwtUtil;

    @BeforeEach
    void setUp() {
        jwtUtil = new JwtUtil("test-secret-key-that-is-at-least-32-characters-long", 60_000, 120_000);
    }

    @Test
    void accessTokenIsAcceptedOnlyAsAccessToken() {
        String token = jwtUtil.createAccessToken("player01");

        assertTrue(jwtUtil.validateAccessToken(token));
        assertFalse(jwtUtil.validateRefreshToken(token));
        assertEquals("player01", jwtUtil.getLoginId(token));
    }

    @Test
    void refreshTokenIsAcceptedOnlyAsRefreshToken() {
        String token = jwtUtil.createRefreshToken("player01");

        assertTrue(jwtUtil.validateRefreshToken(token));
        assertFalse(jwtUtil.validateAccessToken(token));
    }

    @Test
    void tokenSignedWithAnotherSecretIsRejected() {
        String token = jwtUtil.createAccessToken("player01");
        JwtUtil anotherServer = new JwtUtil(
                "another-test-secret-key-that-is-longer-than-32-characters", 60_000, 120_000);

        assertFalse(anotherServer.validateAccessToken(token));
    }

    @Test
    void tokensContainUniqueIdentifiers() {
        String first = jwtUtil.createAccessToken("player01");
        String second = jwtUtil.createAccessToken("player01");

        assertNotEquals(first, second);
    }

    @Test
    void unsafeConfigurationIsRejected() {
        assertThrows(IllegalStateException.class,
                () -> new JwtUtil("short-secret", 60_000, 120_000));
        assertThrows(IllegalStateException.class,
                () -> new JwtUtil("test-secret-key-that-is-at-least-32-characters-long", 0, 120_000));
    }
}
