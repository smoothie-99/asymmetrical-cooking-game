package com.game.gameserver.entity;

import java.time.LocalDateTime;

import org.hibernate.annotations.CreationTimestamp;
import org.hibernate.annotations.UpdateTimestamp;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;

@Entity
@Table(name = "users")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class Users {

    @Id
    @GeneratedValue(strategy=GenerationType.IDENTITY) //serial처럼 번호 1씩 자동 증가
    private Long id;

    @Column(name = "login_id", length=10, unique = true, nullable = false)
    private String loginId;

    @Column(nullable=false)
    private String password;

    @Column(length=100, unique = true, nullable=false)
    private String email;

    @Column(name="is_email_verified")
    @Builder.Default
    private boolean isEmailVerified = false;

    @Column(length=10, unique = true, nullable=false)
    private String nickname;

    @Column(name="refresh_token_hash", length = 64)
    private String refreshTokenHash;

    @Column(name="last_login_time")
    private LocalDateTime lastLoginTime;

    @Column(name="clear_progress_level")
    @Builder.Default
    private Integer clearProgressLevel = 1;

    @CreationTimestamp
    @Column(name="created_at", updatable=false)
    private LocalDateTime createdAt;

    @UpdateTimestamp
    @Column(name="updated_at")
    private LocalDateTime updatedAt;

}
