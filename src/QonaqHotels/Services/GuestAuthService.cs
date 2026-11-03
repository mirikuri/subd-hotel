using Microsoft.AspNetCore.Identity;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Services;

public enum GuestAuthOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
    Disabled,
    LoginTaken,
    DocumentTaken
}

public record GuestAuthResult(
    GuestAuthOutcome Outcome,
    Guest?           Guest,
    string?          ErrorMessage,
    int?             MinutesRemaining
);

public class GuestAuthService
{
    private readonly Db                     _db;
    private readonly PasswordHasher<Guest>  _hasher;

    public GuestAuthService(Db db, PasswordHasher<Guest> hasher)
    {
        _db     = db;
        _hasher = hasher;
    }

    public async Task<GuestAuthResult> RegisterAsync(
        string  login,
        string  password,
        string  fullName,
        string  documentNumber,
        string? phone,
        string? email)
    {
        await using var conn = await _db.OpenAsync();

        // Логин проверяет уникальный индекс guests_login_lower_key (без учёта регистра),
        // документ — UNIQUE guests_document_number_key.
        // Предварительные SELECT COUNT(*) не нужны: между ними и INSERT мог успеть
        // зарегистрироваться кто-то ещё, а ограничение сработает в любом случае.
        var guest = new Guest
        {
            Login          = login.ToLowerInvariant(),
            FullName       = fullName,
            DocumentNumber = documentNumber,
            Phone          = phone,
            Email          = email,
            IsActive       = true
        };
        guest.PasswordHash = _hasher.HashPassword(guest, password);

        try
        {
            // is_active, failed_attempts, registered_at заполняют DEFAULT-ы схемы
            await using var ins = new NpgsqlCommand("""
                INSERT INTO guests (full_name, document_number, phone, email, login, password_hash)
                VALUES (@fn, @doc, @phone, @email, @login, @hash)
                RETURNING id, registered_at
                """, conn);
            ins.Parameters.AddWithValue("fn",    fullName);
            ins.Parameters.AddWithValue("doc",   documentNumber);
            ins.Parameters.AddWithValue("phone", (object?)phone ?? DBNull.Value);
            ins.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);
            ins.Parameters.AddWithValue("login", guest.Login);
            ins.Parameters.AddWithValue("hash",  guest.PasswordHash);

            await using var r = await ins.ExecuteReaderAsync();
            await r.ReadAsync();
            guest.Id           = r.GetInt32(0);
            guest.RegisteredAt = r.GetDateTime(1);
        }
        catch (PostgresException ex) when (ex.SqlState == DbErrors.UniqueViolation)
        {
            return ex.ConstraintName == "guests_document_number_key"
                ? new GuestAuthResult(GuestAuthOutcome.DocumentTaken, null,
                    "Гость с таким документом уже зарегистрирован", null)
                : new GuestAuthResult(GuestAuthOutcome.LoginTaken, null,
                    "Этот логин уже занят", null);
        }

        return new GuestAuthResult(GuestAuthOutcome.Success, guest, null, null);
    }

    public async Task<GuestAuthResult> AttemptLoginAsync(string login, string password)
    {
        await using var conn = await _db.OpenAsync();

        // Ищем гостя по логину
        await using var cmd = new NpgsqlCommand("""
            SELECT id, full_name, document_number, login, password_hash,
                   is_active, failed_attempts, locked_until, last_login_at
            FROM guests
            WHERE lower(login) = lower(@l)
            """, conn);
        cmd.Parameters.AddWithValue("l", login);

        Guest? guest = null;
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                guest = new Guest
                {
                    Id             = r.GetInt32(0),
                    FullName       = r.GetString(1),
                    DocumentNumber = r.GetString(2),
                    Login          = r.GetString(3),
                    PasswordHash   = r.IsDBNull(4) ? null : r.GetString(4),
                    IsActive       = r.GetBoolean(5),
                    FailedAttempts = r.GetInt32(6),
                    LockedUntil    = r.IsDBNull(7) ? null : r.GetDateTime(7),
                    LastLoginAt    = r.IsDBNull(8) ? null : r.GetDateTime(8)
                };
            }
        }

        if (guest == null || guest.PasswordHash == null)
        {
            var dummy = new Guest();
            var dummyHash = _hasher.HashPassword(dummy, "dummy_qonaq_guest_sentinel");
            _hasher.VerifyHashedPassword(dummy, dummyHash, password);
            return new GuestAuthResult(GuestAuthOutcome.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        if (!guest.IsActive)
            return new GuestAuthResult(GuestAuthOutcome.Disabled, null,
                "Учётная запись отключена", null);

        if (guest.LockedUntil.HasValue && guest.LockedUntil > DateTime.UtcNow)
        {
            var mins = (int)Math.Ceiling((guest.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return new GuestAuthResult(GuestAuthOutcome.LockedOut, null,
                $"Слишком много попыток. Попробуйте через {mins} мин.", mins);
        }

        var verify = _hasher.VerifyHashedPassword(guest, guest.PasswordHash, password);

        if (verify == PasswordVerificationResult.Failed)
        {
            // Атомарный счётчик — как в LoginService: БД сама прибавляет 1 к текущему значению
            await using var fail = new NpgsqlCommand("""
                UPDATE guests
                SET failed_attempts = CASE WHEN failed_attempts + 1 >= @max
                                           THEN 0 ELSE failed_attempts + 1 END,
                    locked_until    = CASE WHEN failed_attempts + 1 >= @max
                                           THEN now() + make_interval(mins => @lockMinutes)
                                           ELSE locked_until END
                WHERE id = @id
                """, conn);
            fail.Parameters.AddWithValue("max",         LoginService.MaxFailedAttempts);
            fail.Parameters.AddWithValue("lockMinutes", LoginService.LockMinutes);
            fail.Parameters.AddWithValue("id",          guest.Id);
            await fail.ExecuteNonQueryAsync();

            return new GuestAuthResult(GuestAuthOutcome.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        // Успех
        var newHash = verify == PasswordVerificationResult.SuccessRehashNeeded
            ? _hasher.HashPassword(guest, password)
            : guest.PasswordHash;

        await using (var success = new NpgsqlCommand("""
            UPDATE guests
            SET failed_attempts = 0, locked_until = NULL,
                last_login_at = now(), password_hash = @hash
            WHERE id = @id
            RETURNING last_login_at
            """, conn))
        {
            success.Parameters.AddWithValue("hash", newHash);
            success.Parameters.AddWithValue("id",   guest.Id);
            guest.LastLoginAt = (DateTime?)await success.ExecuteScalarAsync();
        }

        guest.FailedAttempts = 0;
        guest.LockedUntil    = null;
        guest.PasswordHash   = newHash;

        return new GuestAuthResult(GuestAuthOutcome.Success, guest, null, null);
    }
}
