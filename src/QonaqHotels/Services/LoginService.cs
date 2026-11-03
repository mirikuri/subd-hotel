using Microsoft.AspNetCore.Identity;
using Npgsql;
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
    // После MaxFailedAttempts неверных паролей подряд учётка блокируется на LockMinutes
    public const int MaxFailedAttempts = 5;
    public const int LockMinutes       = 15;

    private readonly Db                    _db;
    private readonly PasswordHasher<User>  _hasher;

    public LoginService(Db db, PasswordHasher<User> hasher)
    {
        _db     = db;
        _hasher = hasher;
    }

    public async Task<LoginAttemptResult> AttemptLoginAsync(string login, string password)
    {
        await using var conn = await _db.OpenAsync();

        // Ищем пользователя по логину (JOIN с employees для получения имени и даты увольнения)
        await using var cmd = new NpgsqlCommand("""
            SELECT u.id, u.login, u.password_hash, u.role, u.is_active,
                   u.failed_attempts, u.locked_until, u.last_login_at,
                   e.full_name, e.dismissal_date, e.hotel_id
            FROM users u
            JOIN employees e ON e.id = u.employee_id
            WHERE lower(u.login) = lower(@login)
            """, conn);
        cmd.Parameters.AddWithValue("login", login);

        User? user = null;
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                user = new User
                {
                    Id              = r.GetInt32(0),
                    Login           = r.GetString(1),
                    PasswordHash    = r.GetString(2),
                    Role            = r.GetString(3),
                    IsActive        = r.GetBoolean(4),
                    FailedAttempts  = r.GetInt32(5),
                    LockedUntil     = r.IsDBNull(6) ? null : r.GetDateTime(6),
                    LastLoginAt     = r.IsDBNull(7) ? null : r.GetDateTime(7),
                    EmployeeName    = r.GetString(8),
                    DismissalDate   = r.IsDBNull(9) ? null : r.GetFieldValue<DateOnly>(9),
                    EmployeeHotelId = r.GetInt32(10)
                };
            }
        }

        // Timing-safe: всегда хэшируем даже если пользователь не найден
        if (user == null)
        {
            var dummy = new User();
            var dummyHash = _hasher.HashPassword(dummy, "dummy_qonaq_sentinel_2026");
            _hasher.VerifyHashedPassword(dummy, dummyHash, password);
            return new LoginAttemptResult(LoginResult.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        // is_active до lockout — отключённые admin'ом не должны видеть абсурдные минуты
        if (!user.IsActive)
        {
            return new LoginAttemptResult(LoginResult.Disabled, null,
                "Учётная запись отключена. Обратитесь к администратору", null);
        }

        // Проверяем временную блокировку
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            var mins = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return new LoginAttemptResult(LoginResult.LockedOut, null,
                $"Слишком много попыток. Попробуйте через {mins} мин.", mins);
        }

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (verify == PasswordVerificationResult.Failed)
        {
            // Счётчик увеличивает сама БД (failed_attempts + 1 от текущего значения в строке),
            // поэтому два одновременных неверных входа не перезапишут друг друга.
            // На MaxFailedAttempts-й ошибке — блокировка на LockMinutes и сброс счётчика.
            // В SET все выражения видят старые значения строки.
            await using var fail = new NpgsqlCommand("""
                UPDATE users
                SET failed_attempts = CASE WHEN failed_attempts + 1 >= @max
                                           THEN 0 ELSE failed_attempts + 1 END,
                    locked_until    = CASE WHEN failed_attempts + 1 >= @max
                                           THEN now() + make_interval(mins => @lockMinutes)
                                           ELSE locked_until END
                WHERE id = @id
                """, conn);
            fail.Parameters.AddWithValue("max",         MaxFailedAttempts);
            fail.Parameters.AddWithValue("lockMinutes", LockMinutes);
            fail.Parameters.AddWithValue("id",          user.Id);
            await fail.ExecuteNonQueryAsync();

            return new LoginAttemptResult(LoginResult.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        // Пароль верный — проверяем дату увольнения
        if (user.DismissalDate.HasValue && user.DismissalDate <= Clock.Today)
        {
            return new LoginAttemptResult(LoginResult.Disabled, null,
                "Учётная запись отключена. Обратитесь к администратору", null);
        }

        // Успех — обнуляем счётчик и обновляем время входа
        var newHash = verify == PasswordVerificationResult.SuccessRehashNeeded
            ? _hasher.HashPassword(user, password)
            : user.PasswordHash;

        await using (var success = new NpgsqlCommand("""
            UPDATE users
            SET failed_attempts = 0, locked_until = NULL,
                last_login_at = now(), password_hash = @hash
            WHERE id = @id
            RETURNING last_login_at
            """, conn))
        {
            success.Parameters.AddWithValue("hash", newHash);
            success.Parameters.AddWithValue("id",   user.Id);
            user.LastLoginAt = (DateTime?)await success.ExecuteScalarAsync();
        }

        user.FailedAttempts = 0;
        user.LockedUntil    = null;
        user.PasswordHash   = newHash;

        return new LoginAttemptResult(LoginResult.Success, user, null, null);
    }
}
