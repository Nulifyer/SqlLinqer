using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Tests.Regression;
using static SqlLinqer.Tests.Sqlite.CheckAssertions;

namespace SqlLinqer.Tests.Sqlite;

internal static class RelationshipChecks
{
    internal static void Run(BaseConnector connector, Microsoft.Data.Sqlite.SqliteConnection connection, string label, Action<string, Action> test)
    {
        test($"{label}: bidirectional links use the requested schema and preserve conditions sync and async", () =>
        {
            using var transaction = connection.BeginTransaction();
            Result(connector.ExecuteNonQuery(new RenderedQuery("""
                CREATE TABLE main.relationship_people (Id INTEGER PRIMARY KEY, Name TEXT);
                CREATE TABLE main.relationship_links (from_id INTEGER, to_id INTEGER);
                INSERT INTO main.relationship_people VALUES (99, 'wrong schema');
                INSERT INTO main.relationship_links VALUES (1, 99);
                ATTACH DATABASE ':memory:' AS relationship_data;
                CREATE TABLE relationship_data.relationship_people (Id INTEGER PRIMARY KEY, Name TEXT);
                CREATE TABLE relationship_data.relationship_links (from_id INTEGER, to_id INTEGER);
                INSERT INTO relationship_data.relationship_people VALUES (1, 'allowed'), (2, 'allowed'), (3, 'blocked'), (4, 'allowed');
                INSERT INTO relationship_data.relationship_links VALUES (1, 2), (3, 1), (1, 1);
                """), transaction));
            foreach (bool asynchronous in new[] { false, true })
            {
                var query = new SelectQuery<RelationshipMappingChecks.Person>("relationship_data").SelectRootColumns()
                    .Select(x => x.Peers).Select(x => x.TypedPeers).Select(x => x.DirectedPeers).Select(x => x.FilteredPeers);
                var response = asynchronous ? query.ExecuteAsync(connector, transaction).GetAwaiter().GetResult() : query.Execute(connector, transaction);
                var rows = Result(response);
                var first = rows.Single(x => x.Id == 1);
                Expect(first.Peers.Select(x => x.Id).Order().SequenceEqual(new[] { 2, 3 }), "Bidirectional direct mapping lost a direction or included itself");
                Expect(first.TypedPeers.Select(x => x.Id).Order().SequenceEqual(new[] { 2, 3 }), "Typed linking members ignored column mappings or schema");
                Expect(first.DirectedPeers.Select(x => x.Id).Order().SequenceEqual(new[] { 1, 2 }), "Directed mapping changed its meaning");
                Expect(first.FilteredPeers.Select(x => x.Id).SequenceEqual(new[] { 2 }), "Additional conditions reached the wrong join");
                Expect(rows.Single(x => x.Id == 2).Peers.Single().Id == 1 && rows.Single(x => x.Id == 4).Peers.Count == 0,
                    "Reverse or empty relationships differ");
                Expect(transaction.Connection == connection, "Relationship execution completed the caller transaction");
            }
            transaction.Rollback();
        });
    }
}
