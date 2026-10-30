using System.Data;
using System.Runtime.CompilerServices;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Native;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Tests.Regression;
using Root = SqlLinqer.Tests.Regression.NativeResultRegressionTests.Root;

namespace SqlLinqer.Tests.Performance;

internal static class ProfilingWorkloads
{
    private static SelectQuery<StressRoot> Graph(Table table)
    {
        var query = new SelectQuery<StressRoot>(table).SelectRootColumns();
        foreach (var path in new[] { "Owner", "Owner.Manager", "Owner.Manager.Region", "Owner.Reports", "Owner.Reports.Region",
            "Backup", "Backup.Region", "Projects", "Projects.Owner", "Projects.Tickets", "Projects.Tickets.Assignee",
            "Projects.Tickets.Category", "Members", "Members.Manager", "Members.Region" }) query.Select(path.Split('.'));
        return query.Where(x => x.Projects.First().Tickets.First().Category.Id, 1, SqlOp.EQ).OrderByAuto();
    }
    internal static int Run(string[] args)
    {
        string layer = args[0]; int seconds = int.Parse(args[1]);
        var query = Graph(Table.GetCached<StressRoot>("bench"));
        DataTable records = null;
        Func<long> operation = layer switch
        {
            "model" => () => Model(),
            "capture" => () => SnapshotGraph.Capture(query).Table.UUID.Length,
            "render" => () => query.Render(DbFlavor.SqlServer).Text.Length,
            "map" => PrepareMapping(out records),
            _ => throw new ArgumentException("Expected model, capture, render, or map")
        };
        try { ProfilingSession.Run(layer, seconds, () => Task.FromResult(operation())).GetAwaiter().GetResult(); }
        finally { records?.Dispose(); }
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Model() => Graph(new Table(typeof(StressRoot), "bench")).Selects.Count;

    private static Func<long> PrepareMapping(out DataTable records)
    {
        var connector = new FakeConnector(new FakeDatabase(), DbFlavor.SqlServer);
        var plan = NativeResultRegressionTests.Graph().Capture().RenderForExecution<Root>(connector).NativePlan;
        using var template = NativeResultRegressionTests.Records(plan);
        var data = records = template.Clone();
        for (int root = 1; root <= 2000; root++)
            foreach (DataRow source in template.Rows)
            {
                var values = source.ItemArray; values[0] = (long)root;
                data.Rows.Add(values);
            }
        return () => Map(plan, data);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Map(NativeResultPlan plan, DataTable records)
    {
        using var reader = records.CreateDataReader();
        var rows = new NativeResultMapper<Root>(plan, DbFlavor.SqlServer).Read(reader);
        if (rows.Count != 2000 || rows.Any(r => r.Id != 7 || r.Tags.Count != 2 || r.Orders.Count != 2
            || r.Orders.Any(o => o.Items.Count != 1) || r.Profile?.Id != 1 || r.Profile.Note != null))
            throw new InvalidOperationException("Mapping profile produced an incomplete graph");
        return rows.Count;
    }
}
