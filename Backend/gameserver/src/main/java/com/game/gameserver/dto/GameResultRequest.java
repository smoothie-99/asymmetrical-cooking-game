package com.game.gameserver.dto;

import lombok.Getter;
import lombok.Setter;

@Getter
@Setter
public class GameResultRequest {
    private Integer stage;
    private Integer achievementLevel; // 0: FAIL, 1: CLEAR, 2: PERFECT
}
