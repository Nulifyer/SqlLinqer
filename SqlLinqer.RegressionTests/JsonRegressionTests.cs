using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SqlLinqer.Components.Render;

namespace SqlLinqer.RegressionTests;

internal static class JsonRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
        {
            test($"JSON loads fields, private setters and getter-only properties for {flavor}", () =>
            {
                var model = Read<JsonModel>("""
                    {"id":7,"Field":8,"ReadOnly":"loaded","Inherited":"base",
                     "Computed":"ignored","Unknown":"ignored"}
                    """, flavor);
                Expect(model.Id == 7 && model.Field == 8 && model.ReadOnly == "loaded" && model.Inherited == "base", "Model members were not populated");
                Expect(model.PrivateConstructorCalled && model.Computed == "computed", "Constructor or computed property behavior changed");
            });
            test($"JSON loads nested objects, collections and nulls for {flavor}", () =>
            {
                var model = Read<JsonModel>("""
                    {"Child":{"Id":1},"Children":[{"Id":2},{"Id":3}],"Optional":null,"OptionalEnabled":null}
                    """, flavor);
                Expect(model.Child.Id == 1 && model.Children.Select(x => x.Id).SequenceEqual(new[] { 2, 3 }), "Nested models were not loaded");
                Expect(model.Optional == null && model.OptionalEnabled == null, "Nullable members changed");
            });
            test($"JSON loads string enums, quoted numbers and numeric booleans for {flavor}", () =>
            {
                var model = Read<JsonModel>("""{"Gender":"F","Field":"12","Enabled":1,"OptionalEnabled":0} """, flavor);
                Expect(model.Gender == Gender.F && model.Field == 12 && model.Enabled && model.OptionalEnabled == false, "Database value conversion failed");
            });
            test($"JSON loads numeric enums and native booleans for {flavor}", () =>
            {
                var model = Read<JsonModel>("""{"Gender":1,"Enabled":false,"OptionalEnabled":true} """, flavor);
                Expect(model.Gender == Gender.F && !model.Enabled && model.OptionalEnabled == true, "Native value conversion failed");
            });
            test($"JSON loads database date strings independent of culture for {flavor}", () =>
            {
                var previous = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    var model = Read<JsonModel>("""
                        {"When":"2025-06-15 13:14:15.123456","Offset":"2025-06-15 13:14:15+02:00",
                         "Key":"167aa39f-73f3-4516-a78b-bcedf95ee59e"}
                        """, flavor);
                    Expect(model.When == new DateTime(2025, 6, 15, 13, 14, 15).AddTicks(1234560), "Database datetime changed");
                    Expect(model.Offset.Offset == TimeSpan.FromHours(2) && model.Offset.Hour == 13, "Offset was lost");
                    Expect(model.Key == Guid.Parse("167aa39f-73f3-4516-a78b-bcedf95ee59e"), "GUID was not loaded");
                }
                finally { CultureInfo.CurrentCulture = previous; }
            });
            test($"JSON loads ISO UTC dates for {flavor}", () =>
            {
                var model = Read<JsonModel>("""{"When":"2025-06-15T13:14:15Z","Offset":"2025-06-15T13:14:15Z"} """, flavor);
                Expect(model.When.Kind == DateTimeKind.Utc && model.Offset.Offset == TimeSpan.Zero, "UTC dates lost timezone information");
            });
            test($"JSON honors native attributes for {flavor}", () =>
            {
                var model = Read<AttributeModel>("""{"db_name":"loaded","Ignored":"changed","IgnoredOnRead":"changed","Secret":"included"} """, flavor);
                Expect(model.Name == "loaded" && model.Ignored == "initial" && model.IgnoredOnRead == "initial" && model.ReadSecret() == "included", "Native attributes were not honored");
            });
            test($"JSON honors explicitly annotated constructors for {flavor}", () =>
            {
                var model = Read<AnnotatedConstructorModel>("""{"Id":9} """, flavor);
                Expect(model.Id == 9 && model.Source == "annotated", "Private default constructor overrode JsonConstructor");
            });
            test($"JSON preserves anonymous aggregate projections for {flavor}", () =>
            {
                var model = ReadLike(new { Id = 0, Name = "", Optional = (int?)null }, """{"Id":3,"Name":"aggregate","Optional":null} """, flavor);
                Expect(model.Id == 3 && model.Name == "aggregate" && model.Optional == null, "Anonymous projection was not loaded");
            });
            test($"JSON returns an empty collection for no rows for {flavor}", () =>
            {
                var database = new FakeDatabase { JsonRows = Array.Empty<string>() };
                var response = new FakeConnector(database, flavor).ExecuteReaderJson<JsonModel>(new RenderedQuery("SELECT json"));
                Expect(response.State == ResponseState.Valid && response.Result.Count == 0, "Empty results failed");
            });
            test($"JSON parse errors report failure and dispose resources for {flavor}", () =>
            {
                var database = new FakeDatabase { JsonRows = new[] { "invalid JSON" } };
                var response = new FakeConnector(database, flavor).ExecuteReaderJson<JsonModel>(new RenderedQuery("SELECT json"));
                Expect(response.State == ResponseState.Error && response.Error.InnerException is JsonException, "JSON failure was not wrapped in the response");
                Expect(database.Connections.Single().IsDisposed && database.Executed.Single().IsDisposed, "JSON failure leaked resources");
            });
        }
    }

    private static T Read<T>(string json, DbFlavor flavor) where T : class
    {
        // SQL Server can split one FOR JSON result over multiple database rows.
        var database = new FakeDatabase { JsonRows = flavor == DbFlavor.SqlServer ? new[] { "[", json, "]" } : new[] { json } };
        var response = new FakeConnector(database, flavor).ExecuteReaderJson<T>(new RenderedQuery("SELECT json"));
        if (response.State != ResponseState.Valid)
            throw response.Error;
        return response.Result.Single();
    }

    private static T ReadLike<T>(T template, string json, DbFlavor flavor) where T : class => Read<T>(json, flavor);
    private static void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }

    private class JsonModelBase
    {
        public string Inherited { get; private set; }
    }

    private sealed class JsonModel : JsonModelBase
    {
        private JsonModel() { PrivateConstructorCalled = true; }
        public bool PrivateConstructorCalled { get; }
        public int Id { get; private set; }
        public int Field = 0;
        public string ReadOnly { get; }
        public string Computed => "computed";
        public ChildModel Child { get; set; }
        public IEnumerable<ChildModel> Children = Array.Empty<ChildModel>();
        public int? Optional { get; set; }
        public bool Enabled { get; set; }
        public bool? OptionalEnabled { get; set; }
        public Gender Gender { get; set; }
        public DateTime When { get; set; }
        public DateTimeOffset Offset { get; set; }
        public Guid Key { get; set; }
    }

    private enum Gender { M, F }
    private sealed class ChildModel { public int Id { get; set; } }

    private sealed class AttributeModel
    {
        [JsonPropertyName("db_name")] public string Name { get; private set; }
        [JsonIgnore] public string Ignored { get; } = "initial";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public string IgnoredOnRead { get; } = "initial";
        [JsonInclude] private string Secret { get; set; }
        public string ReadSecret() => Secret;
    }

    private sealed class AnnotatedConstructorModel
    {
        private AnnotatedConstructorModel() { Source = "private"; }
        [JsonConstructor] public AnnotatedConstructorModel(int id) { Id = id; Source = "annotated"; }
        public int Id { get; }
        public string Source { get; }
    }
}
