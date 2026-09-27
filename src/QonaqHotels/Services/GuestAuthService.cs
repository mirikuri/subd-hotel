using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    private readonly HotelDbContext        _db;
    private readonly PasswordHasher<Guest> _hasher;

    public GuestAuthService(HotelDbContext db, PasswordHasher<Guest> hasher)
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
        var loginLower = login.ToLower();

        var loginExists = await _db.Guests
            .AnyAsync(g => g.Login != null && g.Login.ToLower() == loginLower);
        if (loginExists)
            return new GuestAuthResult(GuestAuthOutcome.LoginTaken, null,
                "Этот логин уже занят", null);

        var docExists = await _db.Guests
            .AnyAsync(g => g.DocumentNumber == documentNumber);
        if (docExists)
            return new GuestAuthResult(GuestAuthOutcome.DocumentTaken, null,
                "Гость с таким документом уже зарегистрирован", null);

        var guest = new Guest
        {
            Login          = loginLower,
            FullName       = fullName,
            DocumentNumber = documentNumber,
            Phone          = phone,
            Email          = email,
            IsActive       = true,
            FailedAttempts = 0
        };
        guest.PasswordHash = _hasher.HashPassword(guest, password);

        _db.Guests.Add(guest);
        await _db.SaveChangesAsync();

        return new GuestAuthResult(GuestAuthOutcome.Success, guest, null, null);
    }

    public async Task<GuestAuthResult> AttemptLoginAsync(string login, string password)
    {
        var loginLower = login.ToLower();

        var guest = await _db.Guests
            .FirstOrDefaultAsync(g => g.Login != null && g.Login.ToLower() == loginLower);

        if (guest == null || guest.PasswordHash == null)
        {
            var dummy = new Guest();
            var dummyHash = _hasher.HashPassword(dummy, "dummy_qonaq_guest_sentinel");
            _hasher.VerifyHashedPassword(dummy, dummyHash, password);
            return new GuestAuthResult(GuestAuthOutcome.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        if (guest.LockedUntil.HasValue && guest.LockedUntil > DateTime.UtcNow)
        {
            var mins = (int)Math.Ceiling((guest.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return new GuestAuthResult(GuestAuthOutcome.LockedOut, null,
                $"Слишком много попыток. Попробуйте через {mins} мин.", mins);
        }

        var verify = _hasher.VerifyHashedPassword(guest, guest.PasswordHash, password);

        if (verify == PasswordVerificationResult.Failed)
        {
            guest.FailedAttempts++;
            if (guest.FailedAttempts >= 5)
            {
                guest.LockedUntil    = DateTime.UtcNow.AddMinutes(15);
                guest.FailedAttempts = 0;
            }
            await _db.SaveChangesAsync();
            return new GuestAuthResult(GuestAuthOutcome.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        if (!guest.IsActive)
            return new GuestAuthResult(GuestAuthOutcome.Disabled, null,
                "Учётная запись отключена", null);

        guest.FailedAttempts = 0;
        guest.LockedUntil    = null;
        guest.LastLoginAt    = DateTime.UtcNow;

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            guest.PasswordHash = _hasher.HashPassword(guest, password);

        await _db.SaveChangesAsync();
        return new GuestAuthResult(GuestAuthOutcome.Success, guest, null, null);
    }
}
