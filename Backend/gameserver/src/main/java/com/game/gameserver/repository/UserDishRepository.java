package com.game.gameserver.repository;

import java.util.List;
import java.util.Optional;

import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;
import org.springframework.stereotype.Repository;

import com.game.gameserver.entity.Dish;
import com.game.gameserver.entity.UserDish;
import com.game.gameserver.entity.Users;

import jakarta.persistence.LockModeType;

@Repository
public interface UserDishRepository extends JpaRepository<UserDish, Long> {

    List<UserDish> findByUser(Users user);

    @Query("select ud from UserDish ud join fetch ud.dish where ud.user = :user order by ud.dish.stage")
    List<UserDish> findCollectionByUser(@Param("user") Users user);

    @Lock(LockModeType.PESSIMISTIC_WRITE)
    Optional<UserDish> findByUserAndDish(Users user, Dish dish);

    void deleteByUser(Users user);
}
