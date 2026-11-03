using Microsoft.AspNetCore.Identity;
using Npgsql;
using QonaqHotels.Models;

namespace QonaqHotels.Data;

// Первый запуск на пустой базе: создаёт учётную запись admin.
// Справочники и демо-данные — в db/seed.sql (накатывается скриптом db/reset.sh).
public static class Seeder
{
    public static async Task SeedAsync(Db db, PasswordHasher<User> hasher, ILogger logger)
    {
        await using var conn = await db.OpenAsync();

        await using (var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM users)", conn))
        {
            if ((bool)(await check.ExecuteScalarAsync())!) return;
        }

        int hotelId;
        await using (var hotelCmd = new NpgsqlCommand("SELECT id FROM hotels ORDER BY id LIMIT 1", conn))
        {
            if (await hotelCmd.ExecuteScalarAsync() is not int id)
            {
                logger.LogWarning(
                    "Seeder: таблица hotels пуста — учётная запись администратора не создана.");
                return;
            }
            hotelId = id;
        }

        var password = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Переменная окружения ADMIN_INITIAL_PASSWORD не задана.");

        // Должность, сотрудник и учётка — одной транзакцией: при сбое не останется
        // сотрудника без учётки, а повторный запуск начнёт с чистого листа.
        await using var tx = await conn.BeginTransactionAsync();

        // positions.name — UNIQUE, поэтому ON CONFLICT (name) не создаст дубль,
        // если должность уже есть (например, из db/seed.sql).
        int positionId;
        await using (var posCmd = new NpgsqlCommand("""
            INSERT INTO positions (name, role) VALUES ('администратор', @role)
            ON CONFLICT (name) DO NOTHING
            """, conn, tx))
        {
            posCmd.Parameters.AddWithValue("role", Roles.Admin);
            await posCmd.ExecuteNonQueryAsync();
        }
        await using (var posId = new NpgsqlCommand(
            "SELECT id FROM positions WHERE name = 'администратор'", conn, tx))
        {
            positionId = (int)(await posId.ExecuteScalarAsync())!;
        }

        int employeeId;
        await using (var empCmd = new NpgsqlCommand("""
            INSERT INTO employees (hotel_id, position_id, full_name, hire_date)
            VALUES (@h, @p, 'Администратор Системы', CURRENT_DATE)
            RETURNING id
            """, conn, tx))
        {
            empCmd.Parameters.AddWithValue("h", hotelId);
            empCmd.Parameters.AddWithValue("p", positionId);
            employeeId = (int)(await empCmd.ExecuteScalarAsync())!;
        }

        await using (var userCmd = new NpgsqlCommand("""
            INSERT INTO users (employee_id, login, password_hash, role)
            VALUES (@e, 'admin', @h, @role)
            """, conn, tx))
        {
            userCmd.Parameters.AddWithValue("e",    employeeId);
            userCmd.Parameters.AddWithValue("h",    hasher.HashPassword(new User { Login = "admin" }, password));
            userCmd.Parameters.AddWithValue("role", Roles.Admin);
            await userCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        logger.LogInformation("Seeder: учётная запись 'admin' создана.");
    }
}
