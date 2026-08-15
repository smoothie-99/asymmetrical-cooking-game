package com.game.gameserver.repository;

import java.util.List;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

import com.game.gameserver.entity.UserDish;
import com.game.gameserver.entity.Users;

@Repository
public interface UserDishRepository extends JpaRepository<UserDish, Long> {

    List<UserDish> findByUser(Users user);
    void deleteByUser(Users user);
}
