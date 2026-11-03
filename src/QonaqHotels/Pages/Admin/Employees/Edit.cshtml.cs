using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Employees;

[Authorize(Policy = "RequireAdmin")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    [BindProperty] public int Id { get; set; }

    [BindProperty, Required(ErrorMessage = "Укажите ФИО"),
     StringLength(150, MinimumLength = 2, ErrorMessage = "ФИО должно содержать от 2 до 150 символов")]   // employees.full_name varchar(150)
    public string FullName { get; set; } = "";

    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public int PositionId { get; set; }
    [BindProperty] public DateOnly HireDate { get; set; }
    [BindProperty] public DateOnly? DismissalDate { get; set; }
    [BindProperty] public bool IsWorking { get; set; }

    public SelectList Hotels { get; set; } = null!;
    public SelectList Positions { get; set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();

        await using (var cmd = new NpgsqlCommand("""
            SELECT id, full_name, phone, hotel_id, position_id, hire_date, dismissal_date
            FROM employees
            WHERE id = @id
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();

            Id            = r.GetInt32(0);
            FullName      = r.GetString(1);
            Phone         = r.IsDBNull(2) ? null : r.GetString(2);
            HotelId       = r.GetInt32(3);
            PositionId    = r.GetInt32(4);
            HireDate      = r.GetFieldValue<DateOnly>(5);
            DismissalDate = r.IsDBNull(6) ? null : r.GetFieldValue<DateOnly>(6);
            IsWorking     = DismissalDate == null;
        }

        await LoadListsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var today = Clock.Today;

        if (!string.IsNullOrWhiteSpace(Phone) &&
            !Regex.IsMatch(Phone.Trim(), @"^\+[0-9]{11,12}$"))
            ModelState.AddModelError(nameof(Phone), "Формат: +XXXXXXXXXXX (11–12 цифр после +)");

        if (HireDate > today.AddDays(30))
            ModelState.AddModelError(nameof(HireDate), "Дата приёма не может быть более чем на 30 дней в будущем.");

        if (!IsWorking && DismissalDate.HasValue)
        {
            if (DismissalDate.Value > today)
                ModelState.AddModelError(nameof(DismissalDate), "Нельзя указать дату увольнения в будущем.");
            else if (DismissalDate.Value < HireDate)
                ModelState.AddModelError(nameof(DismissalDate), "Дата увольнения не может быть раньше даты приёма.");
        }

        await using var conn = await _db.OpenAsync();

        if (!ModelState.IsValid)
            return await RedisplayAsync(conn);

        if (IsWorking)
            DismissalDate = null;
        else
            DismissalDate ??= today;

        // Сотрудник, роль его учётки и её активность меняются вместе
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await using (var cmd = new NpgsqlCommand("""
                UPDATE employees
                SET full_name = @fn, phone = @phone, hotel_id = @h,
                    position_id = @p, hire_date = @hd, dismissal_date = @dd
                WHERE id = @id
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("fn",    FullName.Trim());
                cmd.Parameters.AddWithValue("phone", string.IsNullOrWhiteSpace(Phone) ? DBNull.Value : Phone.Trim());
                cmd.Parameters.AddWithValue("h",     HotelId);
                cmd.Parameters.AddWithValue("p",     PositionId);
                cmd.Parameters.AddWithValue("hd",    HireDate);
                cmd.Parameters.AddWithValue("dd",    (object?)DismissalDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("id",    Id);
                if (await cmd.ExecuteNonQueryAsync() == 0) return NotFound();
            }

            await EmployeeAccounts.SyncRoleAsync(conn, tx, Id);

            if (DismissalDate is null)
                await EmployeeAccounts.EnableAsync(conn, tx, Id);
            else
                await EmployeeAccounts.DisableAsync(conn, tx, Id);   // дата увольнения ≤ сегодня (проверено выше)

            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            await tx.RollbackAsync();
            ModelState.AddModelError("", DbErrors.Describe(ex));
            return await RedisplayAsync(conn);
        }

        return RedirectToPage("Index");
    }

    private async Task<IActionResult> RedisplayAsync(NpgsqlConnection conn)
    {
        await LoadListsAsync(conn);
        return Page();
    }

    private async Task LoadListsAsync(NpgsqlConnection conn)
    {
        Hotels    = await AdminLists.SelectListAsync(conn, AdminLists.Hotels);
        Positions = await AdminLists.SelectListAsync(conn, AdminLists.Positions);
    }
}
