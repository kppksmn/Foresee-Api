using System.Data;
using System.Security.Cryptography;
using System.Text;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Infrastructure.Repositories;

public class DbConnectionFactory
{
    private readonly string _connectionString;
    public DbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL") 
            ?? throw new InvalidOperationException("PostgreSQL connection string is not configured.");
    }
    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}

public class PasswordHasher
{
    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }
}

public class UserRepository
{
    private readonly DbConnectionFactory _db;
    public UserRepository(DbConnectionFactory db) => _db = db;

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = "SELECT id, username, password_hash AS PasswordHash, role, is_active AS IsActive, last_login_at AS LastLoginAt FROM users WHERE LOWER(username) = LOWER(@username) AND deleted_at IS NULL;";
        return await conn.QueryFirstOrDefaultAsync<User>(new CommandDefinition(sql, new { username = (username ?? "").Trim() }, cancellationToken: ct));
    }

    public async Task<User?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = "SELECT * FROM users WHERE id = @id AND deleted_at IS NULL;";
        return await conn.QueryFirstOrDefaultAsync<User>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<long> CreateUserAsync(User user, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        user.Username = (user.Username ?? "").Trim().ToLowerInvariant();
        const string sql = @"
            INSERT INTO users (username, password_hash, role, is_active, created_by, created_at)
            VALUES (@Username, @PasswordHash, @Role, @IsActive, @CreatedBy, @CreatedAt)
            RETURNING id;";
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, user, cancellationToken: ct));
    }
}

public class UserProfileRepository
{
    private readonly DbConnectionFactory _db;
    public UserProfileRepository(DbConnectionFactory db) => _db = db;

    public async Task<UserProfile?> GetByUserIdAsync(long userId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = "SELECT * FROM user_profiles WHERE user_id = @userId AND is_active = TRUE AND deleted_at IS NULL;";
        return await conn.QueryFirstOrDefaultAsync<UserProfile>(new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }

    public async Task<UserProfile?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = "SELECT * FROM user_profiles WHERE id = @id AND is_active = TRUE AND deleted_at IS NULL;";
        return await conn.QueryFirstOrDefaultAsync<UserProfile>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<long> CreateUserProfileAsync(UserProfile profile, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = @"
            INSERT INTO user_profiles (user_id, employee_code, first_name, last_name, phone, email, id_card_no, birth_date, license_no, license_issue_date, license_expiration_date, vehicle_id, is_active, created_by, created_at)
            VALUES (@UserId, @EmployeeCode, @FirstName, @LastName, @Phone, @Email, @IdCardNo, @BirthDate, @LicenseNo, @LicenseIssueDate, @LicenseExpirationDate, @VehicleId, @IsActive, @CreatedBy, @CreatedAt)
            RETURNING id;";
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, profile, cancellationToken: ct));
    }
}

public class JobRepository
{
    private readonly DbConnectionFactory _db;
    public JobRepository(DbConnectionFactory db) => _db = db;

    public async Task<Job?> GetByIdAndDriverAsync(long jobId, long userId, long driverProfileId = 0, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = @"
            SELECT * FROM jobs 
            WHERE id = @jobId 
              AND (driver_id = @userId OR companion_id = @userId) 
              AND deleted_at IS NULL;";
        return await conn.QueryFirstOrDefaultAsync<Job>(new CommandDefinition(sql, new { jobId, userId }, cancellationToken: ct));
    }

    public async Task<Job?> GetByIdAsync(long jobId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = "SELECT * FROM jobs WHERE id = @jobId AND deleted_at IS NULL;";
        return await conn.QueryFirstOrDefaultAsync<Job>(new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
    }

    public async Task<JobDto?> GetJobDetailForDriverAsync(long jobId, long userId, long driverProfileId = 0, bool isAdmin = false, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT j.id, j.job_number as JobNumber, j.title, j.description, j.driver_id as DriverId,
                   COALESCE(NULLIF(TRIM(COALESCE(p.first_name, '') || ' ' || COALESCE(p.last_name, '')), ''), u.username, 'พนักงาน #' || CAST(j.driver_id AS TEXT)) as DriverName,
                   j.vehicle_id as VehicleId,
                   v.plate_number as VehiclePlate,
                   vt.name as VehicleType,
                   j.status, j.pickup_location as PickupLocation,
                   j.pickup_lat as PickupLat, j.pickup_lng as PickupLng,
                   j.contact_name as ContactName, j.contact_phone as ContactPhone,
                   j.companions as Companions,
                   j.companion_id as CompanionId,
                   CASE 
                       WHEN j.companion_id IS NULL THEN j.companions
                       ELSE COALESCE(
                           NULLIF(TRIM(COALESCE(cp_p.first_name, '') || ' ' || COALESCE(cp_p.last_name, '')), ''),
                           cp_u.username,
                           j.companions
                       )
                   END as CompanionName,
                   j.scheduled_start_at as ScheduledStartAt,
                   j.started_at as StartedAt, j.arrived_at as ArrivedAt, j.completed_at as CompletedAt,
                   j.cancelled_at as CancelledAt, j.cancellation_reason as CancellationReason,
                   j.cancelled_by as CancelledBy,
                   COALESCE(
                       NULLIF(TRIM(COALESCE(cb_p.first_name, '') || ' ' || COALESCE(cb_p.last_name, '')), ''),
                       cb_u.username,
                       (
                           SELECT COALESCE(NULLIF(TRIM(COALESCE(h_p.first_name, '') || ' ' || COALESCE(h_p.last_name, '')), ''), h_u.username)
                           FROM job_status_histories h
                           JOIN users h_u ON h_u.id = h.changed_by
                           LEFT JOIN user_profiles h_p ON h_p.user_id = h_u.id AND h_p.deleted_at IS NULL
                           WHERE h.job_id = j.id AND h.to_status = 'Cancelled'
                           ORDER BY h.created_at DESC
                           LIMIT 1
                       )
                   ) as CancelledByName
            FROM jobs j
            LEFT JOIN user_profiles p ON p.user_id = j.driver_id
            LEFT JOIN users u ON u.id = j.driver_id
            LEFT JOIN user_profiles cp_p ON cp_p.user_id = j.companion_id AND cp_p.deleted_at IS NULL
            LEFT JOIN users cp_u ON cp_u.id = j.companion_id AND cp_u.deleted_at IS NULL
            LEFT JOIN vehicles v ON j.vehicle_id = v.id
            LEFT JOIN vehicle_types vt ON v.vehicle_type_id = vt.id
            LEFT JOIN users cb_u ON cb_u.id = j.cancelled_by AND cb_u.deleted_at IS NULL
            LEFT JOIN user_profiles cb_p ON cb_p.user_id = cb_u.id AND cb_p.deleted_at IS NULL
            WHERE j.id = @jobId 
              AND (@isAdmin = TRUE OR j.driver_id = @userId OR j.companion_id = @userId)
              AND j.deleted_at IS NULL;";

        return await conn.QueryFirstOrDefaultAsync<JobDto>(new CommandDefinition(sql, new { jobId, userId, isAdmin }, cancellationToken: ct));
    }

    public async Task<IEnumerable<JobDto>> GetUnfinishedJobsForDriverAsync(long userId, long driverProfileId = 0, bool isAdmin = false, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT j.id, j.job_number as JobNumber, j.title, j.description, j.driver_id as DriverId,
                   COALESCE(NULLIF(TRIM(COALESCE(p.first_name, '') || ' ' || COALESCE(p.last_name, '')), ''), u.username, 'พนักงาน #' || CAST(j.driver_id AS TEXT)) as DriverName,
                   j.vehicle_id as VehicleId,
                   v.plate_number as VehiclePlate,
                   vt.name as VehicleType,
                   j.status, j.pickup_location as PickupLocation,
                   j.pickup_lat as PickupLat, j.pickup_lng as PickupLng,
                   j.contact_name as ContactName, j.contact_phone as ContactPhone,
                   j.companions as Companions,
                   j.companion_id as CompanionId,
                   CASE 
                       WHEN j.companion_id IS NULL THEN j.companions
                       ELSE COALESCE(
                           NULLIF(TRIM(COALESCE(cp_p.first_name, '') || ' ' || COALESCE(cp_p.last_name, '')), ''),
                           cp_u.username,
                           j.companions
                       )
                   END as CompanionName,
                   j.scheduled_start_at as ScheduledStartAt,
                   j.started_at as StartedAt, j.arrived_at as ArrivedAt, j.completed_at as CompletedAt,
                   j.cancelled_at as CancelledAt, j.cancellation_reason as CancellationReason
            FROM jobs j
            LEFT JOIN user_profiles p ON p.user_id = j.driver_id
            LEFT JOIN users u ON u.id = j.driver_id
            LEFT JOIN user_profiles cp_p ON cp_p.user_id = j.companion_id AND cp_p.deleted_at IS NULL
            LEFT JOIN users cp_u ON cp_u.id = j.companion_id AND cp_u.deleted_at IS NULL
            LEFT JOIN vehicles v ON j.vehicle_id = v.id
            LEFT JOIN vehicle_types vt ON v.vehicle_type_id = vt.id
            WHERE (@isAdmin = TRUE OR j.driver_id = @userId OR j.companion_id = @userId)
              AND j.status NOT IN ('Completed', 'Cancelled')
              AND j.deleted_at IS NULL
            ORDER BY j.scheduled_start_at ASC NULLS LAST, j.created_at DESC;";

        return await conn.QueryAsync<JobDto>(new CommandDefinition(sql, new { userId, isAdmin }, cancellationToken: ct));
    }

    public async Task<(IEnumerable<JobDto> Items, int TotalCount)> GetUnfinishedJobsForDriverPaginatedAsync(
        long userId, 
        long driverProfileId = 0, 
        bool isAdmin = false, 
        string? status = null, 
        string? search = null, 
        int sqlOffset = 0, 
        int pageSize = 25, 
        CancellationToken ct = default)
    {
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;
        if (sqlOffset < 0) sqlOffset = 0;

        using var conn = _db.CreateConnection();
        var countSql = @"
            SELECT COUNT(1) 
            FROM jobs j
            LEFT JOIN user_profiles p ON p.user_id = j.driver_id
            LEFT JOIN users u ON u.id = j.driver_id
            LEFT JOIN vehicles v ON j.vehicle_id = v.id
            WHERE (@isAdmin = TRUE OR j.driver_id = @userId OR j.companion_id = @userId)
              AND j.status NOT IN ('Completed', 'Cancelled')
              AND j.deleted_at IS NULL
              AND (@status IS NULL OR @status = '' OR @status = 'All' OR j.status = @status)
              AND (@search IS NULL OR @search = '' OR j.job_number ILIKE '%' || @search || '%' OR j.title ILIKE '%' || @search || '%' OR j.pickup_location ILIKE '%' || @search || '%' OR COALESCE(p.first_name || ' ' || p.last_name, u.username, '') ILIKE '%' || @search || '%');";

        var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, new { userId, isAdmin, status, search }, cancellationToken: ct));

        var sql = @"
            SELECT j.id, j.job_number as JobNumber, j.title, j.description, j.driver_id as DriverId,
                   COALESCE(NULLIF(TRIM(COALESCE(p.first_name, '') || ' ' || COALESCE(p.last_name, '')), ''), u.username, 'พนักงาน #' || CAST(j.driver_id AS TEXT)) as DriverName,
                   j.vehicle_id as VehicleId,
                   v.plate_number as VehiclePlate,
                   vt.name as VehicleType,
                   j.status, j.pickup_location as PickupLocation,
                   j.pickup_lat as PickupLat, j.pickup_lng as PickupLng,
                   j.contact_name as ContactName, j.contact_phone as ContactPhone,
                   j.companions as Companions,
                   j.companion_id as CompanionId,
                   CASE 
                       WHEN j.companion_id IS NULL THEN j.companions
                       ELSE COALESCE(
                           NULLIF(TRIM(COALESCE(cp_p.first_name, '') || ' ' || COALESCE(cp_p.last_name, '')), ''),
                           cp_u.username,
                           j.companions
                       )
                   END as CompanionName,
                   j.scheduled_start_at as ScheduledStartAt,
                   j.started_at as StartedAt, j.arrived_at as ArrivedAt, j.completed_at as CompletedAt,
                   j.cancelled_at as CancelledAt, j.cancellation_reason as CancellationReason
            FROM jobs j
            LEFT JOIN user_profiles p ON p.user_id = j.driver_id
            LEFT JOIN users u ON u.id = j.driver_id
            LEFT JOIN user_profiles cp_p ON cp_p.user_id = j.companion_id AND cp_p.deleted_at IS NULL
            LEFT JOIN users cp_u ON cp_u.id = j.companion_id AND cp_u.deleted_at IS NULL
            LEFT JOIN vehicles v ON j.vehicle_id = v.id
            LEFT JOIN vehicle_types vt ON v.vehicle_type_id = vt.id
            WHERE (@isAdmin = TRUE OR j.driver_id = @userId OR j.companion_id = @userId)
              AND j.status NOT IN ('Completed', 'Cancelled')
              AND j.deleted_at IS NULL
              AND (@status IS NULL OR @status = '' OR @status = 'All' OR j.status = @status)
              AND (@search IS NULL OR @search = '' OR j.job_number ILIKE '%' || @search || '%' OR j.title ILIKE '%' || @search || '%' OR j.pickup_location ILIKE '%' || @search || '%' OR COALESCE(p.first_name || ' ' || p.last_name, u.username, '') ILIKE '%' || @search || '%')
            ORDER BY j.scheduled_start_at ASC NULLS LAST, j.created_at DESC
            LIMIT @pageSize OFFSET @sqlOffset;";

        var list = await conn.QueryAsync<JobDto>(new CommandDefinition(sql, new { userId, isAdmin, status, search, pageSize, sqlOffset }, cancellationToken: ct));
        return (list, totalCount);
    }

    public async Task<(IEnumerable<JobDto> Items, int TotalCount)> GetJobHistoryForDriverAsync(
        long userId, 
        long driverProfileId = 0, 
        string? statusFilter = null,
        int sqlOffset = 0, 
        int pageSize = 25, 
        bool isAdmin = false,
        CancellationToken ct = default)
    {
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;
        if (sqlOffset < 0) sqlOffset = 0;

        using var conn = _db.CreateConnection();
        var countSql = @"
            SELECT COUNT(1) 
            FROM jobs j
            WHERE (@isAdmin = TRUE OR j.driver_id = @userId OR j.companion_id = @userId)
              AND j.deleted_at IS NULL
              AND (
                CASE 
                  WHEN @statusFilter = 'Completed' THEN j.status = 'Completed'
                  WHEN @statusFilter = 'Cancelled' THEN j.status = 'Cancelled'
                  ELSE j.status IN ('Completed', 'Cancelled')
                END
              );";

        var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, new { userId, statusFilter, isAdmin }, cancellationToken: ct));

        var sql = @"
            SELECT j.id, j.job_number as JobNumber, j.title, j.description, j.driver_id as DriverId,
                   COALESCE(NULLIF(TRIM(COALESCE(p.first_name, '') || ' ' || COALESCE(p.last_name, '')), ''), u.username, 'พนักงาน #' || CAST(j.driver_id AS TEXT)) as DriverName,
                   j.vehicle_id as VehicleId,
                   v.plate_number as VehiclePlate,
                   vt.name as VehicleType,
                   j.status, j.pickup_location as PickupLocation,
                   j.pickup_lat as PickupLat, j.pickup_lng as PickupLng,
                   j.contact_name as ContactName, j.contact_phone as ContactPhone,
                   j.companions as Companions,
                   j.companion_id as CompanionId,
                   CASE 
                       WHEN j.companion_id IS NULL THEN j.companions
                       ELSE COALESCE(
                           NULLIF(TRIM(COALESCE(cp_p.first_name, '') || ' ' || COALESCE(cp_p.last_name, '')), ''),
                           cp_u.username,
                           j.companions
                       )
                   END as CompanionName,
                   j.scheduled_start_at as ScheduledStartAt,
                   j.started_at as StartedAt, j.arrived_at as ArrivedAt, j.completed_at as CompletedAt,
                   j.cancelled_at as CancelledAt, j.cancellation_reason as CancellationReason,
                   j.cancelled_by as CancelledBy,
                   COALESCE(
                       NULLIF(TRIM(COALESCE(cb_p.first_name, '') || ' ' || COALESCE(cb_p.last_name, '')), ''),
                       cb_u.username,
                       (
                           SELECT COALESCE(NULLIF(TRIM(COALESCE(h_p.first_name, '') || ' ' || COALESCE(h_p.last_name, '')), ''), h_u.username)
                           FROM job_status_histories h
                           JOIN users h_u ON h_u.id = h.changed_by
                           LEFT JOIN user_profiles h_p ON h_p.user_id = h_u.id AND h_p.deleted_at IS NULL
                           WHERE h.job_id = j.id AND h.to_status = 'Cancelled'
                           ORDER BY h.created_at DESC
                           LIMIT 1
                       )
                   ) as CancelledByName
            FROM jobs j
            LEFT JOIN user_profiles p ON p.user_id = j.driver_id
            LEFT JOIN users u ON u.id = j.driver_id
            LEFT JOIN user_profiles cp_p ON cp_p.user_id = j.companion_id AND cp_p.deleted_at IS NULL
            LEFT JOIN users cp_u ON cp_u.id = j.companion_id AND cp_u.deleted_at IS NULL
            LEFT JOIN vehicles v ON j.vehicle_id = v.id
            LEFT JOIN vehicle_types vt ON v.vehicle_type_id = vt.id
            LEFT JOIN users cb_u ON cb_u.id = j.cancelled_by AND cb_u.deleted_at IS NULL
            LEFT JOIN user_profiles cb_p ON cb_p.user_id = cb_u.id AND cb_p.deleted_at IS NULL
            WHERE (@isAdmin = TRUE OR j.driver_id = @userId OR j.companion_id = @userId)
              AND j.deleted_at IS NULL
              AND (
                CASE 
                  WHEN @statusFilter = 'Completed' THEN j.status = 'Completed'
                  WHEN @statusFilter = 'Cancelled' THEN j.status = 'Cancelled'
                  ELSE j.status IN ('Completed', 'Cancelled')
                END
              )
            ORDER BY COALESCE(j.updated_at, j.created_at) DESC
            LIMIT @pageSize OFFSET @sqlOffset;";

        var list = await conn.QueryAsync<JobDto>(new CommandDefinition(sql, new { userId, statusFilter, pageSize, sqlOffset, isAdmin }, cancellationToken: ct));
        return (list, totalCount);
    }

    public async Task<IEnumerable<JobDto>> GetJobsForDriverAsync(long driverId, string? status, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT j.id, j.job_number as JobNumber, j.title, j.description, j.driver_id as DriverId,
                   COALESCE(NULLIF(TRIM(COALESCE(p.first_name, '') || ' ' || COALESCE(p.last_name, '')), ''), u.username, 'พนักงาน #' || CAST(j.driver_id AS TEXT)) as DriverName,
                   j.vehicle_id as VehicleId,
                   v.plate_number as VehiclePlate, j.status, j.pickup_location as PickupLocation,
                   j.pickup_lat as PickupLat, j.pickup_lng as PickupLng,
                   j.contact_name as ContactName, j.contact_phone as ContactPhone,
                   j.companions as Companions,
                   j.companion_id as CompanionId,
                   CASE 
                       WHEN j.companion_id IS NULL THEN j.companions
                       ELSE COALESCE(
                           NULLIF(TRIM(COALESCE(cp_p.first_name, '') || ' ' || COALESCE(cp_p.last_name, '')), ''),
                           cp_u.username,
                           j.companions
                       )
                   END as CompanionName,
                   j.scheduled_start_at as ScheduledStartAt,
                   j.started_at as StartedAt, j.arrived_at as ArrivedAt, j.completed_at as CompletedAt,
                   j.cancelled_at as CancelledAt, j.cancellation_reason as CancellationReason
            FROM jobs j
            LEFT JOIN user_profiles p ON p.user_id = j.driver_id
            LEFT JOIN users u ON u.id = j.driver_id
            LEFT JOIN user_profiles cp_p ON cp_p.user_id = j.companion_id AND cp_p.deleted_at IS NULL
            LEFT JOIN users cp_u ON cp_u.id = j.companion_id AND cp_u.deleted_at IS NULL
            LEFT JOIN vehicles v ON j.vehicle_id = v.id
            WHERE (j.driver_id = @driverId OR j.companion_id = @driverId) AND j.deleted_at IS NULL";

        if (!string.IsNullOrEmpty(status))
        {
            sql += " AND j.status = @status";
        }
        sql += " ORDER BY j.created_at DESC;";

        return await conn.QueryAsync<JobDto>(new CommandDefinition(sql, new { driverId, status }, cancellationToken: ct));
    }

    public async Task<bool> UpdateStatusAtomicAsync(long jobId, string expectedStatus, string newStatus, long userId, DateTime actionTime, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        string updateColumn = newStatus switch
        {
            "Started" => ", started_at = @actionTime",
            "Arrived" => ", arrived_at = @actionTime",
            "Completed" => ", completed_at = @actionTime",
            _ => ""
        };

        string sql = $@"
            UPDATE jobs 
            SET status = @newStatus, row_version = row_version + 1, updated_at = @actionTime, updated_by = @userId {updateColumn}
            WHERE id = @jobId AND status = @expectedStatus AND deleted_at IS NULL;";

        int affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { jobId, expectedStatus, newStatus, userId, actionTime }, transaction: tx, cancellationToken: ct));
        if (affected == 0)
        {
            tx.Rollback();
            return false;
        }

        const string historySql = @"
            INSERT INTO job_status_histories (job_id, from_status, to_status, changed_by, created_at)
            VALUES (@jobId, @expectedStatus, @newStatus, @userId, @actionTime);";

        await conn.ExecuteAsync(new CommandDefinition(historySql, new { jobId, expectedStatus, newStatus, userId, actionTime }, transaction: tx, cancellationToken: ct));

        tx.Commit();
        return true;
    }
}

public class AuditLogRepository
{
    private readonly DbConnectionFactory _db;
    public AuditLogRepository(DbConnectionFactory db) => _db = db;

    public async Task LogAsync(long? userId, string action, string entityName, string? entityId = null, string? details = null, string? ipAddress = null, CancellationToken ct = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            const string sql = @"
                INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, ip_address, created_at)
                VALUES (@userId, @action, @entityName, @entityId, @details, @ipAddress, CURRENT_TIMESTAMP);";
            await conn.ExecuteAsync(new CommandDefinition(sql, new { userId, action, entityName, entityId, details, ipAddress }, cancellationToken: ct));
        }
        catch
        {
            // Do not fail main operation if audit logging fails
        }
    }
}

public class MenuManagementRepository
{
    private readonly DbConnectionFactory _db;
    public MenuManagementRepository(DbConnectionFactory db) => _db = db;

    public async Task<List<MenuManagementMenuResponse>> GetMenusAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = @"
            SELECT id, name_th AS NameTh, endpoint, menu_type AS MenuType, 
                   external_url AS ExternalUrl, target_path AS TargetPath, open_mode AS OpenMode, 
                   authentication_mode AS AuthenticationMode, parent_id AS ParentId, seq, 
                   is_public AS IsPublic, is_marketing AS IsMarketing, is_read AS IsRead, 
                   is_create AS IsCreate, is_update AS IsUpdate, is_delete AS IsDelete, 
                   is_import AS IsImport, is_export AS IsExport, created_by AS CreatedBy, 
                   created_at AS CreatedAt, updated_by AS UpdatedBy, updated_at AS UpdatedAt
            FROM public.menus
            WHERE deleted_at IS NULL
            ORDER BY seq ASC, id ASC;";
        var list = await conn.QueryAsync<Menu>(new CommandDefinition(sql, cancellationToken: ct));
        return list.Select(MenuManagementMenuResponse.From).ToList();
    }

    public async Task<List<MenuManagementMenuTreeResponse>> GetMenuTreeAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = @"
            SELECT id, name_th AS NameTh, endpoint, menu_type AS MenuType, 
                   external_url AS ExternalUrl, target_path AS TargetPath, open_mode AS OpenMode, 
                   authentication_mode AS AuthenticationMode, parent_id AS ParentId, seq, 
                   is_public AS IsPublic, is_marketing AS IsMarketing, is_read AS IsRead, 
                   is_create AS IsCreate, is_update AS IsUpdate, is_delete AS IsDelete, 
                   is_import AS IsImport, is_export AS IsExport, created_by AS CreatedBy, 
                   created_at AS CreatedAt, updated_by AS UpdatedBy, updated_at AS UpdatedAt
            FROM public.menus
            WHERE deleted_at IS NULL
            ORDER BY seq ASC, id ASC;";
        var list = (await conn.QueryAsync<Menu>(new CommandDefinition(sql, cancellationToken: ct))).ToList();
        return BuildMenuTree(list);
    }

    public async Task<Menu?> GetMenuByIdAsync(int id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = @"
            SELECT id, name_th AS NameTh, endpoint, menu_type AS MenuType, 
                   external_url AS ExternalUrl, target_path AS TargetPath, open_mode AS OpenMode, 
                   authentication_mode AS AuthenticationMode, parent_id AS ParentId, seq, 
                   is_public AS IsPublic, is_marketing AS IsMarketing, is_read AS IsRead, 
                   is_create AS IsCreate, is_update AS IsUpdate, is_delete AS IsDelete, 
                   is_import AS IsImport, is_export AS IsExport, created_by AS CreatedBy, 
                   created_at AS CreatedAt, updated_by AS UpdatedBy, updated_at AS UpdatedAt
            FROM public.menus
            WHERE deleted_at IS NULL AND id = @id;";
        return await conn.QueryFirstOrDefaultAsync<Menu>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<bool> ExistsEndpointAsync(string endpoint, int? exceptId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        using var conn = _db.CreateConnection();
        const string sql = @"
            SELECT COUNT(1)
            FROM public.menus
            WHERE deleted_at IS NULL AND endpoint = @endpoint AND (@exceptId IS NULL OR id <> @exceptId);";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { endpoint = endpoint.Trim(), exceptId }, cancellationToken: ct)) > 0;
    }

    public async Task<int> CreateMenuAsync(MenuManagementUpsertMenuRequest req, string createdBy, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        long? createdByUserId = long.TryParse(createdBy, out var cBy) ? cBy : null;
        const string sql = @"
            INSERT INTO public.menus
            (
                name_th, endpoint, menu_type, external_url, target_path,
                open_mode, authentication_mode, parent_id, seq, is_public, is_marketing,
                is_read, is_create, is_update, is_delete, is_import, is_export,
                created_by, created_at, updated_by, updated_at
            )
            VALUES
            (
                @NameTh, @Endpoint, @MenuType, @ExternalUrl, @TargetPath,
                @OpenMode, @AuthenticationMode, @ParentId, @Seq, @IsPublic, @IsMarketing,
                @IsRead, @IsCreate, @IsUpdate, @IsDelete, @IsImport, @IsExport,
                @createdByUserId, CURRENT_TIMESTAMP, @createdByUserId, CURRENT_TIMESTAMP
            )
            RETURNING id;";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            req.NameTh,
            Endpoint = string.IsNullOrWhiteSpace(req.Endpoint) ? null : req.Endpoint.Trim(),
            MenuType = (int)req.MenuType,
            ExternalUrl = string.IsNullOrWhiteSpace(req.ExternalUrl) ? null : req.ExternalUrl.Trim(),
            TargetPath = string.IsNullOrWhiteSpace(req.TargetPath) ? null : req.TargetPath.Trim(),
            OpenMode = (int)req.OpenMode,
            AuthenticationMode = (int)req.AuthenticationMode,
            req.ParentId,
            req.Seq,
            req.IsPublic,
            req.IsMarketing,
            req.IsRead,
            req.IsCreate,
            req.IsUpdate,
            req.IsDelete,
            req.IsImport,
            req.IsExport,
            createdByUserId
        }, cancellationToken: ct));
    }

    public async Task<int> UpdateMenuAsync(int id, MenuManagementUpsertMenuRequest req, string updatedBy, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        long? updatedByUserId = long.TryParse(updatedBy, out var uBy) ? uBy : null;
        const string sql = @"
            UPDATE public.menus SET
                name_th = @NameTh,
                endpoint = @Endpoint,
                menu_type = @MenuType,
                external_url = @ExternalUrl,
                target_path = @TargetPath,
                open_mode = @OpenMode,
                authentication_mode = @AuthenticationMode,
                parent_id = @ParentId,
                seq = @Seq,
                is_public = @IsPublic,
                is_marketing = @IsMarketing,
                is_read = @IsRead,
                is_create = @IsCreate,
                is_update = @IsUpdate,
                is_delete = @IsDelete,
                is_import = @IsImport,
                is_export = @IsExport,
                updated_by = @updatedByUserId,
                updated_at = CURRENT_TIMESTAMP
            WHERE deleted_at IS NULL AND id = @id;";
        return await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            id,
            req.NameTh,
            Endpoint = string.IsNullOrWhiteSpace(req.Endpoint) ? null : req.Endpoint.Trim(),
            MenuType = (int)req.MenuType,
            ExternalUrl = string.IsNullOrWhiteSpace(req.ExternalUrl) ? null : req.ExternalUrl.Trim(),
            TargetPath = string.IsNullOrWhiteSpace(req.TargetPath) ? null : req.TargetPath.Trim(),
            OpenMode = (int)req.OpenMode,
            AuthenticationMode = (int)req.AuthenticationMode,
            req.ParentId,
            req.Seq,
            req.IsPublic,
            req.IsMarketing,
            req.IsRead,
            req.IsCreate,
            req.IsUpdate,
            req.IsDelete,
            req.IsImport,
            req.IsExport,
            updatedByUserId
        }, cancellationToken: ct));
    }

    public async Task<int> DeleteMenuAsync(int id, string deletedBy, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        long? deletedByUserId = long.TryParse(deletedBy, out var dBy) ? dBy : null;
        const string sql = @"
            WITH RECURSIVE menu_tree AS (
                SELECT id FROM public.menus WHERE id = @id AND deleted_at IS NULL
                UNION ALL
                SELECT m.id FROM public.menus m
                JOIN menu_tree mt ON m.parent_id = mt.id
                WHERE m.deleted_at IS NULL
            )
            UPDATE public.menus
            SET deleted_by = @deletedByUserId, deleted_at = CURRENT_TIMESTAMP, updated_at = CURRENT_TIMESTAMP
            WHERE id IN (SELECT id FROM menu_tree);";
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { id, deletedByUserId }, cancellationToken: ct));
    }

    private static List<MenuManagementMenuTreeResponse> BuildMenuTree(List<Menu> menus)
    {
        var lookup = menus.ToDictionary(m => m.Id);
        var childrenByParent = new Dictionary<int, List<Menu>>();

        foreach (var m in menus)
        {
            var pId = m.ParentId ?? 0;
            if (!childrenByParent.ContainsKey(pId))
            {
                childrenByParent[pId] = [];
            }
            childrenByParent[pId].Add(m);
        }

        List<MenuManagementMenuTreeResponse> GetChildren(int parentId)
        {
            if (!childrenByParent.TryGetValue(parentId, out var children)) return [];
            return children
                .OrderBy(c => c.Seq)
                .ThenBy(c => c.Id)
                .Select(c => MenuManagementMenuTreeResponse.From(c, GetChildren(c.Id)))
                .ToList();
        }

        return GetChildren(0);
    }

    public async Task<List<UserMenuPermissionNodeDto>> GetUserMenuPermissionsTreeAsync(long userId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var targetUserRole = await conn.ExecuteScalarAsync<string>(
            "SELECT role FROM users WHERE id = @userId AND deleted_at IS NULL;",
            new { userId });
        var isTargetAdmin = string.Equals(targetUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

        const string sql = @"
            SELECT 
                m.id AS menuid,
                m.parent_id AS parentid,
                m.name_th AS nameth,
                m.endpoint,
                m.seq,
                m.is_public AS ispublic,
                m.is_marketing AS ismarketing,
                m.is_read AS canread,
                m.is_create AS cancreate,
                m.is_update AS canupdate,
                m.is_delete AS candelete,
                m.is_import AS canimport,
                m.is_export AS canexport,
                COALESCE(ump.is_read, FALSE) AS isread,
                COALESCE(ump.is_create, FALSE) AS iscreate,
                COALESCE(ump.is_update, FALSE) AS isupdate,
                COALESCE(ump.is_delete, FALSE) AS isdelete,
                COALESCE(ump.is_import, FALSE) AS isimport,
                COALESCE(ump.is_export, FALSE) AS isexport
            FROM menus m
            LEFT JOIN user_menu_permissions ump ON ump.menu_id = m.id AND ump.user_id = @userId
            WHERE m.deleted_at IS NULL
            ORDER BY m.seq ASC, m.id ASC;";

        var rows = (await conn.QueryAsync(new CommandDefinition(sql, new { userId }, cancellationToken: ct))).ToList();

        var nodes = rows.Select(r =>
        {
            var ep = (string?)r.endpoint;
            var isLockedAdminMenu = isTargetAdmin && (ep == "/menu-managements" || ep == "/menu-managements/permissions");

            return new UserMenuPermissionNodeDto
            {
                MenuId = (long)r.menuid,
                ParentId = r.parentid != null ? (long)r.parentid : null,
                NameTh = (string)r.nameth,
                Endpoint = ep,
                Seq = (int)r.seq,
                IsPublic = (bool)r.ispublic,
                IsMarketing = (bool)r.ismarketing,
                CanRead = (bool)r.canread,
                CanCreate = (bool)r.cancreate,
                CanUpdate = (bool)r.canupdate,
                CanDelete = (bool)r.candelete,
                CanImport = (bool)r.canimport,
                CanExport = (bool)r.canexport,
                IsRead = isLockedAdminMenu || (bool)r.isread,
                IsCreate = isLockedAdminMenu || (bool)r.iscreate,
                IsUpdate = isLockedAdminMenu || (bool)r.isupdate,
                IsDelete = isLockedAdminMenu || (bool)r.isdelete,
                IsImport = isLockedAdminMenu || (bool)r.isimport,
                IsExport = isLockedAdminMenu || (bool)r.isexport,
                Children = []
            };
        }).ToList();

        var childrenByParent = nodes
            .GroupBy(n => n.ParentId ?? 0)
            .ToDictionary(g => g.Key, g => g.ToList());

        List<UserMenuPermissionNodeDto> BuildTree(long parentId)
        {
            if (!childrenByParent.TryGetValue(parentId, out var children)) return [];
            foreach (var child in children)
            {
                child.Children = BuildTree(child.MenuId);
            }
            return children.OrderBy(c => c.Seq).ThenBy(c => c.MenuId).ToList();
        }

        return BuildTree(0);
    }

    public async Task UpdateUserMenuPermissionsAsync(long userId, List<UserMenuPermissionItemRequest> permissions, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        if (conn is System.Data.Common.DbConnection dbConn && dbConn.State != System.Data.ConnectionState.Open)
        {
            await dbConn.OpenAsync(ct);
        }

        var targetUserRole = await conn.ExecuteScalarAsync<string>(
            "SELECT role FROM users WHERE id = @userId AND deleted_at IS NULL;",
            new { userId });
        var isTargetAdmin = string.Equals(targetUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

        using var transaction = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(@"
                DELETE FROM user_menu_permissions WHERE user_id = @userId;",
                new { userId }, transaction: transaction, cancellationToken: ct));

            const string insertSql = @"
                INSERT INTO user_menu_permissions 
                    (user_id, menu_id, is_read, is_create, is_update, is_delete, is_import, is_export, created_at)
                VALUES 
                    (@UserId, @MenuId, @IsRead, @IsCreate, @IsUpdate, @IsDelete, @IsImport, @IsExport, CURRENT_TIMESTAMP)
                ON CONFLICT (user_id, menu_id) DO UPDATE SET
                    is_read = EXCLUDED.is_read,
                    is_create = EXCLUDED.is_create,
                    is_update = EXCLUDED.is_update,
                    is_delete = EXCLUDED.is_delete,
                    is_import = EXCLUDED.is_import,
                    is_export = EXCLUDED.is_export,
                    updated_at = CURRENT_TIMESTAMP;";

            var lockedMenuIds = new HashSet<long>();
            if (isTargetAdmin)
            {
                var adminMenuIds = await conn.QueryAsync<long>(@"
                    SELECT id FROM menus 
                    WHERE deleted_at IS NULL AND endpoint IN ('/menu-managements', '/menu-managements/permissions');",
                    transaction: transaction);
                foreach (var id in adminMenuIds) lockedMenuIds.Add(id);
            }

            var processedMenuIds = new HashSet<long>();

            foreach (var perm in permissions)
            {
                var isLocked = lockedMenuIds.Contains(perm.MenuId);
                var isRead = isLocked || perm.IsRead;
                var isCreate = isLocked || perm.IsCreate;
                var isUpdate = isLocked || perm.IsUpdate;
                var isDelete = isLocked || perm.IsDelete;
                var isImport = isLocked || perm.IsImport;
                var isExport = isLocked || perm.IsExport;

                processedMenuIds.Add(perm.MenuId);

                // Only insert if at least one permission is true
                if (isRead || isCreate || isUpdate || isDelete || isImport || isExport)
                {
                    await conn.ExecuteAsync(new CommandDefinition(insertSql, new
                    {
                        UserId = userId,
                        perm.MenuId,
                        IsRead = isRead,
                        IsCreate = isCreate,
                        IsUpdate = isUpdate,
                        IsDelete = isDelete,
                        IsImport = isImport,
                        IsExport = isExport
                    }, transaction: transaction, cancellationToken: ct));
                }
            }

            // Ensure any locked admin menus not sent in payload are still inserted
            foreach (var lockedId in lockedMenuIds)
            {
                if (!processedMenuIds.Contains(lockedId))
                {
                    await conn.ExecuteAsync(new CommandDefinition(insertSql, new
                    {
                        UserId = userId,
                        MenuId = lockedId,
                        IsRead = true,
                        IsCreate = true,
                        IsUpdate = true,
                        IsDelete = true,
                        IsImport = true,
                        IsExport = true
                    }, transaction: transaction, cancellationToken: ct));
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<UserNavMenuDto>> GetNavigableMenusForUserAsync(long userId, string role, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        bool isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

        const string sql = @"
            SELECT 
                m.id,
                m.parent_id AS parentid,
                m.name_th AS nameth,
                m.endpoint,
                m.menu_type AS menutype,
                m.external_url AS externalurl,
                m.target_path AS targetpath,
                m.open_mode AS openmode,
                m.seq,
                m.is_public AS ispublic,
                COALESCE(ump.is_read, FALSE) AS isread,
                COALESCE(ump.is_create, FALSE) AS iscreate,
                COALESCE(ump.is_update, FALSE) AS isupdate,
                COALESCE(ump.is_delete, FALSE) AS isdelete,
                COALESCE(ump.is_import, FALSE) AS isimport,
                COALESCE(ump.is_export, FALSE) AS isexport
            FROM menus m
            LEFT JOIN user_menu_permissions ump ON ump.menu_id = m.id AND ump.user_id = @userId
            WHERE m.deleted_at IS NULL
            ORDER BY m.seq ASC, m.id ASC;";

        var rows = (await conn.QueryAsync(new CommandDefinition(sql, new { userId }, cancellationToken: ct))).ToList();

        var allItems = rows.Select(r =>
        {
            var ep = (string?)r.endpoint;
            var isLockedAdminMenu = isAdmin && (ep == "/menu-managements" || ep == "/menu-managements/permissions");

            return new
            {
                Id = (long)r.id,
                ParentId = r.parentid != null ? (long)r.parentid : (long?)null,
                NameTh = (string)r.nameth,
                Endpoint = ep,
                MenuType = (int)r.menutype,
                ExternalUrl = (string?)r.externalurl,
                TargetPath = (string?)r.targetpath,
                OpenMode = (int)r.openmode,
                Seq = (int)r.seq,
                IsPublic = (bool)r.ispublic,
                IsRead = isLockedAdminMenu || (bool)r.isread || (bool)r.ispublic,
                IsCreate = isLockedAdminMenu || (bool)r.iscreate,
                IsUpdate = isLockedAdminMenu || (bool)r.isupdate,
                IsDelete = isLockedAdminMenu || (bool)r.isdelete,
                IsImport = isLockedAdminMenu || (bool)r.isimport,
                IsExport = isLockedAdminMenu || (bool)r.isexport,
            };
        }).ToList();

        // Collect IDs of all menus where IsRead is true, PLUS their parent ancestors
        var accessibleIds = new HashSet<long>();
        var itemMap = allItems.ToDictionary(i => i.Id);
        foreach (var item in allItems.Where(i => i.IsRead))
        {
            var current = item;
            while (current != null)
            {
                accessibleIds.Add(current.Id);
                if (current.ParentId != null && itemMap.TryGetValue(current.ParentId.Value, out var parent))
                {
                    current = parent;
                }
                else
                {
                    break;
                }
            }
        }

        var visibleNodes = allItems
            .Where(i => accessibleIds.Contains(i.Id))
            .Select(i => new UserNavMenuDto
            {
                Id = i.Id,
                ParentId = i.ParentId,
                NameTh = i.NameTh,
                Endpoint = i.Endpoint,
                MenuType = i.MenuType,
                ExternalUrl = i.ExternalUrl,
                TargetPath = i.TargetPath,
                OpenMode = i.OpenMode,
                Seq = i.Seq,
                IsRead = i.IsRead,
                IsCreate = i.IsCreate,
                IsUpdate = i.IsUpdate,
                IsDelete = i.IsDelete,
                IsImport = i.IsImport,
                IsExport = i.IsExport,
                Children = []
            }).ToList();

        var childrenByParent = visibleNodes
            .GroupBy(n => n.ParentId ?? 0)
            .ToDictionary(g => g.Key, g => g.ToList());

        List<UserNavMenuDto> BuildNavTree(long parentId)
        {
            if (!childrenByParent.TryGetValue(parentId, out var children)) return [];
            foreach (var child in children)
            {
                child.Children = BuildNavTree(child.Id);
            }
            return children.OrderBy(c => c.Seq).ThenBy(c => c.Id).ToList();
        }

        return BuildNavTree(0);
    }

    public async Task<bool> HasMenuPermissionAsync(long userId, string role, string endpoint, string action = "read", CancellationToken ct = default)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var cleanEp = endpoint.Trim().ToLowerInvariant();
        var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

        // Core admin menus always granted to Admin role
        if (isAdmin && (cleanEp == "/menu-managements" || cleanEp == "/menu-managements/permissions"))
        {
            return true;
        }

        using var conn = _db.CreateConnection();
        var actionCol = action.ToLowerInvariant() switch
        {
            "create" => "ump.is_create",
            "update" => "ump.is_update",
            "delete" => "ump.is_delete",
            "import" => "ump.is_import",
            "export" => "ump.is_export",
            _ => "ump.is_read"
        };

        var sql = $@"
            SELECT COUNT(1)
            FROM menus m
            LEFT JOIN user_menu_permissions ump ON ump.menu_id = m.id AND ump.user_id = @userId
            WHERE m.deleted_at IS NULL 
              AND LOWER(m.endpoint) = @cleanEp
              AND (m.is_public = TRUE OR {actionCol} = TRUE);";

        var count = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { userId, cleanEp }, cancellationToken: ct));
        return count > 0;
    }
}
