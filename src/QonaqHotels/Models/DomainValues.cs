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

    // Роль вошедшего гостя сайта (claim ставит RegisterLogin, в БД не хранится)
    public const string Guest      = "гость";
}

// rooms.view
public static class RoomView
{
    public static readonly string[] All = ["город", "парк", "двор", "горы"];
}

// room_types.bed_type
public static class BedType
{
    public static readonly string[] All = ["односпальная", "двуспальная", "две односпальные", "king-size"];
}

// rooms.status
public static class RoomStatus
{
    public const string Free     = "свободен";
    public const string Occupied = "занят";
    public const string Cleaning = "уборка";
    public const string Repair   = "ремонт";
}
