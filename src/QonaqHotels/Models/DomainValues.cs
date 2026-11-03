namespace QonaqHotels.Models;

// Значения, которые разрешают CHECK-ограничения в db/schema.sql.
// Меняются только вместе со схемой, иначе INSERT/UPDATE упадёт с нарушением CHECK.

// bookings.status
public static class BookingStatus
{
    public const string New       = "новая";
    public const string Confirmed = "подтверждена";
    public const string CheckedIn = "заселён";
    public const string Completed = "завершена";
    public const string Cancelled = "отменена";
}

// users.role (и positions.role, из которой роль копируется пользователю)
public static class Roles
{
    public const string Admin      = "админ";
    public const string Reception  = "ресепшен";
    public const string Accountant = "бухгалтер";
}

// rooms.status
public static class RoomStatus
{
    public const string Free     = "свободен";
    public const string Occupied = "занят";
    public const string Cleaning = "уборка";
    public const string Repair   = "ремонт";
}
