using Npgsql;

namespace QonaqHotels.Pages.Admin.Employees;

// Учётная запись (users) следует за сотрудником (employees).
// Вызывается внутри транзакции вместе с изменением сотрудника, чтобы не получилось
// «уволен, но учётка активна» или наоборот.
internal static class EmployeeAccounts
{
    // Уволенный сотрудник не должен входить: учётка отключается и блокируется
    public static async Task DisableAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int employeeId)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE users
            SET is_active = false, locked_until = now() + interval '100 years'
            WHERE employee_id = @id
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    // Сотрудник снова работает: учётка включается, счётчик неудачных входов сбрасывается
    public static async Task EnableAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int employeeId)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE users
            SET is_active = true, locked_until = NULL, failed_attempts = 0
            WHERE employee_id = @id
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    // Роль учётки берётся из должности сотрудника.
    // Если роль должности не проходит users_role_check, UPDATE упадёт и откатит всю транзакцию.
    public static async Task SyncRoleAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int employeeId)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE users u
            SET role = p.role
            FROM employees e
            JOIN positions p ON p.id = e.position_id
            WHERE e.id = @id AND u.employee_id = e.id
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }
}
