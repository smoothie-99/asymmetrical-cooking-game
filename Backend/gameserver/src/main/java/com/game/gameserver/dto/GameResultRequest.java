package com.game.gameserver.dto;

import lombok.Getter;
import lombok.Setter;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Positive;

@Getter
@Setter
public class GameResultRequest {
    @NotNull
    @Positive
    private Integer stage;

    @NotNull
    @Min(0)
    @Max(2)
    private Integer achievementLevel; // 0: FAIL, 1: CLEAR, 2: PERFECT
}
