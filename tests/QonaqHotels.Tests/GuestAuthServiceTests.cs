using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Services;

namespace QonaqHotels.Tests;

public class GuestAuthServiceTests
{
    private static HotelDbContext CreateContext(string name)
    {
        var opts = new DbContextOptionsBuilder<HotelDbContext>()
            .UseInMemoryDatabase(name).Options;
        return new HotelDbContext(opts);
    }

    private static GuestAuthService NewService(HotelDbContext db)
        => new(db, new PasswordHasher<Guest>());

    [Fact]
    public async Task Register_CreatesGuestWithHashedPassword()
    {
        using var db = CreateContext("reg-basic");
        var svc = NewService(db);

        var result = await svc.RegisterAsync(
            login: "ivanov",
            password: "Secret123!",
            fullName: "Иван Иванов",
            documentNumber: "AA123456",
            phone: "+77001234567",
            email: "i@example.com");

        Assert.Equal(GuestAuthOutcome.Success, result.Outcome);
        var guest = await db.Guests.FirstAsync(g => g.Login == "ivanov");
        Assert.NotNull(guest.PasswordHash);
        Assert.NotEqual("Secret123!", guest.PasswordHash);
        Assert.True(guest.IsActive);
    }

    [Fact]
    public async Task Register_DuplicateLogin_Fails()
    {
        using var db = CreateContext("reg-dup");
        var svc = NewService(db);

        await svc.RegisterAsync("ivanov", "Pass1!", "И И", "AA1", null, null);
        var second = await svc.RegisterAsync("IVANOV", "Pass2!", "П П", "AA2", null, null);

        Assert.Equal(GuestAuthOutcome.LoginTaken, second.Outcome);
    }

    [Fact]
    public async Task Register_DuplicateDocument_Fails()
    {
        using var db = CreateContext("reg-dup-doc");
        var svc = NewService(db);

        await svc.RegisterAsync("ivanov", "Pass1!", "И И", "AA123", null, null);
        var second = await svc.RegisterAsync("petrov", "Pass2!", "П П", "AA123", null, null);

        Assert.Equal(GuestAuthOutcome.DocumentTaken, second.Outcome);
    }

    [Theory]
    [InlineData("ivanov")]
    [InlineData("IVANOV")]
    [InlineData("Ivanov")]
    public async Task Login_CaseInsensitive_Succeeds(string loginInput)
    {
        using var db = CreateContext($"case-{loginInput}");
        var svc = NewService(db);
        await svc.RegisterAsync("ivanov", "Pass123!", "И И", $"D-{loginInput}", null, null);

        var result = await svc.AttemptLoginAsync(loginInput, "Pass123!");

        Assert.Equal(GuestAuthOutcome.Success, result.Outcome);
        Assert.NotNull(result.Guest);
    }

    [Fact]
    public async Task Login_UserNotFound_ReturnsGenericError()
    {
        using var db = CreateContext("guest-not-found");
        var svc = NewService(db);

        var result = await svc.AttemptLoginAsync("ghost", "any");

        Assert.Equal(GuestAuthOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsCounter()
    {
        using var db = CreateContext("guest-wrong-pw");
        var svc = NewService(db);
        await svc.RegisterAsync("ivanov", "Pass1!", "И И", "D1", null, null);

        await svc.AttemptLoginAsync("ivanov", "wrong");

        var g = await db.Guests.FirstAsync(x => x.Login == "ivanov");
        Assert.Equal(1, g.FailedAttempts);
    }

    [Fact]
    public async Task Login_FiveWrong_LocksAccount()
    {
        using var db = CreateContext("guest-lockout");
        var svc = NewService(db);
        await svc.RegisterAsync("ivanov", "Pass1!", "И И", "D1", null, null);

        for (int i = 0; i < 5; i++)
            await svc.AttemptLoginAsync("ivanov", "wrong");

        var g = await db.Guests.FirstAsync(x => x.Login == "ivanov");
        Assert.NotNull(g.LockedUntil);
        Assert.True(g.LockedUntil > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_Locked_ReturnsLockout()
    {
        using var db = CreateContext("guest-locked");
        var svc = NewService(db);
        await svc.RegisterAsync("ivanov", "Pass1!", "И И", "D1", null, null);
        var g = await db.Guests.FirstAsync(x => x.Login == "ivanov");
        g.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        await db.SaveChangesAsync();

        var result = await svc.AttemptLoginAsync("ivanov", "Pass1!");

        Assert.Equal(GuestAuthOutcome.LockedOut, result.Outcome);
    }

    [Fact]
    public async Task Login_Inactive_ReturnsDisabled()
    {
        using var db = CreateContext("guest-inactive");
        var svc = NewService(db);
        await svc.RegisterAsync("ivanov", "Pass1!", "И И", "D1", null, null);
        var g = await db.Guests.FirstAsync(x => x.Login == "ivanov");
        g.IsActive = false;
        await db.SaveChangesAsync();

        var result = await svc.AttemptLoginAsync("ivanov", "Pass1!");

        Assert.Equal(GuestAuthOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task Login_Success_ClearsCounterAndUpdatesLastLogin()
    {
        using var db = CreateContext("guest-success");
        var svc = NewService(db);
        await svc.RegisterAsync("ivanov", "Pass1!", "И И", "D1", null, null);
        var pre = await db.Guests.FirstAsync(x => x.Login == "ivanov");
        pre.FailedAttempts = 2;
        await db.SaveChangesAsync();

        await svc.AttemptLoginAsync("ivanov", "Pass1!");

        var g = await db.Guests.FirstAsync(x => x.Login == "ivanov");
        Assert.Equal(0, g.FailedAttempts);
        Assert.NotNull(g.LastLoginAt);
    }
}
