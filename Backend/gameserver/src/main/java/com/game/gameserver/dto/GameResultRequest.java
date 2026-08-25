package com.game.gameserver.dto;

import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;
import lombok.Getter;
import lombok.Setter;

@Getter
@Setter
public class GameResultRequest {
    @NotNull(message = "스테이지를 입력해주세요.")
    @Min(value = 1, message = "스테이지는 1 이상이어야 합니다.")
    @Max(value = 12, message = "스테이지는 12 이하여야 합니다.")
    private Integer stage;

    @NotNull(message = "달성 등급을 입력해주세요.")
    @Min(value = 0, message = "달성 등급은 0 이상이어야 합니다.")
    @Max(value = 2, message = "달성 등급은 2 이하여야 합니다.")
    private Integer achievementLevel; // 0: FAIL, 1: CLEAR, 2: PERFECT
}
