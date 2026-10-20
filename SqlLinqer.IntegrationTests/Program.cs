using System.Data;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Exceptions;
using SqlLinqer.IntegrationTests;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

var targets = new[]
{
    (DbFlavor.PostgreSql, "SQLLINQER_TEST_POSTGRES"),
    (DbFlavor.MySql, "SQLLINQER_TEST_MYSQL"),
};
if (targets.Any(target => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(target.Item2))))
{
    Console.Error.WriteLine("Run TestDBs/test-containers.sh to create the disposable PostgreSQL and MySQL databases.");
    return 2;
}
int passed = 0, failed = 0;
void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
T Valid<T>(SqlResponse<T> response)
{
    if (response.State != ResponseState.Valid)
    {
        string query = (response.Error as SqlResponseException)?.Query.Text;
        throw new Exception(response.Error?.Message + (query == null ? "" : "\nSQL: " + query), response.Error);
    }
    return response.Result;
}
void ThrowsUnsupported(Action action)
{
    try { action(); }
    catch (NotSupportedException) { return; }
    throw new Exception("Expected an unsupported operation to fail before writing.");
}

foreach (var (flavor, variable) in targets)
{
    var database = new DatabaseFixture(flavor, Environment.GetEnvironmentVariable(variable));
    Console.WriteLine($"Waiting for {flavor}...");
    try { database.WaitUntilReady(); }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    var connector = database.Connector;
    SqlLinqer.Default.Connector = connector;
    void Test(string name, Action run)
    {
        try
        {
            database.Reset();
            run();
            passed++;
            Console.WriteLine($"PASS {flavor}: {name}");
        }
        catch (Exception error)
        {
            failed++;
            Console.Error.WriteLine($"FAIL {flavor}: {name}\n{error}");
        }
    }
    IntegrationItem Read(int id) => Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.Id, id, SqlOp.EQ).Execute(connector)).Single();
    IntegrationItem NewItem(string name) => new(name, true, 1, DatabaseFixture.SampleDate.AddTicks(1234560));

    Test("native JSON loads fields, dates, enums and private model members", () =>
    {
        var item = Read(1);
        Expect(item.Id == 1 && item.Name == "Item 1" && item.Enabled && item.LoadedViaPrivateConstructor, "Model members were not loaded.");
        Expect(item.HappenedAt == DatabaseFixture.SampleDate.AddTicks(1234560) && item.State == IntegrationState.Active, "Date or string enum conversion failed.");
        Expect(Read(2).HappenedAt == null && !Read(2).Enabled, "Nullable date or false boolean was lost.");
    });
    Test("empty selections return an empty list", () =>
        Expect(Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.Id, -1, SqlOp.EQ).Execute(connector)).Count == 0, "Expected no rows."));
    Test("query values remain parameters", () =>
    {
        var rows = Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.Name, "'; DROP TABLE it_items; --", SqlOp.EQ).Execute(connector));
        Expect(rows.Count == 0 && database.Count() == 22, "Parameter changed query behavior.");
    });
    Test("boolean and NULL filters", () =>
    {
        Expect(Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.Enabled, true, SqlOp.EQ).Execute(connector)).Count == 11, "Boolean filter returned wrong rows.");
        var rows = Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.CategoryId, SqlNull.NULL, SqlOp.EQ).Execute(connector));
        Expect(rows.Single().Id == 3, "NULL filter returned wrong rows.");
    });
    Test("one-to-one relationship JSON", () =>
    {
        var rows = Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Select(x => x.Category).Where(x => x.Id, 1, SqlOp.EQ).Execute(connector));
        Expect(rows.Single().Category.Name == "engineering", "Nested category did not load.");
    });
    Test("one-to-many relationship JSON", () =>
    {
        var rows = Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Select(x => x.Children).Where(x => x.Id, 1, SqlOp.EQ).Execute(connector));
        Expect(rows.Single().Children.Select(x => x.Id).Order().SequenceEqual(new[] { 1, 2 }), "Nested children did not load.");
    });
    Test("one-to-many relationship filter", () =>
    {
        var rows = Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.Children.First().Name, "child one", SqlOp.EQ).Execute(connector));
        Expect(rows.Single().Id == 1, "Relationship filter returned wrong rows.");
    });
    Test("last-page totals and query reuse", () =>
    {
        var query = new SelectQuery<IntegrationItem>().SelectRootColumns().Page(3, 10);
        var response = query.ExecuteWithTotalCount(connector);
        Expect(Valid(response).Count == 2 && response.TotalResults == 22, "Last-page total was incorrect.");
        var repeated = query.ExecuteWithTotalCount(connector);
        Expect(Valid(repeated).Count == 2 && repeated.TotalResults == 22, "Count rendering changed the query.");
    });
    Test("empty-page totals", () =>
    {
        var response = new SelectQuery<IntegrationItem>().SelectRootColumns().Page(4, 10).ExecuteWithTotalCount(connector);
        Expect(Valid(response).Count == 0 && response.TotalResults == 22, "Empty page lost total count.");
    });
    Test("Top preserves the full count", () =>
    {
        var response = new SelectQuery<IntegrationItem>().SelectRootColumns().Top(3).ExecuteWithTotalCount(connector);
        Expect(Valid(response).Count == 3 && response.TotalResults == 22, "Top or its count was incorrect.");
    });
    Test("anonymous aggregate projections", () =>
    {
        var query = IntegrationItem.BeginSelectBuilder().BuildQuery(x => new { Id = x.Select(y => y.Id), Name = x.Select(y => y.Name) }).Top(3);
        Expect(Valid(query.Execute(connector)).Count == 3, "Anonymous results were not loaded.");
    });
    Test("plain insert commits and preserves values", () =>
    {
        string name = "inserted 'quoted' = value";
        Expect(Valid(new InsertQuery<IntegrationItem>().InsertAuto(NewItem(name)).Execute(connector)) == 1, "Insert count was incorrect.");
        var rows = Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.Name, name, SqlOp.EQ).Execute(connector));
        Expect(rows.Single().Enabled && rows.Single().HappenedAt == DatabaseFixture.SampleDate.AddTicks(1234560) && database.Count() == 23, "Inserted data changed.");
    });
    Test("plain update commits", () =>
    {
        Expect(Valid(new UpdateQuery<IntegrationItem>().Update(x => x.Name, "updated").Where(x => x.Id, 1, SqlOp.EQ).Execute(connector)) == 1, "Update count was incorrect.");
        Expect(database.ReadName(1) == "updated", "Update did not commit.");
    });
    Test("insert preserves a null date", () =>
    {
        Valid(new InsertQuery<IntegrationItem>().InsertAuto(new IntegrationItem("null date", false, null, null)).Execute(connector));
        var item = Read(23);
        Expect(item.HappenedAt == null && item.CategoryId == null && !item.Enabled, "Nullable insert values changed.");
    });
    Test("arithmetic update", () =>
    {
        Valid(new UpdateQuery<IntegrationItem>().Update(x => x.CategoryId, new BasicMathOperation(BasicMathOperation.MathOp.Add, 1)).Where(x => x.Id, 1, SqlOp.EQ).Execute(connector));
        Expect(Read(1).CategoryId == 2, "Arithmetic update failed.");
    });
    Test("update from a joined column", () =>
    {
        Valid(new UpdateQuery<IntegrationItem>().Update(x => x.Name, x => x.Category.Name).Where(x => x.Id, 1, SqlOp.EQ).Execute(connector));
        Expect(database.ReadName(1) == "engineering", "Joined value was not assigned.");
    });
    Test("update using a joined filter", () =>
    {
        Expect(Valid(new UpdateQuery<IntegrationItem>().Update(x => x.Name, "joined").Where(x => x.Category.Name, "engineering", SqlOp.EQ).Execute(connector)) == 10, "Joined update matched wrong rows.");
        Expect(database.ReadName(1) == "joined" && database.ReadName(2) == "Item 2" && database.ReadName(3) == "Item 3", "Joined update touched unrelated rows.");
    });
    Test("plain delete commits", () =>
    {
        Expect(Valid(new DeleteQuery<IntegrationItem>().Where(x => x.Id, 1, SqlOp.EQ).Execute(connector)) == 1 && database.Count() == 21, "Delete did not commit.");
    });
    Test("delete using a joined filter", () =>
    {
        Expect(Valid(new DeleteQuery<IntegrationItem>().Where(x => x.Category.Name, "engineering", SqlOp.EQ).Execute(connector)) == 10, "Joined delete count was incorrect.");
        Expect(database.Count() == 12 && database.ReadName(2) == "Item 2" && database.ReadName(3) == "Item 3", "Joined delete touched unrelated rows.");
    });
    Test("empty IN never deletes rows", () =>
    {
        Expect(Valid(new DeleteQuery<IntegrationItem>().Where(x => x.Id, Array.Empty<int>(), SqlArrayOp.IN).Execute(connector)) == 0 && database.Count() == 22, "Empty IN deleted rows.");
    });
    Test("empty NOT IN includes all rows", () =>
        Expect(Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.Id, Array.Empty<int>(), SqlArrayOp.NOTIN).Execute(connector)).Count == 22, "Empty NOT IN excluded rows."));
    Test("caller transaction can roll back an insert", () =>
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        Valid(new InsertQuery<IntegrationItem>().InsertAuto(NewItem("transactional")).Execute(transaction));
        var rows = Valid(new SelectQuery<IntegrationItem>().SelectRootColumns().Where(x => x.Name, "transactional", SqlOp.EQ).Execute(transaction));
        Expect(rows.Count == 1 && database.Count() == 22, "Insert escaped its transaction or select ignored the transaction.");
        transaction.Rollback();
        Expect(database.Count() == 22, "Rolled-back insert persisted.");
    });
    Test("caller transaction can roll back a delete", () =>
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        Valid(new DeleteQuery<IntegrationItem>().Where(x => x.Id, 1, SqlOp.EQ).Execute(transaction));
        Expect(Valid(connector.ExecuteScalar<long>(new RenderedQuery($"SELECT COUNT(*) FROM {database.Q("it_items")}"), transaction)) == 21, "Delete did not use caller transaction.");
        Expect(database.Count() == 22, "Delete committed independently.");
        transaction.Rollback();
        Expect(database.Count() == 22, "Rolled-back delete persisted.");
    });
    Test("caller retains failed transaction and connection", () =>
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var response = connector.ExecuteNonQuery(new RenderedQuery("SELECT definitely_missing_column FROM it_items"), transaction);
        Expect(response.State == ResponseState.Error && connection.State == ConnectionState.Open, "Query failure disposed caller connection.");
        transaction.Rollback();
        Expect(database.Count() == 22, "Rollback failed after query error.");
    });
    Test("failed owned write rolls back and later queries work", () =>
    {
        var response = new UpdateQuery<IntegrationItem>().Update(x => x.Name, (string)null).Where(x => x.Id, 1, SqlOp.EQ).Execute(connector);
        Expect(response.State == ResponseState.Error, "NOT NULL constraint should reject update.");
        Expect(database.Count() == 22 && database.ReadName(1) == "Item 1", "Failed update changed data or leaked connection state.");
    });
    if (flavor == DbFlavor.PostgreSql)
    {
        Test("insert preserves a local timestamp", () =>
        {
            var date = DateTime.SpecifyKind(DatabaseFixture.SampleDate, DateTimeKind.Local);
            Valid(new InsertQuery<IntegrationItem>().InsertAuto(new IntegrationItem("local date", true, 1, date)).Execute(connector));
            Expect(Read(23).HappenedAt == date, "Local timestamp changed its wall-clock value.");
        });
        Test("insert preserves a UTC timestamp with time zone", () =>
        {
            database.Execute("ALTER TABLE it_items ALTER COLUMN happened_at TYPE TIMESTAMPTZ(6)");
            var date = DateTime.SpecifyKind(DatabaseFixture.SampleDate, DateTimeKind.Utc);
            Valid(new InsertQuery<IntegrationItem>().InsertAuto(new IntegrationItem("UTC date", true, 1, date)).Execute(connector));
            var actual = Read(23).HappenedAt;
            Expect(actual.HasValue && actual.Value.Kind != DateTimeKind.Unspecified && actual.Value.ToUniversalTime() == date,
                $"UTC timestamp changed its instant: {actual:O} ({actual?.Kind}).");
        });
        Test("insert returns generated primary keys", () =>
        {
            var response = new InsertWithPrimaryKeyQuery<IntegrationItem, int>().InsertAuto(NewItem("returned")).Execute(connector);
            Expect(Valid(response) == 1 && response.TouchedPrimaryKeys.Single() == 23 && database.Count() == 23, "Inserted keys were not captured.");
        });
        Test("update returns affected primary keys", () =>
        {
            var response = new UpdateWithPrimaryKeyQuery<IntegrationItem, int>().Update(x => x.Name, "returned").Where(x => x.Id, 1, SqlOp.EQ).Execute(connector);
            Expect(Valid(response) == 1 && response.TouchedPrimaryKeys.Single() == 1, "Updated keys were not captured.");
        });
        Test("delete returns affected primary keys", () =>
        {
            var response = new DeleteWithPrimaryKeyQuery<IntegrationItem, int>().Where(x => x.Id, 1, SqlOp.EQ).Execute(connector);
            Expect(Valid(response) == 1 && response.TouchedPrimaryKeys.Single() == 1 && database.Count() == 21, "Deleted keys were not captured.");
        });
    }
    else
    {
        Test("unsupported returned-row requests perform no writes", () =>
        {
            ThrowsUnsupported(() => new InsertWithPrimaryKeyQuery<IntegrationItem, int>().InsertAuto(NewItem("unsupported")).Execute(connector));
            ThrowsUnsupported(() => new UpdateWithPrimaryKeyQuery<IntegrationItem, int>().Update(x => x.Name, "unsupported").Where(x => x.Id, 1, SqlOp.EQ).Execute(connector));
            ThrowsUnsupported(() => new DeleteWithPrimaryKeyQuery<IntegrationItem, int>().Where(x => x.Id, 1, SqlOp.EQ).Execute(connector));
            Expect(database.Count() == 22 && database.ReadName(1) == "Item 1", "Unsupported request performed a write.");
        });
    }
}
SqlLinqer.Default.Connector = null;
Console.WriteLine($"{passed} integration checks passed, {failed} failed.");
return failed == 0 ? 0 : 1;
