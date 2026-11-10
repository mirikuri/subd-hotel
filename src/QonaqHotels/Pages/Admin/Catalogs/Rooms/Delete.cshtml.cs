using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.Rooms;

public class DeleteModel : DeleteRowModel
{
    public DeleteModel(Db db) : base(db) { }
    protected override string Table => "rooms";
    protected override string TitleSql => "SELECT h.name || ', ' || r.room_number FROM rooms r JOIN hotels h ON h.id = r.hotel_id WHERE r.id = @id";
    public override string What => "номер";
}
