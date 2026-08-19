using Infrastructure.Repositories;
using Xunit;

namespace UnitTests;

public class SecurityAndJobTests
{
    [Fact]
    public void PasswordHasher_ShouldVerifyCorrectPassword()
    {
        var password = "SecurePassword123!";
        var hash = PasswordHasher.HashPassword(password);

        var isValid = PasswordHasher.VerifyPassword(password, hash);
        var isInvalid = PasswordHasher.VerifyPassword("WrongPassword", hash);

        Assert.True(isValid);
        Assert.False(isInvalid);
    }
}
