namespace QonaqHotels.Pages.Admin;

public class FilterBarModel
{
    public string FormId { get; set; } = "filter-form";
    public List<FilterField> Fields { get; set; } = [];
}

public class FilterField
{
    public string Type { get; set; } = "text";
    public string Name { get; set; } = "";
    public string Placeholder { get; set; } = "";

    public List<(string Value, string Label)> Options { get; set; } = [];

    // Каскадная фильтрация
    public string? DependsOn { get; set; }
    public string? OptionsHandler { get; set; }
    public string? OptionsParameter { get; set; }
}