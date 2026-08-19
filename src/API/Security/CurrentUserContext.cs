using System.Security.Claims;
using Core.Interfaces;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;

namespace API.Security;

public class CurrentUser : ICurrentUser
{
    public long UserId { get; }
    public string Role { get; }

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (long.TryParse(sub, out var userId))
            {
                UserId = userId;
            }
            Role = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        }
        else
        {
            Role = string.Empty;
        }
    }
}

public class CurrentDriver : ICurrentDriver
{
    public long UserId { get; }
    public long DriverId { get; }

    public CurrentDriver(ICurrentUser currentUser, UserProfileRepository profileRepo)
    {
        UserId = currentUser.UserId;
        if (currentUser.Role == "Driver" && UserId > 0)
        {
            var driver = profileRepo.GetByUserIdAsync(UserId).GetAwaiter().GetResult();
            if (driver != null)
            {
                DriverId = driver.Id;
            }
        }
    }
}
