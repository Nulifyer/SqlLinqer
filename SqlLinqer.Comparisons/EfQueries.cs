using Microsoft.EntityFrameworkCore;

namespace SqlLinqer.Comparisons;

internal sealed class ComparisonDb(DbContextOptions<ComparisonDb> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Customer>().ToTable("Customers").HasKey(x => x.Id);
        model.Entity<Order>().ToTable("Orders").HasKey(x => x.Id);
        model.Entity<Label>().ToTable("Labels").HasKey(x => x.Id);
        model.Entity<Customer>().HasMany(x => x.Orders).WithOne().HasForeignKey(x => x.CustomerId);
        model.Entity<Customer>().HasMany(x => x.Labels).WithOne().HasForeignKey(x => x.CustomerId);
    }
}

internal sealed class EfQueries : ICompetitor
{
    private readonly DbContextOptions<ComparisonDb> options;
    private readonly bool split;
    public EfQueries(string connectionString, CommandLog log, bool split)
    {
        this.split = split;
        options = new DbContextOptionsBuilder<ComparisonDb>().UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).AddInterceptors(new EfCommandLog(log)).Options;
    }
    private IQueryable<Customer> Query(ComparisonDb db, Workload w)
    {
        IQueryable<Customer> query = db.Customers.Where(x => x.Active == 1 && x.Id <= w.Rows).OrderBy(x => x.Id);
        if (w.Graph)
        {
            query = query.Include(x => x.Orders.OrderBy(child => child.Id)).Include(x => x.Labels.OrderBy(child => child.Id));
            query = split ? query.AsSplitQuery() : query.AsSingleQuery();
        }
        return w.Page ? query.Skip(w.Offset).Take(w.PageSize) : query;
    }
    public async Task<QueryResult> Load(Workload w)
    {
        await using var db = new ComparisonDb(options);
        long? total = w.Page ? await db.Customers.Where(x => x.Active == 1 && x.Id <= w.Rows).LongCountAsync() : null;
        return new QueryResult(await Query(db, w).ToListAsync(), total);
    }
    public async IAsyncEnumerable<Customer> Stream(Workload w)
    {
        await using var db = new ComparisonDb(options);
        await foreach (var row in Query(db, w).AsAsyncEnumerable()) yield return row;
    }
}
