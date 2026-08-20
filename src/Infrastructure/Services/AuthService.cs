using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Core.DTOs;
using Core.Entities;
using Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

public class AuthService
{
    private readonly UserRepository _userRepo;
    private readonly UserProfileRepository _profileRepo;
    private readonly DbConnectionFactory _db;
    private readonly IConfiguration _config;

    public AuthService(UserRepository userRepo, UserProfileRepository profileRepo, DbConnectionFactory db, IConfiguration config)
    {
        _userRepo = userRepo;
        _profileRepo = profileRepo;
        _db = db;
        _config = config;
    }

    public async Task<(AuthResponseDto? Dto, string? ErrorCode, string Message)> AuthenticateAsync(string username, string password, int channel = 1, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByUsernameAsync(username, ct);
        if (user == null || !user.IsActive)
        {
            return (null, "INVALID_CREDENTIALS", "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง");
        }

        var isPassValid = PasswordHasher.VerifyPassword(password, user.PasswordHash);
        if (!isPassValid)
        {
            return (null, "INVALID_CREDENTIALS", "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง");
        }

        // Channel 1: Admin Dashboard (Web) -> Admin Only
        if (channel == 1)
        {
            if (user.Role != "Admin")
            {
                return (null, "FORBIDDEN_ROLE", "เฉพาะผู้ดูแลระบบ (Admin) เท่านั้นที่สามารถเข้าสู่ระบบนี้ได้");
            }
        }
        // Channel 2: Mobile -> Admin & Driver
        else if (channel == 2)
        {
            if (user.Role != "Admin" && user.Role != "Driver")
            {
                return (null, "FORBIDDEN_ROLE", "คุณไม่มีสิทธิ์เข้าใช้งานระบบ Mobile");
            }
        }
        else
        {
            return (null, "INVALID_CHANNEL", "ช่องทางการเข้าสู่ระบบไม่ถูกต้อง (Channel 1: Admin Dashboard, Channel 2: Mobile)");
        }

        var token = GenerateJwtToken(user);
        var refreshToken = Guid.NewGuid().ToString("N");
        var tokenHash = PasswordHasher.HashPassword(refreshToken);
        var refreshExpDays = double.Parse(_config["Jwt:RefreshTokenExpirationDays"] ?? "30");

        using (var conn = _db.CreateConnection())
        {
            await Dapper.SqlMapper.ExecuteAsync(conn, @"
                UPDATE users
                SET last_login_at = CURRENT_TIMESTAMP
                WHERE id = @UserId;",
                new { UserId = user.Id });

            await Dapper.SqlMapper.ExecuteAsync(conn, @"
                INSERT INTO refresh_tokens (user_id, token_hash, expires_at, created_at)
                VALUES (@UserId, @TokenHash, @ExpiresAt, CURRENT_TIMESTAMP);",
                new { UserId = user.Id, TokenHash = tokenHash, ExpiresAt = DateTime.UtcNow.AddDays(refreshExpDays) });
        }

        var dto = new AuthResponseDto(
            AccessToken: token,
            RefreshToken: refreshToken,
            ExpiresIn: int.Parse(_config["Jwt:AccessTokenExpirationMinutes"] ?? "30") * 60,
            UserId: user.Id,
            Username: user.Username
        );

        return (dto, null, "เข้าสู่ระบบสำเร็จ");
    }

    public async Task<(AuthResponseDto? Dto, string? ErrorCode, string Message)> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return (null, "INVALID_REFRESH_TOKEN", "กรุณาระบุ Refresh token");
        }

        using var conn = _db.CreateConnection();
        var tokenHash = PasswordHasher.HashPassword(refreshToken);
        var record = await Dapper.SqlMapper.QueryFirstOrDefaultAsync(conn, @"
            SELECT r.id, r.user_id AS UserId, r.expires_at AS ExpiresAt, r.revoked_at AS RevokedAt, u.username, u.role, u.is_active AS IsActive
            FROM refresh_tokens r
            JOIN users u ON u.id = r.user_id
            WHERE r.token_hash = @tokenHash AND r.revoked_at IS NULL AND u.deleted_at IS NULL;",
            new { tokenHash });

        if (record == null || !(bool)record.isactive || (DateTime)record.expiresat < DateTime.UtcNow)
        {
            return (null, "INVALID_REFRESH_TOKEN", "Refresh token ไม่ถูกต้องหรือหมดอายุแล้ว");
        }

        var user = new User
        {
            Id = (long)record.userid,
            Username = (string)record.username,
            Role = (string)record.role,
            IsActive = (bool)record.isactive
        };

        var newJwt = GenerateJwtToken(user);
        var newRefreshToken = Guid.NewGuid().ToString("N");
        var newHash = PasswordHasher.HashPassword(newRefreshToken);
        var refreshExpDays = double.Parse(_config["Jwt:RefreshTokenExpirationDays"] ?? "30");

        var newRecordId = await Dapper.SqlMapper.ExecuteScalarAsync<long>(conn, @"
            INSERT INTO refresh_tokens (user_id, token_hash, expires_at, created_at)
            VALUES (@UserId, @TokenHash, @ExpiresAt, CURRENT_TIMESTAMP)
            RETURNING id;",
            new { UserId = user.Id, TokenHash = newHash, ExpiresAt = DateTime.UtcNow.AddDays(refreshExpDays) });

        await Dapper.SqlMapper.ExecuteAsync(conn, @"
            UPDATE refresh_tokens
            SET revoked_at = CURRENT_TIMESTAMP, replaced_by_token_id = @newRecordId
            WHERE id = @OldId;",
            new { newRecordId, OldId = (long)record.id });

        var dto = new AuthResponseDto(
            AccessToken: newJwt,
            RefreshToken: newRefreshToken,
            ExpiresIn: int.Parse(_config["Jwt:AccessTokenExpirationMinutes"] ?? "30") * 60,
            UserId: user.Id,
            Username: user.Username
        );

        return (dto, null, "Refresh token สำเร็จ");
    }

    public async Task<MobileUserResponseDto?> GetMobileUserProfileAsync(long userId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT u.id AS UserId, u.username AS Username, u.role AS Role, u.is_active AS IsActive, u.created_at AS CreatedAt,
                   p.id AS ProfileId, p.employee_code AS EmployeeCode, p.first_name AS FirstName, p.last_name AS LastName,
                   p.phone AS Phone, p.email AS Email, p.id_card_no AS IdCardNo, p.birth_date AS BirthDate,
                   p.license_no AS LicenseNo, p.license_issue_date AS LicenseIssueDate, p.license_expiration_date AS LicenseExpirationDate,
                   v.id AS VehicleId, v.plate_number AS PlateNumber, v.model AS VehicleModel, vt.name AS VehicleType, v.capacity AS VehicleCapacity
            FROM users u
            LEFT JOIN user_profiles p ON p.user_id = u.id AND p.deleted_at IS NULL
            LEFT JOIN vehicles v ON v.id = p.vehicle_id AND v.deleted_at IS NULL
            LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
            WHERE u.id = @userId AND u.deleted_at IS NULL;";

        var row = await Dapper.SqlMapper.QueryFirstOrDefaultAsync(conn, sql, new { userId });
        if (row == null) return null;

        DriverProfileDto? driverDto = null;
        if (row.profileid != null)
        {
            LicenseInfoDto? licenseDto = null;
            if (!string.IsNullOrEmpty((string?)row.licenseno))
            {
                DateTime? issueDate = ToNullableDateTime(row.licenseissuedate);
                DateTime? expDate = ToNullableDateTime(row.licenseexpirationdate);
                var licenseStatus = expDate.HasValue && expDate.Value < DateTime.UtcNow.Date ? "Expired" : "Valid";
                licenseDto = new LicenseInfoDto(
                    (string)row.licenseno,
                    issueDate,
                    expDate,
                    licenseStatus
                );
            }

            VehicleInfoDto? vehicleDto = null;
            if (row.vehicleid != null)
            {
                vehicleDto = new VehicleInfoDto(
                    (long)row.vehicleid,
                    (string)(row.platenumber ?? ""),
                    (string)(row.vehiclemodel ?? ""),
                    (string?)row.vehicletype,
                    ToDouble(row.vehiclecapacity)
                );
            }

            var firstName = (string?)row.firstname ?? "";
            var lastName = (string?)row.lastname ?? "";
            var fullName = $"{firstName} {lastName}".Trim();

            driverDto = new DriverProfileDto(
                Id: (long)row.profileid,
                EmployeeCode: (string?)row.employeecode ?? "",
                FirstName: firstName,
                LastName: lastName,
                FullName: string.IsNullOrEmpty(fullName) ? (string)row.username : fullName,
                Phone: (string?)row.phone ?? "",
                Email: (string?)row.email,
                IdCardNo: (string?)row.idcardno,
                BirthDate: ToNullableDateTime(row.birthdate),
                License: licenseDto,
                AssignedVehicle: vehicleDto
            );
        }

        DateTime createdAt = ToNullableDateTime(row.createdat) ?? DateTime.UtcNow;

        return new MobileUserResponseDto(
            UserId: (long)row.userid,
            Username: (string)row.username,
            Role: (string)row.role,
            IsActive: (bool)row.isactive,
            Driver: driverDto,
            CreatedAt: createdAt
        );
    }

    private static DateTime? ToNullableDateTime(object? value)
    {
        if (value == null) return null;
        if (value is DateTime dt) return dt;
        if (value is DateOnly d) return d.ToDateTime(TimeOnly.MinValue);
        if (DateTime.TryParse(value.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static double ToDouble(object? value)
    {
        if (value == null) return 0.0;
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is decimal m) return (double)m;
        if (value is int i) return i;
        if (value is long l) return l;
        if (double.TryParse(value.ToString(), out var parsed)) return parsed;
        return 0.0;
    }

    public async Task<MobileMeResponseDto?> GetProfileAsync(long userId, string role, CancellationToken ct = default)
    {
        var mobileUser = await GetMobileUserProfileAsync(userId, ct);
        if (mobileUser == null) return null;
        return new MobileMeResponseDto(
            UserId: mobileUser.UserId,
            Username: mobileUser.Username,
            Role: mobileUser.Role,
            IsActive: mobileUser.IsActive,
            Driver: mobileUser.Driver,
            CreatedAt: mobileUser.CreatedAt
        );
    }

    public async Task<(ChangePasswordVerifyResponseDto? Dto, string? ErrorCode, string Message)> VerifyForgotPasswordAsync(string username, string idCardNo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return (null, "USERNAME_REQUIRED", "กรุณาระบุชื่อผู้ใช้ (Username)");
        }

        if (string.IsNullOrWhiteSpace(idCardNo))
        {
            return (null, "IDCARD_REQUIRED", "กรุณาระบุเลขบัตรประจำตัวประชาชน");
        }

        var cleanIdCard = idCardNo.Replace("-", "").Trim();

        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT u.id, u.username, u.password_hash AS PasswordHash, u.role, u.is_active AS IsActive
            FROM users u
            JOIN user_profiles p ON p.user_id = u.id AND p.deleted_at IS NULL
            WHERE LOWER(u.username) = LOWER(@username) 
              AND (p.id_card_no = @idCardNo OR REPLACE(COALESCE(p.id_card_no, ''), '-', '') = @cleanIdCard)
              AND u.deleted_at IS NULL;";

        var user = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<User>(conn, new Dapper.CommandDefinition(sql, new { username = username.Trim(), idCardNo = idCardNo.Trim(), cleanIdCard }, cancellationToken: ct));

        if (user == null || !user.IsActive)
        {
            return (null, "INVALID_CREDENTIALS", "ชื่อผู้ใช้หรือเลขบัตรประจำตัวประชาชนไม่ถูกต้อง");
        }

        var token = GeneratePasswordChangeToken(user, "password_reset");
        return (new ChangePasswordVerifyResponseDto(token, 600), null, "ยืนยันตัวตนถูกต้อง สามารถตั้งรหัสผ่านใหม่ได้");
    }

    public async Task<(ChangePasswordVerifyResponseDto? Dto, string? ErrorCode, string Message)> VerifyPasswordForChangeAsync(long userId, string password, CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            return (null, "UNAUTHORIZED", "ไม่พบข้อมูลผู้ใช้งานที่เข้าสู่ระบบ");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return (null, "PASSWORD_REQUIRED", "กรุณาระบุรหัสผ่านปัจจุบัน");
        }

        using var conn = _db.CreateConnection();
        var user = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<User>(conn, new Dapper.CommandDefinition(@"
            SELECT id, username, password_hash AS PasswordHash, role, is_active AS IsActive
            FROM users
            WHERE id = @userId AND deleted_at IS NULL;", new { userId }, cancellationToken: ct));

        if (user == null || !user.IsActive)
        {
            return (null, "USER_NOT_FOUND", "ไม่พบบัญชีผู้ใช้ หรือผู้ใช้นี้ถูกระงับการใช้งาน");
        }

        var isPassValid = PasswordHasher.VerifyPassword(password, user.PasswordHash);
        if (!isPassValid)
        {
            return (null, "INVALID_PASSWORD", "รหัสผ่านปัจจุบันไม่ถูกต้อง");
        }

        var token = GeneratePasswordChangeToken(user, "password_change");
        return (new ChangePasswordVerifyResponseDto(token, 600), null, "ยืนยันรหัสผ่านถูกต้อง สามารถตั้งรหัสผ่านใหม่ได้");
    }

    public async Task<(bool Success, string? ErrorCode, string Message)> SetNewPasswordAsync(string token, string password, string confirmPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "TOKEN_REQUIRED", "กรุณาระบุ Verification Token");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, "PASSWORD_REQUIRED", "กรุณาระบุรหัสผ่านใหม่");
        }

        if (password.Length < 6)
        {
            return (false, "INVALID_PASSWORD_LENGTH", "รหัสผ่านใหม่ต้องมีความยาวอย่างน้อย 6 ตัวอักษร");
        }

        if (password != confirmPassword)
        {
            return (false, "PASSWORD_MISMATCH", "รหัสผ่านและยืนยันรหัสผ่านไม่ตรงกัน");
        }

        var (isValid, userId) = ValidatePasswordChangeToken(token);
        if (!isValid || userId <= 0)
        {
            return (false, "INVALID_OR_EXPIRED_TOKEN", "Verification Token ไม่ถูกต้องหรือหมดอายุแล้ว กรุณายืนยันรหัสผ่านใหม่อีกครั้ง");
        }

        using var conn = _db.CreateConnection();
        var user = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<User>(conn, new Dapper.CommandDefinition(@"
            SELECT id, username, password_hash AS PasswordHash, role, is_active AS IsActive
            FROM users
            WHERE id = @userId AND deleted_at IS NULL;", new { userId }, cancellationToken: ct));

        if (user == null || !user.IsActive)
        {
            return (false, "USER_NOT_FOUND", "ไม่พบบัญชีผู้ใช้ หรือผู้ใช้นี้ถูกระงับการใช้งาน");
        }

        var newHash = PasswordHasher.HashPassword(password);

        await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(@"
            UPDATE users
            SET password_hash = @newHash, updated_at = CURRENT_TIMESTAMP
            WHERE id = @userId;", new { newHash, userId }, cancellationToken: ct));

        // Revoke all existing refresh tokens
        await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(@"
            UPDATE refresh_tokens
            SET revoked_at = CURRENT_TIMESTAMP
            WHERE user_id = @userId AND revoked_at IS NULL;", new { userId }, cancellationToken: ct));

        // Insert audit log
        try
        {
            await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(@"
                INSERT INTO audit_logs (user_id, action, entity_name, entity_id, details, created_at)
                VALUES (@userId, 'CHANGE_PASSWORD', 'users', CAST(@userId AS TEXT), 'User changed password via verification token', CURRENT_TIMESTAMP);",
                new { userId }, cancellationToken: ct));
        }
        catch
        {
            // Ignore audit log failure
        }

        return (true, null, "เปลี่ยนรหัสผ่านสำเร็จเรียบร้อยแล้ว กรุณาเข้าสู่ระบบใหม่อีกครั้ง");
    }

    private string GeneratePasswordChangeToken(User user, string purpose = "password_change")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "super_secret_key_that_is_long_enough_123456"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("purpose", purpose),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "ForeseeAPI",
            audience: _config["Jwt:Audience"] ?? "ForeseeClients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private (bool IsValid, long UserId) ValidatePasswordChangeToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "super_secret_key_that_is_long_enough_123456"));

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _config["Jwt:Issuer"] ?? "ForeseeAPI",
                ValidateAudience = true,
                ValidAudience = _config["Jwt:Audience"] ?? "ForeseeClients",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out var validatedToken);

            var purposeClaim = principal.FindFirst("purpose")?.Value;
            if (purposeClaim != "password_change" && purposeClaim != "password_reset") return (false, 0);

            var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value 
                        ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (long.TryParse(subClaim, out var userId))
            {
                return (true, userId);
            }

            return (false, 0);
        }
        catch
        {
            return (false, 0);
        }
    }

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "super_secret_key_that_is_long_enough_123456"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var expiresMinutes = double.Parse(_config["Jwt:AccessTokenExpirationMinutes"] ?? "30");
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "ForeseeAPI",
            audience: _config["Jwt:Audience"] ?? "ForeseeClients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
