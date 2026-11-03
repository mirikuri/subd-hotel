using Microsoft.AspNetCore.Identity;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Services;

namespace QonaqHotels.Tests;

public class GuestAuthServiceTests : IAsyncLifetime
{
    private readonly Db _db;
    private readonly string _prefix;

    public GuestAuthServiceTests()
    {
        _db     = TestDb.Create();
        _prefix = $"t{Guid.NewGuid():N}"[..9] + "_";
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var conn = _db.Open();
        using var cmd  = new NpgsqlCommand(
            "DELETE FROM guests WHERE login LIKE @p OR document_number LIKE @p", conn);
        cmd.Parameters.AddWithValue("p", _prefix + "%");
        await cmd.ExecuteNonQueryAsync();
    }

    private GuestAuthService NewService() => new(_db, new PasswordHasher<Guest>());
    private string L(string s) => _prefix + s;

    [Fact]
    public async Task Register_CreatesGuestWithHashedPassword()
    {
        var svc    = NewService();
        var result = await svc.RegisterAsync(L("ivanov"), "Secret123!", "Иван Иванов",
            L("AA123456"), "+77001234567", "i@example.com");

        Assert.Equal(GuestAuthOutcome.Success, result.Outcome);

        using var conn = _db.Open();
        using var cmd  = new NpgsqlCommand(
            "SELECT password_hash, is_active FROM guests WHERE login = @l", conn);
        cmd.Parameters.AddWithValue("l", L("ivanov"));
        using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.NotNull(r.GetString(0));
        Assert.True(r.GetBoolean(1));
    }

    [Fact]
    public async Task Register_DuplicateLogin_Fails()
    {
        var svc = NewService();
        await svc.RegisterAsync(L("ivanov"), "Pass1!", "И И", L("AA1"), null, null);
        var second = await svc.RegisterAsync(L("IVANOV"), "Pass2!", "П П", L("AA2"), null, null);
        Assert.Equal(GuestAuthOutcome.LoginTaken, second.Outcome);
    }

    [Fact]
    public async Task Register_DuplicateDocument_Fails()
    {
        var svc = NewService();
        await svc.RegisterAsync(L("ivanov"), "Pass1!", "И И", L("AA123"), null, null);
        var second = await svc.RegisterAsync(L("petrov"), "Pass2!", "П П", L("AA123"), null, null);
        Assert.Equal(GuestAuthOutcome.DocumentTaken, second.Outcome);
    }

    [Theory]
    [InlineData("ivanov")]
    [InlineData("IVANOV")]
    [InlineData("Ivanov")]
    public async Task Login_CaseInsensitive_Succeeds(string loginInput)
    {
        var svc = NewService();
        await svc.RegisterAsync(L("ivanov"), "Pass123!", "И И", L("doc"), null, null);
        var result = await svc.AttemptLoginAsync(L(loginInput), "Pass123!");
        Assert.Equal(GuestAuthOutcome.Success, result.Outcome);
        Assert.NotNull(result.Guest);
    }

    [Fact]
    public async Task Login_UserNotFound_ReturnsGenericError()
    {
        var svc    = NewService();
        var result = await svc.AttemptLoginAsync(L("ghost"), "any");
        Assert.Equal(GuestAuthOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsCounter()
    {
        var svc = NewService();
        await svc.RegisterAsync(L("ivanov"), "Pass1!", "И И", L("D1"), null, null);
        await svc.AttemptLoginAsync(L("ivanov"), "wrong");

        using var conn = _db.Open();
        using var cmd  = new NpgsqlCommand(
            "SELECT failed_attempts FROM guests WHERE login = @l", conn);
        cmd.Parameters.AddWithValue("l", L("ivanov"));
        var val = await cmd.ExecuteScalarAsync();
        Assert.Equal(1, (int)val!);
    }

    [Fact]
    public async Task Login_FiveWrong_LocksAccount()
    {
        var svc = NewService();
        await svc.RegisterAsync(L("ivanov"), "Pass1!", "И И", L("D1"), null, null);
        for (int i = 0; i < 5; i++)
            await svc.AttemptLoginAsync(L("ivanov"), "wrong");

        using var conn = _db.Open();
        using var cmd  = new NpgsqlCommand(
            "SELECT locked_until FROM guests WHERE login = @l", conn);
        cmd.Parameters.AddWithValue("l", L("ivanov"));
        var val = (DateTime?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(val);
        Assert.True(val > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_Locked_ReturnsLockout()
    {
        var svc = NewService();
        await svc.RegisterAsync(L("ivanov"), "Pass1!", "И И", L("D1"), null, null);
        using (var conn = _db.Open())
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE guests SET locked_until = @lu WHERE login = @l", conn);
            cmd.Parameters.AddWithValue("lu", DateTime.UtcNow.AddMinutes(10));
            cmd.Parameters.AddWithValue("l", L("ivanov"));
            await cmd.ExecuteNonQueryAsync();
        }
        var result = await svc.AttemptLoginAsync(L("ivanov"), "Pass1!");
        Assert.Equal(GuestAuthOutcome.LockedOut, result.Outcome);
    }

    [Fact]
    public async Task Login_Inactive_ReturnsDisabled()
    {
        var svc = NewService();
        await svc.RegisterAsync(L("ivanov"), "Pass1!", "И И", L("D1"), null, null);
        using (var conn = _db.Open())
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE guests SET is_active = false WHERE login = @l", conn);
            cmd.Parameters.AddWithValue("l", L("ivanov"));
            await cmd.ExecuteNonQueryAsync();
        }
        var result = await svc.AttemptLoginAsync(L("ivanov"), "Pass1!");
        Assert.Equal(GuestAuthOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task Login_Success_ClearsCounterAndUpdatesLastLogin()
    {
        var svc = NewService();
        await svc.RegisterAsync(L("ivanov"), "Pass1!", "И И", L("D1"), null, null);
        using (var conn = _db.Open())
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE guests SET failed_attempts = 2 WHERE login = @l", conn);
            cmd.Parameters.AddWithValue("l", L("ivanov"));
            await cmd.ExecuteNonQueryAsync();
        }
        await svc.AttemptLoginAsync(L("ivanov"), "Pass1!");

        using var c2  = _db.Open();
        using var q   = new NpgsqlCommand(
            "SELECT failed_attempts, last_login_at FROM guests WHERE login = @l", c2);
        q.Parameters.AddWithValue("l", L("ivanov"));
        using var r = await q.ExecuteReaderAsync();
        await r.ReadAsync();
        Assert.Equal(0, r.GetInt32(0));
        Assert.False(r.IsDBNull(1));
    }
}
