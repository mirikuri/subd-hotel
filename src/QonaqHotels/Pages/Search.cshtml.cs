using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QonaqHotels.Pages;

public class SearchModel : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To   { get; set; }

    public string? DateError { get; private set; }

    public void OnGet()
    {
        if (From.HasValue && To.HasValue && To <= From)
            DateError = "Дата выезда должна быть позже даты заезда.";
    }
}
