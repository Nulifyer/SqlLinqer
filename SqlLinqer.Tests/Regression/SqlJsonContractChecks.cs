using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Exceptions;

namespace SqlLinqer.Tests.Regression;

// Runs against actual database-produced JSON in every native provider suite.
internal static class SqlJsonContractChecks
{
    private const string Roots = "json_contract_roots", Children = "json_contract_children";
    private const string RootName = "O'Reilly \"quoted\" \\ \n雪";
    private static readonly byte[] RootPayload = Enumerable.Range(0, 1024).Select(i => (byte)(i % 256)).ToArray();

    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        RelatedAggregateProviderChecks.Run(connector, test);
        ValueConversionProviderChecks.Run(connector, test);
        ComplexRelationshipChecks.Run(connector, test);
        RelationshipStressChecks.Run(connector, test);
        string Q(string value) => value.DbWrap(connector.DbFlavor);
        string TableName(string value) => string.IsNullOrEmpty(connector.Options.DefaultSchema) ? Q(value)
            : Q(connector.Options.DefaultSchema) + "." + Q(value);
        void Sql(string text) => Result(connector.ExecuteNonQuery(new RenderedQuery(text)));
        string textType = connector.DbFlavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "NVARCHAR(128)" : "VARCHAR(128)";
        string binaryType = connector.DbFlavor switch
        {
            DbFlavor.SqlServer or DbFlavor.AzureSql => "VARBINARY(MAX)",
            DbFlavor.PostgreSql => "BYTEA",
            _ => "BLOB",
        };
        try
        {
            Sql($"CREATE TABLE {TableName(Roots)} ({Q("id")} INTEGER PRIMARY KEY, {Q("db_name")} {textType} NULL, {Q("featured_id")} INTEGER NULL, {Q("payload")} {binaryType} NULL); " +
                $"CREATE TABLE {TableName(Children)} ({Q("id")} INTEGER PRIMARY KEY, {Q("parent_id")} INTEGER NOT NULL, {Q("db_name")} {textType} NULL);");
            Result(new InsertQuery<PlainRoot>(connector.Options.DefaultSchema).InsertAuto(new[]
            {
                new PlainRoot { Id = 1, Name = RootName, FeaturedId = 10, Payload = RootPayload },
                new PlainRoot { Id = 2, Name = null },
            }).Execute(connector));
            Result(new InsertQuery<PlainChild>(connector.Options.DefaultSchema).InsertAuto(new[]
            {
                new PlainChild { Id = 10, ParentId = 1, Name = "child 雪" },
                new PlainChild { Id = 11, ParentId = 1, Name = null },
            }).Execute(connector));

            foreach (string mode in new[] { "Execute", "ExecuteAsync", "ExecuteAsyncStream" })
            {
                bool asynchronous = mode != "Execute";
                test($"SQL JSON: API metadata and serializer changes preserve nested database values, read={mode}", () =>
                {
                    var plainQuery = new SelectQuery<PlainRoot>(connector.Options.DefaultSchema).SelectRootColumns().Select(x => x.Children).Select(x => x.Featured);
                    var baseline = Read(plainQuery, mode);
                    var query = new SelectQuery<ApiRoot>(connector.Options.DefaultSchema).SelectRootColumns().Select(x => x.Children).Select(x => x.Featured);
                    var rows = Read(query, mode);
                    Check(rows.Count == baseline.Count && rows.Count == 2, "API metadata changed root row count");
                    CheckApiRows(rows);
                    var before = query.Capture();

                    var apiOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        NumberHandling = JsonNumberHandling.WriteAsString,
                        ReferenceHandler = ReferenceHandler.Preserve,
                    };
                    using var api = JsonDocument.Parse(JsonSerializer.Serialize(rows.Single(x => x.Id == 1), apiOptions));
                    Check(api.RootElement.GetProperty("displayName").GetString() == "api:" + RootName
                        && !api.RootElement.TryGetProperty("Id", out _) && !api.RootElement.TryGetProperty("id", out _),
                        "API serialization did not use its independent names, ignores, and converter");
                    Check(api.RootElement.GetProperty("apiChildren").GetProperty("$values").GetArrayLength() == 2,
                        "API reference handling was not applied");

                    // Changing API metadata after both serializers have cached the same types
                    // must not change SQL loading or its captured result shape.
                    var resolver = new DefaultJsonTypeInfoResolver();
                    resolver.Modifiers.Add(info => { foreach (var property in info.Properties) property.Name = "changed_" + property.Name; });
                    JsonSerializer.Serialize(rows[0], new JsonSerializerOptions { TypeInfoResolver = resolver });
                    bool requiredRejected = false;
                    try { JsonSerializer.Deserialize<ApiRoot>("{}"); }
                    catch (JsonException error) when (error.Message.Contains("ApiOnly")) { requiredRejected = true; }
                    Check(requiredRejected, "API required-member validation was not applied");
                    ExpectApiFailure(() => JsonSerializer.Deserialize<ApiRoot>("{\"displayName\":\"API\",\"ApiOnly\":\"API\"}"));
                    CheckApiRows(Read(query, mode));
                    var captured = before.Render(connector);
                    Check(!captured.Text.Contains("displayName") && !captured.Text.Contains("ApiOnly"), "API names entered the SQL projection");
                    CheckApiRows(ReadJson<ApiRoot>(captured, mode));
                    Check(baseline.Single(x => x.Id == 1).Name == RootName && baseline.Single(x => x.Id == 2).Children.Count == 0,
                        "Baseline database JSON differed from the expected values");
                });

                test($"SQL JSON: throwing API converters on roots, members and collections never run, read={mode}", () =>
                {
                    var query = new SelectQuery<GuardedRoot>(connector.Options.DefaultSchema).SelectRootColumns().Select(x => x.Children).Select(x => x.Featured);
                    // Warm the API's failed metadata cache before SQL sees this type.
                    ExpectApiFailure(() => JsonSerializer.Serialize(new GuardedRoot()));
                    int attempts = PoisonConverterFactory.Attempts;
                    for (int i = 0; i < 2; i++)
                    {
                        var rows = Read(query, mode);
                        var first = rows.Single(x => x.Id == 1);
                        var empty = rows.Single(x => x.Id == 2);
                        Check(first.Name == RootName && first.Children.Count == 2 && first.Featured.Name == "child 雪",
                            "API converters changed nested SQL results");
                        Check(empty.Name == null && empty.Featured == null && empty.Children.Count == 0,
                            "API converters changed SQL null or empty collection values");
                    }
                    Check(PoisonConverterFactory.Attempts == attempts, "SQL loading constructed an API converter");
                });

                test($"SQL JSON: API attributes leave insert, update, filter and delete mappings intact, read={mode}", () =>
                {
                    using var connection = connector.CreateConnection(); connection.Open();
                    using var transaction = connection.BeginTransaction();
                    try
                    {
                        var insert = new InsertQuery<ApiRoot>(connector.Options.DefaultSchema).InsertAuto(ApiRoot.Create(3, RootName));
                        Result(asynchronous ? insert.ExecuteAsync(connector, transaction).GetAwaiter().GetResult() : insert.Execute(connector, transaction));
                        var update = new UpdateQuery<ApiRoot>(connector.Options.DefaultSchema).Update(x => x.Name, "updated 雪").Where(x => x.Id, 3, SqlOp.EQ);
                        Result(asynchronous ? update.ExecuteAsync(connector, transaction).GetAwaiter().GetResult() : update.Execute(connector, transaction));
                        var read = new SelectQuery<ApiRoot>(connector.Options.DefaultSchema).SelectRootColumns().Where(x => x.Id, 3, SqlOp.EQ);
                        Check(Read(read, mode, transaction).Single().Name == "updated 雪", "API metadata changed SQL write or read mappings");
                        var delete = new DeleteQuery<ApiRoot>(connector.Options.DefaultSchema).Where(x => x.Id, 3, SqlOp.EQ);
                        Result(asynchronous ? delete.ExecuteAsync(connector, transaction).GetAwaiter().GetResult() : delete.Execute(connector, transaction));
                        Check(Read(read, mode, transaction).Count == 0, "API metadata changed the delete filter");
                    }
                    finally { transaction.Rollback(); }
                });

                test($"SQL JSON: empty results, paging, projections and aggregates, read={mode}", () =>
                {
                    var empty = new SelectQuery<PlainRoot>(connector.Options.DefaultSchema).SelectRootColumns()
                        .Where(x => x.Id, -1, SqlOp.EQ);
                    Check(Read(empty, mode).Count == 0, "Empty result did not produce an empty sequence");
                    var page = new SelectQuery<PlainRoot>(connector.Options.DefaultSchema).SelectRootColumns()
                        .OrderBy(x => x.Id, SqlDir.ASC).Page(2, 1);
                    var last = Read(page, mode).Single();
                    Check(last.Id == 2 && last.Name == null && last.Payload == null, "Page or nullable values changed");
                    var projection = new SelectQueryInitializer<PlainRoot>(connector.Options.DefaultSchema)
                        .BuildQuery(x => new { Id = x.Select(y => y.Id), Name = x.Select(y => y.Name) })
                        .OrderBy(x => x.Id, SqlDir.ASC);
                    var projected = Read(projection, mode);
                    Check(projected.Count == 2 && projected[0].Id == 1 && projected[0].Name == RootName && projected[1].Name == null,
                        "Anonymous projection values changed");
                    var aggregate = new SelectQueryInitializer<PlainRoot>(connector.Options.DefaultSchema)
                        .BuildQuery(x => new { Count = x.Count() });
                    Check(Read(aggregate, mode).Single().Count == 2, "Aggregate result changed");
                });

                test($"SQL JSON: DISTINCT paging preserves binary and relationship values, read={mode}", () =>
                {
                    var query = new SelectQueryInitializer<PlainRoot>(connector.Options.DefaultSchema).BuildQuery(q => new
                    {
                        Id = q.Select(x => x.Id),
                        Payload = q.Select(x => x.Payload),
                        Featured = q.Select(x => x.Featured),
                        Children = q.Select(x => x.Children)
                    }).Distinct().OrderBy(x => x.Id, SqlDir.ASC).Page(1, 1);
                    var row = Read(query, mode).Single();
                    Check(row.Id == 1 && row.Payload.SequenceEqual(RootPayload) && row.Featured.Name == "child 雪" && row.Children.Count == 2,
                        "Derived DISTINCT projection changed binary or nested JSON encoding");
                    var page = query.ExecuteWithTotalCount(connector);
                    Check(page.State == ResponseState.Valid && page.TotalResults == 2, page.Error?.ToString() ?? "DISTINCT binary/relationship count differs");
                    row = Read(query.Page(2, 1), mode).Single();
                    Check(row.Id == 2 && row.Payload == null && row.Featured == null && row.Children.Count == 0,
                        "Derived DISTINCT projection changed SQL nulls or empty collections");
                });

                test($"SQL JSON: large Unicode value survives native transport, read={mode}", () =>
                {
                    string expected = string.Concat(Enumerable.Repeat("雪😀\"\\\n", 16384));
                    var command = new RenderedQuery { Parameters = new ParameterCollection(connector.DbFlavor) };
                    string value = command.Parameters.AddParameter(expected);
                    command.Text = connector.DbFlavor switch
                    {
                        DbFlavor.SqlServer or DbFlavor.AzureSql => $"SELECT 1 AS [Id], {value} AS [Name] FOR JSON PATH, INCLUDE_NULL_VALUES",
                        DbFlavor.PostgreSql => $"SELECT json_build_object('Id', 1, 'Name', CAST({value} AS TEXT))",
                        DbFlavor.MySql => $"SELECT JSON_OBJECT('Id', 1, 'Name', {value})",
                        _ => $"SELECT json_object('Id', 1, 'Name', {value})",
                    };
                    var actual = ReadJson<PlainRoot>(command, mode).Single();
                    Check(actual.Id == 1 && actual.Name == expected, "Large JSON value was truncated or corrupted");
                });
            }

            test("SQL JSON: async-stream early exit closes the reader and preserves caller rollback", () =>
            {
                using var connection = connector.CreateConnection(); connection.Open();
                using var transaction = connection.BeginTransaction();
                Result(new InsertQuery<PlainRoot>(connector.Options.DefaultSchema)
                    .InsertAuto(new PlainRoot { Id = 3, Name = "rollback stream" }).Execute(connector, transaction));
                async Task ReadFirst()
                {
                    int delivered = 0;
                    await foreach (var row in new SelectQuery<PlainRoot>(connector.Options.DefaultSchema).SelectRootColumns()
                        .OrderBy(x => x.Id, SqlDir.ASC).ExecuteAsyncStream(connector, transaction))
                    { delivered++; Check(row.Id == 1, "First streamed key changed"); break; }
                    Check(delivered == 1, "Stream did not deliver its first row");
                }
                ReadFirst().GetAwaiter().GetResult();
                Check(transaction.Connection == connection && connection.State == System.Data.ConnectionState.Open,
                    "Stream completed the caller transaction or connection");
                // Reusing this same connection detects a reader left open by early exit.
                Check(Result(connector.ExecuteScalar<int>(new($"SELECT COUNT(*) FROM {TableName(Roots)}"), transaction)) == 3,
                    "Reader cleanup or transaction visibility changed");
                transaction.Rollback();
                Check(Read(new SelectQuery<PlainRoot>(connector.Options.DefaultSchema).SelectRootColumns(), "Execute").Count == 2,
                    "Stream committed the caller's insert");
            });

            test("SQL JSON: async-stream cancellation disposes the execution and preserves its token", () =>
            {
                using var cancellation = new CancellationTokenSource();
                async Task CancelAfterFirst()
                {
                    int delivered = 0;
                    try
                    {
                        await foreach (var row in new SelectQuery<PlainRoot>(connector.Options.DefaultSchema).SelectRootColumns()
                            .ExecuteAsyncStream(connector, cancellationToken: cancellation.Token))
                        { delivered++; cancellation.Cancel(); }
                    }
                    catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token)
                    { Check(delivered == 1, "Cancellation delivered extra rows"); return; }
                    throw new InvalidOperationException("Expected stream cancellation with the supplied token");
                }
                CancelAfterFirst().GetAwaiter().GetResult();
                Check(Read(new SelectQuery<PlainRoot>(connector.Options.DefaultSchema).SelectRootColumns(), "ExecuteAsync").Count == 2,
                    "Cancelled stream affected subsequent execution");
            });

            test("SQL JSON: async-stream database errors retain the rendered query", () =>
            {
                // Older SQLite engines can treat an unqualified quoted name as a string literal.
                var command = new RenderedQuery($"SELECT {Q("contract_root")}.{Q("missing_contract_column")} FROM {TableName(Roots)} AS {Q("contract_root")}");
                try { Collect(connector.ExecuteReaderJsonAsyncStream<PlainRoot>(command)).GetAwaiter().GetResult(); }
                catch (SqlResponseException error)
                {
                    Check(ReferenceEquals(error.Query, command) && error.InnerException is DbException,
                        "Provider error or command context was lost");
                    return;
                }
                throw new InvalidOperationException("Expected a provider error from streaming execution");
            });

            test("SQL JSON: concurrent API serialization and snapshot loading keep independent contracts", () =>
            {
                var snapshot = new SelectQuery<ApiRoot>(connector.Options.DefaultSchema).SelectRootColumns().Select(x => x.Children).Select(x => x.Featured).Capture();
                var apiOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper };
                Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
                {
                    var rows = Result(await connector.ExecuteReaderJsonAsync<ApiRoot>(snapshot.Render(connector)));
                    CheckApiRows(rows);
                    using var api = JsonDocument.Parse(JsonSerializer.Serialize(rows.Single(x => x.Id == 1), apiOptions));
                    Check(api.RootElement.GetProperty("displayName").GetString() == "api:" + RootName,
                        "Concurrent SQL loading changed the API contract");
                    CheckApiRows(Result(await connector.ExecuteReaderJsonAsync<ApiRoot>(snapshot.Render(connector))));
                })).GetAwaiter().GetResult();
            });

            List<T> Read<T>(IQueryExecutable<List<T>> query, string mode, DbTransaction transaction = null) where T : class
                => mode switch
                {
                    "Execute" => Result(query.Execute(connector, transaction)),
                    "ExecuteAsync" => Result(((IAsyncQueryExecutable<List<T>>)query).ExecuteAsync(connector, transaction).GetAwaiter().GetResult()),
                    "ExecuteAsyncStream" => Collect(((IAsyncQueryExecutableStream<T>)query).ExecuteAsyncStream(connector, transaction)).GetAwaiter().GetResult(),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                };

            List<T> ReadJson<T>(RenderedQuery query, string mode) where T : class
                => mode switch
                {
                    "Execute" => Result(connector.ExecuteReaderJson<T>(query)),
                    "ExecuteAsync" => Result(connector.ExecuteReaderJsonAsync<T>(query).GetAwaiter().GetResult()),
                    "ExecuteAsyncStream" => Collect(connector.ExecuteReaderJsonAsyncStream<T>(query)).GetAwaiter().GetResult(),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                };
        }
        finally
        {
            Sql($"DROP TABLE IF EXISTS {TableName(Children)}; DROP TABLE IF EXISTS {TableName(Roots)};");
        }
    }

    private static void CheckApiRows(List<ApiRoot> rows)
    {
        var first = rows.Single(x => x.Id == 1);
        var empty = rows.Single(x => x.Id == 2);
        Check(first.Name == RootName && first.ApiOnly == null && first.FeaturedId == 10 && first.Featured.Name == "child 雪",
            "API member names, ignores, validation, or constructors changed SQL values");
        Check(first.Payload.SequenceEqual(RootPayload) && empty.Payload == null, "Binary SQL values changed");
        Check(first.Children.OrderBy(x => x.Id).Select(x => x.Name).SequenceEqual(new[] { "child 雪", null }),
            "API collection creation rules changed child values");
        Check(empty.Name == null && empty.Featured == null && empty.Children.Count == 0, "SQL null/empty values were lost");
    }

    private static T Result<T>(SqlResponse<T> response) => response.State == ResponseState.Valid ? response.Result
        : throw new InvalidOperationException("SQL JSON contract check failed", response.Error);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> stream)
    {
        var rows = new List<T>();
        await foreach (var row in stream) rows.Add(row);
        return rows;
    }
    private static void ExpectApiFailure(Action action)
    {
        try { action(); }
        catch (Exception error) when (error.ToString().Contains("API-only")) { return; }
        throw new InvalidOperationException("The API configuration did not exercise its rejecting constructor or converter");
    }

    [SqlTable(Roots)]
    private sealed class PlainRoot
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("db_name")] public string Name { get; set; }
        [SqlColumn("featured_id")] public int? FeaturedId { get; set; }
        [SqlColumn("payload", System.Data.DbType.Binary)] public byte[] Payload { get; set; }
        [SqlOneToMany(nameof(PlainChild.ParentId))] public List<PlainChild> Children { get; set; }
        [SqlOneToOne(nameof(FeaturedId))] public PlainChild Featured { get; set; }
    }

    [SqlTable(Children)]
    private sealed class PlainChild
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("parent_id")] public int ParentId { get; set; }
        [SqlColumn("db_name")] public string Name { get; set; }
    }

    [SqlTable(Roots), JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class ApiRoot
    {
        private ApiRoot() { }
        [JsonConstructor] public ApiRoot(int id) => throw new InvalidOperationException("API-only constructor");
        internal static ApiRoot Create(int id, string name) => new() { Id = id, Name = name };
        [SqlPrimaryKey(false), SqlColumn("id"), JsonIgnore] public int Id { get; private set; }
        [SqlColumn("db_name"), JsonPropertyName("displayName"), JsonConverter(typeof(ApiStringConverter))] public string Name { get; private set; }
        [SqlColumn("featured_id"), JsonIgnore] public int? FeaturedId { get; private set; }
        [SqlColumn("payload", System.Data.DbType.Binary)] public byte[] Payload { get; private set; }
        [SqlOneToMany(nameof(ApiChild.ParentId)), JsonPropertyName("apiChildren")]
        public List<ApiChild> Children { get; set; } = new() { new() { Id = -1 } };
        [SqlOneToOne(nameof(FeaturedId)), JsonPropertyName("apiFeatured")] public ApiChild Featured { get; private set; }
        [SqlIgnore, JsonRequired] public string ApiOnly { get; set; }
    }

    [SqlTable(Children)]
    private sealed class ApiChild
    {
        [SqlPrimaryKey(false), SqlColumn("id"), JsonIgnore] public int Id { get; set; }
        [SqlColumn("parent_id"), JsonIgnore] public int ParentId { get; set; }
        [SqlColumn("db_name"), JsonPropertyName("childLabel"), JsonConverter(typeof(ApiStringConverter))] public string Name { get; set; }
    }

    private sealed class ApiStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => "API-only read";
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue("api:" + value);
    }

    [SqlTable(Roots), JsonConverter(typeof(PoisonConverterFactory))]
    private sealed class GuardedRoot
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("db_name"), JsonConverter(typeof(PoisonConverterFactory))] public string Name { get; set; }
        [SqlColumn("featured_id")] public int? FeaturedId { get; set; }
        [SqlColumn("payload", System.Data.DbType.Binary)] public byte[] Payload { get; set; }
        [SqlOneToMany(nameof(GuardedChild.ParentId)), JsonConverter(typeof(PoisonConverterFactory)), JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
        public GuardedChildren Children { get; set; } = new() { new() { Id = -1 } };
        [SqlOneToOne(nameof(FeaturedId))] public GuardedChild Featured { get; set; }
    }

    [SqlTable(Children), JsonConverter(typeof(PoisonConverterFactory))]
    private sealed class GuardedChild
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("parent_id")] public int ParentId { get; set; }
        [SqlColumn("db_name"), JsonConverter(typeof(PoisonConverterFactory))] public string Name { get; set; }
    }

    [JsonConverter(typeof(PoisonConverterFactory))]
    private sealed class GuardedChildren : List<GuardedChild> { }

    private sealed class PoisonConverterFactory : JsonConverterFactory
    {
        internal static int Attempts;
        public PoisonConverterFactory() { Interlocked.Increment(ref Attempts); throw new InvalidOperationException("API-only converter"); }
        public override bool CanConvert(Type typeToConvert) => true;
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => throw new InvalidOperationException("API-only converter");
    }
}
