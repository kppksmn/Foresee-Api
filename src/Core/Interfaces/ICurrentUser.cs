namespace Core.Interfaces;

public interface ICurrentUser
{
    long UserId { get; }
    string Role { get; }
}

public interface ICurrentDriver
{
    long UserId { get; }
    long DriverId { get; }
}
