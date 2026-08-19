namespace Core.DTOs;

public class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RefreshTokenRequestDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public record AuthResponseDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    long UserId,
    string Username,
    string TokenType = "Bearer"
);

public record LicenseInfoDto(
    string LicenseNo,
    DateTime IssueDate,
    DateTime ExpirationDate,
    string Status
);

public record DriverProfileDto(
    long Id,
    string EmployeeCode,
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    LicenseInfoDto License
);

public record MobileMeResponseDto(
    long UserId,
    string Role,
    DriverProfileDto? Driver
);

public record JobDto(
    long Id,
    string JobNumber,
    string Title,
    string? Description,
    long? DriverId,
    string? DriverName,
    long? VehicleId,
    string? VehiclePlate,
    string Status,
    string PickupLocation,
    string? DropoffLocation,
    DateTime? ScheduledStartAt,
    DateTime? StartedAt,
    DateTime? ArrivedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string? CancellationReason
);

public record CreateJobDto(
    string Title,
    string? Description,
    long? DriverId,
    long? VehicleId,
    string PickupLocation,
    double? PickupLat,
    double? PickupLng,
    string? ContactName,
    string? ContactPhone,
    string? Companions,
    string? DropoffLocation,
    DateTime? ScheduledStartAt
);

public record UpdateJobDto(
    string Title,
    string? Description,
    long? DriverId,
    long? VehicleId,
    string? Status,
    string? CancellationReason,
    string PickupLocation,
    double? PickupLat,
    double? PickupLng,
    string? ContactName,
    string? ContactPhone,
    string? Companions,
    string? DropoffLocation,
    DateTime? ScheduledStartAt
);

public record AssignJobDto(long DriverId, long? VehicleId);
public record CancelJobDto(string Reason);

public record UserDto(
    long Id,
    string Username,
    string Role,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt
);

public record CreateUserDto(
    string Username,
    string Password,
    string Role,
    DriverDetailDto? DriverDetail
);

public record UpdateUserDto(
    string Role,
    bool? IsActive,
    string? Password,
    DriverDetailDto? DriverDetail
);

public record DriverDetailDto(
    string EmployeeCode,
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    string? IdCardNo,
    DateTime? BirthDate,
    string? LicenseNo,
    DateTime? LicenseIssueDate,
    DateTime? LicenseExpirationDate,
    long? VehicleId
);
public record VehicleDto(
    long Id,
    string PlateNumber,
    string Model,
    string? VehicleType,
    double Capacity,
    bool IsActive,
    DateTime CreatedAt
);

public record CreateVehicleDto(
    string PlateNumber,
    string Model,
    string? VehicleType,
    double Capacity
);

public record UpdateVehicleDto(
    string PlateNumber,
    string Model,
    string? VehicleType,
    double Capacity,
    bool IsActive
);

public record CreateVehicleTypeDto(
    string Name,
    string? Description
);

public record UpdateVehicleTypeDto(
    string Name,
    string? Description
);
