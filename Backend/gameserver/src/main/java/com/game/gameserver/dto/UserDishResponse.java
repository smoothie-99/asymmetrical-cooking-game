package com.game.gameserver.dto;

import lombok.Builder;
import lombok.Getter;

@Getter
@Builder
public class UserDishResponse {

    private String dishName;
    private Integer stage;
    private Integer achievementLevel; 
    private String acquiredAt;
}
