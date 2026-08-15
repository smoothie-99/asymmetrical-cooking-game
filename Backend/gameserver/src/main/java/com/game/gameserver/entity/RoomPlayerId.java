package com.game.gameserver.entity;

import java.io.Serializable;

import lombok.AllArgsConstructor;
import lombok.EqualsAndHashCode;
import lombok.NoArgsConstructor;

@EqualsAndHashCode
@NoArgsConstructor
@AllArgsConstructor
public class RoomPlayerId implements Serializable{

    private Long gameRoom;
    private Long user;

}
