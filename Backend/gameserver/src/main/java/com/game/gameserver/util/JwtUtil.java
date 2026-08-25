package com.game.gameserver.util;

import java.nio.charset.StandardCharsets;
import java.util.Date;
import java.util.UUID;

import javax.crypto.SecretKey;

import org.springframework.stereotype.Component;
import org.springframework.beans.factory.annotation.Value;

import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;

@Component
public class JwtUtil {

    private static final String TOKEN_TYPE_CLAIM = "tokenType";
    private static final String ACCESS_TOKEN_TYPE = "access";
    private static final String REFRESH_TOKEN_TYPE = "refresh";

    private final SecretKey secretKey;
    private final long accessTokenExp;
    private final long refreshTokenExp;

    public JwtUtil(
            @Value("${app.jwt.secret}") String secretString,
            @Value("${app.jwt.access-expiration-ms:900000}") long accessTokenExp,
            @Value("${app.jwt.refresh-expiration-ms:604800000}") long refreshTokenExp) {
        if (secretString == null || secretString.length() < 32) {
            throw new IllegalStateException("JWT_SECRET must contain at least 32 characters.");
        }
        if (accessTokenExp <= 0 || refreshTokenExp <= 0) {
            throw new IllegalStateException("JWT expiration values must be positive.");
        }
        this.secretKey = Keys.hmacShaKeyFor(secretString.getBytes(StandardCharsets.UTF_8));
        this.accessTokenExp = accessTokenExp;
        this.refreshTokenExp = refreshTokenExp;
    }

    public String createAccessToken(String loginId){
        return createToken(loginId, ACCESS_TOKEN_TYPE, accessTokenExp);
    }

    public String createRefreshToken(String loginId){
        return createToken(loginId, REFRESH_TOKEN_TYPE, refreshTokenExp);
    }

    private String createToken(String loginId, String tokenType, long expTime){
        Date now = new Date();
        Date expiryDate = new Date(now.getTime() + expTime);
        
        return Jwts.builder()
                .subject(loginId)
                .id(UUID.randomUUID().toString())
                .claim(TOKEN_TYPE_CLAIM, tokenType)
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
        return validateAccessToken(token);
    }

    public boolean validateAccessToken(String token) {
        return validateTokenType(token, ACCESS_TOKEN_TYPE);
    }

    public boolean validateRefreshToken(String token) {
        return validateTokenType(token, REFRESH_TOKEN_TYPE);
    }

    private boolean validateTokenType(String token, String expectedType) {
        try {
            String actualType = Jwts.parser()
                    .verifyWith(secretKey)
                    .build()
                    .parseSignedClaims(token)
                    .getPayload()
                    .get(TOKEN_TYPE_CLAIM, String.class);
            return expectedType.equals(actualType);
        } catch (Exception e) {
            return false;
        }
    }
}
