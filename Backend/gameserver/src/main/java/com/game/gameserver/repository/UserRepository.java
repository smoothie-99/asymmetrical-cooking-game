package com.game.gameserver.repository;

import java.util.Optional;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import com.game.gameserver.entity.Users;

import jakarta.persistence.LockModeType;

public interface UserRepository extends JpaRepository<Users, Long> {

    Optional<Users> findByLoginId(String loginId);

    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select u from Users u where u.loginId = :loginId")
    Optional<Users> findByLoginIdForUpdate(@Param("loginId") String loginId);

    Optional<Users> findByEmail(String email);
    
    boolean existsByLoginId(String loginId);
    boolean existsByEmail(String email);
    boolean existsByNickname(String nickname);
    boolean existsByNicknameAndIdNot(String nickname, Long id);

}
