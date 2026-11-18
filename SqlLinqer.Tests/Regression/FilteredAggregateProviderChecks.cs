using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

// Runs conditional aggregates on every live provider.
internal static class FilteredAggregateProviderChecks
{
    internal static void Run(BaseConnector db, Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
            test($"aggregates: filtered counts and sums per group, async={asynchronous}", () => CheckProvider(db, asynchronous).GetAwaiter().GetResult());
    }

    private static async Task CheckProvider(BaseConnector db, bool asynchronous)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string table = (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : Q(db.Options.DefaultSchema) + ".") + Q("aggregate_events");
        try
        {
            db.BeginSql($"CREATE TABLE {table} ({Q("id")} INTEGER PRIMARY KEY, {Q("document_id")} INTEGER NOT NULL, {Q("source")} VARCHAR(16) NOT NULL, {Q("bytes")} INTEGER NOT NULL)")
                .Execute().EnsureSuccess();
            db.BeginInsert<Event>().InsertAuto(new[]
            {
                new Event { Id = 1, DocumentId = 1, Source = "qr", Bytes = 10 },
                new Event { Id = 2, DocumentId = 1, Source = "qr", Bytes = 20 },
                new Event { Id = 3, DocumentId = 1, Source = "web", Bytes = 5 },
                new Event { Id = 4, DocumentId = 2, Source = "web", Bytes = 7 },
            }).Execute().EnsureSuccess();

            string source = "qr";
            var query = db.BeginSelectBuilder<Event>().BuildQuery(q => new
            {
                Document = q.Select(e => e.DocumentId),
                Views = q.Count(),
                Qr = q.Count(w => w.Where(e => e.Source, source, SqlOp.EQ)),
                QrBig = q.CountBig(w => w.Where(e => e.Source, source, SqlOp.EQ)),
                LargeQr = q.Count(w => w.Where(e => e.Source, source, SqlOp.EQ).Where(e => e.Bytes, 15, SqlOp.GT)),
                QrBytes = q.Sum(e => e.Bytes, w => w.Where(e => e.Source, source, SqlOp.EQ)),
                Bytes = q.Sum(e => e.Bytes),
                QrValues = q.Count(e => e.Bytes, w => w.Where(e => e.Source, source, SqlOp.EQ)),
                QrValuesBig = q.CountBig(e => e.Bytes, w => w.Where(e => e.Source, source, SqlOp.EQ)),
                LargestQr = (int?)q.Max(e => e.Bytes, w => w.Where(e => e.Source, source, SqlOp.EQ)),
                SmallestQr = (int?)q.Min(e => e.Bytes, w => w.Where(e => e.Source, source, SqlOp.EQ)),
                AverageQr = (decimal?)q.Avg(e => e.Bytes, w => w.Where(e => e.Source, source, SqlOp.EQ)),
            }).OrderBy(row => row.Document, SqlDir.ASC);
            var rows = (asynchronous ? await query.ExecuteAsync() : query.Execute()).GetResultOrThrow();
            Check(rows.Count == 2, "Grouping changed");
            Check(rows[0].Views == 3 && rows[0].Qr == 2 && rows[0].QrBig == 2 && rows[0].LargeQr == 1 && rows[0].QrBytes == 30 && rows[0].Bytes == 35,
                $"Filtered aggregates changed for a matching group: {rows[0]}");
            Check(rows[0].QrValues == 2 && rows[0].QrValuesBig == 2 && rows[0].LargestQr == 20 && rows[0].SmallestQr == 10 && rows[0].AverageQr == 15,
                $"Filtered value counts, extremes, or average changed for a matching group: {rows[0]}");
            Check(rows[1].QrValues == 0 && rows[1].QrValuesBig == 0 && rows[1].LargestQr == null && rows[1].SmallestQr == null && rows[1].AverageQr == null,
                $"A group without matches did not return zero counts and NULL extremes: {rows[1]}");
            Check(rows[1].Views == 1 && rows[1].Qr == 0 && rows[1].QrBig == 0 && rows[1].LargeQr == 0 && rows[1].QrBytes == 0 && rows[1].Bytes == 7,
                $"Filtered aggregates changed for a group without matches: {rows[1]}");

            var page = asynchronous ? await query.Page(2, 1).ExecuteWithTotalCountAsync() : query.Page(2, 1).ExecuteWithTotalCount();
            Check(page.EnsureSuccess().TotalResults == 2 && page.Result.Single().Document == 2 && page.Result.Single().Qr == 0, "Paging or the group total changed");

            var overall = db.BeginSelectBuilder<Event>().BuildQuery(q => new
            {
                Web = q.Count(w => w.Where(e => e.Source, "web", SqlOp.EQ)),
                WebBytes = q.Sum(e => e.Bytes, w => w.Where(e => e.Source, "web", SqlOp.EQ)),
            }).Execute().GetResultOrThrow().Single();
            Check(overall.Web == 2 && overall.WebBytes == 12, "An ungrouped filtered aggregate changed");

            // Distinct counts and joined text accept the same filter. The values of joined text arrive in no set order.
            db.BeginInsert<Event>().InsertAuto(new[]
            {
                new Event { Id = 5, DocumentId = 1, Source = "qr", Bytes = 10 },
                new Event { Id = 6, DocumentId = 1, Source = "app", Bytes = 10 },
            }).Execute().EnsureSuccess();
            var distinct = db.BeginSelectBuilder<Event>().BuildQuery(q => new
            {
                Document = q.Select(e => e.DocumentId),
                Sizes = q.Count(e => e.Bytes, true),
                QrSizes = q.Count(e => e.Bytes, true, w => w.Where(e => e.Source, source, SqlOp.EQ)),
                QrSizesBig = q.CountBig(e => e.Bytes, true, w => w.Where(e => e.Source, source, SqlOp.EQ)),
                QrRows = q.Count(e => e.Bytes, false, w => w.Where(e => e.Source, source, SqlOp.EQ)),
                Sources = q.StringAgg(e => e.Source, "|"),
                OtherSources = q.StringAgg(e => e.Source, "|", w => w.Where(e => e.Source, source, SqlOp.NOT)),
            }).OrderBy(row => row.Document, SqlDir.ASC);
            var counted = (asynchronous ? await distinct.ExecuteAsync() : distinct.Execute()).GetResultOrThrow();
            Check(counted[0].Sizes == 3 && counted[0].QrSizes == 2 && counted[0].QrSizesBig == 2 && counted[0].QrRows == 3,
                $"A filtered distinct count changed for a matching group: {counted[0]}");
            Check(counted[1].Sizes == 1 && counted[1].QrSizes == 0 && counted[1].QrSizesBig == 0 && counted[1].QrRows == 0,
                $"A filtered distinct count changed for a group without matches: {counted[1]}");
            string Sorted(string joined) => joined == null ? null : string.Join("|", joined.Split('|').OrderBy(x => x, StringComparer.Ordinal));
            Check(Sorted(counted[0].Sources) == "app|qr|qr|qr|web" && Sorted(counted[0].OtherSources) == "app|web",
                $"Joined text changed for a matching group: {counted[0]}");
            Check(counted[1].Sources == "web" && counted[1].OtherSources == "web", $"Joined text changed for a single-row group: {counted[1]}");
            var none = db.BeginSelectBuilder<Event>().BuildQuery(q => new
            {
                Document = q.Select(e => e.DocumentId),
                Missing = q.StringAgg(e => e.Source, ", ", w => w.Where(e => e.Source, "none", SqlOp.EQ)),
            }).Execute().GetResultOrThrow();
            Check(none.Count == 2 && none.All(row => row.Missing == null), "Joined text without matching rows was not NULL");
        }
        finally { db.BeginSql($"DROP TABLE IF EXISTS {table}").Execute(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    [SqlTable("aggregate_events")]
    public sealed class Event
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("document_id")] public int DocumentId { get; set; }
        [SqlColumn("source")] public string Source { get; set; }
        [SqlColumn("bytes")] public int Bytes { get; set; }
    }
}
