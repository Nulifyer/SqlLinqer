using Microsoft.Data.Sqlite;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Diagnostics;
using SqlLinqer.Modeling;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SampleDatabase>();
builder.Services.AddSqlLinqer(
    provider => provider.GetRequiredService<SampleDatabase>().CreateConnection(),
    DbFlavor.Sqlite,
    (provider, options) =>
    {
        options.CommandTimeout = TimeSpan.FromSeconds(10);
        options.Observer = new SqlLinqerDiagnostics(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(SqlLinqerDiagnostics.LoggerCategoryName),
            options: null,
            optionsProvider: () => provider.GetRequiredService<IConfiguration>()
                .GetSection("SqlLinqer:Diagnostics").Get<SqlLinqerDiagnosticsOptions>() ?? new());
    });

var app = builder.Build();
app.Services.GetRequiredService<SampleDatabase>();
app.MapGet("/", () => new { endpoints = new[] { "/users", "/users?name=Ada", "/users/page?page=1&pageSize=2", "/users/stream" } });
app.MapGet("/users", async (BaseConnector db, string? name, CancellationToken cancellationToken) =>
{
    var query = db.BeginSelect<User>().SelectRootColumns().Tag("users.search");
    if (!string.IsNullOrWhiteSpace(name)) query.Where(user => user.Name, $"%{name}%", SqlOp.LIKE);
    return (await query.OrderBy(user => user.Id, SqlDir.ASC).ExecuteAsync(cancellationToken: cancellationToken)).GetResultOrThrow();
});
app.MapGet("/users/page", async (BaseConnector db, int? page, int? pageSize, CancellationToken cancellationToken) =>
{
    int requestedPage = page ?? 1, requestedSize = pageSize ?? 2;
    if (requestedPage < 1 || requestedSize < 1 || requestedSize > 100)
        return Results.BadRequest(new { error = "page must be positive; pageSize must be between 1 and 100" });
    var response = (await db.BeginSelect<User>().SelectRootColumns().Tag("users.page")
        .OrderBy(user => user.Id, SqlDir.ASC).Page(requestedPage, requestedSize)
        .ExecuteWithTotalCountAsync(cancellationToken: cancellationToken)).EnsureSuccess();
    return Results.Ok(new { items = response.Result, total = response.TotalResults });
});
app.MapGet("/users/stream", (BaseConnector db, CancellationToken cancellationToken) =>
    db.BeginSelect<User>().SelectRootColumns().Tag("users.stream").OrderBy(user => user.Id, SqlDir.ASC)
        .ExecuteAsyncStream(cancellationToken: cancellationToken));
app.Run();

[SqlTable("users")]
public sealed class User
{
    [SqlPrimaryKey] public int Id { get; set; }
    [SqlColumn("user_name"), JsonPropertyName("displayName")]
    public string Name { get; set; } = "";
}

sealed class SampleDatabase : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "sqllinqer-example-" + Guid.NewGuid().ToString("N") + ".db");
    public SampleDatabase()
    {
        using var connection = CreateConnection(); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE users (Id INTEGER PRIMARY KEY, user_name TEXT NOT NULL);";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO users (user_name) VALUES ($name)";
        command.Parameters.Add("$name", SqliteType.Text);
        foreach (string name in new[] { "Ada Lovelace", "Grace Hopper", "Margaret Hamilton" })
        { command.Parameters[0].Value = name; command.ExecuteNonQuery(); }
    }
    public SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
    public void Dispose() => File.Delete(path);
}
