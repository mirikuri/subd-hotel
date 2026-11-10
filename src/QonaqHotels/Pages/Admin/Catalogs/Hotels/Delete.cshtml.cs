using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.Hotels;

public class DeleteModel : DeleteRowModel
{
    public DeleteModel(Db db) : base(db) { }
    protected override string Table => "hotels";
    protected override string TitleSql => "SELECT name FROM hotels WHERE id = @id";
    public override string What => "отель";
}
