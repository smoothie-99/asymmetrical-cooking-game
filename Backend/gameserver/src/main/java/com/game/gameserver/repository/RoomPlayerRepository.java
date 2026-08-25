package com.game.gameserver.repository;

import org.springframework.data.jpa.repository.JpaRepository;

import com.game.gameserver.entity.RoomPlayerId;
import com.game.gameserver.entity.RoomPlayers;
import com.game.gameserver.entity.Users;

public interface RoomPlayerRepository extends JpaRepository<RoomPlayers, RoomPlayerId> {
    void deleteByUser(Users user);
}
