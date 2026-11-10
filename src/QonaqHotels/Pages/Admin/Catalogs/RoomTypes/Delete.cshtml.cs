using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.RoomTypes;

public class DeleteModel : DeleteRowModel
{
    public DeleteModel(Db db) : base(db) { }
    protected override string Table => "room_types";
    protected override string TitleSql => "SELECT name FROM room_types WHERE id = @id";
    public override string What => "тип номера";
}
