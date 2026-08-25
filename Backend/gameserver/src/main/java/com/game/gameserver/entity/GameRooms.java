package com.game.gameserver.entity;

import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;

import org.hibernate.annotations.CreationTimestamp;
import org.hibernate.annotations.JdbcTypeCode;
import org.hibernate.annotations.UpdateTimestamp;
import org.hibernate.type.SqlTypes;

import jakarta.persistence.*;
import jakarta.persistence.GeneratedValue;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Entity
@Table(name = "game_rooms")
@Getter
@NoArgsConstructor
public class GameRooms {

    @Id
    @GeneratedValue(strategy=GenerationType.IDENTITY)
    private Long id;

    @Column(name="room_name", length=20, nullable = false)
    private String roomName="우당탕탕 요리방";

    @Column(name="room_code", length=10, unique = true, nullable = false)
    private String roomCode;

    @Column(name="current_level")
    private Integer currentLevel = 1;

    @JdbcTypeCode(SqlTypes.JSON)
    @Column(name="cook_book", columnDefinition = "jsonb")
    private List<Object> cookBook = new ArrayList<>();

    @Column(name="final_score")
    private Integer finalScore = 0;

    @Column(name="start_time")
    private LocalDateTime startTime;

    @Column(name="end_time")
    private LocalDateTime endTime;

    @CreationTimestamp
    @Column(name="created_at", updatable=false)
    private LocalDateTime createdAt;

    @UpdateTimestamp
    @Column(name="updated_at")
    private LocalDateTime updatedAt;
}
