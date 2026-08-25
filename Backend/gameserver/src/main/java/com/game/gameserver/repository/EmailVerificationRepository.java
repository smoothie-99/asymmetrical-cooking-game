package com.game.gameserver.repository;

import java.util.Optional;

import org.springframework.data.jpa.repository.JpaRepository;
import com.game.gameserver.entity.EmailVerification;

public interface EmailVerificationRepository extends JpaRepository<EmailVerification, Long> {
    Optional<EmailVerification> findByTokenHash(String tokenHash);
    Optional<EmailVerification> findByEmail(String email);
    void deleteByEmail(String email);


}
