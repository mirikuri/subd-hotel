using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Services;

public enum LoginResult { Success, InvalidCredentials, LockedOut, Disabled }

public record LoginAttemptResult(
    LoginResult Outcome,
    User?       User,
    string?     ErrorMessage,
    int?        MinutesRemaining
);

public class LoginService
{
    private readonly HotelDbContext      _db;
    private readonly PasswordHasher<User> _hasher;

    public LoginService(HotelDbContext db, PasswordHasher<User> hasher)
    {
        _db     = db;
        _hasher = hasher;
    }

    public async Task<LoginAttemptResult> AttemptLoginAsync(string login, string password)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Login.ToLower() == login.ToLower());

        // Timing-safe: always hash-verify even when user is null
        if (user == null)
        {
            var dummy = new User();
            var dummyHash = _hasher.HashPassword(dummy, "dummy_qonaq_sentinel_2026");
            _hasher.VerifyHashedPassword(dummy, dummyHash, password);
            return new LoginAttemptResult(LoginResult.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        // Re-attach for mutations
        var tracked = await _db.Users.Include(u => u.Employee)
            .FirstAsync(u => u.Id == user.Id);

        // Check lockout before verifying password
        if (tracked.LockedUntil.HasValue && tracked.LockedUntil > DateTime.UtcNow)
        {
            var mins = (int)Math.Ceiling((tracked.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return new LoginAttemptResult(LoginResult.LockedOut, null,
                $"Слишком много попыток. Попробуйте через {mins} мин.", mins);
        }

        var verify = _hasher.VerifyHashedPassword(tracked, tracked.PasswordHash, password);

        if (verify == PasswordVerificationResult.Failed)
        {
            tracked.FailedAttempts++;
            if (tracked.FailedAttempts >= 5)
            {
                tracked.LockedUntil    = DateTime.UtcNow.AddMinutes(15);
                tracked.FailedAttempts = 0;
            }
            await _db.SaveChangesAsync();
            return new LoginAttemptResult(LoginResult.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        // Password correct — check account status
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!tracked.IsActive ||
            (tracked.Employee.DismissalDate.HasValue &&
             tracked.Employee.DismissalDate <= today))
        {
            return new LoginAttemptResult(LoginResult.Disabled, null,
                "Учётная запись отключена. Обратитесь к администратору", null);
        }

        // Success
        tracked.FailedAttempts = 0;
        tracked.LockedUntil    = null;
        tracked.LastLoginAt    = DateTime.UtcNow;

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            tracked.PasswordHash = _hasher.HashPassword(tracked, password);

        await _db.SaveChangesAsync();
        return new LoginAttemptResult(LoginResult.Success, tracked, null, null);
    }
}
