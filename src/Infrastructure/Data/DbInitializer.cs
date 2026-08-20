using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Infrastructure.Repositories;

namespace Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IDbConnection db)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS users (
                id BIGSERIAL PRIMARY KEY,
                username VARCHAR(100) NOT NULL,
                password_hash VARCHAR(255) NOT NULL,
                role VARCHAR(50) NOT NULL,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                last_login_at TIMESTAMPTZ NULL,
                created_by BIGINT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by BIGINT NULL,
                updated_at TIMESTAMPTZ NULL,
                deleted_by BIGINT NULL,
                deleted_at TIMESTAMPTZ NULL
            );

            CREATE TABLE IF NOT EXISTS user_profiles (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
                employee_code VARCHAR(50) NOT NULL,
                first_name VARCHAR(100) NOT NULL,
                last_name VARCHAR(100) NOT NULL,
                id_card_no VARCHAR(20) NULL,
                phone VARCHAR(20) NOT NULL,
                email VARCHAR(255) NULL,
                birth_date DATE NULL,
                license_no VARCHAR(100) NULL,
                license_issue_date DATE NULL,
                license_expiration_date DATE NULL,
                vehicle_id BIGINT NULL REFERENCES vehicles(id) ON DELETE SET NULL,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_by BIGINT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by BIGINT NULL,
                updated_at TIMESTAMPTZ NULL,
                deleted_by BIGINT NULL,
                deleted_at TIMESTAMPTZ NULL
            );

            -- Add birth_date and id_card_no columns if missing on existing user_profiles
            ALTER TABLE user_profiles ADD COLUMN IF NOT EXISTS birth_date DATE NULL;
            ALTER TABLE user_profiles ADD COLUMN IF NOT EXISTS id_card_no VARCHAR(20) NULL;

            -- Drop old foreign key constraints on existing tables that referenced drivers table
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'jobs') THEN
                    ALTER TABLE jobs DROP CONSTRAINT IF EXISTS jobs_driver_id_fkey;
                END IF;
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'job_assignment_histories') THEN
                    ALTER TABLE job_assignment_histories DROP CONSTRAINT IF EXISTS job_assignment_histories_driver_id_fkey;
                END IF;
            END $$;

            -- Always drop drivers table explicitly
            DROP TABLE IF EXISTS drivers CASCADE;

            -- Remove old unique constraints if exist
            ALTER TABLE users DROP CONSTRAINT IF EXISTS users_username_key;

            -- Partial Unique Indexes
            CREATE UNIQUE INDEX IF NOT EXISTS uq_users_username_active ON users(username) WHERE deleted_at IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_user_profiles_employee_code_active ON user_profiles(employee_code) WHERE deleted_at IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_user_profiles_id_card_no_active ON user_profiles(id_card_no) WHERE deleted_at IS NULL AND id_card_no IS NOT NULL AND id_card_no != '';

            CREATE TABLE IF NOT EXISTS vehicle_types (
                id BIGSERIAL PRIMARY KEY,
                name VARCHAR(100) NOT NULL UNIQUE,
                description VARCHAR(255) NULL,
                created_by BIGINT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by BIGINT NULL,
                updated_at TIMESTAMPTZ NULL,
                deleted_by BIGINT NULL,
                deleted_at TIMESTAMPTZ NULL
            );

            ALTER TABLE vehicle_types ADD COLUMN IF NOT EXISTS created_by BIGINT NULL;
            ALTER TABLE vehicle_types ADD COLUMN IF NOT EXISTS updated_by BIGINT NULL;
            ALTER TABLE vehicle_types ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
            ALTER TABLE vehicle_types ADD COLUMN IF NOT EXISTS deleted_by BIGINT NULL;
            ALTER TABLE vehicle_types ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;

            CREATE TABLE IF NOT EXISTS vehicles (
                id BIGSERIAL PRIMARY KEY,
                plate_number VARCHAR(50) NOT NULL UNIQUE,
                model VARCHAR(100) NOT NULL,
                vehicle_type_id BIGINT NULL REFERENCES vehicle_types(id) ON DELETE SET NULL,
                capacity DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_by BIGINT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by BIGINT NULL,
                updated_at TIMESTAMPTZ NULL,
                deleted_by BIGINT NULL,
                deleted_at TIMESTAMPTZ NULL
            );

            CREATE TABLE IF NOT EXISTS refresh_tokens (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                token_hash VARCHAR(255) NOT NULL UNIQUE,
                expires_at TIMESTAMPTZ NOT NULL,
                revoked_at TIMESTAMPTZ NULL,
                replaced_by_token_id BIGINT NULL REFERENCES refresh_tokens(id),
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS jobs (
                id BIGSERIAL PRIMARY KEY,
                job_number VARCHAR(50) NOT NULL UNIQUE,
                title VARCHAR(200) NOT NULL,
                description TEXT NULL,
                driver_id BIGINT NULL REFERENCES user_profiles(id) ON DELETE SET NULL,
                vehicle_id BIGINT NULL REFERENCES vehicles(id) ON DELETE SET NULL,
                status VARCHAR(50) NOT NULL DEFAULT 'Pending',
                pickup_location TEXT NOT NULL,
                pickup_lat DOUBLE PRECISION NULL,
                pickup_lng DOUBLE PRECISION NULL,
                scheduled_start_at TIMESTAMPTZ NULL,
                started_at TIMESTAMPTZ NULL,
                arrived_at TIMESTAMPTZ NULL,
                completed_at TIMESTAMPTZ NULL,
                cancelled_at TIMESTAMPTZ NULL,
                cancellation_reason TEXT NULL,
                row_version INT NOT NULL DEFAULT 1,
                created_by BIGINT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NULL,
                deleted_by BIGINT NULL,
                deleted_at TIMESTAMPTZ NULL
            );

            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS pickup_lat DOUBLE PRECISION NULL;
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS pickup_lng DOUBLE PRECISION NULL;
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS contact_name VARCHAR(200) NULL;
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS contact_phone VARCHAR(50) NULL;
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS companions TEXT NULL;
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS updated_by BIGINT NULL;
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS cancelled_by BIGINT NULL;
            ALTER TABLE jobs DROP COLUMN IF EXISTS dropoff_location;

            CREATE TABLE IF NOT EXISTS job_status_histories (
                id BIGSERIAL PRIMARY KEY,
                job_id BIGINT NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
                from_status VARCHAR(50) NULL,
                to_status VARCHAR(50) NOT NULL,
                changed_by BIGINT NOT NULL REFERENCES users(id),
                notes TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS job_assignment_histories (
                id BIGSERIAL PRIMARY KEY,
                job_id BIGINT NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
                driver_id BIGINT NOT NULL REFERENCES user_profiles(id),
                assigned_by BIGINT NOT NULL REFERENCES users(id),
                assigned_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                unassigned_at TIMESTAMPTZ NULL
            );

            -- Always execute DROP TABLE IF EXISTS drivers CASCADE to remove drivers table if present
            DROP TABLE IF EXISTS drivers CASCADE;

            CREATE TABLE IF NOT EXISTS notification_outbox (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES users(id),
                title VARCHAR(200) NOT NULL,
                body TEXT NOT NULL,
                payload_json TEXT NULL,
                is_processed BOOLEAN NOT NULL DEFAULT FALSE,
                processed_at TIMESTAMPTZ NULL,
                is_read BOOLEAN NOT NULL DEFAULT FALSE,
                read_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE notification_outbox ADD COLUMN IF NOT EXISTS is_read BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE notification_outbox ADD COLUMN IF NOT EXISTS read_at TIMESTAMPTZ NULL;

            CREATE TABLE IF NOT EXISTS audit_logs (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NULL,
                action VARCHAR(100) NOT NULL,
                entity_name VARCHAR(100) NOT NULL,
                entity_id VARCHAR(100) NULL,
                details TEXT NULL,
                details_json TEXT NULL,
                ip_address VARCHAR(50) NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS details TEXT NULL;

            CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
            CREATE INDEX IF NOT EXISTS idx_user_profiles_user_id ON user_profiles(user_id);
            CREATE INDEX IF NOT EXISTS idx_user_profiles_employee_code ON user_profiles(employee_code);
            CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id ON refresh_tokens(user_id);
            CREATE INDEX IF NOT EXISTS idx_jobs_driver_id ON jobs(driver_id);
            CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs(status);
            CREATE INDEX IF NOT EXISTS idx_jobs_created_at ON jobs(created_at);
            CREATE INDEX IF NOT EXISTS idx_job_status_histories_job_id ON job_status_histories(job_id);
            CREATE INDEX IF NOT EXISTS idx_job_assignment_histories_job_id ON job_assignment_histories(job_id);
        ";

        await db.ExecuteAsync(sql);

        var passwordHash = PasswordHasher.HashPassword("admin123");
        var adminExists = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM users WHERE username = 'admin';");
        if (adminExists == 0)
        {
            var insertAdminSql = @"
                INSERT INTO users (username, password_hash, role, is_active, created_at)
                VALUES ('admin', @passwordHash, 'Admin', TRUE, CURRENT_TIMESTAMP);";

            await db.ExecuteAsync(insertAdminSql, new { passwordHash });
        }
        else
        {
            var updateAdminSql = @"
                UPDATE users 
                SET password_hash = @passwordHash, is_active = TRUE 
                WHERE username = 'admin';";

            await db.ExecuteAsync(updateAdminSql, new { passwordHash });
        }

        var seedTypesSql = @"
            INSERT INTO vehicle_types (name, description) VALUES
            ('รถกระบะ 4 ล้อ (Pick-up Truck)', 'รถกระบะบรรทุกทึบ/ตู้เย็น'),
            ('รถบรรทุก 6 ล้อ (Medium Truck)', 'รถบรรทุก 6 ล้อตู้แห้ง/พื้นเรียบ'),
            ('รถบรรทุก 10 ล้อ (Heavy Truck)', 'รถบรรทุก 10 ล้อตู้แห้ง/พื้นเรียบ'),
            ('รถหัวลาก (Trailer / Tractor)', 'รถบรรทุกคอนเทนเนอร์')
            ON CONFLICT (name) DO NOTHING;
        ";
        await db.ExecuteAsync(seedTypesSql);

        var seedVehiclesSql = @"
            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '1กข-1234', 'Isuzu D-Max 1.9 Ddi', id, 1.5, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถกระบะ 4 ล้อ (Pick-up Truck)'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '70-5678', 'Hino 500 Victor 260HP', id, 8.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถบรรทุก 6 ล้อ (Medium Truck)'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '71-9988', 'Isuzu GXZ 360', id, 15.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถบรรทุก 10 ล้อ (Heavy Truck)'
            ON CONFLICT (plate_number) DO NOTHING;
        ";
        await db.ExecuteAsync(seedVehiclesSql);
    }
}
