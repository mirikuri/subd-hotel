using Npgsql;

namespace QonaqHotels.Pages.Admin.Users;

internal static class UserLogins
{
    // Логин занят другим пользователем (без учёта регистра, как индекс users_login_lower_key).
    // Окончательно уникальность проверяет индекс; это лишь заранее понятное сообщение у поля.
    // exceptUserId — сам редактируемый пользователь (0 при создании: id начинаются с 1).
    public static async Task<bool> IsTakenAsync(NpgsqlConnection conn, string login, int exceptUserId)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM users
                WHERE lower(login) = lower(@login) AND id <> @except
            )
            """, conn);
        cmd.Parameters.AddWithValue("login",  login);
        cmd.Parameters.AddWithValue("except", exceptUserId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}
