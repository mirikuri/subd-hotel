using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.Services;

public class DeleteModel : DeleteRowModel
{
    public DeleteModel(Db db) : base(db) { }
    protected override string Table => "services";
    protected override string TitleSql => "SELECT s.name || ' (' || h.name || ')' FROM services s JOIN hotels h ON h.id = s.hotel_id WHERE s.id = @id";
    public override string What => "услугу";
}
