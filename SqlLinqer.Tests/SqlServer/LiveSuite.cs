using System.Data;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.SqlServer;

internal static class LiveSuite
{
    internal static string CurrentCheck { get; private set; } = "configuration";
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    internal static T Valid<T>(SqlResponse<T> response)
    {
        if (response.State != ResponseState.Valid)
            throw new InvalidOperationException("Database operation failed", response.Error?.InnerException ?? response.Error);
        return response.Result;
    }
    internal static BaseConnector Connector(string connectionString, string schema = null, DbFlavor flavor = DbFlavor.AzureSql)
        => new ConnectionFactoryConnector(() => new SqlConnection(connectionString), flavor,
            new ConnectorOptions { CommandTimeout = TimeSpan.FromSeconds(30), DefaultSchema = schema });

    internal static void Run(string connectionString, bool tempOnly, Action<string, Action> run, DbFlavor flavor = DbFlavor.AzureSql)
    {
        CurrentCheck = "configuration";
        Require(flavor == DbFlavor.AzureSql || flavor == DbFlavor.SqlServer, "Unsupported live SQL flavor");
        var settings = new SqlConnectionStringBuilder(connectionString);
        Require(settings.Encrypt != SqlConnectionEncryptOption.Optional, "Live suite requires Encrypt=true or Strict");
        if (flavor == DbFlavor.AzureSql)
            Require(!settings.TrustServerCertificate, "Live Azure SQL requires TrustServerCertificate=false");
        Require(!string.IsNullOrWhiteSpace(settings.InitialCatalog)
            && !settings.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase), "Select an explicit test Database other than master");
        var connector = Connector(settings.ConnectionString, flavor: flavor);
        using var connection = (SqlConnection)connector.CreateConnection();
        connection.Open();
        string engine = flavor == DbFlavor.SqlServer ? "SQL Server" : "Azure SQL Database";
        CurrentCheck = engine + " engine identity";
        using (var command = new SqlCommand("SELECT CONVERT(int, SERVERPROPERTY('EngineEdition'))", connection))
        {
            int edition = Convert.ToInt32(command.ExecuteScalar());
            Require(flavor == DbFlavor.AzureSql ? edition == 5 : edition is 2 or 3 or 4,
                "This suite requires " + engine);
        }
        void Live(string name, Action action)
        {
            CurrentCheck = name;
            run(name, action);
        }
        Live(engine + " engine identity", () => Require(connection.State == ConnectionState.Open, "Connection is not open"));
        TemporarySuite(connection, connector, Live);
        if (tempOnly)
            Console.WriteLine("SKIP live DDL suite: --temp-only omits TVPs, stored procedures and cross-connection transaction tests.");
        else
            ScopedSuite(connection, settings.ConnectionString, Live, flavor);
    }

    internal static void NonQuery(SqlConnection connection, string sql, SqlTransaction transaction = null)
    {
        using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.ExecuteNonQuery();
    }
    internal static int Scalar(SqlConnection connection, string sql, SqlTransaction transaction = null)
    {
        using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        return Convert.ToInt32(command.ExecuteScalar());
    }
    internal const string TempSetup = """
        CREATE TABLE #SqlLinqerAzureParents (Id int IDENTITY PRIMARY KEY, Name nvarchar(200) NULL, Enabled bit NOT NULL, ProfileId int NULL);
        CREATE TABLE #SqlLinqerAzureProfiles (Id int IDENTITY PRIMARY KEY, Name nvarchar(200) NULL);
        CREATE TABLE #SqlLinqerAzureChildren (Id int IDENTITY PRIMARY KEY, ParentId int NOT NULL, Name nvarchar(200) NULL);
        CREATE TABLE #SqlLinqerAzureLeaves (Id int IDENTITY PRIMARY KEY, ChildId int NOT NULL, Name nvarchar(200) NULL);
        CREATE TABLE #SqlLinqerAzureTypes (Id int IDENTITY PRIMARY KEY, Tiny tinyint, Small smallint, Big bigint, Amount decimal(38,18),
            Real real, [Double] float, Token uniqueidentifier, Flag bit, Optional int NULL, Stamp datetime2(7), OffsetStamp datetimeoffset(7),
            Clock time(7), Bytes varbinary(max), Text nvarchar(max), Letter nchar(1));
        """;
    internal const string TempCleanup = """
        DROP TABLE IF EXISTS #SqlLinqerAzureTypes;
        DROP TABLE IF EXISTS #SqlLinqerAzureLeaves;
        DROP TABLE IF EXISTS #SqlLinqerAzureChildren;
        DROP TABLE IF EXISTS #SqlLinqerAzureProfiles;
        DROP TABLE IF EXISTS #SqlLinqerAzureParents;
        """;
    internal static AzureTypes SampleTypes() => new()
    {
        Tiny = 255, Small = short.MinValue, Big = 9007199254740993L, Amount = 1234567890123456.123456789012m,
        Real = 1.25f, Double = -12345.125, Token = Guid.Parse("32f85ce3-e3e6-4aa8-b96f-4536a921c09f"),
        Flag = true, Optional = null, Stamp = new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(1234567),
        OffsetStamp = new DateTimeOffset(2024, 2, 29, 13, 14, 15, TimeSpan.FromHours(5.5)).AddTicks(7654321),
        Clock = new TimeSpan(0, 2, 3, 4).Add(TimeSpan.FromTicks(1234567)),
        Bytes = new byte[] { 0, 1, 127, 128, 255 }, Text = "雪 \"quoted\" \\ \n", Letter = '雪',
    };

    private static void TemporarySuite(SqlConnection connection, BaseConnector connector, Action<string, Action> live)
    {
        try
        {
            NonQuery(connection, TempSetup);
            using (var tx = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                int firstId = 0;
                live("insert identity keys and relationship fixtures", () =>
                {
                    var profiles = new InsertWithPrimaryKeyQuery<AzureProfile, int>().InsertAuto(new AzureProfile { Name = "profile" }).Execute(connector, tx);
                    Require(Valid(profiles) == 1, "Profile insert failed");
                    var response = new InsertWithPrimaryKeyQuery<AzureParent, int>().InsertAuto(new[]
                    {
                        new AzureParent { Name = "alpha \"雪\"", Enabled = true, ProfileId = profiles.TouchedPrimaryKeys.Single() },
                        new AzureParent { Name = "beta", Enabled = false },
                        new AzureParent { Name = null, Enabled = true },
                    }).Execute(connector, tx);
                    Require(Valid(response) == 3 && response.TouchedPrimaryKeys.Distinct().Count() == 3, "Insert keys or count differ");
                    firstId = Valid(new SelectQuery<AzureParent>().SelectRootColumns()
                        .Where(x => x.Name, "alpha \"雪\"", SqlOp.EQ).Execute(connector, tx)).Single().Id;
                    var children = new InsertWithPrimaryKeyQuery<AzureChild, int>().InsertAuto(new[]
                    {
                        new AzureChild { ParentId = firstId, Name = "child" },
                        new AzureChild { ParentId = firstId, Name = null },
                    }).Execute(connector, tx);
                    Require(Valid(children) == 2, "Child insert failed");
                    Require(Valid(new InsertQuery<AzureLeaf>().InsertAuto(new[]
                    {
                        new AzureLeaf { ChildId = children.TouchedPrimaryKeys.Max(), Name = "leaf" },
                        new AzureLeaf { ChildId = children.TouchedPrimaryKeys.Max(), Name = null },
                    }).Execute(connector, tx)) == 2, "Leaf insert failed");
                });
                live("typed JSON one-to-one one-to-many null empty nested ordering", () =>
                {
                    var query = new SelectQuery<AzureParent>().SelectRootColumns().Select(x => x.Profile)
                        .Select(x => x.Children).Select(new[] { nameof(AzureParent.Children), nameof(AzureChild.Leaves) })
                        .OrderByAuto().OrderBy(x => x.Id, SqlDir.ASC);
                    var rows = Valid(query.Execute(connector, tx));
                    var alpha = rows.Single(x => x.Name == "alpha \"雪\"");
                    var beta = rows.Single(x => x.Name == "beta");
                    var unnamed = rows.Single(x => x.Name == null);
                    Require(rows.Count == 3 && alpha.Enabled && rows.Select(x => x.Id).SequenceEqual(rows.Select(x => x.Id).Order()), "Typed JSON values/order differ");
                    Require(alpha.Profile?.Name == "profile" && beta.Profile == null && unnamed.Profile == null, "One-to-one null/object differs");
                    var children = alpha.Children;
                    Require(children.Count == 2 && children[0].Id > children[1].Id && children.Any(x => x.Name == null)
                        && beta.Children.Count == 0 && unnamed.Children.Count == 0, "One-to-many ordering/null/empty differs");
                    Require(children[0].Leaves.Count == 2 && children[0].Leaves[0].Id < children[0].Leaves[1].Id
                        && children[0].Leaves.Any(x => x.Name == null) && children[1].Leaves.Count == 0, "Nested JSON ordering/empty differs");
                });
                live("paging Top grouped Distinct and aggregate totals", () =>
                {
                    var orderedIds = Valid(new SelectQuery<AzureParent>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector, tx)).Select(x => x.Id).ToArray();
                    var page = new SelectQuery<AzureParent>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Page(2, 1).ExecuteWithTotalCount(connector, tx);
                    Require(Valid(page).Single().Id == orderedIds[1] && page.TotalResults == 3, "Paging or count differs");
                    var top = new SelectQuery<AzureParent>().SelectRootColumns().Top(1).ExecuteWithTotalCount(connector, tx);
                    Require(Valid(top).Count == 1 && top.TotalResults == 3, "Top differs");
                    var groups = AzureParent.BeginSelectBuilder().BuildQuery(x => new { Enabled = x.Select(y => y.Enabled), Count = x.Count(), Max = x.Max(y => y.Id) })
                        .OrderBy(x => x.Enabled, SqlDir.ASC).Page(2, 1).ExecuteWithTotalCount(connector, tx);
                    Require(Valid(groups).Single().Enabled && groups.Result[0].Count == 2 && groups.TotalResults == 2, "Grouped totals differ");
                    var distinct = new SelectQuery<AzureParent>().Select(x => x.Enabled).Distinct().OrderBy(x => x.Enabled, SqlDir.ASC).Page(1, 1).ExecuteWithTotalCount(connector, tx);
                    Require(!Valid(distinct).Single().Enabled && distinct.TotalResults == 2, "Distinct total differs");
                    var relatedDistinct = new SelectQuery<AzureParent>().Select(x => x.Id).Distinct()
                        .Where(x => x.Children.First().Name, "child", SqlOp.EQ).OrderBy(x => x.Id, SqlDir.ASC)
                        .Page(1, 1).ExecuteWithTotalCount(connector, tx);
                    Require(Valid(relatedDistinct).Single().Id == firstId && relatedDistinct.TotalResults == 1,
                        "Generated relationship CTE distinct total differs");
                    var aggregate = AzureParent.BeginSelectBuilder().BuildQuery(x => new { Count = x.Count() }).Top(1).ExecuteWithTotalCount(connector, tx);
                    Require(Valid(aggregate).Single().Count == 3 && aggregate.TotalResults == 1, "Ungrouped aggregate total differs");
                });
                live("parameter and typed JSON type round trips", () =>
                {
                    var expected = SampleTypes();
                    Require(Valid(new InsertQuery<AzureTypes>().InsertAuto(expected).Execute(connector, tx)) == 1, "Type insert failed");
                    var actual = Valid(new SelectQuery<AzureTypes>().SelectRootColumns().Execute(connector, tx)).Single();
                    Require(actual.Tiny == expected.Tiny && actual.Small == expected.Small && actual.Big == expected.Big
                        && actual.Amount == expected.Amount && actual.Real == expected.Real && actual.Double == expected.Double
                        && actual.Token == expected.Token && actual.Flag && actual.Optional == null && actual.Stamp == expected.Stamp
                        && actual.OffsetStamp.EqualsExact(expected.OffsetStamp) && actual.Clock == expected.Clock
                        && actual.Bytes.SequenceEqual(expected.Bytes) && actual.Text == expected.Text && actual.Letter == expected.Letter, "Type values changed");
                });
                live("direct non-JSON rendering and CTE execution", () =>
                {
                    var query = new SelectQuery<AzureParent>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC);
                    var rendered = new RenderedQuery(); rendered.Text = query.Render(rendered.Parameters, DbFlavor.AzureSql, false);
                    using var table = Valid(connector.ExecuteReaderDataTable(rendered, tx));
                    Require(table.Rows.Count == 3 && table.Rows.Cast<DataRow>().Any(x => Equals(x["Name"], "alpha \"雪\"")), "Direct rendering differs");
                    var cteRows = Valid(new ParentCteSelect().Execute(connector, tx));
                    Require(cteRows.Count == 3 && cteRows.Any(x => x.Id == firstId && x.Name == "alpha \"雪\""), "CTE values failed");
                    var ctePage = new ParentCteSelect().Distinct().OrderBy(x => x.Id, SqlDir.ASC).Page(2, 1).ExecuteWithTotalCount(connector, tx);
                    Require(Valid(ctePage).Count == 1 && ctePage.TotalResults == 3, "CTE distinct total failed");
                });
                live("joined source update and joined delete outputs", () =>
                {
                    using var output = Valid(new UpdateQuery<AzureParent>().Update(x => x.Name, x => x.Profile.Name)
                        .Where(x => x.Profile.Name, "profile", SqlOp.EQ).Output(x => x.Id).Output(x => x.Name).ExecuteWithOutput(connector, tx));
                    Require(output.Rows.Count == 1 && (int)output.Rows[0]["Id"] == firstId && (string)output.Rows[0]["Name"] == "profile", "Joined update differs");
                    using var deleted = Valid(new DeleteQuery<AzureParent>().Where(x => x.Profile.Name, "profile", SqlOp.EQ)
                        .Output(x => x.Id).Output(x => x.Name).ExecuteWithOutput(connector, tx));
                    Require(deleted.Rows.Count == 1 && (int)deleted.Rows[0]["Id"] == firstId && (string)deleted.Rows[0]["Name"] == "profile", "Joined delete differs");
                });
                live("update delete returned keys and exists", () =>
                {
                    var remaining = Valid(new SelectQuery<AzureParent>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector, tx));
                    int id = remaining[0].Id;
                    Require(Valid(new SelectPrimaryKeyExistsQuery<AzureParent, int>(id).Execute(connector, tx)), "Exists false for present row");
                    var updated = new UpdateWithPrimaryKeyQuery<AzureParent, int>().Update(x => x.Enabled, true).Where(x => x.Id, id, SqlOp.EQ).Execute(connector, tx);
                    Require(Valid(updated) == 1 && updated.TouchedPrimaryKeys.Single() == id, "Update keys differ");
                    var deleted = new DeleteWithPrimaryKeyQuery<AzureParent, int>().Where(x => x.Id, id, SqlOp.EQ).Execute(connector, tx);
                    Require(Valid(deleted) == 1 && deleted.TouchedPrimaryKeys.Single() == id, "Delete keys differ");
                    Require(!Valid(new SelectPrimaryKeyExistsQuery<AzureParent, int>(id).Execute(connector, tx)), "Exists true after delete");
                });
                live("truncate participates in caller rollback", () =>
                {
                    Valid(new TruncateQuery<AzureChild>().Execute(connector, tx));
                    Require(Scalar(connection, "SELECT COUNT(*) FROM #SqlLinqerAzureChildren", tx) == 0, "Truncate failed");
                });
                live("caller transaction remains usable and rolls back", () =>
                {
                    Require(tx.Connection == connection && connection.State == ConnectionState.Open, "Connector completed caller transaction");
                    Require(Valid(new InsertQuery<AzureParent>().InsertAuto(new AzureParent { Name = "rollback", Enabled = true }).Execute(connector, tx)) == 1, "Caller transaction unusable");
                    tx.Rollback();
                    Require(Scalar(connection, "SELECT COUNT(*) FROM #SqlLinqerAzureParents") == 0
                        && Scalar(connection, "SELECT COUNT(*) FROM #SqlLinqerAzureTypes") == 0, "Caller rollback failed");
                });
            }
            live("caller statement failure leaves caller transaction usable", () =>
            {
                using var tx = connection.BeginTransaction();
                var failed = connector.ExecuteNonQuery(new RenderedQuery("INSERT INTO #SqlLinqerAzureParents (Name, Enabled) VALUES (N'before', 1); THROW 51001, 'intentional test failure', 1;"), tx);
                Require(failed.State == ResponseState.Error && tx.Connection == connection, "Caller failure was not retained");
                Require(Scalar(connection, "SELECT COUNT(*) FROM #SqlLinqerAzureParents", tx) == 1, "Connector rolled back caller transaction");
                tx.Rollback();
                Require(Scalar(connection, "SELECT COUNT(*) FROM #SqlLinqerAzureParents") == 0, "Caller rollback after failure failed");
            });
        }
        finally { NonQuery(connection, TempCleanup); }
    }

    internal static string ScopedTableSetup(string schema) => $"""
        CREATE TABLE [{schema}].[Rows] (Id int IDENTITY PRIMARY KEY, Name nvarchar(200) NULL, Optional int NULL, Amount decimal(38,18) NOT NULL CHECK (Amount >= 0));
        CREATE TYPE [{schema}].[IdList] AS TABLE (Id int NOT NULL);
        """;
    internal static string TableProcedure(string schema) => $"""
        CREATE PROCEDURE [{schema}].[ReadRows] @MinId int, @Name nvarchar(200) = NULL
        AS BEGIN SET NOCOUNT ON;
            SELECT Id, Name FROM [{schema}].[Rows] WHERE Id >= @MinId AND (@Name IS NULL OR Name = @Name) ORDER BY Id;
        END
        """;
    internal static string JsonProcedure(string schema) => $"""
        CREATE PROCEDURE [{schema}].[ReadJson] @Ids [{schema}].[IdList] READONLY
        AS BEGIN SET NOCOUNT ON;
            SELECT r.Id, r.Name FROM [{schema}].[Rows] AS r INNER JOIN @Ids AS i ON r.Id = i.Id ORDER BY r.Id FOR JSON PATH, INCLUDE_NULL_VALUES;
        END
        """;
    internal static string ScopedCleanup(string schema) => $"""
        DROP PROCEDURE IF EXISTS [{schema}].[ReadJson];
        DROP PROCEDURE IF EXISTS [{schema}].[TypedCallable];
        DROP PROCEDURE IF EXISTS [{schema}].[ReadRows];
        DROP TABLE IF EXISTS [{schema}].[Rows];
        DECLARE @drop nvarchar(max) = N'';
        SELECT @drop = @drop + N'DROP TYPE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';'
            FROM sys.table_types WHERE schema_id = SCHEMA_ID(N'{schema}');
        EXEC sys.sp_executesql @drop;
        DROP SCHEMA [{schema}];
        """;

    private static void ScopedSuite(SqlConnection connection, string connectionString, Action<string, Action> live, DbFlavor flavor)
    {
        // Only this generated schema is ever created, queried or removed by the DDL suite.
        string schema = "SqlLinqerAzure_" + Guid.NewGuid().ToString("N");
        bool created = false;
        Console.WriteLine("Live test schema: " + schema);
        var connector = Connector(connectionString, schema, flavor);
        int TypeCount() => Scalar(connection, $"SELECT COUNT(*) FROM sys.table_types WHERE schema_id = SCHEMA_ID(N'{schema}')");
        int RowCount() => Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[Rows]");
        InsertQuery<ScopedRow> Insert(params ScopedRow[] rows) => new InsertQuery<ScopedRow>(schema).InsertAuto(rows);
        try
        {
            live("unique test schema type table and procedures", () =>
            {
                NonQuery(connection, $"CREATE SCHEMA [{schema}]"); created = true;
                NonQuery(connection, ScopedTableSetup(schema));
                NonQuery(connection, TableProcedure(schema));
                NonQuery(connection, JsonProcedure(schema));
                NonQuery(connection, CallableChecks.Procedure(schema));
            });
            live("owned transactions commit and rollback failed batches", () =>
            {
                Require(Valid(Insert(new ScopedRow { Name = "committed", Amount = 1 }).Execute(connector)) == 1 && RowCount() == 1, "Owned commit failed");
                var failed = connector.ExecuteNonQuery(new RenderedQuery($"INSERT INTO [{schema}].[Rows] (Name, Amount) VALUES (N'before error', 2); THROW 51002, 'intentional test failure', 1;"));
                Require(failed.State == ResponseState.Error && RowCount() == 1, "Owned batch failure did not roll back");
            });
            CallableChecks.RunLive(connector, connection, live);
            live("existing table type procedure input and caller transaction", () =>
            {
                using var ids = new DataTable(); ids.Columns.Add("Id", typeof(int));
                ids.Rows.Add(Scalar(connection, $"SELECT TOP (1) Id FROM [{schema}].[Rows] ORDER BY Id"));
                using var transaction = connection.BeginTransaction();
                var rows = Valid(new StoredProcedureQuery<ProcedureRow>("ReadJson", schema)
                    .AddTvpParameter("Ids", ids, $"{schema}.IdList").ExecuteJson(connector, transaction));
                Require(rows.Single().Name == "committed" && transaction.Connection == connection,
                    "Existing table type input or caller ownership differs");
                transaction.Rollback();
            });
            live("TVP insert identity keys nullable decimal and type cleanup", () =>
            {
                int beforeTypes = TypeCount();
                var result = new InsertWithPrimaryKeyQuery<ScopedRow, int>(schema).InsertAuto(new[]
                {
                    new ScopedRow { Name = "TVP 雪", Amount = 2.125m, Optional = null },
                    new ScopedRow { Name = null, Amount = 3, Optional = 17 },
                }).ExecuteTvp(connector);
                Require(Valid(result) == 2 && result.TouchedPrimaryKeys.Distinct().Count() == 2 && RowCount() == 3
                    && TypeCount() == beforeTypes, "TVP keys/count/type cleanup differs");
                var rows = Valid(new SelectQuery<ScopedRow>(schema).SelectRootColumns().Where(x => x.Id, result.TouchedPrimaryKeys, SqlArrayOp.IN)
                    .OrderBy(x => x.Id, SqlDir.ASC).Execute(connector));
                var named = rows.Single(x => x.Name == "TVP 雪");
                var unnamed = rows.Single(x => x.Name == null);
                Require(named.Optional == null && named.Amount == 2.125m && unnamed.Optional == 17, "TVP values differ");
            });
            live("TVP output rows use one owned transaction", () =>
            {
                int beforeTypes = TypeCount();
                using var rows = Valid(Insert(new ScopedRow { Name = "output", Amount = 4 }).Output(x => x.Id).Output(x => x.Name).ExecuteTvpWithOutput(connector));
                Require(rows.Rows.Count == 1 && Convert.ToInt32(rows.Rows[0]["Id"]) > 0
                    && (string)rows.Rows[0]["Name"] == "output" && TypeCount() == beforeTypes, "TVP output/type cleanup differs");
            });
            live("TVP caller ownership rollback and failure cleanup", () =>
            {
                int beforeRows = RowCount(), beforeTypes = TypeCount();
                using (var tx = connection.BeginTransaction())
                {
                    Require(Valid(Insert(new ScopedRow { Name = "caller", Amount = 5 }).ExecuteTvp(connector, tx)) == 1, "Caller TVP failed");
                    using var output = Valid(Insert(new ScopedRow { Name = "caller output", Amount = 6 }).Output(x => x.Id).ExecuteTvpWithOutput(connector, tx));
                    Require(output.Rows.Count == 1 && tx.Connection == connection
                        && Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[Rows]", tx) == beforeRows + 2, "Caller TVP ownership differs");
                    tx.Rollback();
                }
                Require(RowCount() == beforeRows && TypeCount() == beforeTypes, "Caller TVP rollback differs");
                var failed = Insert(new ScopedRow { Name = "invalid", Amount = -1 }).ExecuteTvp(connector);
                Require(failed.State == ResponseState.Error && RowCount() == beforeRows && TypeCount() == beforeTypes, "Failed TVP leaked rows/type");
                var failedOutput = Insert(new ScopedRow { Amount = -2 }).Output(x => x.Id).ExecuteTvpWithOutput(connector);
                Require(failedOutput.State == ResponseState.Error && RowCount() == beforeRows && TypeCount() == beforeTypes, "Failed TVP output leaked rows/type");
                using (var tx = connection.BeginTransaction())
                {
                    var callerFailure = Insert(new ScopedRow { Amount = -3 }).ExecuteTvp(connector, tx);
                    Require(callerFailure.State == ResponseState.Error && tx.Connection == connection, "Caller TVP failure lost ownership");
                    tx.Rollback();
                }
                Require(RowCount() == beforeRows && TypeCount() == beforeTypes, "Failed caller TVP rollback leaked type");
            });
            live("stored procedure typed null DataTable JSON and TVP execution", () =>
            {
                using var table = Valid(new StoredProcedureQuery<ProcedureRow>("ReadRows", schema)
                    .AddParameter("MinId", 0, DbType.Int32).AddParameter("Name", null, DbType.String).ExecuteDataTable(connector));
                Require(table.Rows.Count == 4 && (string)table.Rows[0]["Name"] == "committed", "Procedure DataTable differs");
                using var ids = new DataTable(); ids.Columns.Add("Id", typeof(int));
                foreach (DataRow row in table.Rows) ids.Rows.Add(row["Id"]);
                using var tx = connection.BeginTransaction();
                var rows = Valid(new StoredProcedureQuery<ProcedureRow>("ReadJson", schema).AddTvpParameter("Ids", ids, $"{schema}.IdList").ExecuteJson(connector, tx));
                Require(rows.Count == 4 && rows.Any(x => x.Name == null) && tx.Connection == connection, "Procedure JSON/TVP/caller ownership differs");
                tx.Rollback();
                ids.Clear();
                Require(Valid(new StoredProcedureQuery<ProcedureRow>("ReadJson", schema).AddTvpParameter("Ids", ids, $"{schema}.IdList").ExecuteJson(connector)).Count == 0, "Empty procedure TVP differs");
            });
            TvpChecks.RunLive(connector, connection, connectionString, schema, live);
            SqlLinqer.Tests.Regression.SqlJsonContractChecks.Run(connector, live);
            live("owned truncate commits", () =>
            {
                Valid(new TruncateQuery<ScopedRow>(schema).Execute(connector));
                Require(RowCount() == 0, "Owned truncate failed");
            });
        }
        finally
        {
            if (created)
            {
                string previousCheck = CurrentCheck;
                live("unique schema and all test objects cleaned up", () =>
                {
                    NonQuery(connection, ScopedCleanup(schema));
                    Require(Scalar(connection, $"SELECT COUNT(*) FROM sys.schemas WHERE name = N'{schema}'") == 0, "Schema cleanup failed");
                });
                CurrentCheck = previousCheck;
            }
        }
    }
}
