package com.game.gameserver.util;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class JwtUtilTest {

    private final JwtUtil jwtUtil = new JwtUtil(
            "test-jwt-secret-must-be-at-least-32-bytes-long",
            300_000L,
            604_800_000L);

    @Test
    void refreshTokenCannotBeUsedAsAccessToken() {
        String accessToken = jwtUtil.createAccessToken("tester");
        String refreshToken = jwtUtil.createRefreshToken("tester");

        assertTrue(jwtUtil.validateAccessToken(accessToken));
        assertTrue(jwtUtil.validateRefreshToken(refreshToken));
        assertFalse(jwtUtil.validateAccessToken(refreshToken));
        assertFalse(jwtUtil.validateRefreshToken(accessToken));
    }
}
