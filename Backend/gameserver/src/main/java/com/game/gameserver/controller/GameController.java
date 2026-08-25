package com.game.gameserver.controller;

import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.game.gameserver.dto.GameResultRequest;
import com.game.gameserver.service.UserService;

import lombok.RequiredArgsConstructor;
import jakarta.validation.Valid;

@RestController
@RequestMapping("/api/game")
@RequiredArgsConstructor
public class GameController {

    private final UserService userService;

    @PostMapping("/result")
    public ResponseEntity<String> saveGameResult(
            @AuthenticationPrincipal String loginId,
            @Valid @RequestBody GameResultRequest request) {
        
        userService.saveDishResult(loginId, request.getStage(), request.getAchievementLevel());
        return ResponseEntity.ok("게임 결과가 도감에 저장되었습니다.");
    }
}
