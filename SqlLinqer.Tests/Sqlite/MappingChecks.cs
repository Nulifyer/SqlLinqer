using Microsoft.Data.Sqlite;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using static SqlLinqer.Tests.Sqlite.CheckAssertions;

namespace SqlLinqer.Tests.Sqlite;

internal static class MappingChecks
{
    internal static void Run(BaseConnector connector, SqliteConnection keeper, Item item, string label, Action<string, Action> Test)
    {
        var token = item.Token;
        var date = item.Created.Value;
        var offset = item.OffsetDate.Value;
        Test($"{label}: typed root JSON roundtrips provider values", () =>
        {
            var rows = Result(new SelectQuery<Item>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector));
            Expect(rows.Count == 3);
            var row = rows[0];
            Expect(row.Name == item.Name && row.Enabled && row.Status == State.Active);
            Expect(row.Token == token && row.Created == date && row.OffsetDate == offset);
            Expect(row.Amount == item.Amount && row.Bytes.SequenceEqual(item.Bytes));
            Expect(rows[1].Bytes.Length == 0 && rows[2].Bytes == null && rows[2].CategoryId == null);
        });
        Test($"{label}: direct render and connector render share parameter syntax", () =>
        {
            var query = new SelectQuery<Item>().Select(x => x.Name).Where(x => x.Name, item.Name, SqlOp.EQ);
            var direct = query.Render(DbFlavor.Sqlite);
            Expect(direct.Parameters.GetAll().Single().Placehodler == "@P1");
            Expect(Result(connector.ExecuteReaderModel<Item>(direct)).Single().Name == item.Name);
            Expect(Result(query.Execute(connector)).Single().Name == item.Name);
        });
        Test($"{label}: DateTime clock roundtrips but Kind is not stored", () =>
        {
            using var transaction = keeper.BeginTransaction();
            var utc = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            var inserted = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item { Created = utc }).Execute(connector, transaction);
            Result(inserted);
            var row = Result(new SelectQuery<Item>().Select(x => x.Created)
                .Where(x => x.Id, inserted.TouchedPrimaryKeys.Single(), SqlOp.EQ).Execute(connector, transaction)).Single();
            Expect(row.Created.Value.Ticks == utc.Ticks && row.Created.Value.Kind == DateTimeKind.Unspecified);
            transaction.Rollback();
        });
    }
}
