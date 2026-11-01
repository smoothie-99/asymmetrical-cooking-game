package com.game.gameserver;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.IntUnaryOperator;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.test.context.ActiveProfiles;

import com.game.gameserver.entity.UserDish;
import com.game.gameserver.entity.Users;
import com.game.gameserver.repository.EmailVerificationRepository;
import com.game.gameserver.repository.RoomPlayerRepository;
import com.game.gameserver.repository.UserDishRepository;
import com.game.gameserver.repository.UserRepository;
import com.game.gameserver.service.UserService;

@SpringBootTest
@ActiveProfiles("test")
class GameResultConcurrencyTest {

    private static final String LOGIN_ID = "concurrent01";
    private static final String PASSWORD = "Password1!";
    private static final int STAGE = 1;
    private static final int REQUEST_COUNT = 100;
    private static final int WORKER_COUNT = 20;

    @Autowired
    private UserService userService;

    @Autowired
    private UserRepository userRepository;

    @Autowired
    private UserDishRepository userDishRepository;

    @Autowired
    private RoomPlayerRepository roomPlayerRepository;

    @Autowired
    private EmailVerificationRepository emailVerificationRepository;

    @Autowired
    private PasswordEncoder passwordEncoder;

    private ExecutorService executor;

    @BeforeEach
    void setUp() {
        roomPlayerRepository.deleteAllInBatch();
        userDishRepository.deleteAllInBatch();
        userRepository.deleteAllInBatch();
        emailVerificationRepository.deleteAllInBatch();

        userRepository.saveAndFlush(Users.builder()
                .loginId(LOGIN_ID)
                .password(passwordEncoder.encode(PASSWORD))
                .email("concurrent01@example.com")
                .nickname("동시성테스트")
                .isEmailVerified(true)
                .clearProgressLevel(1)
                .build());
    }

    @AfterEach
    void tearDown() throws InterruptedException {
        if (executor != null && !executor.isTerminated()) {
            executor.shutdownNow();
            executor.awaitTermination(10, TimeUnit.SECONDS);
        }
    }

    @Test
    void oneHundredMixedResultsPreserveHighestLevelAndSingleRecord() throws Exception {
        userService.saveDishResult(LOGIN_ID, STAGE, 0);

        runConcurrently(index -> index % 3);

        assertHighestLevelAndNoDuplicates();
    }

    @Test
    void oneHundredLowerResultsNeverOverwritePerfect() throws Exception {
        userService.saveDishResult(LOGIN_ID, STAGE, 2);

        runConcurrently(index -> index % 2);

        assertHighestLevelAndNoDuplicates();
    }

    private void runConcurrently(IntUnaryOperator levelProvider) throws Exception {
        executor = Executors.newFixedThreadPool(WORKER_COUNT);
        CountDownLatch workersReady = new CountDownLatch(WORKER_COUNT);
        CountDownLatch start = new CountDownLatch(1);
        AtomicInteger completedRequests = new AtomicInteger();
        List<Future<?>> futures = new ArrayList<>();

        try {
            for (int index = 0; index < REQUEST_COUNT; index++) {
                int achievementLevel = levelProvider.applyAsInt(index);

                futures.add(executor.submit(() -> {
                    workersReady.countDown();

                    if (!start.await(30, TimeUnit.SECONDS)) {
                        throw new IllegalStateException("동시 실행 시작 대기 시간이 초과되었습니다.");
                    }

                    userService.saveDishResult(LOGIN_ID, STAGE, achievementLevel);
                    completedRequests.incrementAndGet();
                    return null;
                }));
            }

            assertThat(workersReady.await(30, TimeUnit.SECONDS))
                    .as("모든 작업 스레드가 시작 준비를 마쳐야 한다")
                    .isTrue();

            start.countDown();

            for (Future<?> future : futures) {
                future.get(90, TimeUnit.SECONDS);
            }

            assertThat(completedRequests.get())
                    .as("예외 없이 처리된 서비스 호출 수")
                    .isEqualTo(REQUEST_COUNT);
        } finally {
            start.countDown();
            executor.shutdownNow();
            executor.awaitTermination(10, TimeUnit.SECONDS);
        }
    }

    private void assertHighestLevelAndNoDuplicates() {
        Users user = userRepository.findByLoginId(LOGIN_ID).orElseThrow();
        List<UserDish> records = userDishRepository.findByUser(user);

        assertThat(records)
                .as("동일 사용자와 스테이지의 기록은 한 건만 존재해야 한다")
                .hasSize(1);
        assertThat(records.get(0).getAchievementLevel())
                .as("낮은 등급 요청이 최고 등급을 덮어쓰면 안 된다")
                .isEqualTo(2);
        assertThat(user.getClearProgressLevel())
                .as("동시 요청에도 진행도는 한 단계만 증가해야 한다")
                .isEqualTo(2);
    }
}
