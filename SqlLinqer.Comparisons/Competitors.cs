using System.Data.Common;
using Dapper;
using SqlKata;
using SqlKata.Compilers;
using SqlKata.Execution;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Comparisons;

internal interface ICompetitor
{
    Task<QueryResult> Load(Workload workload);
    IAsyncEnumerable<Customer> Stream(Workload workload);
}

internal static class Competitors
{
    public static readonly string[] Names = ["sqllinqer", "dapper", "sqlkata", "linq2db", "ef-single", "ef-split"];
    public static ICompetitor Create(string name, string connectionString, CommandLog log) => name switch
    {
        "sqllinqer" => new SqlLinqerQueries(connectionString, log),
        "dapper" => new DapperQueries(connectionString, log),
        "sqlkata" => new SqlKataQueries(connectionString, log),
        "linq2db" => new LinqQueries(connectionString, log),
        "ef-single" => new EfQueries(connectionString, log, false),
        "ef-split" => new EfQueries(connectionString, log, true),
        _ => throw new ArgumentException($"Unknown library: {name}")
    };

    public static QueryResult Assemble(List<Customer> roots, IEnumerable<Order> orders, IEnumerable<Label> labels, long? total)
    {
        var byId = roots.ToDictionary(x => x.Id);
        foreach (var order in orders) byId[order.CustomerId].Orders.Add(order);
        foreach (var label in labels) byId[label.CustomerId].Labels.Add(label);
        return new QueryResult(roots, total);
    }
}

internal sealed class SqlLinqerQueries : ICompetitor
{
    private readonly BaseConnector connector;
    public SqlLinqerQueries(string connectionString, CommandLog log) => connector = new ConnectionFactoryConnector(
        () => new LoggedConnection(connectionString, log), DbFlavor.Sqlite);

    private static SelectQuery<Customer> Query(Workload w)
    {
        var query = new SelectQuery<Customer>().SelectRootColumns()
            .Where(x => x.Active, 1, SqlOp.EQ).Where(x => x.Id, w.Rows, SqlOp.LTE);
        if (w.Graph) query.Select(x => x.Orders).Select(x => x.Labels).OrderByAuto(true);
        query.OrderBy(x => x.Id, SqlDir.ASC);
        if (w.Page) query.Limit(w.PageSize, w.Offset);
        return query;
    }
    public async Task<QueryResult> Load(Workload w)
    {
        if (w.Page)
        {
            var response = await Query(w).ExecuteWithTotalCountAsync(connector);
            if (response.State != ResponseState.Valid) throw response.Error;
            return new QueryResult(response.Result, response.TotalResults);
        }
        else
        {
            var response = await Query(w).ExecuteAsync(connector);
            if (response.State != ResponseState.Valid) throw response.Error;
            return new QueryResult(response.Result);
        }
    }
    public IAsyncEnumerable<Customer> Stream(Workload w) => Query(w).ExecuteAsyncStream(connector);
}

internal sealed class DapperQueries(string connectionString, CommandLog log) : ICompetitor
{
    private const string Filter = "FROM Customers WHERE Active = @active AND Id <= @rows";
    private static string Root(Workload w) => "SELECT Id, Name, Note, Active " + Filter + " ORDER BY Id" + (w.Page ? " LIMIT @size OFFSET @offset" : "");
    private static object Parameters(Workload w) => new { active = 1, rows = w.Rows, size = w.PageSize, offset = w.Offset };

    public async Task<QueryResult> Load(Workload w)
    {
        await using var connection = new LoggedConnection(connectionString, log);
        await connection.OpenAsync();
        if (!w.Graph) return new QueryResult((await connection.QueryAsync<Customer>(Root(w), Parameters(w))).AsList());
        string root = Root(w);
        string keys = "SELECT Id " + Filter + " ORDER BY Id" + (w.Page ? " LIMIT @size OFFSET @offset" : "");
        string sql = (w.Page ? "SELECT COUNT(*) " + Filter + ";" : "") + root + ";"
            + $"SELECT Id, CustomerId, Amount FROM Orders WHERE CustomerId IN ({keys}) ORDER BY CustomerId, Id;"
            + $"SELECT Id, CustomerId, Text FROM Labels WHERE CustomerId IN ({keys}) ORDER BY CustomerId, Id;";
        // Three result sets in one ADO.NET command, with no join multiplication.
        using var reader = await connection.QueryMultipleAsync(sql, Parameters(w));
        long? total = w.Page ? await reader.ReadSingleAsync<long>() : null;
        var roots = (await reader.ReadAsync<Customer>()).AsList();
        var orders = await reader.ReadAsync<Order>();
        var labels = await reader.ReadAsync<Label>();
        return Competitors.Assemble(roots, orders, labels, total);
    }
    public async IAsyncEnumerable<Customer> Stream(Workload w)
    {
        await using var connection = new LoggedConnection(connectionString, log);
        await connection.OpenAsync();
        await foreach (var row in connection.QueryUnbufferedAsync<Customer>(Root(w), Parameters(w))) yield return row;
    }
}

internal sealed class SqlKataQueries(string connectionString, CommandLog log) : ICompetitor
{
    private readonly SqliteCompiler compiler = new();
    private static Query Root(QueryFactory db, Workload w)
    {
        var query = db.Query("Customers").Select("Id", "Name", "Note", "Active")
            .Where("Active", 1).Where("Id", "<=", w.Rows).OrderBy("Id");
        return w.Page ? query.Limit(w.PageSize).Offset(w.Offset) : query;
    }
    public async Task<QueryResult> Load(Workload w)
    {
        using var db = new QueryFactory(new LoggedConnection(connectionString, log), compiler);
        await ((DbConnection)db.Connection).OpenAsync();
        long? total = w.Page ? await db.Query("Customers").Where("Active", 1).Where("Id", "<=", w.Rows).CountAsync<long>() : null;
        var root = Root(db, w);
        var roots = (await root.GetAsync<Customer>()).AsList();
        if (!w.Graph) return new QueryResult(roots, total);
        var keys = root.Clone().ClearComponent("select").Select("Id");
        var orders = await db.Query("Orders").Select("Id", "CustomerId", "Amount").WhereIn("CustomerId", keys)
            .OrderBy("CustomerId", "Id").GetAsync<Order>();
        var labels = await db.Query("Labels").Select("Id", "CustomerId", "Text").WhereIn("CustomerId", keys)
            .OrderBy("CustomerId", "Id").GetAsync<Label>();
        return Competitors.Assemble(roots, orders, labels, total);
    }
    public async IAsyncEnumerable<Customer> Stream(Workload w)
    {
        using var db = new QueryFactory(new LoggedConnection(connectionString, log), compiler);
        await ((DbConnection)db.Connection).OpenAsync();
        var compiled = compiler.Compile(Root(db, w));
        // SqlKata's GetAsync buffers. Use its compiler with Dapper's public
        // unbuffered API for this explicitly labelled composition.
        await foreach (var row in ((DbConnection)db.Connection).QueryUnbufferedAsync<Customer>(compiled.Sql, compiled.NamedBindings)) yield return row;
    }
}
