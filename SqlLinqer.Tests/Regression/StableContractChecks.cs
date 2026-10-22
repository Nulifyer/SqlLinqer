using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

// Compile this file unchanged against v2.1.0 and the working library with
// TestDBs/test-stable-contracts.sh. Keep fixtures on their shared public API.
internal static class StableContractChecks
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (DbFlavor flavor in new[] { DbFlavor.SqlServer, DbFlavor.PostgreSql, DbFlavor.MySql })
        {
            test($"2.1 contract: numeric and boolean string properties, {flavor}", () =>
            {
                var row = Read<TextRow>("""{"Number":9223372036854775807,"Fraction":1.25,"Enabled":true,"Optional":null}""", flavor);
                if (!(row.Number == "9223372036854775807" && row.Fraction == "1.25" && row.Enabled == "true" && row.Optional == null))
                    throw new Exception($"Scalar text differs: number={row.Number}, fraction={row.Fraction}, boolean={row.Enabled}, null={row.Optional == null}");
            });
            test($"2.1 contract: fields, private setters, getter-only and inherited members, {flavor}", () =>
            {
                var row = Read<MemberRow>("""{"Id":7,"Field":8,"Label":"loaded","Inherited":"base"}""", flavor);
                Check(row.Id == 7 && row.Field == 8 && row.Label == "loaded" && row.Inherited == "base" && row.Constructed);
            });
            test($"2.1 contract: constructor arguments and remaining writable members, {flavor}", () =>
            {
                var row = Read<ConstructorRow>("""{"id":9,"Name":"nine","Extra":"loaded"}""", flavor);
                Check(row.Id == 9 && row.Name == "nine" && row.Extra == "loaded");
            });
            test($"2.1 contract: missing members, nulls and unknown nested data, {flavor}", () =>
            {
                var row = Read<OptionalRow>("""{"Optional":null,"Label":null,"Unknown":{"Nested":[1,2]}}""", flavor);
                Check(row.Id == 17 && row.Optional == null && row.Label == null && row.Values.SequenceEqual(new[] { 4 }));
            });
            test($"2.1 contract: quoted numbers, enum names and numeric booleans, {flavor}", () =>
            {
                var row = Read<ValueRow>("""{"Id":"42","State":"Ready","Enabled":1,"Amount":"12.50"}""", flavor);
                Check(row.Id == 42 && row.State == State.Ready && row.Enabled && row.Amount == 12.50m);
                row = Read<ValueRow>("""{"State":1,"Enabled":false}""", flavor);
                Check(row.State == State.Ready && !row.Enabled);
            });
            test($"2.1 contract: dates, offsets, GUIDs and binary values, {flavor}", () =>
            {
                var row = Read<SpecialRow>("""{"When":"2025-06-15T13:14:15Z","Offset":"2025-06-15T13:14:15+02:00","Key":"167aa39f-73f3-4516-a78b-bcedf95ee59e","Bytes":"AAEC/w=="}""", flavor);
                Check(row.When.Kind == DateTimeKind.Utc && row.When.Hour == 13 && row.Offset.Offset == TimeSpan.FromHours(2)
                    && row.Key == Guid.Parse("167aa39f-73f3-4516-a78b-bcedf95ee59e") && row.Bytes.SequenceEqual(new byte[] { 0, 1, 2, 255 }));
            });
            test($"2.1 contract: nested relationship objects, collections and nulls, {flavor}", () =>
            {
                var row = Read<ParentRow>("""{"Id":1,"Child":{"Id":2},"Children":[{"Id":3},{"Id":4}],"Absent":null}""", flavor);
                Check(row.Child.Id == 2 && row.Children.Select(x => x.Id).SequenceEqual(new[] { 3, 4 }) && row.Absent == null);
            });
            test($"2.1 contract: invalid structured strings and invalid numbers fail, {flavor}", () =>
            {
                Check(TryRead<TextRow>("""{"Number":{"Id":1}}""", flavor).State == ResponseState.Error);
                Check(TryRead<ValueRow>("""{"Id":"bad"}""", flavor).State == ResponseState.Error);
            });
            test($"2.1 contract: BuildQuery aliases, grouped fields and typed loading, {flavor}", () =>
            {
                const string json = """{"Label":"ready","Count":2}""";
                var database = new FakeDatabase { JsonRows = flavor == DbFlavor.SqlServer ? new[] { "[", json, "]" } : new[] { json } };
                var query = new SelectQueryInitializer<QueryRow>().BuildQuery(q => new
                {
                    Label = q.Select(x => x.Name),
                    Count = q.Count()
                }).Where(x => x.Name, "ready", SqlOp.EQ).OrderBy(x => x.Label, SqlDir.ASC).Page(1, 1);
                var response = query.Execute(new FakeConnector(database, flavor));
                Check(response.State == ResponseState.Valid && response.Result.Single().Label == "ready" && response.Result.Single().Count == 2);
                string sql = database.Executed.Single().CommandText;
                Check(sql.Contains("GROUP BY") && sql.Contains("COUNT(") && sql.Contains("Label"));
            });
        }
    }

    private static T Read<T>(string json, DbFlavor flavor) where T : class
    {
        var result = TryRead<T>(json, flavor);
        return result.State == ResponseState.Valid ? result.Result.Single() : throw result.Error;
    }
    private static SqlResponse<List<T>> TryRead<T>(string json, DbFlavor flavor) where T : class
    {
        var database = new FakeDatabase { JsonRows = flavor == DbFlavor.SqlServer ? new[] { "[", json, "]" } : new[] { json } };
        return new FakeConnector(database, flavor).ExecuteReaderJson<T>(new("fixture"));
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Stable SQL JSON contract differs"); }

    private sealed class TextRow { public string Number { get; set; } public string Fraction { get; set; } public string Enabled { get; set; } public string Optional { get; set; } }
    private class MemberBase { public string Inherited { get; private set; } }
    private sealed class MemberRow : MemberBase
    {
        private MemberRow() { Constructed = true; }
        public bool Constructed { get; }
        public int Id { get; private set; }
        public int Field = 0;
        public string Label { get; }
    }
    private sealed class ConstructorRow(int id, string name)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public string Extra { get; set; }
    }
    private sealed class OptionalRow
    {
        public int Id { get; set; } = 17;
        public int? Optional { get; set; }
        public string Label { get; set; } = "default";
        public List<int> Values { get; set; } = new() { 4 };
    }
    private enum State { Empty, Ready }
    private sealed class ValueRow { public int Id { get; set; } public State State { get; set; } public bool Enabled { get; set; } public decimal Amount { get; set; } }
    private sealed class SpecialRow { public DateTime When { get; set; } public DateTimeOffset Offset { get; set; } public Guid Key { get; set; } public byte[] Bytes { get; set; } }
    private sealed class ChildRow { [SqlPrimaryKey] public int Id { get; set; } public int ParentId { get; set; } }
    private sealed class QueryRow { [SqlPrimaryKey] public int Id { get; set; } public string Name { get; set; } }
    private sealed class ParentRow
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int? ChildId { get; set; }
        [SqlOneToOne(nameof(ChildId))] public ChildRow Child { get; set; }
        [SqlOneToOne(nameof(ChildId))] public ChildRow Absent { get; set; }
        [SqlOneToMany(nameof(ChildRow.ParentId))] public List<ChildRow> Children { get; set; }
    }
}
