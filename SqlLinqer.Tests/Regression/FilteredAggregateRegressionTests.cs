using System.Text.RegularExpressions;
using SqlLinqer;
using SqlLinqer.Queries.Typed;
using Event = SqlLinqer.Tests.Regression.FilteredAggregateProviderChecks.Event;

namespace SqlLinqer.Tests.Regression;

internal static class FilteredAggregateRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        SqlLinqer.Queries.Core.SelectQuery Query() => new SelectQueryInitializer<Event>().BuildQuery(q => new
        {
            Document = q.Select(e => e.DocumentId),
            Qr = q.Count(w => w.Where(e => e.Source, "qr", SqlOp.EQ)),
            QrBytes = q.Sum(e => e.Bytes, w => w.Where(e => e.Source, "qr", SqlOp.EQ)),
        });
        SqlLinqer.Queries.Core.SelectQuery Family() => new SelectQueryInitializer<Event>().BuildQuery(q => new
        {
            Values = q.Count(e => e.Bytes, w => w.Where(e => e.Source, "qr", SqlOp.EQ)),
            BigValues = q.CountBig(e => e.Bytes, w => w.Where(e => e.Source, "qr", SqlOp.EQ)),
            Largest = (int?)q.Max(e => e.Bytes, w => w.Where(e => e.Source, "qr", SqlOp.EQ)),
            Smallest = (int?)q.Min(e => e.Bytes, w => w.Where(e => e.Source, "qr", SqlOp.EQ)),
            Average = (decimal?)q.Avg(e => e.Bytes, w => w.Where(e => e.Source, "qr", SqlOp.EQ)),
        });
        foreach (var flavor in Enum.GetValues<DbFlavor>())
            test($"every filtered aggregate renders its filter for {flavor}", () =>
            {
                string sql = Regex.Replace(Family().Render(flavor).Text, @"\s+", " ");
                bool clause = flavor is DbFlavor.PostgreSql or DbFlavor.Sqlite or DbFlavor.DuckDb;
                Check(Regex.Matches(sql, clause ? @"FILTER \(WHERE " : @"\(CASE WHEN ").Count == 5, sql);
                Check(!sql.Contains("COALESCE"), "A filter changed what an aggregate returns for no rows: " + sql);
            });
        foreach (var flavor in Enum.GetValues<DbFlavor>())
            test($"filtered aggregates render FILTER or CASE WHEN for {flavor}", () =>
            {
                var rendered = Query().Render(flavor);
                string sql = Regex.Replace(rendered.Text, @"\s+", " ");
                bool clause = flavor is DbFlavor.PostgreSql or DbFlavor.Sqlite or DbFlavor.DuckDb;
                Check(Regex.Matches(sql, @"FILTER \(WHERE ").Count == (clause ? 2 : 0), sql);
                Check(Regex.Matches(sql, @"\(CASE WHEN ").Count == (clause ? 0 : 2), sql);
                Check(!sql.Contains("COALESCE"), "A filtered sum no longer follows an unfiltered sum for a group without matches: " + sql);
                Check(sql.Contains("GROUP BY") && rendered.Parameters.Count == 3, "Grouping or bindings changed: " + sql);
                string count = Regex.Replace(Query().RenderForCount(new SqlLinqer.Components.Render.ParameterCollection(flavor), flavor), @"\s+", " ");
                Check(!count.Contains("FILTER") && !count.Contains("CASE WHEN"), "A group count kept unused measures: " + count);
            });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
