using System.Data;
using System.Reflection;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Typed;
using static SqlLinqer.Tests.DuckDb.CheckAssertions;

namespace SqlLinqer.Tests.DuckDb;

internal static class QueryChecks
{
    internal static void Run(BaseConnector connector, Person person, Action<string, Action> Test)
    {
        void Sql(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        Test("nested many, one, empty arrays and absent one", () =>
        {
            var rows = Result(new SelectQuery<Person>().SelectAuto().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector));
            Expect(rows.Count == 4 && rows[0].Children.Count == 3 && rows[0].Category.Name == "group", "Nested values missing");
            Expect(rows[1].Children.Count == 0 && rows[1].Category == null, "Empty relationship semantics changed");
        });
        Test("nested ordering and pagination before JSON aggregation", () =>
        {
            var query = new ConfigurableSelect<Person>();
            query.SelectAuto();
            var nested = query.Relationship(nameof(Person.Children));
            ConfigureNested(nested, limit: 1, offset: 1);
            var rows = Result(query.Where(x => x.Id, 100, SqlOp.EQ).Execute(connector));
            Expect(rows.Single().Children.Single().Name == "m", "Nested pagination/order was applied after aggregation");
        });
        Test("count paging and Top preserve parameters and query state", () =>
        {
            var query = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id, 99, SqlOp.GT).OrderBy(x => x.Id, SqlDir.ASC).Page(2, 2);
            var page = query.ExecuteWithTotalCount(connector);
            Expect(Result(page).Select(x => x.Id).SequenceEqual([102, 103]) && page.TotalResults == 4, "Incorrect page/total");
            var top = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id, 99, SqlOp.GT).Top(1).ExecuteWithTotalCount(connector);
            Expect(Result(top).Count == 1 && top.TotalResults == 4, "Incorrect Top total");
            var empty = new SelectQuery<Person>().SelectRootColumns().Page(10, 2).ExecuteWithTotalCount(connector);
            Expect(Result(empty).Count == 0 && empty.TotalResults == 4, "Incorrect empty page total");
            Expect(query.Render(DbFlavor.DuckDb).Text == query.Render(DbFlavor.DuckDb).Text, "Count mutated query");
        });
        Test("parameterized subquery and relationship filters", () =>
        {
            var sub = new SelectQuery<Person>().Select(x => x.Id).Where(x => x.Name, "two", SqlOp.EQ);
            var query = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id, sub, SqlArrayOp.IN).Where(x => x.Id, 99, SqlOp.GT);
            Expect(Result(query.Execute(connector)).Single().Name == "two", "Parameterized IN subquery failed");
            Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x => x.Children.First().Name, "a", SqlOp.EQ).Execute(connector)).Single().Id == 100, "Relationship filter failed");
        });
        Test("relationship counts and ordering by related rows", () =>
        {
            var page = new SelectQuery<Person>().SelectAuto().Where(x => x.Children.First().Name, "a", SqlOp.EQ).Top(1).ExecuteWithTotalCount(connector);
            Expect(Result(page).Single().Id == 100 && page.TotalResults == 1, "Relationship count failed");
            var ordered = Result(new SelectQuery<Person>().SelectRootColumns().OrderBy(x => x.Children.First().Name, SqlDir.ASC).Execute(connector));
            Expect(ordered.First().Id == 100, "Related ordering failed");
        });
        Test("direct Render flavor, connector, existing collection and repeated dialect rendering", () =>
        {
            var query = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Name, "two", SqlOp.EQ);
            var first = query.Render(DbFlavor.DuckDb);
            Expect(first.Text == query.Render(connector).Text && first.Parameters.GetAll().Single().Placehodler == "$P1", "Wrong DuckDB parameter syntax");
            Expect(query.Render(DbFlavor.SqlServer).Parameters.GetAll().Single().Placehodler == "@P1", "Other dialect changed");
            Expect(first.Text == query.Render(DbFlavor.DuckDb).Text, "Repeated render changed SQL");
            Expect(Result(connector.ExecuteReaderJson<Person>(first)).Single().Name == "two", "Direct flavor execution failed");
            var parameters = new ParameterCollection();
            string sql = query.Render(parameters, DbFlavor.DuckDb);
            Expect(sql.Contains("$P1") && parameters.GetAll().Single().Value.Equals("two"), "Existing collection syntax wrong");
        });
        Test("existing parameters remain intact when rendering another query", () =>
        {
            var parameters = new ParameterCollection(DbFlavor.DuckDb);
            string first = parameters.AddParameter(100);
            var query = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id, 101, SqlOp.EQ);
            string sql = query.Render(parameters, DbFlavor.DuckDb);
            var rendered = new RenderedQuery($"{sql} AND {first} = 100") { Parameters = parameters };
            Expect(Result(connector.ExecuteReaderJson<Person>(rendered)).Single().Id == 101 && parameters.GetAll().Count() == 2, "Parameters lost/collided");
        });
        Test("common string/date functions and aggregate aliases", () =>
        {
            var functions = new SelectQueryInitializer<Person>().BuildQuery(x => new { Name = x.UPPER(p => p.Name), Year = x.YEAR(p => p.Born), Month = x.MONTH(p => p.Born), Day = x.DAY(p => p.Born), Tail = x.RIGHT(p => p.Name, 2) }).Where(x => x.Id, 100, SqlOp.EQ);
            var row = Result(functions.Execute(connector)).Single();
            Expect(row.Year == 2024 && row.Month == 2 && row.Day == 29 && row.Name == person.Name.ToUpperInvariant() && row.Tail == "_%\\"[^2..], "Function mismatch");
            var aggregate = new SelectQueryInitializer<Person>().BuildQuery(x => new { Total = x.CountBig(p => p.Id), Sum = x.Sum(p => p.Id), Average = (double)x.Avg(p => p.Id) });
            var total = Result(aggregate.Execute(connector)).Single();
            Expect(total.Total == 4 && total.Sum == 406 && total.Average == 101.5, "Aggregate mismatch");
        });
        Test("grouped parameterized functions and nested function expressions", () =>
        {
            var table = Table.GetCached<Person>();
            var query = new ConfigurableSelect<Person>();
            query.Add(new SelectFuncStatement(SqlFunc.RIGHT, "Tail", table.Columns.TryGet("Name"), 2));
            query.Add(new SelectFuncStatement(SqlFunc.COUNT_BIG, "Total", table.Columns.TryGet("Id")));
            var groups = Result(connector.ExecuteReaderJson<GroupSummary>(query.Render(DbFlavor.DuckDb)));
            Expect(groups.Sum(x => x.Total) == 4, "Grouped parameterized function failed");
            var nested = new ConfigurableSelect<Person>();
            nested.Add(new SelectFuncStatement(SqlFunc.UPPER, "Name", new SelectFuncStatement(SqlFunc.LOWER, "inner", table.Columns.TryGet("Name"))));
            Expect(Result(nested.Where(x => x.Id, 101, SqlOp.EQ).Execute(connector)).Single().Name == "TWO", "Nested scalar function failed");
        });
        Test("imported named parameters avoid generated placeholder collisions", () =>
        {
            var source = new ParameterCollection(DbFlavor.DuckDb); source.AddParameter(100);
            var parameters = new ImportedParameters(); parameters.Import(source);
            Expect(parameters.AddParameter(101) == "$P2", "Imported parameter was overwritten");
            var query = new RenderedQuery("SELECT $P1 + $P2") { Parameters = parameters };
            Expect(Result(connector.ExecuteScalar<int>(query)) == 201, "Imported parameters failed to bind");
        });
        Test("all supported aggregate aliases execute in the native engine", () =>
        {
            var column = Table.GetCached<Person>().Columns.TryGet("Id");
            foreach (var function in new[] { SqlFunc.COUNT_BIG, SqlFunc.STDEV, SqlFunc.STDEVP, SqlFunc.VAR, SqlFunc.VARP, SqlFunc.MIN, SqlFunc.MAX })
            {
                var query = new ConfigurableSelect<Person>();
                query.Add(new SelectFuncStatement(function, "Value", column));
                var value = Result(connector.ExecuteReaderJson<NumericSummary>(query.Render(DbFlavor.DuckDb))).Single().Value;
                Expect(value > 0, "Aggregate failed: " + function);
            }
        });
        Test("nested relationships preserve JSON scalar types", () =>
        {
            var rows = Result(new SelectQuery<Envelope>().SelectAuto().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector));
            Expect(rows[0].Person.Token == person.Token && rows[0].Person.Payload.SequenceEqual(person.Payload) && rows[0].Person.Children.Count == 3, "Nested type or relationship changed");
            Expect(rows[1].Person == null, "Missing nested object changed");
        });
        Test("parameterized scalar types and binary equality", () =>
        {
            Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x => x.Token, person.Token, SqlOp.EQ).Where(x => x.Kind, Kind.Second, SqlOp.EQ).Where(x => x.Enabled, true, SqlOp.EQ).Where(x => x.Born, person.Born, SqlOp.EQ).Where(x => x.Amount, person.Amount, SqlOp.EQ).Execute(connector)).Single().Id == 100, "Typed predicate binding failed");
            Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x => x.Payload, person.Payload, SqlOp.EQ).Execute(connector)).Single().Id == 100, "Binary predicate failed");
        });
        Test("LIKE escaping preserves literal wildcard and backslash characters", () =>
        {
            string pattern = person.Name.EscapeForSqlLike(DbFlavor.DuckDb);
            Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x => x.Name, pattern, SqlOp.LIKE).Execute(connector)).Single().Id == 100, "Escaped LIKE failed");
            Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x => x.Name, pattern, SqlOp.NOTLIKE).Execute(connector)).Count == 3, "Escaped NOT LIKE failed");
        });
        Test("byte arrays remain IN lists for byte-valued columns", () =>
        {
            Sql("CREATE TABLE byte_values (Id UTINYINT); INSERT INTO byte_values VALUES (1),(2),(3)");
            var rows = Result(new SelectQuery<ByteValue>().SelectRootColumns().Where(x => x.Id, new byte[] { 1, 2 }, SqlArrayOp.IN).Execute(connector));
            Expect(rows.Select(x => x.Id).Order().SequenceEqual(new byte[] { 1, 2 }), "Numeric byte IN list changed");
        });
        Test("date, timestamp with offset, string enum and nullable JSON values", () =>
        {
            Sql("CREATE TABLE typed_values (Id INTEGER, Flag BOOLEAN, Kind VARCHAR, Token UUID, Day DATE, Stamp TIMESTAMPTZ, Value DECIMAL(18,4), Payload BLOB)");
            var stamp = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(-5));
            var command = new RenderedQuery("INSERT INTO typed_values VALUES (1, false, 'Second', '" + person.Token + "', DATE '2024-02-29', TIMESTAMPTZ '2024-02-29 12:34:56-05', -123.4567, from_hex('00FF')), (2,NULL,NULL,NULL,NULL,NULL,NULL,NULL)");
            Result(connector.ExecuteNonQuery(command));
            Expect(Result(new InsertQuery<TypedValues>().InsertAuto(new TypedValues { Id = 3, Stamp = stamp, Day = person.Born.Date, Payload = [42] }).Execute(connector)) == 1, "Nullable/offset parameters failed");
            var rows = Result(new SelectQuery<TypedValues>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector));
            Expect(rows[0].Stamp.Value.ToUniversalTime() == stamp.ToUniversalTime() && rows[0].Day == person.Born.Date && rows[0].Flag == false && rows[0].Kind == Kind.Second && rows[0].Token == person.Token && rows[0].Value == -123.4567m && rows[0].Payload.SequenceEqual(new byte[] { 0, 255 }), "Additional JSON types changed");
            Expect(rows[2].Stamp.Value.ToUniversalTime() == stamp.ToUniversalTime() && rows[2].Day == person.Born.Date && rows[2].Payload.Single() == 42, "Offset parameter round trip failed");
            Expect(rows[1].Flag == null && rows[1].Kind == null && rows[1].Token == null && rows[1].Day == null && rows[1].Stamp == null && rows[1].Value == null && rows[1].Payload == null, "Nullable JSON changed");
        });
    }

    internal static void Aggregates(BaseConnector connector, Action<string, Action> Test)
    {
        Test("pure aggregate root FROM, nested numeric conversions and empty aggregates", () =>
        {
            var pure = new SelectQueryInitializer<Person>().BuildQuery(x => new { Count = x.Count(), Big = x.CountBig() });
            var total = Result(pure.ExecuteWithTotalCount(connector));
            Expect(total.Single().Count == 4 && total.Single().Big == 4, "Pure aggregate lost root FROM");
            var numeric = new SelectQueryInitializer<Person>().BuildQuery(x => new { Average = (double?)(double)x.Avg(p => p.Id), Wide = (long)x.Sum(p => p.Id) });
            var values = Result(numeric.Execute(connector)).Single();
            Expect(values.Average == 101.5 && values.Wide == 406, "Nullable numeric conversion failed");
            var empty = new SelectQueryInitializer<Person>().BuildQuery(x => new { Average = (double?)(double)x.Avg(p => p.Id), Sum = (long?)x.Sum(p => p.Id), Count = x.Count() }).Where(x => x.Id, -1, SqlOp.EQ);
            var none = Result(empty.Execute(connector)).Single();
            Expect(none.Average == null && none.Sum == null && none.Count == 0, "Empty aggregate NULL changed");
        });
        Test("grouped and distinct totals count results and preserve parameters", () =>
        {
            var grouped = new SelectQueryInitializer<Person>().BuildQuery(x => new { Category = x.Select(p => p.CategoryId), Total = x.Count() });
            var page = grouped.ExecuteWithTotalCount(connector);
            Expect(Result(page).Count == 2 && page.TotalResults == 2, "Grouped total counted underlying rows");
            var distinct = new SelectQuery<Person>().Select(x => x.CategoryId).Distinct();
            var result = distinct.ExecuteWithTotalCount(connector);
            Expect(Result(result).Count == 2 && result.TotalResults == 2, "Distinct total counted underlying rows");
            var table = Table.GetCached<Person>();
            var functions = new ConfigurableSelect<Person>();
            functions.Add(new SelectFuncStatement(SqlFunc.RIGHT, "Tail", table.Columns.TryGet("Name"), 2));
            functions.Add(new SelectFuncStatement(SqlFunc.COUNT_BIG, "Total", table.Columns.TryGet("Id")));
            var rendered = new RenderedQuery(); rendered.Text = functions.RenderForCount(rendered.Parameters, DbFlavor.DuckDb);
            Expect(Result(connector.ExecuteScalar<int>(rendered)) == 4, "Grouped function count lost parameters");
            var nested = new SelectQuery<Person>().SelectAuto().Where(x => x.Children.First().Name, "a", SqlOp.EQ).ExecuteWithTotalCount(connector);
            Expect(Result(nested).Count == 1 && nested.TotalResults == 1, "Nested total failed");
        });
        Test("aggregate and distinct paging preserve grouping", () =>
        {
            var pure = new SelectQueryInitializer<Person>().BuildQuery(x => new { Count = x.Count() }).Page(1, 1).ExecuteWithTotalCount(connector);
            Expect(Result(pure).Single().Count == 4 && pure.TotalResults == 1, "Paging introduced aggregate grouping");
            var grouped = new SelectQueryInitializer<Person>().BuildQuery(x => new { Category = x.Select(p => p.CategoryId), Total = x.Count() }).Page(1, 1).ExecuteWithTotalCount(connector);
            var row = Result(grouped).Single();
            Expect(grouped.TotalResults == 2 && row.Total == (row.Category == 1 ? 1 : 3), "Paging changed result groups");
            var distinct = new SelectQuery<Person>().Select(x => x.CategoryId).Distinct().Page(1, 1).ExecuteWithTotalCount(connector);
            Expect(Result(distinct).Count == 1 && distinct.TotalResults == 2, "Paging introduced hidden distinct column");
        });
        Test("CASE values and nested functions group by the projected expression", () =>
        {
            var query = new SelectQueryInitializer<Person>().BuildQuery(x => new { Label = x.Case<int>(c => c.AddCaseColumn(p => p.CategoryId, w => w.Where(p => p.Id, 102, SqlOp.LT)).AddElseValue(0)), Total = x.Count() });
            var groups = Result(query.ExecuteWithTotalCount(connector));
            Expect(groups.Count == 2 && groups.Single(x => x.Label == 1).Total == 1 && groups.Single(x => x.Label == 0).Total == 3, "CASE did not group projected values");
            var ordered = query.OrderBy(x => x.Label, SqlDir.DESC).Page(1, 1).ExecuteWithTotalCount(connector);
            Expect(Result(ordered).Single().Label == 1 && ordered.TotalResults == 2, "CASE ordering rebound group parameters");
            var table = Table.GetCached<Person>();
            var text = new CaseStatement<Person>(table, "inner");
            text.AddCaseValue("small", w => w.Where(p => p.Id, 102, SqlOp.LT)).AddElseValue("large");
            var nested = new ConfigurableSelect<Person>();
            nested.Add(new SelectFuncStatement(SqlFunc.UPPER, "Tail", text));
            nested.Add(new SelectFuncStatement(SqlFunc.COUNT, "Total", table.Columns.TryGet("Id")));
            var rows = Result(connector.ExecuteReaderJson<GroupSummary>(nested.Render(DbFlavor.DuckDb)));
            Expect(rows.Count == 2 && rows.All(x => x.Total == 2) && rows.Select(x => x.Tail).Order().SequenceEqual(new[] { "LARGE", "SMALL" }), "Function wrapping CASE failed");
            var conditional = new CaseStatement<Person>(table, "Tail");
            conditional.AddCaseValue(new SelectFuncStatement(SqlFunc.UPPER, "inner", table.Columns.TryGet("Name")), w => w.Where(p => p.Id, 101, SqlOp.EQ)).AddElseValue("other");
            var outer = new ConfigurableSelect<Person>(); outer.Add(conditional);
            outer.Add(new SelectFuncStatement(SqlFunc.COUNT, "Total", table.Columns.TryGet("Id")));
            var nestedRows = Result(connector.ExecuteReaderJson<GroupSummary>(outer.Render(DbFlavor.DuckDb)));
            Expect(nestedRows.Count == 2 && nestedRows.Single(x => x.Tail == "CHANGED").Total == 1 && nestedRows.Single(x => x.Tail == "other").Total == 3, "CASE wrapping function failed");
        });
        Test("LEFT projection dispatch and nested functions retain aliases", () =>
        {
            var query = new SelectQueryInitializer<Person>().BuildQuery(x => new { Prefix = x.LEFT(p => p.Name, 2), Upper = x.UPPER(p => x.LEFT(q => q.Name, 2)) }).Where(x => x.Id, 101, SqlOp.EQ);
            var row = Result(query.Execute(connector)).Single();
            Expect(row.Prefix == "ch" && row.Upper == "CH", "LEFT dispatch or nested scalar failed");
        });
    }

    internal static void Predicates(BaseConnector connector, Action<string, Action> Test)
    {
        void Sql(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        Test("custom predicate Finalize overrides retain behavior across dialects", () =>
        {
            var predicate = new CustomPredicate(Table.GetCached<Person>().Columns.TryGet("Id"));
            var query = new SelectQuery<Person>().SelectRootColumns().Where(predicate);
            Expect(Result(query.Execute(connector)).Count == 0, "DuckDB bypassed custom Finalize override");
            Expect(query.Render(DbFlavor.SqlServer).Text.Contains("1 = 0"), "Existing dialect bypassed custom Finalize override");
        });
        Test("native word predicates preserve boundaries wildcards escaping NULL and dialect renders", () =>
        {
            Sql("CREATE TABLE words(Id INTEGER, Name VARCHAR); INSERT INTO words VALUES(1,'cat'),(2,'a cat!'),(3,'cats'),(4,'cat_dog'),(5,NULL),(6,'a.c'),(7,'a%c'),(8,'CAT'),(9,'cat-cat')");
            var query = new SelectQuery<Word>().SelectRootColumns().Where(x => x.Name, "cat", SqlOp.LIKEWORD);
            string first = query.Render(DbFlavor.DuckDb).Text;
            Expect(query.Render(DbFlavor.SqlServer).Text.Contains("LIKE"), "Other dialect render failed");
            Expect(query.Render(DbFlavor.DuckDb).Text == first, "Word render mutated query");
            Expect(Result(query.Execute(connector)).Select(x => x.Id).Order().SequenceEqual(new[] { 1, 2, 9 }), "ASCII boundaries or case changed");
            Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x => x.Name, "cat", SqlOp.NOTLIKEWORD).Execute(connector)).Select(x => x.Id).Order().SequenceEqual(new[] { 3, 4, 6, 7, 8 }), "Negative word NULL semantics changed");
            Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x => x.Name, "cat", SqlOp.NOTLIKEWORDORNULL).Execute(connector)).Count == 6, "Word OR NULL failed");
            Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x => x.Name, "a%c".EscapeForSqlLike(DbFlavor.DuckDb), SqlOp.LIKEWORD).Execute(connector)).Single().Id == 7, "Word literal wildcard failed");
            Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x => x.Name, "a.c", SqlOp.LIKEWORD).Execute(connector)).Single().Id == 6, "Regex punctuation was not literal");
            Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x => x.Name, "a_c", SqlOp.LIKEWORD).Execute(connector)).Count == 2, "Word LIKE wildcard failed");
            var group = new SelectQuery<Word>().SelectRootColumns(); group.Where(group.NewWhere(SqlWhereOp.OR).Where(x => x.Name, "cat", SqlOp.LIKEWORD).Where(x => x.Name, "a.c", SqlOp.LIKEWORD));
            Expect(Result(group.Execute(connector)).Count == 4, "Grouped word filter failed");
            Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x => x.Children.First().Name, "a", SqlOp.LIKEWORD).Execute(connector)).Single().Id == 100, "Related CTE word filter failed");
            Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x => x.Children.First().Name, "a", SqlOp.NOTLIKEWORD).Execute(connector)).Count == 3, "Related negative word filter failed");
        });
    }

    private static void ConfigureNested(SqlLinqer.Queries.Core.SelectQuery query, int limit, int offset)
    {
        // The existing public typed API exposes no nested paging controls. Configure the core
        // collections here to exercise its nested renderer without expanding the library API.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SqlLinqer.Queries.Core.SelectQuery);
        ((PagingControls)type.GetField("PagingControls", flags).GetValue(query)).SetLimit(limit, offset);
        var table = (Table)typeof(SqlLinqer.Queries.Core.Abstract.TableQuery).GetField("Table", flags).GetValue(query);
        var orders = (SqlLinqer.Components.Ordering.OrderByCollection)type.GetField("OrderBys", flags).GetValue(query);
        orders.AddStatement(new SqlLinqer.Components.Ordering.ColumnOrderBy(table.Columns.TryGet("Name"), SqlDir.ASC));
    }
}
