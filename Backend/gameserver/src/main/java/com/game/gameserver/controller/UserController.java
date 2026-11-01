package com.game.gameserver.controller;

import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.game.gameserver.dto.ProfileUpdateRequest;
import com.game.gameserver.dto.PasswordChangeRequest;
import com.game.gameserver.dto.UserDishResponse;
import com.game.gameserver.dto.UserResponse;
import com.game.gameserver.service.UserService;

import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;

import java.util.List;

import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PatchMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.PostMapping;
import jakarta.validation.Valid;

@RestController
@RequestMapping("/api/users")
@RequiredArgsConstructor
@Slf4j
public class UserController {

    private final UserService userService;

    @GetMapping("/me")
    public ResponseEntity<UserResponse> getMyInfo(@AuthenticationPrincipal String loginId) {
        UserResponse userInfo = userService.getMyInfo(loginId);
        return ResponseEntity.ok(userInfo);
    }

    @PatchMapping("/me")
    public ResponseEntity<UserResponse> updateProfile(@AuthenticationPrincipal String loginId,
            @Valid @RequestBody ProfileUpdateRequest request) {

        UserResponse updatedUser = userService.updateProfile(loginId, request);
        return ResponseEntity.ok(updatedUser);
    }

    @PatchMapping("/me/password")
    public ResponseEntity<String> changePassword(
            @AuthenticationPrincipal String loginId,
            @Valid @RequestBody PasswordChangeRequest request) {
        userService.changePassword(loginId, request);
        return ResponseEntity.ok("비밀번호가 변경되었습니다.");
    }

    @GetMapping("/me/collection")
    public ResponseEntity<List<UserDishResponse>> getMyCollection(@AuthenticationPrincipal String loginId) {
        List<UserDishResponse> collection = userService.getUserCollection(loginId);
        return ResponseEntity.ok(collection);
    }

    @PostMapping("/logout")
    public ResponseEntity<String> logout(@AuthenticationPrincipal String loginId) {
        userService.logout(loginId);

        // 클라이언트 측에서 JWT 토큰을 삭제하도록 안내
        return ResponseEntity.ok("로그아웃 되었습니다.");
    }

    @DeleteMapping("/me")
    public ResponseEntity<String> withdraw(@AuthenticationPrincipal String loginId) {
        userService.withdraw(loginId);
        return ResponseEntity.ok("회원 탈퇴가 완료되었습니다. 이용해주셔서 감사합니다.");
    }
}
