using System.Security.Claims;

namespace QonaqHotels.Services;

// Гость сайта входит с claim NameIdentifier = "guest:<id>" (RegisterLogin.SignInGuestAsync)
public static class GuestClaims
{
    private const string Prefix = "guest:";

    public static int? GuestId(ClaimsPrincipal user)
    {
        var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return value != null && value.StartsWith(Prefix, StringComparison.Ordinal)
               && int.TryParse(value[Prefix.Length..], out var id)
            ? id
            : null;
    }
}
