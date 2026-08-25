package com.game.gameserver.exception;

import java.time.Instant;
import java.util.Map;

import lombok.Builder;
import lombok.Getter;

@Getter
@Builder
public class ApiErrorResponse {

    private Instant timestamp;
    private int status;
    private String code;
    private String message;
    private Map<String, String> fieldErrors;
}
