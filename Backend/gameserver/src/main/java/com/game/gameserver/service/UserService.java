package com.game.gameserver.service;

import java.util.List;

import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.game.gameserver.dto.PasswordChangeRequest;
import com.game.gameserver.dto.ProfileUpdateRequest;
import com.game.gameserver.dto.UserDishResponse;
import com.game.gameserver.dto.UserResponse;
import com.game.gameserver.entity.Dish;
import com.game.gameserver.entity.UserDish;
import com.game.gameserver.entity.Users;
import com.game.gameserver.repository.DishRepository;
import com.game.gameserver.repository.UserDishRepository;
import com.game.gameserver.repository.UserRepository;
import com.game.gameserver.repository.RoomPlayerRepository;

import lombok.RequiredArgsConstructor;

@Service
@RequiredArgsConstructor
@Transactional(readOnly = true)
public class UserService {

    private final UserRepository userRepository;
    private final UserDishRepository userDishRepository;
    private final DishRepository dishRepository;
    private final PasswordEncoder passwordEncoder;
    private final RoomPlayerRepository roomPlayerRepository;

    public UserResponse getMyInfo(String loginId) {
        Users user = userRepository.findByLoginId(loginId)
                .orElseThrow(() -> new IllegalArgumentException("사용자를 찾을 수 없습니다."));

        return UserResponse.builder()
                .loginId(user.getLoginId())
                .email(user.getEmail())
                .nickname(user.getNickname())
                .clearProgressLevel(user.getClearProgressLevel())
                .isEmailVerified(user.isEmailVerified())
                .build();
    }

    @Transactional
    public UserResponse updateProfile(String loginId, ProfileUpdateRequest request) {
        Users user = userRepository.findByLoginId(loginId)
                .orElseThrow(() -> new IllegalArgumentException("사용자를 찾을 수 없습니다."));

        if (request.getNickname() != null) {
            if (!request.getNickname().equals(user.getNickname())
                    && userRepository.existsByNickname(request.getNickname())) {
                throw new IllegalStateException("이미 존재하는 닉네임입니다.");
            }
            user.setNickname(request.getNickname());
        }

        return UserResponse.builder()
                .loginId(user.getLoginId())
                .email(user.getEmail())
                .nickname(user.getNickname())
                .clearProgressLevel(user.getClearProgressLevel())
                .isEmailVerified(user.isEmailVerified())
                .build();
    }

    public List<UserDishResponse> getUserCollection(String loginId) {
        Users user = userRepository.findByLoginId(loginId)
                .orElseThrow(() -> new IllegalArgumentException("사용자를 찾을 수 없습니다."));

        return userDishRepository.findByUser(user).stream()
                .map(ud -> UserDishResponse.builder()
                        .dishName(ud.getDish().getName())
                        .stage(ud.getDish().getStage())
                        .achievementLevel(ud.getAchievementLevel())
                        .acquiredAt(ud.getCreatedAt().toString())
                        .build())
                .toList();
    }

    @Transactional
    public void saveDishResult(String loginId, Integer stage, Integer achievementLevel) {
        validateAchievementLevel(achievementLevel);

        // UserDish가 아직 없는 최초 저장도 동시 요청으로부터 보호하기 위해
        // 자식 행 조회 전에 부모인 사용자 행을 잠근다.
        Users user = userRepository.findByLoginIdForUpdate(loginId)
                .orElseThrow(() -> new IllegalArgumentException("사용자를 찾을 수 없습니다."));

        if (stage == null || stage < 1 || stage > 12) {
            throw new IllegalArgumentException("스테이지는 1부터 12 사이여야 합니다.");
        }
        int clearProgressLevel = user.getClearProgressLevel() == null ? 1 : user.getClearProgressLevel();
        if (user.getClearProgressLevel() == null) {
            user.setClearProgressLevel(clearProgressLevel);
        }
        if (stage > clearProgressLevel) {
            throw new IllegalArgumentException("아직 해금되지 않은 스테이지입니다.");
        }

        Dish dish = dishRepository.findByStage(stage)
                .orElseThrow(() -> new IllegalArgumentException("해당 스테이지의 요리를 찾을 수 없습니다."));

        userDishRepository.findByUserAndDish(user, dish)
                .ifPresentOrElse(existing -> {
                    // 낮은 등급의 재전송으로 기존 최고 기록이 내려가지 않도록 한다.
                    if (achievementLevel > existing.getAchievementLevel()) {
                        existing.setAchievementLevel(achievementLevel);
                    }
                }, () -> userDishRepository.save(UserDish.builder()
                        .user(user)
                        .dish(dish)
                        .achievementLevel(achievementLevel)
                        .build()));

        if (achievementLevel >= 1 && stage == clearProgressLevel && stage < 12) {
            user.setClearProgressLevel(stage + 1);
        }
    }

    private void validateAchievementLevel(Integer achievementLevel) {
        if (achievementLevel == null || achievementLevel < 0 || achievementLevel > 2) {
            throw new IllegalArgumentException("달성 등급은 0(FAIL), 1(CLEAR), 2(PERFECT) 중 하나여야 합니다.");
        }
    }

    @Transactional
    public void changePassword(String loginId, PasswordChangeRequest request) {
        Users user = userRepository.findByLoginId(loginId)
                .orElseThrow(() -> new IllegalArgumentException("사용자를 찾을 수 없습니다."));

        // 현재 비밀번호 확인
        if (!passwordEncoder.matches(request.getCurrentPassword(), user.getPassword())) {
            throw new IllegalArgumentException("현재 비밀번호가 일치하지 않습니다.");
        }

        String newPassword = request.getNewPassword();

        if (!newPassword.equals(request.getNewPasswordConfirm())) {
            throw new IllegalArgumentException("새 비밀번호가 일치하지 않습니다.");
        }

        String passwordPattern = "^(?=.*[A-Za-z])(?=.*\\d)(?=.*[^A-Za-z\\d\\s]).{8,20}$";

        if (!newPassword.matches(passwordPattern)) {
            throw new IllegalArgumentException("비밀번호는 영문, 숫자, 특수문자를 포함해 8~20자여야 합니다.");
        }

        // 새 비밀번호로 업데이트
        String encodedNewPassword = passwordEncoder.encode(newPassword);
        user.setPassword(encodedNewPassword);
        user.setRefreshTokenHash(null);
    }

    @Transactional
    public void logout(String loginId) {
        Users user = userRepository.findByLoginId(loginId)
                .orElseThrow(() -> new IllegalArgumentException("사용자를 찾을 수 없습니다."));

        user.setRefreshTokenHash(null);
    }

    @Transactional
    public void withdraw(String loginId) {
        Users user = userRepository.findByLoginId(loginId)
                .orElseThrow(() -> new IllegalArgumentException("사용자를 찾을 수 없습니다."));

        roomPlayerRepository.deleteByUser(user);
        userDishRepository.deleteByUser(user);

        userRepository.delete(user);
    }
}
