namespace Core.DTOs;

public class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Channel { get; set; } = 1; // 1 = Admin Dashboard (Admin Only), 2 = Mobile (Admin & Driver)
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
    DateTime? IssueDate,
    DateTime? ExpirationDate,
    string Status
);

public record VehicleInfoDto(
    long Id,
    string PlateNumber,
    string Model,
    string? VehicleType,
    double Capacity
);

public record DriverProfileDto(
    long Id,
    string EmployeeCode,
    string FirstName,
    string LastName,
    string FullName,
    string Phone,
    string? Email,
    string? IdCardNo,
    DateTime? BirthDate,
    LicenseInfoDto? License,
    VehicleInfoDto? AssignedVehicle
);

public record MobileUserResponseDto(
    long UserId,
    string Username,
    string Role,
    bool IsActive,
    DriverProfileDto? Driver,
    DateTime CreatedAt
);

public record MobileMeResponseDto(
    long UserId,
    string Username,
    string Role,
    bool IsActive,
    DriverProfileDto? Driver,
    DateTime CreatedAt
);

public class JobDto
{
    public long Id { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? DriverId { get; set; }
    public string? DriverName { get; set; }
    public long? VehicleId { get; set; }
    public string? VehiclePlate { get; set; }
    public string? VehicleType { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PickupLocation { get; set; } = string.Empty;
    public double? PickupLat { get; set; }
    public double? PickupLng { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Companions { get; set; }
    public DateTime? ScheduledStartAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public long? CancelledBy { get; set; }
    public string? CancelledByName { get; set; }
}

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

public record CreatedJobResponseDto(
    long Id,
    string JobNumber,
    string Status
);

public record CreatedEntityResponseDto(
    long Id,
    string? Name
);

public record CreatedUserResponseDto(
    long UserId,
    string Username,
    string Role
);

public class AdminJobListItemDto
{
    public long Id { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? DriverId { get; set; }
    public string? DriverName { get; set; }
    public long? VehicleId { get; set; }
    public string? VehiclePlate { get; set; }
    public string? VehicleType { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PickupLocation { get; set; } = string.Empty;
    public double? PickupLat { get; set; }
    public double? PickupLng { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Companions { get; set; }
    public string? ScheduledDate { get; set; }
    public string? ScheduledTime { get; set; }
    public DateTime? ScheduledStartAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public long? CancelledBy { get; set; }
    public string? CancelledByName { get; set; }
}

public class AdminUserListItemDto
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? IdCardNo { get; set; }
    public string? BirthDate { get; set; }
    public string? LicenseNo { get; set; }
    public string? LicenseExpiration { get; set; }
    public string? LicenseStatus { get; set; }
    public long? VehicleId { get; set; }
    public string? VehiclePlate { get; set; }
    public string? VehicleType { get; set; }
    public int ActiveJobsCount { get; set; }
}

public class AdminVehicleListItemDto
{
    public long Id { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public double Capacity { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? VehicleType { get; set; }
    public long? AssignedDriverId { get; set; }
    public string? AssignedDriverName { get; set; }
    public int ActiveJobsCount { get; set; }
}

public class VehicleTypeItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int VehicleCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ChartStatItemDto
{
    public string Time { get; set; } = string.Empty;
    public int Completed { get; set; }
    public int Inprogress { get; set; }
    public int Cancelled { get; set; }
}

public record DashboardSummaryResponseDto(
    int TotalJobsToday,
    int TotalJobsThisMonth,
    int TotalJobsThisYear,
    int PendingJobs,
    int InProgressJobs,
    int CompletedJobs,
    int CancelledJobs,
    int AvailableDrivers,
    IEnumerable<ChartStatItemDto> HourlyStats,
    IEnumerable<ChartStatItemDto> MonthlyStats,
    IEnumerable<ChartStatItemDto> YearlyStats
);

public class AuditLogListItemDto
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string? Username { get; set; }
    public string? FullName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PaginatedListDto<T>
{
    public IEnumerable<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class JobHistoryResponseDto
{
    public IEnumerable<JobDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
}

public class JobHistoryRequestDto
{
    public int? Offset { get; set; } = 25;
    public string? Status { get; set; }
}

public record ChangePasswordVerifyRequestDto(string Password);
public record ChangePasswordVerifyResponseDto(string Token, int ExpiresIn);
public record SetNewPasswordRequestDto(string Token, string Password, string ConfirmPassword);
public record ForgotPasswordVerifyRequestDto(string Username, string IdCardNo);
public record RegisterDeviceRequestDto(string DeviceId, string? DeviceName, string? DeviceModel, string? AppVersion, string? FcmToken, string? IpAddress);

public class NotificationItemDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationListResponseDto
{
    public IEnumerable<NotificationItemDto> Items { get; set; } = [];
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
}

public record TestNotificationRequestDto(long? UserId, string? FcmToken, string Title, string Body, string? Type, long? JobId);


