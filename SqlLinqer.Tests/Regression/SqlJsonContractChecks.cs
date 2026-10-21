using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

// Runs against actual database-produced JSON in every native provider suite.
internal static class SqlJsonContractChecks
{
    private const string Roots = "json_contract_roots", Children = "json_contract_children";
    private const string RootName = "O'Reilly \"quoted\" \\ \n雪";

    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        string Q(string value) => value.DbWrap(connector.DbFlavor);
        string TableName(string value) => string.IsNullOrEmpty(connector.Options.DefaultSchema) ? Q(value)
            : Q(connector.Options.DefaultSchema) + "." + Q(value);
        void Sql(string text) => Result(connector.ExecuteNonQuery(new RenderedQuery(text)));
        string textType = connector.DbFlavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "NVARCHAR(128)" : "VARCHAR(128)";
        try
        {
            Sql($"CREATE TABLE {TableName(Roots)} ({Q("id")} INTEGER PRIMARY KEY, {Q("db_name")} {textType} NULL, {Q("featured_id")} INTEGER NULL); " +
                $"CREATE TABLE {TableName(Children)} ({Q("id")} INTEGER PRIMARY KEY, {Q("parent_id")} INTEGER NOT NULL, {Q("db_name")} {textType} NULL);");
            Result(new InsertQuery<PlainRoot>(connector.Options.DefaultSchema).InsertAuto(new[]
            {
                new PlainRoot { Id = 1, Name = RootName, FeaturedId = 10 },
                new PlainRoot { Id = 2, Name = null },
            }).Execute(connector));
            Result(new InsertQuery<PlainChild>(connector.Options.DefaultSchema).InsertAuto(new[]
            {
                new PlainChild { Id = 10, ParentId = 1, Name = "child 雪" },
                new PlainChild { Id = 11, ParentId = 1, Name = null },
            }).Execute(connector));

            foreach (bool asynchronous in new[] { false, true })
            {
                test($"SQL JSON: API metadata and serializer changes preserve nested database values, async={asynchronous}", () =>
                {
                    var plainQuery = new SelectQuery<PlainRoot>(connector.Options.DefaultSchema).SelectRootColumns().Select(x => x.Children).Select(x => x.Featured);
                    var baseline = Read(plainQuery, asynchronous);
                    var query = new SelectQuery<ApiRoot>(connector.Options.DefaultSchema).SelectRootColumns().Select(x => x.Children).Select(x => x.Featured);
                    var rows = Read(query, asynchronous);
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
                    CheckApiRows(Read(query, asynchronous));
                    var captured = before.Render(connector);
                    Check(!captured.Text.Contains("displayName") && !captured.Text.Contains("ApiOnly"), "API names entered the SQL projection");
                    CheckApiRows(Result(asynchronous ? connector.ExecuteReaderJsonAsync<ApiRoot>(captured).GetAwaiter().GetResult()
                        : connector.ExecuteReaderJson<ApiRoot>(captured)));
                    Check(baseline.Single(x => x.Id == 1).Name == RootName && baseline.Single(x => x.Id == 2).Children.Count == 0,
                        "Baseline database JSON differed from the expected values");
                });

                test($"SQL JSON: throwing API converters on roots, members and collections never run, async={asynchronous}", () =>
                {
                    var query = new SelectQuery<GuardedRoot>(connector.Options.DefaultSchema).SelectRootColumns().Select(x => x.Children).Select(x => x.Featured);
                    // Warm the API's failed metadata cache before SQL sees this type.
                    ExpectApiFailure(() => JsonSerializer.Serialize(new GuardedRoot()));
                    int attempts = PoisonConverterFactory.Attempts;
                    for (int i = 0; i < 2; i++)
                    {
                        var rows = Read(query, asynchronous);
                        var first = rows.Single(x => x.Id == 1);
                        var empty = rows.Single(x => x.Id == 2);
                        Check(first.Name == RootName && first.Children.Count == 2 && first.Featured.Name == "child 雪",
                            "API converters changed nested SQL results");
                        Check(empty.Name == null && empty.Featured == null && empty.Children.Count == 0,
                            "API converters changed SQL null or empty collection values");
                    }
                    Check(PoisonConverterFactory.Attempts == attempts, "SQL loading constructed an API converter");
                });

                test($"SQL JSON: API attributes leave insert, update, filter and delete mappings intact, async={asynchronous}", () =>
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
                        Check(Read(read, asynchronous, transaction).Single().Name == "updated 雪", "API metadata changed SQL write or read mappings");
                        var delete = new DeleteQuery<ApiRoot>(connector.Options.DefaultSchema).Where(x => x.Id, 3, SqlOp.EQ);
                        Result(asynchronous ? delete.ExecuteAsync(connector, transaction).GetAwaiter().GetResult() : delete.Execute(connector, transaction));
                        Check(Read(read, asynchronous, transaction).Count == 0, "API metadata changed the delete filter");
                    }
                    finally { transaction.Rollback(); }
                });
            }

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

            List<T> Read<T>(SelectQuery<T> query, bool asynchronous, DbTransaction transaction = null) where T : class
                => Result(asynchronous ? query.ExecuteAsync(connector, transaction).GetAwaiter().GetResult() : query.Execute(connector, transaction));
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
        Check(first.Children.OrderBy(x => x.Id).Select(x => x.Name).SequenceEqual(new[] { "child 雪", null }),
            "API collection creation rules changed child values");
        Check(empty.Name == null && empty.Featured == null && empty.Children.Count == 0, "SQL null/empty values were lost");
    }

    private static T Result<T>(SqlResponse<T> response) => response.State == ResponseState.Valid ? response.Result
        : throw new InvalidOperationException("SQL JSON contract check failed", response.Error);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
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
