namespace Core.Entities;

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class UserProfile
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? IdCardNo { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? LicenseNo { get; set; }
    public DateTime? LicenseIssueDate { get; set; }
    public DateTime? LicenseExpirationDate { get; set; }
    public long? VehicleId { get; set; }
    public bool IsActive { get; set; } = true;
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Vehicle
{
    public long Id { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long? VehicleTypeId { get; set; }
    public double Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Job
{
    public long Id { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? DriverId { get; set; }
    public long? VehicleId { get; set; }
    public string Status { get; set; } = "Pending";
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
    public int RowVersion { get; set; } = 1;
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class RefreshToken
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? ReplacedByTokenId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserDevice
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? DeviceModel { get; set; }
    public string? AppVersion { get; set; }
    public string? FcmToken { get; set; }
    public string? IpAddress { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class NotificationOutbox
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


