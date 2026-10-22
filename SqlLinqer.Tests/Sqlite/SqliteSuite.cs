using System.Reflection;
using Microsoft.Data.Sqlite;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using static SqlLinqer.Tests.Sqlite.CheckAssertions;

namespace SqlLinqer.Tests.Sqlite;

internal static class SqliteSuite
{
    internal static int Run()
    {
        int passed = 0, failed = 0;
        void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine($"PASS {name}"); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}\n{e}"); }
        }
        var file = Path.Combine(Path.GetTempPath(), $"sqllinqer-{Guid.NewGuid():N}.db");
        try
        {
            foreach (bool memory in new[] { false, true })
            {
                string label = memory ? "shared memory" : "file";
                string connectionString = memory
                    ? $"Data Source=sqllinqer-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False;Default Timeout=4"
                    : new SqliteConnectionStringBuilder { DataSource = file, Pooling = false, DefaultTimeout = 4 }.ToString();
                using var keeper = new SqliteConnection(connectionString);
                keeper.Open();
                using (var version = keeper.CreateCommand())
                {
                    version.CommandText = "SELECT sqlite_version()";
                    string runtime = (string)version.ExecuteScalar();
                    Console.WriteLine($"{label}: Microsoft.Data.Sqlite {typeof(SqliteConnection).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}; SQLite {runtime}; .NET {Environment.Version}");
                    Expect(Version.Parse(runtime) >= new Version(3, 38));
                    string expectedRuntime = Environment.GetEnvironmentVariable("SQLLINQER_EXPECT_SQLITE_VERSION");
                    if (expectedRuntime != null)
                        Expect(runtime == expectedRuntime, $"Expected SQLite {expectedRuntime}, loaded {runtime}");
                }
                var connector = new ConnectionFactoryConnector(() => new SqliteConnection(connectionString), DbFlavor.Sqlite,
                    new ConnectorOptions { CommandTimeout = TimeSpan.FromSeconds(2) });
                void Run(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
                Run("""
                    CREATE TABLE categories (Id INTEGER PRIMARY KEY, Name TEXT);
                    CREATE TABLE items (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, OtherName TEXT, Enabled INTEGER,
                      Status INTEGER, CategoryId INTEGER, Token TEXT, Created TEXT, OffsetDate TEXT, Amount TEXT, Bytes BLOB);
                    CREATE TABLE children (Id INTEGER PRIMARY KEY, ItemId INTEGER, Name TEXT, CategoryId INTEGER);
                    INSERT INTO categories VALUES (1, 'group');
                    INSERT INTO children VALUES (1, 1, 'z', 1), (2, 1, 'a', NULL), (3, 1, 'm', 1), (4, 2, 'other', 1);
                    """);
                var token = Guid.NewGuid();
                var date = new DateTime(2026, 10, 20, 12, 34, 56).AddTicks(1234567);
                var offset = new DateTimeOffset(date, TimeSpan.FromHours(-4));
                var item = new Item
                {
                    Name = "a_%\\'\"",
                    OtherName = "old",
                    Enabled = true,
                    Status = State.Active,
                    CategoryId = 1,
                    Token = token,
                    Created = date,
                    OffsetDate = offset,
                    Amount = 1234567890.123456789m,
                    Bytes = [0, 1, 127, 255]
                };
                Test($"{label}: connector preserves provider timeout and connection string", () =>
                {
                    using var copy = (SqliteConnection)connector.CreateConnection();
                    Expect(copy.DefaultTimeout == 4);
                    Expect(!copy.ConnectionString.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase));
                });
                WriteChecks.Insert(connector, item, label, Test);
                MappingChecks.Run(connector, keeper, item, label, Test);
                QueryChecks.Run(connector, keeper, item, label, Test);
                RelationshipChecks.Run(connector, keeper, label, Test);
                WriteChecks.UpdateAndDelete(connector, keeper, item, label, Test);
                SqlLinqer.Tests.Regression.CompatibleDatabaseTests.Run(connector, (name, action) => Test($"{label}: {name}", action));
                SqlLinqer.Tests.Regression.SqlJsonContractChecks.Run(connector, (name, action) => Test($"{label}: {name}", action));
                Test($"{label}: attached schema remains qualified and missing schemas fail", () =>
                {
                    using var transaction = keeper.BeginTransaction();
                    Result(connector.ExecuteNonQuery(new RenderedQuery("ATTACH DATABASE ':memory:' AS attached; CREATE TABLE attached.categories (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO attached.categories VALUES (9,'attached');"), transaction));
                    var query = new SelectQuery<Category>("attached").SelectRootColumns();
                    Expect(query.Render(DbFlavor.Sqlite).Text.Contains("\"attached\".\"categories\""));
                    Expect(Result(query.Execute(connector, transaction)).Single().Name == "attached");
                    Expect(new SelectQuery<Category>("missing").SelectRootColumns().Execute(connector, transaction).State == ResponseState.Error);
                    var updated = new UpdateWithPrimaryKeyQuery<Category, int>("attached").Update(x => x.Name, "changed").Where(x => x.Id, 9, SqlOp.EQ).Execute(connector, transaction);
                    Expect(Result(updated) == 1 && updated.TouchedPrimaryKeys.Single() == 9);
                    var deleted = new DeleteWithPrimaryKeyQuery<Category, int>("attached").Where(x => x.Id, 9, SqlOp.EQ).Execute(connector, transaction);
                    Expect(Result(deleted) == 1 && deleted.TouchedPrimaryKeys.Single() == 9);
                    Expect(Result(new SelectQuery<Category>("main").SelectRootColumns().Execute(connector, transaction)).Single().Name == "group");
                    transaction.Rollback();
                });
            }
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(file); }
        SqlLinqer.Tests.Regression.AsyncProviderChecks.Run(DbFlavor.Sqlite, Test);
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
