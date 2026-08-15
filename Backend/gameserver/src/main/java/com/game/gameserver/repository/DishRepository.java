package com.game.gameserver.repository;

import com.game.gameserver.entity.Dish;

import java.util.Optional;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

@Repository
public interface DishRepository extends JpaRepository<Dish, Long> {
    Optional<Dish> findByStage(Integer stage);
}
