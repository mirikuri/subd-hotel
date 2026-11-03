using Microsoft.AspNetCore.Identity;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Services;

namespace QonaqHotels.Tests;

public class LoginServiceTests : IAsyncLifetime
{
    private readonly Db _db;
    private readonly string _prefix;
    private int _empId;
    private int _userId;

    public LoginServiceTests()
    {
        _db     = TestDb.Create();
        _prefix = $"t{Guid.NewGuid():N}"[..9] + "_";
    }

    public async Task InitializeAsync()
    {
        using var conn = _db.Open();

        using var emp = new NpgsqlCommand(
            "INSERT INTO employees (hotel_id, position_id, full_name, hire_date) " +
            "VALUES (1, 1, @n, '2024-01-01') RETURNING id", conn);
        emp.Parameters.AddWithValue("n", _prefix + "Тест Тестов");
        _empId = (int)(await emp.ExecuteScalarAsync())!;

        var hasher = new PasswordHasher<User>();
        var tmpUser = new User();
        var hash = hasher.HashPassword(tmpUser, "Pass123!");

        using var usr = new NpgsqlCommand(
            "INSERT INTO users (employee_id, login, password_hash, role, is_active, failed_attempts) " +
            "VALUES (@e, @l, @h, 'админ', true, 0) RETURNING id", conn);
        usr.Parameters.AddWithValue("e", _empId);
        usr.Parameters.AddWithValue("l", _prefix + "admin");
        usr.Parameters.AddWithValue("h", hash);
        _userId = (int)(await usr.ExecuteScalarAsync())!;
    }

    public async Task DisposeAsync()
    {
        using var conn = _db.Open();
        using var cmd  = new NpgsqlCommand(
            "DELETE FROM users WHERE id = @u; DELETE FROM employees WHERE id = @e", conn);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("e", _empId);
        await cmd.ExecuteNonQueryAsync();
    }

    private LoginService NewService() => new(_db, new PasswordHasher<User>());
    private string Login => _prefix + "admin";

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    public async Task AttemptLogin_CaseInsensitive_Succeeds(string suffix)
    {
        var svc    = NewService();
        var input  = _prefix + suffix;
        var result = await svc.AttemptLoginAsync(input, "Pass123!");
        Assert.Equal(LoginResult.Success, result.Outcome);
    }

    [Fact]
    public async Task AttemptLogin_UserNotFound_ReturnsGenericError()
    {
        var svc    = NewService();
        var result = await svc.AttemptLoginAsync(_prefix + "nobody", "any");
        Assert.Equal(LoginResult.InvalidCredentials, result.Outcome);
        Assert.Equal("Неверный логин или пароль", result.ErrorMessage);
    }

    [Fact]
    public async Task AttemptLogin_WrongPassword_IncrementsCounter()
    {
        var svc = NewService();
        await svc.AttemptLoginAsync(Login, "wrong");

        using var conn = _db.Open();
        using var cmd  = new NpgsqlCommand(
            "SELECT failed_attempts FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", _userId);
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task AttemptLogin_FiveWrongPasswords_LocksAccount()
    {
        var svc = NewService();
        using (var conn = _db.Open())
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE users SET failed_attempts = 4 WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _userId);
            await cmd.ExecuteNonQueryAsync();
        }

        var result = await svc.AttemptLoginAsync(Login, "wrong");
        Assert.Equal(LoginResult.InvalidCredentials, result.Outcome);

        using var c2  = _db.Open();
        using var q   = new NpgsqlCommand(
            "SELECT locked_until, failed_attempts FROM users WHERE id = @id", c2);
        q.Parameters.AddWithValue("id", _userId);
        using var r = await q.ExecuteReaderAsync();
        await r.ReadAsync();
        Assert.False(r.IsDBNull(0));
        Assert.True(r.GetDateTime(0) > DateTime.UtcNow);
        Assert.Equal(0, r.GetInt32(1));
    }

    [Fact]
    public async Task AttemptLogin_Locked_ReturnsLockoutError()
    {
        using (var conn = _db.Open())
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE users SET locked_until = @lu WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("lu", DateTime.UtcNow.AddMinutes(10));
            cmd.Parameters.AddWithValue("id", _userId);
            await cmd.ExecuteNonQueryAsync();
        }

        var svc    = NewService();
        var result = await svc.AttemptLoginAsync(Login, "Pass123!");
        Assert.Equal(LoginResult.LockedOut, result.Outcome);
        Assert.NotNull(result.MinutesRemaining);
    }

    [Fact]
    public async Task AttemptLogin_Inactive_ReturnsDisabledError()
    {
        using (var conn = _db.Open())
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE users SET is_active = false WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _userId);
            await cmd.ExecuteNonQueryAsync();
        }

        var svc    = NewService();
        var result = await svc.AttemptLoginAsync(Login, "Pass123!");
        Assert.Equal(LoginResult.Disabled, result.Outcome);
    }

    [Fact]
    public async Task AttemptLogin_DismissedEmployee_ReturnsDisabledError()
    {
        using (var conn = _db.Open())
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE employees SET dismissal_date = @d WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("d", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
            cmd.Parameters.AddWithValue("id", _empId);
            await cmd.ExecuteNonQueryAsync();
        }

        var svc    = NewService();
        var result = await svc.AttemptLoginAsync(Login, "Pass123!");
        Assert.Equal(LoginResult.Disabled, result.Outcome);
    }

    [Fact]
    public async Task AttemptLogin_Success_ClearsCounterAndUpdatesLastLogin()
    {
        using (var conn = _db.Open())
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE users SET failed_attempts = 2 WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _userId);
            await cmd.ExecuteNonQueryAsync();
        }

        var svc = NewService();
        await svc.AttemptLoginAsync(Login, "Pass123!");

        using var c2  = _db.Open();
        using var q   = new NpgsqlCommand(
            "SELECT failed_attempts, locked_until, last_login_at FROM users WHERE id = @id", c2);
        q.Parameters.AddWithValue("id", _userId);
        using var r = await q.ExecuteReaderAsync();
        await r.ReadAsync();
        Assert.Equal(0, r.GetInt32(0));
        Assert.True(r.IsDBNull(1));
        Assert.False(r.IsDBNull(2));
    }
}
