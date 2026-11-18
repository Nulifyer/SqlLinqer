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
        test("raw sql: markers after a colon, escape strings, comments, and non-ASCII names", () =>
        {
            string Text(string sql, DbFlavor flavor, object values) => new RawQuery(sql, values).Render(flavor).Text;
            Check(Text("SELECT (ARRAY[1,2,3])[1:@n]", DbFlavor.PostgreSql, new { n = 2 }) == "SELECT (ARRAY[1,2,3])[1:@n]", "A marker after a slice colon was missed");
            Check(Text("SELECT {'k':@v}", DbFlavor.DuckDb, new { v = 2 }) == "SELECT {'k':$v}", "A marker after a struct colon was missed");
            Check(Text("SELECT JSON_OBJECT('k':@v)", DbFlavor.SqlServer, new { v = 2 }) == "SELECT JSON_OBJECT('k':@v)", "A marker after a JSON colon was missed");
            Check(Text("SELECT x::text, @id", DbFlavor.PostgreSql, new { id = 1 }) == "SELECT x::text, @id", "A cast was misread");
            Check(Text(@"SELECT E'it\'s @x' || @id", DbFlavor.DuckDb, new { id = 1 }) == @"SELECT E'it\'s @x' || $id", "A DuckDB escape string was misread");
            // MySQL and SQLite end a block comment at the first close; the other databases nest them.
            foreach (var flavor in new[] { DbFlavor.MySql, DbFlavor.Sqlite })
                Check(Text("/* a /* b */ SELECT @id", flavor, new { id = 1 }) == "/* a /* b */ SELECT @id", $"A {flavor} comment was treated as nested");
            foreach (var flavor in new[] { DbFlavor.PostgreSql, DbFlavor.SqlServer, DbFlavor.AzureSql, DbFlavor.DuckDb })
                Check(new RawQuery("/* a /* @x */ @y */ SELECT @id", new { id = 1 }).Render(flavor).Parameters.Count == 1, $"A nested {flavor} comment was scanned");
            var named = new RawQuery("SELECT @größe").Value("größe", 2).Render(DbFlavor.SqlServer);
            Check(named.Text == "SELECT @größe" && named.Parameters.GetAll().Single().Placehodler == "@größe", "A non-ASCII marker name was cut short");
            Throws<InvalidOperationException>(() => new RawQuery("SELECT @größe", new { gr = 2 }).Render(DbFlavor.SqlServer), "größe");
        });
        test("raw sql: values come from any string-keyed dictionary", () =>
        {
            Check(new RawQuery("SELECT @a + @b", new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }).Render(DbFlavor.PostgreSql).Parameters.Count == 2, "A typed dictionary was rejected");
            Check(new RawQuery("SELECT @a", new System.Collections.Hashtable { ["a"] = "x" }).Render(DbFlavor.PostgreSql).Parameters.Count == 1, "A Hashtable was rejected");
            Throws<ArgumentException>(() => new RawQuery("SELECT @a", new Dictionary<int, int> { [1] = 1 }), "string");
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
            test($"raw sql: WithoutTransaction on a session connector runs on the held connection without a transaction, async={asynchronous}", () =>
            {
                var database = new FakeDatabase();
                var connector = new FakeConnector(database);
                using var connection = new FakeConnection(database);
                connection.Open();
                var query = connector.OnConnection(connection).BeginSql("SELECT pg_advisory_lock(@key)", new { key = 5L }).WithoutTransaction();
                var affected = asynchronous ? query.ExecuteAsync().GetAwaiter().GetResult() : query.Execute();
                var scalar = asynchronous ? query.ExecuteScalarAsync<long>().GetAwaiter().GetResult() : query.ExecuteScalar<long>();
                Check(affected.State == ResponseState.Valid && scalar.State == ResponseState.Valid, (affected.Error ?? scalar.Error)?.ToString());
                Check(connector.Created == 0, "A pooled connection was created");
                Check(connection.Transactions.Count == 0 && database.Executed.All(c => c.UsedTransaction == null), "A transaction was opened on the held connection");
                Check(connection.State == ConnectionState.Open && !connection.IsDisposed, "The held connection was closed");
                Check(database.Executed.Count == 2 && database.Executed[0].Parameters[0].ParameterName == "@key", "The command did not run on the held connection");
                // The opt-out can be turned off again.
                Check(query.WithoutTransaction(false).Execute().State == ResponseState.Valid && connection.Transactions.Count == 1, "Restoring the owned transaction had no effect");
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
        test("raw sql: a list value renders one parameter for each element", () =>
        {
            var rendered = new RawQuery("SELECT * FROM jobs WHERE id IN (@ids) AND state = @state OR parent IN (@IDS)", new { state = "queued" })
                .ValueList("ids", new[] { 3, 5, 8 }, DbType.Int32).Render(DbFlavor.PostgreSql);
            Check(rendered.Text == "SELECT * FROM jobs WHERE id IN (@ids1, @ids2, @ids3) AND state = @state OR parent IN (@ids1, @ids2, @ids3)", rendered.Text);
            var parameters = rendered.Parameters.GetAll().ToDictionary(p => p.Placehodler);
            Check(parameters.Count == 4 && (int)parameters["@ids2"].Value == 5 && parameters["@ids3"].DbType == DbType.Int32, "List elements were not bound in order");
            Check(new RawQuery("SELECT @ids").ValueList("ids", new List<string> { "a", "b" }).Render(DbFlavor.DuckDb).Text == "SELECT $ids1, $ids2", "DuckDB list placeholders changed");
            // The elements are copied, so later changes to the caller's list do not reach the command.
            var source = new List<int> { 1, 2 };
            var copied = new RawQuery("SELECT @n").ValueList("n", source);
            source.Add(3);
            Check(copied.Render(DbFlavor.Sqlite).Parameters.Count == 2, "A list value followed later changes to its source");
            // Value and ValueList replace each other under one name.
            Check(new RawQuery("SELECT @n").ValueList("n", new[] { 1, 2 }).Value("n", 7).Render(DbFlavor.MySql).Text == "SELECT @n", "Value did not replace a list");
            Check(new RawQuery("SELECT @n").Value("n", 7).ValueList("N", new[] { 1, 2 }).Render(DbFlavor.MySql).Text == "SELECT @N1, @N2", "A list did not replace a value");
            Throws<InvalidOperationException>(() => new RawQuery("SELECT @n").ValueList("n", Array.Empty<int>()).Render(DbFlavor.PostgreSql), "empty");
            Throws<InvalidOperationException>(() => new RawQuery("SELECT 1").ValueList("n", new[] { 1 }).Render(DbFlavor.PostgreSql), "'n'");
            Throws<ArgumentException>(() => new RawQuery("SELECT @n").ValueList("n", "text"), "one value");
            Throws<ArgumentException>(() => new RawQuery("SELECT @n").ValueList("n", new byte[] { 1 }), "one value");
            // A generated element name that another value already uses is rejected instead of binding the wrong value.
            Throws<InvalidOperationException>(() => new RawQuery("SELECT @n1, @n").Value("n1", 1).ValueList("n", new[] { 2 }).Render(DbFlavor.PostgreSql), "@n1");
            // A binary value passed through Values stays one parameter.
            Check(new RawQuery("SELECT @blob", new { blob = new byte[] { 1, 2 } }).Render(DbFlavor.PostgreSql).Parameters.Count == 1, "A byte array was expanded");
        });
        foreach (bool asynchronous in new[] { false, true })
        {
            test($"session connector: commands use the supplied connection and own their transactions, async={asynchronous}", () =>
            {
                var database = new FakeDatabase
                {
                    ReaderFactory = _ => { var rows = new DataTable(); rows.Columns.Add("Id", typeof(int)); rows.Columns.Add("Name", typeof(string)); rows.Rows.Add(1, "a"); return rows.CreateDataReader(); },
                };
                var connector = new FakeConnector(database);
                using var connection = new FakeConnection(database);
                connection.Open();
                var session = connector.OnConnection(connection);
                Check(ReferenceEquals(session.Source, connector) && ReferenceEquals(session.Connection, connection) && ReferenceEquals(session.Options, connector.Options)
                    && session.DbFlavor == connector.DbFlavor, "The session connector does not follow its source");
                // A session created from a session still follows the original source.
                Check(ReferenceEquals(session.OnConnection(connection).Source, connector), "A nested session lost its source");

                var write = session.BeginSql("UPDATE raw_rows SET Name = @name", new { name = "x" });
                Check((asynchronous ? write.ExecuteAsync().GetAwaiter().GetResult() : write.Execute()).State == ResponseState.Valid, "A session write failed");
                Check(connection.Transactions.Count == 1 && connection.Transactions[0].Commits == 1, "A session write did not own and commit a transaction on the connection");
                var read = session.BeginSelect<RawRow>().SelectRootColumns();
                Check((asynchronous ? read.ExecuteAsync().GetAwaiter().GetResult() : read.Execute()).GetResultOrThrow().Single().Name == "a", "A session read failed");
                Check(connection.Transactions.Count == 1, "A session read opened a transaction");

                // A transaction passed to a terminal is used as given.
                using (var transaction = connection.BeginTransaction())
                {
                    Check((asynchronous ? write.ExecuteAsync(transaction).GetAwaiter().GetResult() : write.Execute(transaction)).State == ResponseState.Valid, "A caller transaction failed");
                    Check(connection.Transactions.Count == 2 && connection.Transactions[1].Commits == 0, "A caller transaction was replaced or committed");
                }

                Check(database.Connections.Count == 1 && connector.Created == 0, "A pooled connection was created");
                Check(connection.State == ConnectionState.Open && !connection.IsDisposed, "The session connection was closed");
                Check(connector.BeginSql("SELECT 1").ExecuteScalar<long>().State == ResponseState.Valid && connector.Created == 1,
                    "The source connector stopped using pooled connections");

                database.FailSql = "UPDATE raw_rows";
                var failed = asynchronous ? write.ExecuteAsync().GetAwaiter().GetResult() : write.Execute();
                Check(failed.State == ResponseState.Error && connection.Transactions.Last().Rollbacks == 1 && connection.State == ConnectionState.Open,
                    "A failed session write was not rolled back, or closed the connection");
                database.FailSql = null;

                connection.Close();
                var closed = asynchronous ? read.ExecuteAsync().GetAwaiter().GetResult() : read.Execute();
                Check(closed.State == ResponseState.Error && closed.Error is SqlResponseException { InnerException: InvalidOperationException },
                    "A closed session connection was not reported through the response");
            });
            test($"session connector: a batched insert shares one transaction on the connection, async={asynchronous}", () =>
            {
                var database = new FakeDatabase();
                var connector = new FakeConnector(database);
                connector.Options.ParameterLimit = 2;
                using var connection = new FakeConnection(database);
                connection.Open();
                var insert = new InsertQuery<RawRow>().InsertAuto(new[] { new RawRow { Id = 1, Name = "a" }, new RawRow { Id = 2, Name = "b" } });
                var session = connector.OnConnection(connection);
                var response = asynchronous ? insert.ExecuteAsync(session).GetAwaiter().GetResult() : insert.Execute(session);
                Check(response.State == ResponseState.Valid, response.Error?.ToString());
                Check(database.Executed.Count == 2 && connection.Transactions.Count == 1 && connection.Transactions[0].Commits == 1,
                    "The batches did not share one committed transaction on the session connection");
                Check(database.Connections.Count == 1 && !connection.IsDisposed && connection.State == ConnectionState.Open, "The session connection was replaced or closed");
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
            Check(connector.Created == 3, "A supplied connector was not used for every command");
            // Each terminal family is declared on an interface, as for the typed builders.
            SqlLinqer.Queries.Interfaces.IQueryExecutable<int> executable = query;
            SqlLinqer.Queries.Interfaces.IQueryExecutableScalar scalar = query;
            SqlLinqer.Queries.Interfaces.IAsyncQueryExecutableRows rows = query;
            SqlLinqer.Queries.Interfaces.IAsyncQueryExecutableRowStream stream = query;
            Check(executable.Execute(connector).State == ResponseState.Valid && scalar.ExecuteScalar<long>(connector).State == ResponseState.Valid
                && rows != null && stream != null, "A terminal contract changed");
            // An asynchronous terminal reports a cancelled token before it validates or renders.
            var cancelled = new CancellationToken(true);
            try { new RawQuery("SELECT @missing").ExecuteListAsync<int>(connector, cancellationToken: cancelled).GetAwaiter().GetResult(); throw new Exception("A cancelled token was ignored"); }
            catch (OperationCanceledException) { }
        });
        test("writes: every write owns a transaction, whichever terminal runs it", () =>
        {
            foreach (DbFlavor flavor in new[] { DbFlavor.PostgreSql, DbFlavor.SqlServer, DbFlavor.Sqlite })
            {
                var database = new FakeDatabase
                {
                    ReaderFactory = _ => { var rows = new DataTable(); rows.Columns.Add("Id", typeof(int)); rows.Rows.Add(1); return rows.CreateDataReader(); },
                };
                var connector = new FakeConnector(database, flavor);
                var update = new UpdateQuery<RawRow>().Update(x => x.Name, "n").Where(x => x.Id, 1, SqlOp.EQ).Output(x => x.Id);
                using (var table = update.ExecuteWithOutput(connector).GetResultOrThrow()) Check(table.Rows.Count == 1, "The output table changed");
                Check(update.ExecuteWithOutput<int>(connector).GetResultOrThrow().Single() == 1, "The typed output changed");
                Check(update.Execute(connector).State == ResponseState.Valid, "The plain write failed");
                var transactions = database.Connections.SelectMany(c => c.Transactions).ToList();
                Check(transactions.Count == 3 && transactions.All(t => t.Commits == 1), $"A write ran without an owned transaction on {flavor}");
                // A read never owns one.
                Check(new SelectQuery<RawRow>().SelectRootColumns().Execute(connector).State == ResponseState.Valid
                    && database.Connections.SelectMany(c => c.Transactions).Count() == 3, $"A read opened a transaction on {flavor}");
            }
        });
        test("session connector: commands run through the source connector's hooks", () =>
        {
            var database = new FakeDatabase();
            var connector = new HookedConnector(database);
            using var connection = new FakeConnection(database);
            connection.Open();
            Check(connector.OnConnection(connection).BeginSql("SELECT 1").ExecuteScalar<long>().State == ResponseState.Valid && connector.Commands == 1,
                "A session command bypassed the source connector's command hook");
        });
        test("raw sql: a closed session connection and a failed open return SqlResponseException", () =>
        {
            var database = new FakeDatabase();
            var connector = new FakeConnector(database);
            using var closed = new FakeConnection(database);
            var response = connector.OnConnection(closed).BeginSql("SELECT 1").Execute();
            Check(response.State == ResponseState.Error && response.Error is SqlResponseException { InnerException: InvalidOperationException },
                "A closed session connection was not reported through the response");
            Check(closed.State == ConnectionState.Closed && database.Executed.Count == 0, "A closed session connection was opened");
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

    private sealed class HookedConnector(FakeDatabase database) : SqlLinqer.Connections.BaseConnector(DbFlavor.PostgreSql, new SqlLinqer.Connections.ConnectorOptions())
    {
        public int Commands;
        public override System.Data.Common.DbConnection CreateConnection() => new FakeConnection(database);
        protected override SqlResponse<T> ExecuteCommand<T>(SqlLinqer.Components.Render.RenderedQuery query, Func<SqlResponse<T>> action)
        {
            Commands++;
            return base.ExecuteCommand(query, action);
        }
    }

    [SqlLinqer.Modeling.SqlTable("raw_rows")]
    private sealed class RawRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
