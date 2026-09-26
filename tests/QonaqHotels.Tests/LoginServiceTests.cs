using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Services;

namespace QonaqHotels.Tests;

public class LoginServiceTests
{
    private static HotelDbContext CreateContext(string name)
    {
        var opts = new DbContextOptionsBuilder<HotelDbContext>()
            .UseInMemoryDatabase(name).Options;
        return new HotelDbContext(opts);
    }

    private static async Task<User> SeedUser(
        HotelDbContext db,
        string login = "admin",
        string password = "Pass123!",
        bool isActive = true,
        DateOnly? dismissalDate = null,
        DateTime? lockedUntil = null,
        int failedAttempts = 0)
    {
        var country  = new Country  { Name = "Казахстан" };
        var city     = new City     { Name = "Астана", Country = country };
        var hotel    = new Hotel    { Name = "H", City = city, Address = "a", Stars = 3 };
        var position = new Position { Name = "администратор" };
        db.Countries.Add(country); db.Cities.Add(city);
        db.Hotels.Add(hotel); db.Positions.Add(position);
        await db.SaveChangesAsync();

        var emp = new Employee {
            FullName = "Тест Тестов", HotelId = hotel.Id, PositionId = position.Id,
            HireDate = new DateOnly(2024, 1, 1), DismissalDate = dismissalDate
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var hasher = new PasswordHasher<User>();
        var user = new User {
            Login = login, Role = "админ", IsActive = isActive,
            EmployeeId = emp.Id, FailedAttempts = failedAttempts,
            LockedUntil = lockedUntil
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // Review Focus #1: case-insensitive login
    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    public async Task AttemptLogin_CaseInsensitive_Succeeds(string loginInput)
    {
        using var db = CreateContext($"case-{loginInput}");
        await SeedUser(db, login: "admin", password: "Pass123!");
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync(loginInput, "Pass123!");

        Assert.Equal(LoginResult.Success, result.Outcome);
    }

    // Review Focus #2: non-existent user returns generic error
    [Fact]
    public async Task AttemptLogin_UserNotFound_ReturnsGenericError()
    {
        using var db  = CreateContext("not-found");
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("nobody", "any");

        Assert.Equal(LoginResult.InvalidCredentials, result.Outcome);
        Assert.Equal("Неверный логин или пароль", result.ErrorMessage);
    }

    [Fact]
    public async Task AttemptLogin_WrongPassword_IncrementsCounter()
    {
        using var db = CreateContext("incr-counter");
        var user = await SeedUser(db);
        var svc  = new LoginService(db, new PasswordHasher<User>());

        await svc.AttemptLoginAsync("admin", "wrong");

        var updated = await db.Users.FindAsync(user.Id);
        Assert.Equal(1, updated!.FailedAttempts);
    }

    [Fact]
    public async Task AttemptLogin_FiveWrongPasswords_LocksAccount()
    {
        using var db = CreateContext("lockout");
        var user = await SeedUser(db, failedAttempts: 4);
        var svc  = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("admin", "wrong");

        Assert.Equal(LoginResult.InvalidCredentials, result.Outcome);
        var updated = await db.Users.FindAsync(user.Id);
        Assert.NotNull(updated!.LockedUntil);
        Assert.True(updated.LockedUntil > DateTime.UtcNow);
        Assert.Equal(0, updated.FailedAttempts);
    }

    [Fact]
    public async Task AttemptLogin_Locked_ReturnsLockoutError()
    {
        using var db = CreateContext("locked");
        await SeedUser(db, lockedUntil: DateTime.UtcNow.AddMinutes(10));
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("admin", "Pass123!");

        Assert.Equal(LoginResult.LockedOut, result.Outcome);
        Assert.NotNull(result.MinutesRemaining);
    }

    [Fact]
    public async Task AttemptLogin_Inactive_ReturnsDisabledError()
    {
        using var db = CreateContext("inactive");
        await SeedUser(db, isActive: false);
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("admin", "Pass123!");

        Assert.Equal(LoginResult.Disabled, result.Outcome);
    }

    [Fact]
    public async Task AttemptLogin_DismissedEmployee_ReturnsDisabledError()
    {
        using var db = CreateContext("dismissed");
        await SeedUser(db, dismissalDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("admin", "Pass123!");

        Assert.Equal(LoginResult.Disabled, result.Outcome);
    }

    [Fact]
    public async Task AttemptLogin_Success_ClearsCounterAndUpdatesLastLogin()
    {
        using var db = CreateContext("success");
        var user = await SeedUser(db, failedAttempts: 2);
        var svc  = new LoginService(db, new PasswordHasher<User>());

        await svc.AttemptLoginAsync("admin", "Pass123!");

        var updated = await db.Users.FindAsync(user.Id);
        Assert.Equal(0, updated!.FailedAttempts);
        Assert.Null(updated.LockedUntil);
        Assert.NotNull(updated.LastLoginAt);
    }
}
