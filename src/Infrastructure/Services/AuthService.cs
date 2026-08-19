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
    private readonly IConfiguration _config;

    public AuthService(UserRepository userRepo, UserProfileRepository profileRepo, IConfiguration config)
    {
        _userRepo = userRepo;
        _profileRepo = profileRepo;
        _config = config;
    }

    public async Task<(AuthResponseDto? Dto, string? ErrorCode, string Message)> AuthenticateAsync(string username, string password, CancellationToken ct = default)
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

        if (user.Role != "Admin")
        {
            return (null, "FORBIDDEN_ROLE", "คุณไม่มีสิทธิ์เข้าใช้งานระบบนี้");
        }

        var token = GenerateJwtToken(user);
        var refreshToken = Guid.NewGuid().ToString("N");

        var dto = new AuthResponseDto(
            AccessToken: token,
            RefreshToken: refreshToken,
            ExpiresIn: int.Parse(_config["Jwt:AccessTokenExpirationMinutes"] ?? "30") * 60,
            UserId: user.Id,
            Username: user.Username
        );

        return (dto, null, "เข้าสู่ระบบสำเร็จ");
    }

    public async Task<MobileMeResponseDto?> GetProfileAsync(long userId, string role, CancellationToken ct = default)
    {
        DriverProfileDto? driverDto = null;
        if (role == "Driver")
        {
            var driver = await _profileRepo.GetByUserIdAsync(userId, ct);
            if (driver != null)
            {
                var licenseStatus = driver.LicenseExpirationDate.HasValue && driver.LicenseExpirationDate < DateTime.UtcNow.Date ? "Expired" : "Valid";
                driverDto = new DriverProfileDto(
                    driver.Id,
                    driver.EmployeeCode,
                    driver.FirstName,
                    driver.LastName,
                    driver.Phone,
                    driver.Email,
                    new LicenseInfoDto(driver.LicenseNo ?? "-", driver.LicenseIssueDate ?? DateTime.UtcNow, driver.LicenseExpirationDate ?? DateTime.UtcNow, licenseStatus)
                );
            }
        }

        return new MobileMeResponseDto(userId, role, driverDto);
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
