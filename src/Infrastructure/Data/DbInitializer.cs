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
                active_token_id VARCHAR(255) NULL,
                created_by BIGINT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by BIGINT NULL,
                updated_at TIMESTAMPTZ NULL,
                deleted_by BIGINT NULL,
                deleted_at TIMESTAMPTZ NULL
            );

            ALTER TABLE users ADD COLUMN IF NOT EXISTS active_token_id VARCHAR(255) NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS active_web_token_id VARCHAR(255) NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS active_mobile_token_id VARCHAR(255) NULL;

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

            CREATE TABLE IF NOT EXISTS refresh_tokens (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                token_hash VARCHAR(255) NOT NULL UNIQUE,
                channel INT NOT NULL DEFAULT 1,
                expires_at TIMESTAMPTZ NOT NULL,
                revoked_at TIMESTAMPTZ NULL,
                replaced_by_token_id BIGINT NULL REFERENCES refresh_tokens(id),
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE refresh_tokens ADD COLUMN IF NOT EXISTS channel INT NOT NULL DEFAULT 1;

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
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS companion_id BIGINT NULL REFERENCES users(id) ON DELETE SET NULL;
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS updated_by BIGINT NULL;
            ALTER TABLE jobs ADD COLUMN IF NOT EXISTS cancelled_by BIGINT NULL;
            CREATE INDEX IF NOT EXISTS idx_jobs_companion_id ON jobs(companion_id);
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

            CREATE TABLE IF NOT EXISTS user_devices (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                device_id VARCHAR(255) NOT NULL,
                device_name VARCHAR(255) NULL,
                device_model VARCHAR(255) NULL,
                app_version VARCHAR(50) NULL,
                fcm_token TEXT NULL,
                ip_address VARCHAR(50) NULL,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NULL,
                deleted_at TIMESTAMPTZ NULL
            );

            CREATE TABLE IF NOT EXISTS menus (
                id BIGSERIAL PRIMARY KEY,
                name_th VARCHAR(255) NOT NULL,
                endpoint VARCHAR(255) NULL,
                menu_type INT NOT NULL DEFAULT 1,
                external_url TEXT NULL,
                target_path VARCHAR(255) NULL,
                open_mode INT NOT NULL DEFAULT 1,
                authentication_mode INT NOT NULL DEFAULT 1,
                parent_id BIGINT NULL REFERENCES menus(id) ON DELETE CASCADE,
                seq INT NOT NULL DEFAULT 1,
                is_public BOOLEAN NOT NULL DEFAULT FALSE,
                is_marketing BOOLEAN NOT NULL DEFAULT FALSE,
                is_read BOOLEAN NOT NULL DEFAULT FALSE,
                is_create BOOLEAN NOT NULL DEFAULT FALSE,
                is_update BOOLEAN NOT NULL DEFAULT FALSE,
                is_delete BOOLEAN NOT NULL DEFAULT FALSE,
                is_import BOOLEAN NOT NULL DEFAULT FALSE,
                is_export BOOLEAN NOT NULL DEFAULT FALSE,
                created_by BIGINT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by BIGINT NULL,
                updated_at TIMESTAMPTZ NULL,
                deleted_by BIGINT NULL,
                deleted_at TIMESTAMPTZ NULL
            );

            CREATE TABLE IF NOT EXISTS user_menu_permissions (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                menu_id BIGINT NOT NULL REFERENCES menus(id) ON DELETE CASCADE,
                is_read BOOLEAN NOT NULL DEFAULT FALSE,
                is_create BOOLEAN NOT NULL DEFAULT FALSE,
                is_update BOOLEAN NOT NULL DEFAULT FALSE,
                is_delete BOOLEAN NOT NULL DEFAULT FALSE,
                is_import BOOLEAN NOT NULL DEFAULT FALSE,
                is_export BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NULL,
                CONSTRAINT uq_user_menu UNIQUE (user_id, menu_id)
            );

            CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
            CREATE INDEX IF NOT EXISTS idx_user_profiles_user_id ON user_profiles(user_id);
            CREATE INDEX IF NOT EXISTS idx_user_profiles_employee_code ON user_profiles(employee_code);
            CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id ON refresh_tokens(user_id);
            CREATE INDEX IF NOT EXISTS idx_user_devices_user_id ON user_devices(user_id);
            CREATE INDEX IF NOT EXISTS idx_user_devices_device_id ON user_devices(device_id);
            CREATE INDEX IF NOT EXISTS idx_user_devices_fcm_token ON user_devices(fcm_token);
            CREATE INDEX IF NOT EXISTS idx_user_menu_permissions_user_id ON user_menu_permissions(user_id);
            CREATE INDEX IF NOT EXISTS idx_jobs_driver_id ON jobs(driver_id);
            CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs(status);
            CREATE INDEX IF NOT EXISTS idx_jobs_created_at ON jobs(created_at);
            CREATE INDEX IF NOT EXISTS idx_job_status_histories_job_id ON job_status_histories(job_id);
            CREATE INDEX IF NOT EXISTS idx_job_assignment_histories_job_id ON job_assignment_histories(job_id);
        ";

        await db.ExecuteAsync(sql);
        await db.ExecuteAsync("UPDATE menus SET name_th = 'จัดการงาน' WHERE endpoint = '/jobs' AND name_th = 'งานปัจจุบัน';");

        var passwordHash = PasswordHasher.HashPassword("admin123");
        var adminExists = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM users WHERE username = 'admin';");
        if (adminExists == 0)
        {
            var insertAdminSql = @"
                INSERT INTO users (username, password_hash, role, is_active, created_at)
                VALUES ('admin', @passwordHash, 'Admin', TRUE, CURRENT_TIMESTAMP);";

            await db.ExecuteAsync(insertAdminSql, new { passwordHash });
        }

        var seedTypesSql = @"
            INSERT INTO vehicle_types (name, description) VALUES
            ('รถกระบะ 4 ล้อ', 'รถกระบะบรรทุกทึบ/ตู้เย็น'),
            ('รถกระบะ 4 ล้อตู้ทึบ', 'รถกระบะตู้ทึบความสูงมาตรฐาน สำหรับขนส่งพัสดุ'),
            ('รถบรรทุก 6 ล้อ', 'รถบรรทุก 6 ล้อตู้แห้ง/พื้นเรียบ'),
            ('รถบรรทุก 6 ล้อตู้เย็น', 'รถบรรทุกตู้ควบคุมอุณหภูมิสำหรับอาหารสด'),
            ('รถบรรทุก 10 ล้อ', 'รถบรรทุก 10 ล้อตู้แห้ง/พื้นเรียบ'),
            ('รถบรรทุก 10 ล้อดั๊มพ์', 'รถบรรทุกเทท้ายสำหรับงานก่อสร้างและหินดินทราย'),
            ('รถหัวลาก', 'รถบรรทุกคอนเทนเนอร์ 20/40 ฟุต'),
            ('รถเทรลเลอร์พื้นเรียบ', 'ขนส่งโครงสร้างเหล็ก ท่อขนาดใหญ่ และเครื่องจักร'),
            ('รถควบคุมอุณหภูมิ', 'ขนส่งยา เวชภัณฑ์ และอาหารสดแช่แข็ง'),
            ('รถมอเตอร์ไซค์ส่งด่วน', 'จัดส่งเอกสารและพัสดุด่วนภายในเมือง')
            ON CONFLICT (name) DO NOTHING;
        ";
        await db.ExecuteAsync(seedTypesSql);

        var seedVehiclesSql = @"
            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '1กข-1234', 'Isuzu D-Max 1.9 Ddi', id, 1.5, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถกระบะ 4 ล้อ'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '2ฒฉ-5678', 'Toyota Hilux Revo Single Cab', id, 1.8, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถกระบะ 4 ล้อตู้ทึบ'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '70-5678', 'Hino 500 Victor 260HP', id, 8.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถบรรทุก 6 ล้อ'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '70-8912', 'Isuzu Forward FTR 240', id, 7.5, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถบรรทุก 6 ล้อตู้เย็น'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '71-9988', 'Isuzu GXZ 360', id, 15.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถบรรทุก 10 ล้อ'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '72-3456', 'Hino 700 Victor Prime', id, 16.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถบรรทุก 10 ล้อ'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '72-7890', 'Scania P360 Tipper', id, 18.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถบรรทุก 10 ล้อดั๊มพ์'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '73-1122', 'Volvo FH16 6x4 Prime Mover', id, 32.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถหัวลาก'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '73-4455', 'Mercedes-Benz Actros 3344', id, 35.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถเทรลเลอร์พื้นเรียบ'
            ON CONFLICT (plate_number) DO NOTHING;

            INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_at)
            SELECT '1ขค-9012', 'Toyota Hiace Cold Storage Van', id, 2.0, TRUE, CURRENT_TIMESTAMP FROM vehicle_types WHERE name = 'รถควบคุมอุณหภูมิ'
            ON CONFLICT (plate_number) DO NOTHING;
        ";
        await db.ExecuteAsync(seedVehiclesSql);

        // Seed 10 Users and User Profiles (Admin & Drivers)
        var seedUsers = new[]
        {
            new { Username = "manager01", Role = "Admin", EmpCode = "ADM-002", FName = "มนัส", LName = "ชัยชนะ", Phone = "0811112233", Email = "manas.c@foresee.com", IdCard = "1100500998877", BirthDate = "1985-03-12", License = (string?)null, VehPlate = (string?)null },
            new { Username = "somchai01", Role = "Driver", EmpCode = "DRV-001", FName = "สมชาย", LName = "สุขเกษม", Phone = "0812345601", Email = "somchai01@foresee.com", IdCard = "1100500123456", BirthDate = "1990-05-15", License = "DL-1001", VehPlate = "1กข-1234" },
            new { Username = "somkiat02", Role = "Driver", EmpCode = "DRV-002", FName = "สมเกียรติ", LName = "มั่นคง", Phone = "0812345602", Email = "somkiat02@foresee.com", IdCard = "1100500234567", BirthDate = "1992-08-20", License = "DL-1002", VehPlate = "2ฒฉ-5678" },
            new { Username = "wichai03", Role = "Driver", EmpCode = "DRV-003", FName = "วิชัย", LName = "สว่างวงศ์", Phone = "0812345603", Email = "wichai03@foresee.com", IdCard = "1100500345678", BirthDate = "1988-11-10", License = "DL-1003", VehPlate = "70-5678" },
            new { Username = "prasert04", Role = "Driver", EmpCode = "DRV-004", FName = "ประเสริฐ", LName = "ยิ่งเจริญ", Phone = "0812345604", Email = "prasert04@foresee.com", IdCard = "1100500456789", BirthDate = "1986-04-25", License = "DL-1004", VehPlate = "70-8912" },
            new { Username = "anuson05", Role = "Driver", EmpCode = "DRV-005", FName = "อนุสรณ์", LName = "มีทรัพย์", Phone = "0812345605", Email = "anuson05@foresee.com", IdCard = "1100500567890", BirthDate = "1991-09-05", License = "DL-1005", VehPlate = "71-9988" },
            new { Username = "kittisak06", Role = "Driver", EmpCode = "DRV-006", FName = "กิตติศักดิ์", LName = "เจริญสุข", Phone = "0812345606", Email = "kittisak06@foresee.com", IdCard = "1100500678901", BirthDate = "1989-12-18", License = "DL-1006", VehPlate = "72-3456" },
            new { Username = "surasak07", Role = "Driver", EmpCode = "DRV-007", FName = "สุรศักดิ์", LName = "ศรีวิชัย", Phone = "0812345607", Email = "surasak07@foresee.com", IdCard = "1100500789012", BirthDate = "1993-02-14", License = "DL-1007", VehPlate = "72-7890" },
            new { Username = "nattawut08", Role = "Driver", EmpCode = "DRV-008", FName = "ณัฐวุฒิ", LName = "บุญมี", Phone = "0812345608", Email = "nattawut08@foresee.com", IdCard = "1100500890123", BirthDate = "1987-07-30", License = "DL-1008", VehPlate = "73-1122" },
            new { Username = "phitsanu09", Role = "Driver", EmpCode = "DRV-009", FName = "พิษณุ", LName = "ก้องเกียรติ", Phone = "0812345609", Email = "phitsanu09@foresee.com", IdCard = "1100500901234", BirthDate = "1994-10-08", License = "DL-1009", VehPlate = "73-4455" },
            new { Username = "chavalit10", Role = "Driver", EmpCode = "DRV-010", FName = "ชวลิต", LName = "พงษ์ไพศาล", Phone = "0812345610", Email = "chavalit10@foresee.com", IdCard = "1100500987654", BirthDate = "1995-01-22", License = "DL-1010", VehPlate = "1ขค-9012" },
        };

        var defaultDriverPassHash = PasswordHasher.HashPassword("123456");

        foreach (var u in seedUsers)
        {
            var uExists = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM users WHERE username = @Username;", new { u.Username });
            long uId;
            if (uExists == 0)
            {
                var insertUserSql = @"
                    INSERT INTO users (username, password_hash, role, is_active, created_at)
                    VALUES (@Username, @PassHash, @Role, TRUE, CURRENT_TIMESTAMP)
                    RETURNING id;";
                uId = await db.ExecuteScalarAsync<long>(insertUserSql, new { u.Username, PassHash = defaultDriverPassHash, u.Role });
            }
            else
            {
                uId = await db.ExecuteScalarAsync<long>("SELECT id FROM users WHERE username = @Username;", new { u.Username });
            }

            var profExists = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM user_profiles WHERE user_id = @uId;", new { uId });
            if (profExists == 0)
            {
                var insertProfileSql = @"
                    INSERT INTO user_profiles (
                        user_id, employee_code, first_name, last_name, phone, email, id_card_no,
                        birth_date, license_no, license_issue_date, license_expiration_date,
                        vehicle_id, is_active, created_at
                    )
                    SELECT 
                        @uId, @EmpCode, @FName, @LName, @Phone, @Email, @IdCard,
                        @BirthDate::date, @License, '2020-01-01'::date, '2028-12-31'::date,
                        v.id, TRUE, CURRENT_TIMESTAMP
                    FROM (SELECT 1) dummy
                    LEFT JOIN vehicles v ON v.plate_number = @VehPlate;";

                await db.ExecuteAsync(insertProfileSql, new {
                    uId,
                    u.EmpCode,
                    u.FName,
                    u.LName,
                    u.Phone,
                    u.Email,
                    u.IdCard,
                    u.BirthDate,
                    u.License,
                    u.VehPlate
                });
            }
        }

        // Seed default system menus if table is empty
        var menuCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM menus WHERE deleted_at IS NULL;");
        if (menuCount == 0)
        {
            var seedMenusSql = @"
                INSERT INTO menus (name_th, endpoint, menu_type, seq, is_read, is_create, is_update, is_delete, is_import, is_export, created_at)
                VALUES 
                ('หน้าหลัก (Home)', '/home', 1, 0, TRUE, TRUE, TRUE, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP),
                ('ภาพรวมระบบ', '/dashboard', 1, 1, TRUE, TRUE, TRUE, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP),
                ('จัดการงานขนส่ง', '/jobs', 1, 2, TRUE, TRUE, TRUE, TRUE, FALSE, TRUE, CURRENT_TIMESTAMP),
                ('จัดการผู้ใช้งาน', '/users', 1, 3, TRUE, TRUE, TRUE, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP),
                ('จัดการยานพาหนะ', '/vehicles', 1, 4, TRUE, TRUE, TRUE, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP),
                ('ประเภทรถ', '/vehicle-types', 1, 5, TRUE, TRUE, TRUE, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP),
                ('จัดการเมนูระบบ', '/menu-managements', 1, 6, TRUE, TRUE, TRUE, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP),
                ('กำหนดสิทธิ์เมนู', '/menu-managements/permissions', 1, 7, TRUE, TRUE, TRUE, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP),
                ('ประวัติการใช้งาน', '/audit-logs', 1, 8, TRUE, FALSE, FALSE, FALSE, FALSE, TRUE, CURRENT_TIMESTAMP);
            ";
            await db.ExecuteAsync(seedMenusSql);
        }

        // Seed default full permissions for Admin users
        var seedAdminMenuPermsSql = @"
            INSERT INTO user_menu_permissions (user_id, menu_id, is_read, is_create, is_update, is_delete, is_import, is_export, created_at)
            SELECT u.id, m.id, m.is_read, m.is_create, m.is_update, m.is_delete, m.is_import, m.is_export, CURRENT_TIMESTAMP
            FROM users u
            CROSS JOIN menus m
            WHERE u.role ILIKE 'Admin' AND m.deleted_at IS NULL
            ON CONFLICT (user_id, menu_id) DO NOTHING;";
        await db.ExecuteAsync(seedAdminMenuPermsSql);

        // Seed 10 Jobs
        var seedJobsSql = @"
            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            SELECT 'JOB-20260801', 'ขนส่งอะไหล่รถยนต์ บางนา - อมตะซิตี้ ชลบุรี', 'ส่งสินค้าด่วนโรงงานประกอบรถยนต์ นิคมอมตะซิตี้', 'ศูนย์กระจายสินค้าบางนา กม.18 ต.บางโฉลง อ.บางพลี สมุทรปราการ', 13.6089, 100.7423, 'คุณสมยศ อมตะ', '0891112233', 'นายสมพร (ผู้ช่วย)', 'Completed', NULL, '2026-08-01 08:30:00+07', p.user_id, v.id, '2026-08-01 07:00:00+07'
            FROM user_profiles p JOIN vehicles v ON v.plate_number = '1กข-1234' WHERE p.employee_code = 'DRV-001'
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            SELECT 'JOB-20260802', 'จัดส่งอุปกรณ์อิเล็กทรอนิกส์ ลาดกระบัง - อยุธยา', 'ขนส่งชิปและแผงวงจรอิเล็กทรอนิกส์ ควบคุมความชื้น', 'นิคมอุตสาหกรรมลาดกระบัง แขวงลำปลาทิว เขตลาดกระบัง กรุงเทพฯ', 13.7533, 100.7932, 'คุณวิภาวรรณ', '0892223344', NULL, 'Completed', NULL, '2026-08-02 09:00:00+07', p.user_id, v.id, '2026-08-02 07:30:00+07'
            FROM user_profiles p JOIN vehicles v ON v.plate_number = '2ฒฉ-5678' WHERE p.employee_code = 'DRV-002'
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            SELECT 'JOB-20260803', 'ขนส่งอาหารแช่แข็ง ตลาดไท - มหาชัย สมุทรสาคร', 'สินค้าอาหารทะเลแช่แข็ง ควบคุมอุณหภูมิต่ำกว่า -18 องศาเซลเซียส', 'ตลาดไท ต.คลองหนึ่ง อ.คลองหลวง ปทุมธานี', 14.0792, 100.6175, 'คุณธนพล มหาชัย', '0893334455', 'นายมานพ', 'Completed', NULL, '2026-08-03 10:00:00+07', p.user_id, v.id, '2026-08-03 08:00:00+07'
            FROM user_profiles p JOIN vehicles v ON v.plate_number = '70-8912' WHERE p.employee_code = 'DRV-004'
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            SELECT 'JOB-20260804', 'ลำเลียงวัสดุก่อสร้าง ท่าเรือคลองเตย - พระราม 9', 'ขนส่งเหล็กเส้นและเสาเข็มสำหรับโครงการก่อสร้างอาคารสูง', 'ท่าเรือกรุงเทพ (คลองเตย) แขวงคลองเตย เขตคลองเตย กรุงเทพฯ', 13.7061, 100.5752, 'วิศวกรประจำไซต์งาน', '0894445566', 'นายสุบิน', 'Completed', NULL, '2026-08-04 11:00:00+07', p.user_id, v.id, '2026-08-04 09:00:00+07'
            FROM user_profiles p JOIN vehicles v ON v.plate_number = '72-7890' WHERE p.employee_code = 'DRV-007'
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            SELECT 'JOB-20260805', 'ขนส่งเครื่องจักรกลหนัก แหลมฉบัง - มาบตาพุด', 'ขนส่งเครื่องกำเนิดไฟฟ้าโรงงานอุตสาหกรรมเคมี', 'ท่าเรือแหลมฉบัง ต.ทุ่งสุขลา อ.ศรีราชา ชลบุรี', 13.0827, 100.8833, 'คุณเกียรติศักดิ์', '0895556677', 'นายชัยยุทธ', 'Started', NULL, '2026-08-20 08:00:00+07', p.user_id, v.id, '2026-08-20 07:00:00+07'
            FROM user_profiles p JOIN vehicles v ON v.plate_number = '73-4455' WHERE p.employee_code = 'DRV-009'
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            SELECT 'JOB-20260806', 'กระจายสินค้าอุปโภคบริโภค วังน้อย - นครราชสีมา', 'ขนส่งสินค้าอุปโภคบริโภคส่งห้างสรรพสินค้าภาคอีสาน', 'ศูนย์กระจายสินค้าวังน้อย ต.ลำไทร อ.วังน้อย พระนครศรีอยุธยา', 14.2335, 100.7142, 'ผู้จัดการคลังสินค้าโคราช', '0896667788', NULL, 'Arrived', NULL, '2026-08-20 09:30:00+07', p.user_id, v.id, '2026-08-20 07:30:00+07'
            FROM user_profiles p JOIN vehicles v ON v.plate_number = '72-3456' WHERE p.employee_code = 'DRV-006'
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            SELECT 'JOB-20260807', 'จัดส่งเวชภัณฑ์และยารักษาโรค หลักสี่ - นครปฐม', 'เวชภัณฑ์ควบคุมอุณหภูมิ 2-8 องศาเซลเซียส ส่งโรงพยาบาลศูนย์', 'ศูนย์เวชภัณฑ์หลักสี่ แขวงทุ่งสองห้อง เขตหลักสี่ กรุงเทพฯ', 13.8875, 100.5826, 'เภสัชกรประจำห้องยา', '0897778899', NULL, 'Assigned', NULL, '2026-08-20 13:00:00+07', p.user_id, v.id, '2026-08-20 08:30:00+07'
            FROM user_profiles p JOIN vehicles v ON v.plate_number = '1ขค-9012' WHERE p.employee_code = 'DRV-010'
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            SELECT 'JOB-20260808', 'ขนถ่ายตู้คอนเทนเนอร์ 40 ฟุต ICD ลาดกระบัง - ท่าเรือแหลมฉบัง', 'ส่งตู้สินค้าส่งออกอาหารแปรรูป ขึ้นเรือสินค้าลำที่ 4', 'สถานีบรรจุและแยกสินค้ากล่อง ลาดกระบัง (ICD ลาดกระบัง) กรุงเทพฯ', 13.7291, 100.7512, 'เจ้าหน้าที่ฝ่ายลอจิสติกส์', '0898889900', NULL, 'Assigned', NULL, '2026-08-20 14:00:00+07', p.user_id, v.id, '2026-08-20 09:00:00+07'
            FROM user_profiles p JOIN vehicles v ON v.plate_number = '73-1122' WHERE p.employee_code = 'DRV-008'
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            VALUES (
                'JOB-20260809', 'ขนส่งสินค้าเคมีภัณฑ์อุตสาหกรรม บางปู - ระยอง', 'สินค้าเคมีภัณฑ์บรรจุถัง IBC 1,000 ลิตร มีเอกสาร MSDS แนบ', 'นิคมอุตสาหกรรมบางปู ต.แพรกษา อ.เมือง สมุทรปราการ', 13.5358, 100.6425,
                'คุณประสิทธิ์ เคมีคอล', '0899990011', NULL, 'Pending', NULL,
                '2026-08-21 09:00:00+07', NULL, NULL, CURRENT_TIMESTAMP
            )
            ON CONFLICT (job_number) DO NOTHING;

            INSERT INTO jobs (
                job_number, title, description, pickup_location, pickup_lat, pickup_lng,
                contact_name, contact_phone, companions, status, cancellation_reason,
                scheduled_start_at, driver_id, vehicle_id, created_at
            )
            VALUES (
                'JOB-20260810', 'ขนส่งสินค้าเครื่องดื่ม บางเลน - นนทบุรี', 'ขนส่งน้ำดื่มบรรจุขวด 1,500 ลัง', 'โรงงานน้ำดื่มบางเลน ต.บางเลน อ.บางเลน นครปฐม', 14.0182, 100.1704,
                'คุณสุพจน์', '0890001122', NULL, 'Cancelled', 'ลูกค้ายกเลิกคำสั่งซื้อเนื่องจากเลื่อนกำหนดการรับสินค้าและจัดสรรพื้นที่คลังใหม่',
                '2026-08-19 10:00:00+07', NULL, NULL, '2026-08-19 08:00:00+07'
            )
            ON CONFLICT (job_number) DO NOTHING;
        ";
        await db.ExecuteAsync(seedJobsSql);

        // Seed 10 Audit Logs
        var seedAuditLogsSql = @"
            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'CREATE', 'jobs', 'JOB-20260801', 'สร้างงานขนส่งใหม่: ขนส่งอะไหล่รถยนต์ บางนา - อมตะซิตี้ ชลบุรี', '127.0.0.1', '2026-08-01 07:00:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'UPDATE', 'jobs', 'JOB-20260801', 'มอบหมายงานให้พนักงาน: สมชาย สุขเกษม (DRV-001) รถ 1กข-1234', '127.0.0.1', '2026-08-01 07:15:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'CREATE', 'vehicles', '70-5678', 'เพิ่มข้อมูลยานพาหนะใหม่: Hino 500 Victor 260HP ทะเบียน 70-5678', '127.0.0.1', '2026-08-02 08:00:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'CREATE', 'users', 'somchai01', 'เพิ่มผู้ใช้งานและพนักงานขับรถใหม่: สมชาย สุขเกษม (DRV-001)', '127.0.0.1', '2026-08-02 08:30:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'CREATE', 'vehicle_types', 'รถควบคุมอุณหภูมิ Cold Chain', 'เพิ่มประเภทรถใหม่: รถควบคุมอุณหภูมิ Cold Chain (Cold Storage Van)', '127.0.0.1', '2026-08-03 09:00:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'UPDATE', 'jobs', 'JOB-20260805', 'พนักงานเริ่มเดินทางปฏิบัติงาน (Started)', '127.0.0.1', '2026-08-20 07:00:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'UPDATE', 'jobs', 'JOB-20260806', 'พนักงานเดินทางถึงจุดหมายปลายทาง (Arrived)', '127.0.0.1', '2026-08-20 08:45:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'CREATE', 'jobs', 'JOB-20260809', 'สร้างงานขนส่งใหม่: ขนส่งสินค้าเคมีภัณฑ์อุตสาหกรรม บางปู - ระยอง', '127.0.0.1', '2026-08-20 09:15:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'UPDATE', 'jobs', 'JOB-20260810', 'ยกเลิกงานขนส่ง: ลูกค้ายกเลิกคำสั่งซื้อเนื่องจากเลื่อนกำหนดการรับสินค้า', '127.0.0.1', '2026-08-19 11:30:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;

            INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
            SELECT u.id, 'UPDATE', 'users', 'somkiat02', 'แก้ไขข้อมูลพนักงานและต่ออายุใบอนุญาตขับขี่', '127.0.0.1', '2026-08-20 10:00:00+07' FROM users u WHERE u.username = 'admin' LIMIT 1;
        ";
        var currentAuditCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM audit_logs;");
        if (currentAuditCount < 10)
        {
            await db.ExecuteAsync(seedAuditLogsSql);
        }
    }
}
