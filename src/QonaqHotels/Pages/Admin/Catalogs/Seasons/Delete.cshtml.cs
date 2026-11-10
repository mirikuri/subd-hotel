using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.Seasons;

// Сезон удаляется вместе со своими тарифами (кроме rates, на сезон ничего не ссылается)
public class DeleteModel : DeleteRowModel
{
    public DeleteModel(Db db) : base(db) { }
    protected override string Table => "seasons";
    protected override string TitleSql => "SELECT name FROM seasons WHERE id = @id";
    public override string What => "сезон";

    protected override Task BeforeDeleteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int id) =>
        CatalogDb.ExecAsync(conn, "DELETE FROM rates WHERE season_id = @id", tx, ("id", id));
}
