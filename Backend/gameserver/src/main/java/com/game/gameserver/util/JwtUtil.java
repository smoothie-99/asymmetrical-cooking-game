package com.game.gameserver.util;

import java.nio.charset.StandardCharsets;
import java.util.Date;
import java.util.UUID;

import javax.crypto.SecretKey;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import io.jsonwebtoken.Claims;
import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;

@Component
public class JwtUtil {

    private static final String TOKEN_TYPE_CLAIM = "tokenType";

    private final SecretKey secretKey;
    private final long accessTokenExp;
    private final long refreshTokenExp;

    public JwtUtil(
            @Value("${jwt.secret}") String secretString,
            @Value("${jwt.access-token-expiration-ms}") long accessTokenExp,
            @Value("${jwt.refresh-token-expiration-ms}") long refreshTokenExp) {
        if (secretString == null || secretString.getBytes(StandardCharsets.UTF_8).length < 32) {
            throw new IllegalStateException("JWT_SECRET은 32바이트 이상이어야 합니다.");
        }
        this.secretKey = Keys.hmacShaKeyFor(secretString.getBytes(StandardCharsets.UTF_8));
        this.accessTokenExp = accessTokenExp;
        this.refreshTokenExp = refreshTokenExp;
    }

    public String createAccessToken(String loginId){
        return createToken(loginId, accessTokenExp, TokenType.ACCESS);
    }

    public String createRefreshToken(String loginId){
        return createToken(loginId, refreshTokenExp, TokenType.REFRESH);
    }

    private String createToken(String loginId, long expTime, TokenType tokenType){
        Date now = new Date();
        Date expiryDate = new Date(now.getTime() + expTime);
        
        return Jwts.builder()
                .id(UUID.randomUUID().toString())
                .subject(loginId)
                .claim(TOKEN_TYPE_CLAIM, tokenType.name())
                .issuedAt(now)
                .expiration(expiryDate)
                .signWith(secretKey)
                .compact();
    }

    public String getLoginId(String token) {
        return parseClaims(token).getSubject();
    }

    public boolean validateAccessToken(String token) {
        return validateToken(token, TokenType.ACCESS);
    }

    public boolean validateRefreshToken(String token) {
        return validateToken(token, TokenType.REFRESH);
    }

    private boolean validateToken(String token, TokenType expectedType) {
        try {
            Claims claims = parseClaims(token);
            return expectedType.name().equals(claims.get(TOKEN_TYPE_CLAIM, String.class));
        } catch (Exception e) {
            return false;
        }
    }

    private Claims parseClaims(String token) {
        return Jwts.parser()
                .verifyWith(secretKey)
                .build()
                .parseSignedClaims(token)
                .getPayload();
    }

    private enum TokenType {
        ACCESS,
        REFRESH
    }
}
