package com.game.gameserver.service;

import static org.junit.jupiter.api.Assertions.assertAll;
import static org.junit.jupiter.api.Assertions.assertEquals;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.ActiveProfiles;

import com.game.gameserver.entity.Dish;
import com.game.gameserver.entity.UserDish;
import com.game.gameserver.entity.Users;
import com.game.gameserver.repository.DishRepository;
import com.game.gameserver.repository.UserDishRepository;
import com.game.gameserver.repository.UserRepository;

@SpringBootTest
@ActiveProfiles("test")
class UserDishConcurrencyIntegrationTest {

    private static final int REQUEST_COUNT = 100;
    private static final int WORKER_COUNT = REQUEST_COUNT;

    @Autowired
    private UserService userService;

    @Autowired
    private UserRepository userRepository;

    @Autowired
    private DishRepository dishRepository;

    @Autowired
    private UserDishRepository userDishRepository;

    private Users user;
    private Dish dish;

    @BeforeEach
    void setUp() {
        String suffix = UUID.randomUUID().toString().replace("-", "").substring(0, 8);
        user = userRepository.saveAndFlush(Users.builder()
                .loginId("u" + suffix)
                .password("encoded-password")
                .email(suffix + "@test.local")
                .nickname("n" + suffix)
                .isEmailVerified(true)
                .clearProgressLevel(1)
                .build());

        int stage = 100_000 + Math.floorMod(suffix.hashCode(), 1_000_000);
        dish = dishRepository.saveAndFlush(Dish.builder()
                .name("dish-" + suffix)
                .stage(stage)
                .build());
    }

    @AfterEach
    void tearDown() {
        userDishRepository.deleteAll(userDishRepository.findByUser(user));
        userRepository.deleteById(user.getId());
        dishRepository.deleteById(dish.getId());
    }

    @Test
    @Timeout(60)
    void concurrentMixedResultsCreateOneRowAndKeepHighestAchievement() throws Exception {
        ExecutorService executor = Executors.newFixedThreadPool(WORKER_COUNT);
        CountDownLatch startSignal = new CountDownLatch(1);
        List<Future<?>> futures = new ArrayList<>();

        try {
            for (int i = 0; i < REQUEST_COUNT; i++) {
                int achievementLevel = i % 3;
                futures.add(executor.submit(() -> {
                    startSignal.await();
                    userService.saveDishResult(user.getLoginId(), dish.getStage(), achievementLevel);
                    return null;
                }));
            }

            startSignal.countDown();
            for (Future<?> future : futures) {
                future.get(30, TimeUnit.SECONDS);
            }
        } finally {
            executor.shutdownNow();
            executor.awaitTermination(5, TimeUnit.SECONDS);
        }

        Users persistedUser = userRepository.findByLoginId(user.getLoginId()).orElseThrow();
        List<UserDish> records = userDishRepository.findCollectionByUser(persistedUser);

        assertAll(
                () -> assertEquals(1, records.size(), "동일 user-dish 조합은 한 행만 존재해야 한다."),
                () -> assertEquals(2, records.get(0).getAchievementLevel(), "동시 입력 중 최고 등급이 유지되어야 한다."));
    }
}
