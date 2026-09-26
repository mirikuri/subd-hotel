using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Models;

namespace QonaqHotels.Data;

public static class Seeder
{
    public static async Task SeedAsync(HotelDbContext db, ILogger logger)
    {
        if (await db.Users.AnyAsync()) return;

        var hotel = await db.Hotels.OrderBy(h => h.Id).FirstOrDefaultAsync();
        if (hotel == null)
        {
            logger.LogWarning(
                "Seeder: таблица hotels пуста — учётная запись администратора не создана. " +
                "Выполните db/seed.sql перед запуском приложения.");
            return;
        }

        var password = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Переменная окружения ADMIN_INITIAL_PASSWORD не задана. " +
                "Установите её перед первым запуском.");

        var position = await db.Positions
            .FirstOrDefaultAsync(p => p.Name == "администратор");
        if (position == null)
        {
            position = new Position { Name = "администратор" };
            db.Positions.Add(position);
            await db.SaveChangesAsync();
        }

        var employee = new Employee
        {
            FullName   = "Администратор Системы",
            HotelId    = hotel.Id,
            PositionId = position.Id,
            HireDate   = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var user = new User
        {
            EmployeeId     = employee.Id,
            Login          = "admin",
            Role           = "админ",
            IsActive       = true,
            FailedAttempts = 0
        };
        var hasher = new PasswordHasher<User>();
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeder: учётная запись 'admin' создана.");
    }
}
