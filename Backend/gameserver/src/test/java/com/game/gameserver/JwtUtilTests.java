package com.game.gameserver;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
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
}
