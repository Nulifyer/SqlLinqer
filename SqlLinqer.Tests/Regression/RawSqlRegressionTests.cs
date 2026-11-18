using System.Data;
using SqlLinqer;
using SqlLinqer.Exceptions;
using SqlLinqer.Queries.Raw;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class RawSqlRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var flavor in Enum.GetValues<DbFlavor>())
        {
            string prefix = flavor == DbFlavor.DuckDb ? "$" : "@";
            test($"raw sql: named values render bound placeholders for {flavor}", () =>
            {
                var rendered = new RawQuery("UPDATE jobs SET state = @state, previous = @state WHERE id = @id", new { state = "ready", id = 7 })
                    .Render(flavor);
                Check(rendered.Text == $"UPDATE jobs SET state = {prefix}state, previous = {prefix}state WHERE id = {prefix}id", rendered.Text);
                var parameters = rendered.Parameters.GetAll().ToList();
                Check(parameters.Count == 2, "A repeated marker bound more than one parameter");
                Check(parameters.Any(p => p.Placehodler == prefix + "state" && (string)p.Value == "ready")
                    && parameters.Any(p => p.Placehodler == prefix + "id" && (int)p.Value == 7), "Named values were not bound");
            });
            test($"raw sql: literals, identifiers, and comments are not markers for {flavor}", () =>
            {
                string sql = "SELECT 'a@b.c ''@x''', \"col@y\", @@version /* @z */ FROM t WHERE id = @id -- @w";
                var rendered = new RawQuery(sql).Value("id", 1).Render(flavor);
                Check(rendered.Text == sql.Replace("= @id", "= " + prefix + "id"), rendered.Text);
                Check(rendered.Parameters.Count == 1, "Text inside a literal or comment was bound");
            });
        }
        test("raw sql: dialect quoting is honored", () =>
        {
            string Text(string sql, DbFlavor flavor) => new RawQuery(sql).Value("id", 1).Render(flavor).Text;
            Check(Text("SELECT [a@b] FROM t WHERE id=@id", DbFlavor.SqlServer) == "SELECT [a@b] FROM t WHERE id=@id", "Bracket identifier was scanned");
            Check(Text("SELECT $q$ @x $q$, $$ @y $$, v::text FROM t WHERE tags @> ARRAY[@id]", DbFlavor.PostgreSql)
                == "SELECT $q$ @x $q$, $$ @y $$, v::text FROM t WHERE tags @> ARRAY[@id]", "Dollar quoting or an array subscript was misread");
            Check(Text("SELECT E'it\\'s @x' FROM t WHERE id=@id", DbFlavor.PostgreSql) == "SELECT E'it\\'s @x' FROM t WHERE id=@id", "Escape string was misread");
            Check(Text("SELECT 'it\\'s @x' FROM t WHERE id=@id # @y", DbFlavor.MySql) == "SELECT 'it\\'s @x' FROM t WHERE id=@id # @y", "MySQL escapes were misread");
            Check(Text("SELECT `a@b` FROM t WHERE id=@id", DbFlavor.MySql) == "SELECT `a@b` FROM t WHERE id=@id", "Backtick identifier was scanned");
        });
        test("raw sql: a marker without a value and a value without a marker are rejected", () =>
        {
            Throws<InvalidOperationException>(() => new RawQuery("SELECT @missing").Render(DbFlavor.PostgreSql), "@missing");
            Throws<InvalidOperationException>(() => new RawQuery("SELECT 1", new { unused = 1 }).Render(DbFlavor.PostgreSql), "unused");
            Throws<InvalidOperationException>(() => new RawQuery("SELECT @a").IgnoreUnmatchedMarkers().Value("b", 1).Render(DbFlavor.SqlServer), "'b'");
            var declared = new RawQuery("DECLARE @total int = @start; SELECT @total").IgnoreUnmatchedMarkers().Value("start", 5).Render(DbFlavor.SqlServer);
            Check(declared.Text == "DECLARE @total int = @start; SELECT @total" && declared.Parameters.Count == 1, declared.Text);
            Throws<ArgumentException>(() => new RawQuery("SELECT @a", 5), "properties");
            Throws<ArgumentException>(() => new RawQuery("SELECT 1").Value("not valid", 1), "not valid");
        });
        test("raw sql: values come from objects, dictionaries, and typed replacements", () =>
        {
            var rendered = new RawQuery("INSERT INTO users(username, email, age) VALUES (@Username, @email, @age)",
                    new { username = "ada", email = (string)null })
                .Values(new Dictionary<string, object> { ["age"] = 36 })
                .Value("email", null, DbType.String)
                .Render(DbFlavor.PostgreSql);
            var parameters = rendered.Parameters.GetAll().ToDictionary(p => p.Placehodler);
            Check(rendered.Text.Contains("(@username, @email, @age)"), rendered.Text);
            Check(parameters.Count == 3 && parameters["@email"].DbType == DbType.String && parameters["@email"].Value == DBNull.Value,
                "Typed null replacement was not applied");
            Check(parameters["@username"].DbType == null && (int)parameters["@age"].Value == 36, "Object or dictionary values changed");
        });
        foreach (bool asynchronous in new[] { false, true })
        {
            test($"raw sql: a supplied connection runs without a transaction and stays open, async={asynchronous}", () =>
            {
                var database = new FakeDatabase();
                var connector = new FakeConnector(database);
                using var connection = new FakeConnection(database);
                connection.Open();
                var query = connector.BeginSql("SELECT pg_advisory_lock(@key)", new { key = 5L });
                var affected = asynchronous ? query.ExecuteAsync(connection).GetAwaiter().GetResult() : query.Execute(connection);
                var scalar = asynchronous ? query.ExecuteScalarAsync<long>(connection).GetAwaiter().GetResult() : query.ExecuteScalar<long>(connection);
                Check(affected.State == ResponseState.Valid && scalar.State == ResponseState.Valid, (affected.Error ?? scalar.Error)?.ToString());
                Check(connector.Created == 0, "A pooled connection was created");
                Check(connection.Transactions.Count == 0 && database.Executed.All(c => c.UsedTransaction == null), "A transaction was opened on the supplied connection");
                Check(connection.State == ConnectionState.Open && !connection.IsDisposed, "The supplied connection was closed");
                Check(database.Executed.Count == 2 && database.Executed[0].Parameters[0].ParameterName == "@key", "The command did not run on the supplied connection");
            });
            test($"raw sql: owned transactions follow the terminal and WithoutTransaction, async={asynchronous}", () =>
            {
                int Transactions(Func<RawQuery, ISqlResponse> run, bool without)
                {
                    var database = new FakeDatabase
                    {
                        ReaderFactory = _ => { var rows = new DataTable(); rows.Columns.Add("Id", typeof(long)); rows.Rows.Add(1L); return rows.CreateDataReader(); },
                    };
                    var query = new FakeConnector(database).BeginSql("SELECT @id", new { id = 1 });
                    if (without) query.WithoutTransaction();
                    var response = run(query);
                    Check(response.State == ResponseState.Valid, response.Error?.ToString());
                    Check(database.Connections.All(c => c.IsDisposed), "A pooled connection stayed open");
                    Check(database.Connections.SelectMany(c => c.Transactions).All(t => t.Commits == 1), "An owned transaction was not committed");
                    return database.Connections.Sum(c => c.Transactions.Count);
                }
                Func<RawQuery, ISqlResponse> execute = q => asynchronous ? q.ExecuteAsync().GetAwaiter().GetResult() : q.Execute();
                Func<RawQuery, ISqlResponse> scalar = q => asynchronous ? q.ExecuteScalarAsync<long>().GetAwaiter().GetResult() : q.ExecuteScalar<long>();
                Func<RawQuery, ISqlResponse> list = q => asynchronous ? q.ExecuteListAsync<long>().GetAwaiter().GetResult() : q.ExecuteList<long>();
                Func<RawQuery, ISqlResponse> table = q => asynchronous ? q.ExecuteDataTableAsync().GetAwaiter().GetResult() : q.ExecuteDataTable();
                Check(Transactions(execute, false) == 1 && Transactions(scalar, false) == 1, "Execute or ExecuteScalar lost its owned transaction");
                Check(Transactions(execute, true) == 0 && Transactions(scalar, true) == 0, "WithoutTransaction still opened a transaction");
                Check(Transactions(list, false) == 0 && Transactions(table, false) == 0, "A raw read opened a transaction");
            });
        }
        test("raw sql: an unbound query executes with a supplied connector, like other builders", () =>
        {
            var database = new FakeDatabase();
            var connector = new FakeConnector(database);
            var query = new RawQuery("SELECT @id", new { id = 1 });
            Check(query.Execute(connector).State == ResponseState.Valid && query.ExecuteScalar<long>(connector).State == ResponseState.Valid,
                "The transaction form ignored the supplied connector");
            Check(query.ExecuteAsync(connector).GetAwaiter().GetResult().State == ResponseState.Valid, "The async form ignored the supplied connector");
            using var connection = new FakeConnection(database);
            connection.Open();
            Check(query.Execute(connector, connection).State == ResponseState.Valid
                && query.ExecuteScalarAsync<long>(connector, connection).GetAwaiter().GetResult().State == ResponseState.Valid,
                "The connection form ignored the supplied connector");
            Check(connector.Created == 3 && connection.Transactions.Count == 0, "A supplied connection was replaced or received a transaction");
            SqlLinqer.Queries.Interfaces.IQueryExecutable<int> executable = query;
            Check(executable.Execute(connector).State == ResponseState.Valid, "The standard executable contract changed");
        });
        test("raw sql: a closed supplied connection and a failed open return SqlResponseException", () =>
        {
            var database = new FakeDatabase();
            var connector = new FakeConnector(database);
            using var closed = new FakeConnection(database);
            var response = connector.BeginSql("SELECT 1").Execute(closed);
            Check(response.State == ResponseState.Error && response.Error is SqlResponseException { InnerException: InvalidOperationException },
                "A closed supplied connection was not reported through the response");
            Check(closed.State == ConnectionState.Closed && database.Executed.Count == 0, "A closed supplied connection was opened");
            database.FailAt = "open";
            var failed = connector.BeginSql("SELECT 1").ExecuteList<int>();
            Check(failed.State == ResponseState.Error && failed.Error is SqlResponseException, "An open failure escaped the response");
        });
        foreach (bool asynchronous in new[] { false, true })
            test($"multi-batch insert reports a failed open as SqlResponseException, async={asynchronous}", () =>
            {
                var database = new FakeDatabase { FailAt = "open" };
                var connector = new FakeConnector(database);
                connector.Options.ParameterLimit = 2;
                var insert = new InsertQuery<RawRow>().InsertAuto(new[] { new RawRow { Id = 1, Name = "a" }, new RawRow { Id = 2, Name = "b" } });
                var response = asynchronous ? insert.ExecuteAsync(connector).GetAwaiter().GetResult() : insert.Execute(connector);
                Check(response.State == ResponseState.Error && response.Error is SqlResponseException { Query: not null },
                    "A batch open failure was not wrapped: " + response.Error?.GetType());
            });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action run, string expected) where T : Exception
    {
        try { run(); }
        catch (T error) when (error.Message.Contains(expected)) { return; }
        throw new Exception($"Expected {typeof(T).Name} mentioning '{expected}'");
    }

    [SqlLinqer.Modeling.SqlTable("raw_rows")]
    private sealed class RawRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
