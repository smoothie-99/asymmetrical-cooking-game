CREATE TABLE IF NOT EXISTS users (
    id BIGSERIAL PRIMARY KEY,
    login_id VARCHAR(10) NOT NULL UNIQUE,
    password VARCHAR(255) NOT NULL,
    email VARCHAR(100) NOT NULL UNIQUE,
    is_email_verified BOOLEAN NOT NULL DEFAULT FALSE,
    nickname VARCHAR(10) NOT NULL UNIQUE,
    refresh_token_hash VARCHAR(64),
    last_login_time TIMESTAMP,
    clear_progress_level INTEGER NOT NULL DEFAULT 1,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE users ADD COLUMN IF NOT EXISTS refresh_token_hash VARCHAR(64);
ALTER TABLE users DROP COLUMN IF EXISTS refresh_token;
UPDATE users SET refresh_token_hash = NULL;
UPDATE users SET clear_progress_level = 1 WHERE clear_progress_level IS NULL;
ALTER TABLE users ALTER COLUMN login_id TYPE VARCHAR(10);
ALTER TABLE users ALTER COLUMN email TYPE VARCHAR(100);
ALTER TABLE users ALTER COLUMN nickname TYPE VARCHAR(10);
ALTER TABLE users ALTER COLUMN clear_progress_level SET DEFAULT 1;
ALTER TABLE users ALTER COLUMN clear_progress_level SET NOT NULL;
ALTER TABLE users DROP CONSTRAINT IF EXISTS chk_users_clear_progress_level;
ALTER TABLE users ADD CONSTRAINT chk_users_clear_progress_level
    CHECK (clear_progress_level BETWEEN 1 AND 12);

CREATE TABLE IF NOT EXISTS dish (
    id BIGSERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    stage INTEGER NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_dish_stage ON dish(stage);

CREATE TABLE IF NOT EXISTS user_dish (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(id),
    dish_id BIGINT NOT NULL REFERENCES dish(id),
    achievement_level INTEGER NOT NULL CHECK (achievement_level BETWEEN 0 AND 2),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uk_user_dish_user_id_dish_id UNIQUE (user_id, dish_id)
);

ALTER TABLE user_dish DROP CONSTRAINT IF EXISTS chk_user_dish_achievement_level;
ALTER TABLE user_dish ADD CONSTRAINT chk_user_dish_achievement_level
    CHECK (achievement_level BETWEEN 0 AND 2);

DROP TABLE IF EXISTS email_verification;
CREATE TABLE email_verification (
    id BIGSERIAL PRIMARY KEY,
    email VARCHAR(100) NOT NULL UNIQUE,
    token_hash VARCHAR(64) NOT NULL UNIQUE,
    expiration_time TIMESTAMP NOT NULL,
    is_verified BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS game_rooms (
    id BIGSERIAL PRIMARY KEY,
    room_name VARCHAR(20) NOT NULL DEFAULT '우당탕탕 요리방',
    room_code VARCHAR(10) NOT NULL UNIQUE,
    current_level INTEGER DEFAULT 1,
    cook_book JSONB DEFAULT '[]'::jsonb,
    final_score INTEGER DEFAULT 0,
    start_time TIMESTAMP,
    end_time TIMESTAMP,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS room_players (
    room_id BIGINT NOT NULL REFERENCES game_rooms(id),
    user_id BIGINT NOT NULL REFERENCES users(id),
    role_type VARCHAR(20) NOT NULL,
    joined_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (room_id, user_id)
);

INSERT INTO dish(name, stage) VALUES
    ('Mercenary Salad', 1),
    ('Samurai Stew', 2),
    ('Nun Salad', 3),
    ('Beggar Pasta', 4),
    ('Pirate Soup', 5),
    ('Dragon Steak', 6),
    ('Archmage Skewer', 7),
    ('Golem Skewer', 8),
    ('Orc Salad', 9),
    ('King Pasta', 10),
    ('Goblin Pie', 11),
    ('Demon Pie', 12)
ON CONFLICT (stage) DO NOTHING;
