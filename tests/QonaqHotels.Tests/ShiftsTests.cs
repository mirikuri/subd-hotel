using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin.Shifts;

namespace QonaqHotels.Tests;

// График смен: добавление, запрет пересечения (EXCLUDE shifts_no_overlap), итог недели
public class ShiftsTests
{
    private readonly Db _db = TestDb.Create();
    // далёкий понедельник, где в seed смен нет
    private static readonly DateTime Mon = new(2031, 3, 3, 8, 0, 0);

    private IndexModel Page(int hotelId) => new(_db)
    {
        Hotel = hotelId, Week = DateOnly.FromDateTime(Mon),
        PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
    };

    private async Task<T> ScalarAsync<T>(string sql, params (string, object)[] ps)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task AddShift_OverlapRejected_TotalsCounted()
    {
        // сотрудник ресепшена из seed со ставкой
        var hotelId = await ScalarAsync<int>("SELECT min(hotel_id) FROM employees");
        var empId = await ScalarAsync<int>("""
            SELECT e.id FROM employees e JOIN positions p ON p.id = e.position_id
            WHERE e.hotel_id = @h AND e.dismissal_date IS NULL AND p.hourly_rate IS NOT NULL
            ORDER BY e.id LIMIT 1
            """, ("h", hotelId));
        var rate = await ScalarAsync<decimal>(
            "SELECT p.hourly_rate FROM employees e JOIN positions p ON p.id = e.position_id WHERE e.id = @e", ("e", empId));
        try
        {
            var page = Page(hotelId);
            await page.OnPostAddAsync(empId, Mon, Mon.AddHours(12));
            Assert.Null(page.Error);

            page = Page(hotelId);
            await page.OnPostAddAsync(empId, Mon.AddHours(6), Mon.AddHours(14));      // пересекается
            Assert.Equal("Смена пересекается с другой сменой сотрудника.", page.Error);

            page = Page(hotelId);
            await page.OnPostAddAsync(empId, Mon.AddDays(1), Mon.AddDays(2).AddHours(1)); // 25 часов
            Assert.Equal("Смена не может быть длиннее 24 часов.", page.Error);

            page = Page(hotelId);
            await page.OnGetAsync();
            var row = Assert.Single(page.Rows);
            Assert.Equal(12m, row.Hours);
            Assert.Equal(12m * rate, row.Pay);
            Assert.Equal(12m * rate, Assert.Single(page.Totals).Pay);

            await Page(hotelId).OnPostDeleteAsync(row.Id);
            page = Page(hotelId);
            await page.OnGetAsync();
            Assert.Empty(page.Rows);
        }
        finally
        {
            await ScalarAsync<int>("WITH d AS (DELETE FROM shifts WHERE shift_start >= '2031-01-01') SELECT 1");
        }
    }

    [Fact]
    public async Task AddShift_EmployeeFromAnotherHotel_Refused()
    {
        var hotelId = await ScalarAsync<int>("SELECT min(hotel_id) FROM employees");
        var foreign = await ScalarAsync<int>("SELECT min(id) FROM employees WHERE hotel_id <> @h", ("h", hotelId));

        var page = Page(hotelId);
        await page.OnPostAddAsync(foreign, Mon.AddDays(3), Mon.AddDays(3).AddHours(8));
        Assert.NotNull(page.Error);
    }
}
