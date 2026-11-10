using Microsoft.AspNetCore.Identity;
using QonaqHotels.Models;
using QonaqHotels.Services;

namespace QonaqHotels.Tests;

// Демо-учётки из db/seed.sql: пароль у всех Test1234!
public class DemoAccountsTests
{
    [Theory]
    [InlineData("admin",              "админ")]
    [InlineData("astana.reception1",  "ресепшен")]
    [InlineData("almaty.accountant1", "бухгалтер")]
    public async Task Staff_CanLogIn(string login, string role)
    {
        var res = await new LoginService(TestDb.Create(), new PasswordHasher<User>()).AttemptLoginAsync(login, "Test1234!");
        Assert.Equal(LoginResult.Success, res.Outcome);
        Assert.Equal(role, res.User!.Role);
    }

    [Fact]
    public async Task DismissedStaff_Disabled()
    {
        var res = await new LoginService(TestDb.Create(), new PasswordHasher<User>()).AttemptLoginAsync("astana.reception3", "Test1234!");
        Assert.Equal(LoginResult.Disabled, res.Outcome);
    }

    [Fact]
    public async Task Guest_CanLogIn()
    {
        var res = await new GuestAuthService(TestDb.Create(), new PasswordHasher<Guest>()).AttemptLoginAsync("guest1", "Test1234!");
        Assert.Equal(GuestAuthOutcome.Success, res.Outcome);
    }
}
