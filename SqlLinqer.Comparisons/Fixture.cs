using System.Globalization;
using Microsoft.Data.Sqlite;
using SqlLinqer.Modeling;
using LinqToDB.Mapping;

namespace SqlLinqer.Comparisons;

[SqlTable("Customers"), Table("Customers")]
public sealed class Customer
{
    [SqlPrimaryKey, SqlAutoOrderBy, PrimaryKey, Column] public int Id { get; set; }
    [Column] public string Name { get; set; } = "";
    [Column] public string? Note { get; set; }
    [Column] public int Active { get; set; }
    [SqlOneToMany(nameof(Order.CustomerId))]
    [Association(ThisKey = nameof(Id), OtherKey = nameof(Order.CustomerId))]
    public List<Order> Orders { get; set; } = [];
    [SqlOneToMany(nameof(Label.CustomerId))]
    [Association(ThisKey = nameof(Id), OtherKey = nameof(Label.CustomerId))]
    public List<Label> Labels { get; set; } = [];
}

[SqlTable("Orders"), Table("Orders")]
public sealed class Order
{
    [SqlPrimaryKey, SqlAutoOrderBy, PrimaryKey, Column] public int Id { get; set; }
    [Column] public int CustomerId { get; set; }
    [Column] public int Amount { get; set; }
}

[SqlTable("Labels"), Table("Labels")]
public sealed class Label
{
    [SqlPrimaryKey, SqlAutoOrderBy, PrimaryKey, Column] public int Id { get; set; }
    [Column] public int CustomerId { get; set; }
    [Column] public string Text { get; set; } = "";
}

internal sealed record Workload(string Name, int Rows, int PageSize, int Offset)
{
    public bool Graph => Name is "graph" or "page";
    public bool Page => Name == "page";
    public bool Stream => Name == "stream";
}

// Each adapter returns these same models, including all scalar columns and both
// ordered collections for graph/page workloads. Streams retain no previous rows.
internal sealed record QueryResult(List<Customer> Rows, long? Total = null);

internal static class Fixture
{
    public static string ConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false
    }.ToString();

    public static string Name(int id, int width) => $"customer-{id.ToString(CultureInfo.InvariantCulture)}-雪😀-" + new string('x', width);
    public static string? Note(int id) => id % 5 == 0 ? null : $"note \"{id.ToString(CultureInfo.InvariantCulture)}\"\n雪😀";
    public static int Active(int id) => id % 3 == 0 ? 0 : 1;
    public static string LabelText(int id, int slot) => $"label-{id.ToString(CultureInfo.InvariantCulture)}-{slot}-é雪";

    public static void Create(string path, int rows, int width)
    {
        if (File.Exists(path)) throw new IOException("Fixture already exists; setup never overwrites a database.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Note TEXT, Active INTEGER NOT NULL);
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Amount INTEGER NOT NULL);
            CREATE TABLE Labels (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Text TEXT NOT NULL);
            CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId, Id);
            CREATE INDEX IX_Labels_CustomerId ON Labels(CustomerId, Id);
            CREATE TABLE FixtureInfo (Rows INTEGER NOT NULL, Width INTEGER NOT NULL);
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
        // Reverse insertion order prevents accidental reliance on insertion order.
        for (int id = rows; id >= 1; id--)
        {
            command.Parameters[0].Value = id;
            command.Parameters[1].Value = Name(id, width);
            command.Parameters[2].Value = (object?)Note(id) ?? DBNull.Value;
            command.Parameters[3].Value = Active(id);
            command.ExecuteNonQuery();
        }
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO Orders VALUES ($id, $customer, $amount)";
        command.Parameters.Add("$id", SqliteType.Integer);
        command.Parameters.Add("$customer", SqliteType.Integer);
        command.Parameters.Add("$amount", SqliteType.Integer);
        for (int id = rows; id >= 1; id--)
            for (int slot = id % 5; slot >= 1; slot--)
            {
                command.Parameters[0].Value = checked(id * 10 + slot);
                command.Parameters[1].Value = id;
                command.Parameters[2].Value = id % 1000 * 100 + slot;
                command.ExecuteNonQuery();
            }
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO Labels VALUES ($id, $customer, $text)";
        command.Parameters.Add("$id", SqliteType.Integer);
        command.Parameters.Add("$customer", SqliteType.Integer);
        command.Parameters.Add("$text", SqliteType.Text);
        for (int id = rows; id >= 1; id--)
            for (int slot = id % 4; slot >= 1; slot--)
            {
                command.Parameters[0].Value = checked(id * 10 + slot);
                command.Parameters[1].Value = id;
                command.Parameters[2].Value = LabelText(id, slot);
                command.ExecuteNonQuery();
            }
        transaction.Commit();
        command.Transaction = null;
        command.CommandText = "ANALYZE";
        command.Parameters.Clear();
        command.ExecuteNonQuery();
    }

    public static (int Rows, int Width, string Engine) ReadInfo(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Rows, Width, sqlite_version() FROM FixtureInfo";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("Missing fixture metadata");
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2));
    }

    // Expected values come from fixture rules, independently of every query API.
    public static Digest Expected(Workload workload, int width)
    {
        var digest = new Digest();
        IEnumerable<int> ids = Enumerable.Range(1, workload.Rows).Where(id => Active(id) == 1);
        if (workload.Page) ids = ids.Skip(workload.Offset).Take(workload.PageSize);
        foreach (int id in ids)
        {
            var customer = new Customer { Id = id, Name = Name(id, width), Note = Note(id), Active = Active(id) };
            if (workload.Graph)
            {
                for (int slot = 1; slot <= id % 5; slot++)
                    customer.Orders.Add(new Order { Id = id * 10 + slot, CustomerId = id, Amount = id % 1000 * 100 + slot });
                for (int slot = 1; slot <= id % 4; slot++)
                    customer.Labels.Add(new Label { Id = id * 10 + slot, CustomerId = id, Text = LabelText(id, slot) });
            }
            digest.Add(customer, workload.Graph);
        }
        return digest;
    }
}

// Allocation-free FNV-style fingerprint of every scalar, collection boundary, and
// sequence position. The identical consumption work is timed for every library.
internal sealed class Digest
{
    private ulong hash = 14695981039346656037UL;
    public int Rows { get; private set; }
    public long IdSum { get; private set; }
    public int Orders { get; private set; }
    public int Labels { get; private set; }
    public string Fingerprint => hash.ToString("x16", CultureInfo.InvariantCulture);
    private void Number(int value) { unchecked { hash = (hash ^ (uint)value) * 1099511628211UL; } }
    private void Text(string? value)
    {
        Number(value?.Length ?? -1);
        if (value != null) foreach (char c in value) Number(c);
    }
    public void Add(Customer row, bool graph)
    {
        Rows++;
        IdSum += row.Id;
        Number(row.Id); Text(row.Name); Text(row.Note); Number(row.Active);
        if (!graph) return;
        if (row.Orders == null || row.Labels == null) throw new InvalidDataException("Expected empty collections, not null");
        Number(row.Orders.Count);
        foreach (var order in row.Orders) { Number(order.Id); Number(order.CustomerId); Number(order.Amount); Orders++; }
        Number(row.Labels.Count);
        foreach (var label in row.Labels) { Number(label.Id); Number(label.CustomerId); Text(label.Text); Labels++; }
    }
    public void Verify(Digest expected)
    {
        if (Rows != expected.Rows || IdSum != expected.IdSum || Orders != expected.Orders || Labels != expected.Labels || Fingerprint != expected.Fingerprint)
            throw new InvalidDataException($"Result mismatch: rows={Rows}/{expected.Rows}, orders={Orders}/{expected.Orders}, labels={Labels}/{expected.Labels}, fingerprint={Fingerprint}/{expected.Fingerprint}");
    }
}
