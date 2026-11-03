using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Bookings;

[Authorize(Policy = "RequireReceptionist")]
public class DeleteModel : PageModel
{
    private readonly Db _db;
    public DeleteModel(Db db) => _db = db;

    public BookingRow? Booking { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        Booking = await BookingQueries.LoadRowAsync(conn, id);
        return Booking is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await using var conn = await _db.OpenAsync();

        // Проверка статуса и удаление — один оператор: между «проверил» и «удалил»
        // никто не успеет перевести бронь в другой статус.
        // booking_rooms и service_orders удалятся каскадно (ON DELETE CASCADE).
        try
        {
            await using var del = new NpgsqlCommand(
                "DELETE FROM bookings WHERE id = @id AND status = @new", conn);
            del.Parameters.AddWithValue("id",  id);
            del.Parameters.AddWithValue("new", BookingStatus.New);
            if (await del.ExecuteNonQueryAsync() > 0)
                return RedirectToPage("Index");
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            ModelState.AddModelError("", DbErrors.DescribeDelete(ex));
        }

        // Ничего не удалено: брони нет или статус не «новая» (это покажет страница)
        Booking = await BookingQueries.LoadRowAsync(conn, id);
        if (Booking is null) return NotFound();
        if (ModelState.IsValid)
            ModelState.AddModelError("", "Удалить можно только бронь со статусом «новая».");
        return Page();
    }
}
