package com.game.gameserver.util;

import java.nio.charset.StandardCharsets;
import java.util.Date;

import javax.crypto.SecretKey;

import org.springframework.stereotype.Component;

import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;

@Component
public class JwtUtil {

    private final String secretString = "sangkum-and-choco-cooking-game-2026-very-secret-key-don-not-leak";
    private final SecretKey secretKey = Keys.hmacShaKeyFor(secretString.getBytes(StandardCharsets.UTF_8));

    private final long accessTokenExp = 1000L * 60 * 5; //5분
    private final long refreshTokenExp = 1000L * 60 * 60 * 24 * 7; // 7일

    public String createAccessToken(String loginId){
        return createToken(loginId, accessTokenExp);
    }

    public String createRefreshToken(String loginId){
        return createToken(loginId, refreshTokenExp);
    }

    private String createToken(String loginId, long expTime){
        Date now = new Date();
        Date expiryDate = new Date(now.getTime() + expTime);
        
        return Jwts.builder()
                .subject(loginId)
                .issuedAt(now)
                .expiration(expiryDate)
                .signWith(secretKey)
                .compact();
    }

    public String getLoginId(String token) {
        return Jwts.parser()
                .verifyWith(secretKey)
                .build()
                .parseSignedClaims(token)
                .getPayload()
                .getSubject();
    }

    public boolean validateToken(String token) {
        try {
            Jwts.parser().verifyWith(secretKey).build().parseSignedClaims(token);
            return true;
        } catch (Exception e) {
            // 토큰이 가짜거나, 만료됐거나, 변조됐다면 여기로 와!
            return false;
        }
    }
}
