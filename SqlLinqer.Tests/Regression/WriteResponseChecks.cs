using SqlLinqer.Modeling;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

internal static class WriteResponseChecks
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (string operation in new[] { "insert", "update", "delete" })
            foreach (bool asynchronous in new[] { false, true })
                foreach (string failure in new[] { "execute", "dispose-connection" })
                    test($"{operation} key responses clear output after {failure} failure, async={asynchronous}", () =>
                    {
                        var database = new FakeDatabase { FailAt = failure };
                        var connector = new FakeConnector(database);
                        var insert = new InsertWithPrimaryKeyQuery<Row, int>().InsertAuto(new Row { Id = 7 });
                        var update = new UpdateWithPrimaryKeyQuery<Row, int>().Update(x => x.Id, 8);
                        var delete = new DeleteWithPrimaryKeyQuery<Row, int>();
                        var response = operation switch
                        {
                            "insert" => asynchronous ? insert.ExecuteAsync(connector).GetAwaiter().GetResult() : insert.Execute(connector),
                            "update" => asynchronous ? update.ExecuteAsync(connector).GetAwaiter().GetResult() : update.Execute(connector),
                            _ => asynchronous ? delete.ExecuteAsync(connector).GetAwaiter().GetResult() : delete.Execute(connector)
                        };
                        if (response.State != ResponseState.Error || response.Result != 0 || response.TouchedPrimaryKeys != null
                            || !response.Error.ToString().Contains(failure))
                            throw new Exception("Failed key response retained output or lost its cause");
                        if (!database.Connections.Single().IsDisposed || database.Connections.Single().Commands.Any(c => !c.IsDisposed))
                            throw new Exception("Failed write leaked owned resources");
                    });
    }

    [SqlTable("write_response_rows"), SqlAutoInsert]
    private sealed class Row { [SqlPrimaryKey] public int Id { get; set; } }
}
