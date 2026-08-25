package com.game.gameserver.entity;

import java.time.LocalDateTime;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Entity
@Getter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class EmailVerification {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, unique = true)
    private String email;

    @Column(nullable = false, unique = true)
    private String token;

    @Column(nullable = false)
    private LocalDateTime expirationTime;

    @Column(nullable = false)
    private LocalDateTime lastSentAt;

    @Builder.Default
    private boolean isVerified = false;

    public void verify(){
        this.isVerified = true;
    }

    public void renew(String token, LocalDateTime sentAt, LocalDateTime expirationTime) {
        this.token = token;
        this.lastSentAt = sentAt;
        this.expirationTime = expirationTime;
        this.isVerified = false;
    }

    public boolean canResend(LocalDateTime now) {
        return lastSentAt == null || !lastSentAt.plusMinutes(1).isAfter(now);
    }
    
    public boolean isExpired() {
        return LocalDateTime.now().isAfter(this.expirationTime);
    }
    
}
