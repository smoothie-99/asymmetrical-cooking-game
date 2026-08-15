package com.game.gameserver.repository;

import java.util.Optional;

import org.springframework.data.jpa.repository.JpaRepository;

import com.game.gameserver.entity.Users;

public interface UserRepository extends JpaRepository<Users, Long> {

    Optional<Users> findByLoginId(String loginId);
    Optional<Users> findByEmail(String email);
    
    boolean existsByLoginId(String loginId);
    boolean existsByEmail(String email);
    boolean existsByNickname(String nickname);

}
