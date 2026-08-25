ALTER TABLE email_verification ADD COLUMN IF NOT EXISTS last_sent_at TIMESTAMP;
UPDATE email_verification
SET last_sent_at = COALESCE(last_sent_at, expiration_time - INTERVAL '5 minutes', CURRENT_TIMESTAMP)
WHERE last_sent_at IS NULL;
ALTER TABLE email_verification ALTER COLUMN last_sent_at SET NOT NULL;

WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY email
               ORDER BY is_verified DESC, expiration_time DESC, id DESC
           ) AS row_number
    FROM email_verification
)
DELETE FROM email_verification
WHERE id IN (SELECT id FROM ranked WHERE row_number > 1);

WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY user_id, dish_id
               ORDER BY achievement_level DESC, created_at ASC NULLS LAST, id ASC
           ) AS row_number
    FROM user_dish
)
DELETE FROM user_dish
WHERE id IN (SELECT id FROM ranked WHERE row_number > 1);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM dish GROUP BY stage HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'dish.stage 중복 데이터를 먼저 정리해야 합니다.';
    END IF;
    IF EXISTS (SELECT 1 FROM dish WHERE stage IS NULL OR stage < 1) THEN
        RAISE EXCEPTION 'dish.stage에는 1 이상의 값만 허용됩니다.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM user_dish
        WHERE user_id IS NULL OR dish_id IS NULL
           OR achievement_level IS NULL OR achievement_level NOT BETWEEN 0 AND 2
    ) THEN
        RAISE EXCEPTION 'user_dish 무결성 위반 데이터를 먼저 정리해야 합니다.';
    END IF;
END $$;

ALTER TABLE users ALTER COLUMN clear_progress_level SET DEFAULT 1;
UPDATE users SET clear_progress_level = 1 WHERE clear_progress_level IS NULL;
ALTER TABLE users ALTER COLUMN clear_progress_level SET NOT NULL;
ALTER TABLE users ALTER COLUMN refresh_token TYPE VARCHAR(64);

ALTER TABLE dish ALTER COLUMN stage SET NOT NULL;
ALTER TABLE user_dish ALTER COLUMN user_id SET NOT NULL;
ALTER TABLE user_dish ALTER COLUMN dish_id SET NOT NULL;
ALTER TABLE user_dish ALTER COLUMN achievement_level SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uk_dish_stage') THEN
        ALTER TABLE dish ADD CONSTRAINT uk_dish_stage UNIQUE (stage);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_dish_stage_positive') THEN
        ALTER TABLE dish ADD CONSTRAINT chk_dish_stage_positive CHECK (stage > 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uk_email_verification_email') THEN
        ALTER TABLE email_verification ADD CONSTRAINT uk_email_verification_email UNIQUE (email);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uk_user_dish_user_id_dish_id') THEN
        ALTER TABLE user_dish
            ADD CONSTRAINT uk_user_dish_user_id_dish_id UNIQUE (user_id, dish_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_user_dish_achievement_level') THEN
        ALTER TABLE user_dish
            ADD CONSTRAINT chk_user_dish_achievement_level CHECK (achievement_level BETWEEN 0 AND 2);
    END IF;
END $$;
