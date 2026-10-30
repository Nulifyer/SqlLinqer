using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

try
{
    if (args.Length == 4 && args[0] == "setup")
    {
        if (File.Exists(args[1])) throw new IOException("Setup never replaces an existing database");
        int rows = Nonnegative(args[2]), width = Nonnegative(args[3]);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = args[1], Pooling = false }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Note TEXT, Active INTEGER NOT NULL);
            CREATE TABLE StoredJson (Id INTEGER PRIMARY KEY, Active INTEGER NOT NULL, Payload TEXT NOT NULL);
            CREATE TABLE FixtureInfo (Rows INTEGER, Width INTEGER);
            INSERT INTO FixtureInfo VALUES ($rows, $width);
            """;
        command.Parameters.AddWithValue("$rows", rows);
        command.Parameters.AddWithValue("$width", width);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO Customers VALUES ($id, $name, $note, $active)";
        command.Parameters.Add("$id", SqliteType.Integer);
        command.Parameters.Add("$name", SqliteType.Text);
        command.Parameters.Add("$note", SqliteType.Text);
        command.Parameters.Add("$active", SqliteType.Integer);
        for (int id = rows; id >= 1; id--)
        {
            command.Parameters[0].Value = id;
            command.Parameters[1].Value = Fixture.Name(id, width);
            command.Parameters[2].Value = (object?)Fixture.Note(id) ?? DBNull.Value;
            command.Parameters[3].Value = Fixture.Active(id);
            command.ExecuteNonQuery();
        }
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO StoredJson SELECT Id, Active, " + Fixture.JsonExpression + " FROM Customers";
        command.ExecuteNonQuery();
        transaction.Commit();
        Console.WriteLine(JsonSerializer.Serialize(new { rows, width }));
        return 0;
    }
    if (args.Length != 5 || args[0] != "measure")
        throw new ArgumentException("setup <new-file> <rows> <width> | measure <mode> <file> <warmup> <operations>");
    string mode = args[1];
    if (!new[] { "native", "tabular", "model", "json-standard", "stored-standard" }.Contains(mode))
        throw new ArgumentException("Unknown mode");
    string connectionString = new SqliteConnectionStringBuilder { DataSource = args[2], Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
    int warmup = Positive(args[3]), operations = Positive(args[4]);
    int rowCount, widthCount;
    string engine;
    using (var connection = new SqliteConnection(connectionString))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Rows, Width, sqlite_version() FROM FixtureInfo";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("Missing fixture metadata");
        rowCount = reader.GetInt32(0); widthCount = reader.GetInt32(1); engine = reader.GetString(2);
    }
    // SQL and bindings are created once. This experiment excludes builder and
    // snapshot costs and changes no query compiler behavior.
    var query = new RenderedQuery();
    string active = query.Parameters.AddParameter(1), limit = query.Parameters.AddParameter(rowCount);
    query.Text = mode.StartsWith("stored", StringComparison.Ordinal)
        ? $"SELECT Payload FROM StoredJson WHERE Active = {active} AND Id <= {limit} ORDER BY Id"
        : mode == "json-standard"
            ? $"SELECT {Fixture.JsonExpression} FROM Customers WHERE Active = {active} AND Id <= {limit} ORDER BY Id"
            : $"SELECT Id, Name, Note, Active FROM Customers WHERE Active = {active} AND Id <= {limit} ORDER BY Id";
    var connector = new ConnectionFactoryConnector(() => new SqliteConnection(connectionString), DbFlavor.Sqlite);
    var standardOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
    var expected = new Digest();
    for (int id = 1; id <= rowCount; id++)
        if (Fixture.Active(id) == 1)
            expected.Add(new FlatRow { Id = id, Name = Fixture.Name(id, widthCount), Note = Fixture.Note(id), Active = 1 });

    async Task<Digest> Consume()
    {
        List<FlatRow> rows;
        if (mode is "tabular" or "model")
        {
            var response = mode == "tabular" ? await connector.ExecuteReaderListAsync<FlatRow>(query)
                : await connector.ExecuteReaderModelAsync<FlatRow>(query);
            if (response.State != ResponseState.Valid) throw response.Error;
            rows = response.Result;
        }
        else
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = query.Text;
            command.Parameters.AddWithValue(active, 1);
            command.Parameters.AddWithValue(limit, rowCount);
            using var reader = await command.ExecuteReaderAsync();
            rows = [];
            while (await reader.ReadAsync())
                rows.Add(mode == "native"
                    ? new FlatRow { Id = reader.GetInt32(0), Name = reader.GetString(1), Note = reader.IsDBNull(2) ? null : reader.GetString(2), Active = reader.GetInt32(3) }
                    : JsonSerializer.Deserialize<FlatRow>(reader.GetString(0), standardOptions)!);
        }
        var result = new Digest();
        foreach (var row in rows) result.Add(row);
        return result;
    }
    for (int i = 0; i < warmup; i++) (await Consume()).Verify(expected);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var digests = new Digest[operations];
    long allocatedStart = GC.GetTotalAllocatedBytes(true), started = Stopwatch.GetTimestamp();
    for (int i = 0; i < operations; i++) digests[i] = await Consume();
    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    long allocated = GC.GetTotalAllocatedBytes(true) - allocatedStart;
    foreach (var digest in digests) digest.Verify(expected);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode, fixtureRows = rowCount, width = widthCount, resultRows = expected.Rows, fingerprint = expected.Fingerprint,
        warmup, operations, elapsedMsPerOperation = elapsed / operations, allocatedBytesPerOperation = (double)allocated / operations,
        sql = query.Text, sqliteVersion = engine, runtime = RuntimeInformation.FrameworkDescription
    }));
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

static int Nonnegative(string text) => int.TryParse(text, out int value) && value >= 0 ? value : throw new ArgumentException("Expected a nonnegative integer");
static int Positive(string text) => int.TryParse(text, out int value) && value > 0 ? value : throw new ArgumentException("Expected a positive integer");

[SqlTable("Customers")]
public sealed class FlatRow
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public int Active { get; set; }
}

internal static class Fixture
{
    public const string JsonExpression = "json_object('Id',Id,'Name',Name,'Note',Note,'Active',Active)";
    public static int Active(int id) => id % 3 == 0 ? 0 : 1;
    public static string Name(int id, int width) => $"customer-{id.ToString(CultureInfo.InvariantCulture)}-雪😀-" + new string('x', width);
    public static string? Note(int id) => id % 5 == 0 ? null : $"note \"{id.ToString(CultureInfo.InvariantCulture)}\"\n雪😀";
}

internal sealed class Digest
{
    private ulong hash = 14695981039346656037UL;
    public int Rows { get; private set; }
    public string Fingerprint => hash.ToString("x16", CultureInfo.InvariantCulture);
    private void Number(int value) { unchecked { hash = (hash ^ (uint)value) * 1099511628211UL; } }
    private void Text(string? value) { Number(value?.Length ?? -1); if (value != null) foreach (char c in value) Number(c); }
    public void Add(FlatRow row) { Rows++; Number(row.Id); Text(row.Name); Text(row.Note); Number(row.Active); }
    public void Verify(Digest expected)
    {
        if (Rows != expected.Rows || hash != expected.hash)
            throw new InvalidDataException($"Result mismatch: {Rows}/{expected.Rows} roots; {Fingerprint}/{expected.Fingerprint}");
    }
}
