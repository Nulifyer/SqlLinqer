using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;

namespace SqlLinqer.Comparisons;

internal sealed class LinqQueries : ICompetitor
{
    private readonly DataOptions options;
    public LinqQueries(string connectionString, CommandLog log) => options = new DataOptions()
        .UseSQLite(connectionString, SQLiteProvider.Microsoft).UseInterceptors(new LinqCommandLog(log));

    private static IQueryable<Customer> Query(DataConnection db, Workload w)
    {
        IQueryable<Customer> query = db.GetTable<Customer>().Where(x => x.Active == 1 && x.Id <= w.Rows).OrderBy(x => x.Id);
        if (w.Graph) query = query.LoadWith(x => x.Orders, children => children.OrderBy(x => x.Id))
            .LoadWith(x => x.Labels, children => children.OrderBy(x => x.Id));
        if (w.Page) query = query.Skip(w.Offset).Take(w.PageSize);
        return query;
    }
    public async Task<QueryResult> Load(Workload w)
    {
        await using var db = new DataConnection(options);
        long? total = w.Page ? await db.GetTable<Customer>().Where(x => x.Active == 1 && x.Id <= w.Rows).LongCountAsync() : null;
        return new QueryResult(await Query(db, w).ToListAsync(), total);
    }
    public async IAsyncEnumerable<Customer> Stream(Workload w)
    {
        await using var db = new DataConnection(options);
        await foreach (var row in Query(db, w).AsAsyncEnumerable()) yield return row;
    }
}
