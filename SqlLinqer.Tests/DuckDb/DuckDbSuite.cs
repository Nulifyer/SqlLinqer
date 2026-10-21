using System.Data;
using System.Diagnostics;
using System.Reflection;
using DuckDB.NET.Data;
using SqlLinqer;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.DuckDb;

internal static class DuckDbSuite
{
    internal static int Run()
    {
        int passed = 0, failed = 0;
        void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}\n{error}"); }
        }
        void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
        T Result<T>(SqlResponse<T> response)
        {
            if (response.State != ResponseState.Valid) throw new Exception(response.Error?.ToString());
            return response.Result;
        }
        void Reject(Action action)
        {
            try { action(); } catch (NotSupportedException) { return; }
            throw new Exception("Expected NotSupportedException");
        }
        string path = Path.Combine(Path.GetTempPath(), $"sqlinqer-duckdb-{Guid.NewGuid():N}.db");
        using var example = new DuckDBConnection($"Data Source={path}");
        var connector = new ReplicatorConnector(example, DbFlavor.DuckDb);
        void Sql(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        long Scalar(string sql) => Result(connector.ExecuteScalar<long>(new RenderedQuery(sql)));
        try
        {
            Console.WriteLine($"Provider {typeof(DuckDBConnection).Assembly.GetName().Version}; native {Result(connector.ExecuteScalar<string>(new RenderedQuery("SELECT version()")))}; {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
            Sql("CREATE SEQUENCE person_ids START 100; CREATE TABLE people (Id INTEGER PRIMARY KEY DEFAULT nextval('person_ids'), Name VARCHAR, Enabled BOOLEAN, Kind INTEGER, Token UUID, Born TIMESTAMP, Amount DECIMAL(18,4), Payload BLOB, Note VARCHAR, CategoryId INTEGER); CREATE TABLE categories (Id INTEGER, Name VARCHAR); CREATE TABLE children (Id INTEGER PRIMARY KEY, PersonId INTEGER, Name VARCHAR); INSERT INTO categories VALUES (1, 'group'); INSERT INTO children VALUES (1,100,'z'),(2,100,'a'),(3,100,'m');");
            Sql("CREATE TABLE envelopes (Id INTEGER, PersonId INTEGER); INSERT INTO envelopes VALUES (1,100),(2,-1)");
            var person = new Person { Name = "O'Brien_%\\", Enabled = true, Kind = Kind.Second, Token = Guid.NewGuid(), Born = new DateTime(2024, 2, 29, 12, 34, 56), Amount = 123.4567m, Payload = [0,1,127,128,255], CategoryId = 1 };
            Test("sequence-generated keys and tabular output", () =>
            {
                var response = new InsertWithPrimaryKeyQuery<Person, int>().InsertAuto(person).Execute(connector);
                Expect(Result(response) == 1 && response.TouchedPrimaryKeys.Single() == 100, "Incorrect generated key/count");
            });
            Test("typed root JSON bool enum GUID timestamp decimal binary null", () =>
            {
                var row = Result(new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id, 100, SqlOp.EQ).Execute(connector)).Single();
                Expect(row.Name == person.Name && row.Enabled && row.Kind == Kind.Second && row.Token == person.Token && row.Born == person.Born && row.Amount == person.Amount && row.Payload.SequenceEqual(person.Payload) && row.Note == null, "Typed JSON changed values");
            });
            Test("insert affected rows and returned rows", () =>
            {
                Expect(Result(new InsertQuery<Person>().InsertAuto(new[] { new Person { Name = "two" }, new Person { Name = "three" } }).Execute(connector)) == 2, "Incorrect inserted count");
                using var table = Result(new InsertQuery<Person>().InsertAuto(new Person { Name = "four" }).Output(x => x.Id).Output(x => x.Name).ExecuteWithOutput(connector));
                Expect(table.Rows.Count == 1 && (int)table.Rows[0]["Id"] == 103 && (string)table.Rows[0]["Name"] == "four", "Incorrect returned row");
            });
            Test("DataTable materializes native scalar and binary columns", () =>
            {
                var query = new SelectQuery<Person>("main").SelectRootColumns().Where(x=>x.Id,100,SqlOp.EQ);
                var rendered = new RenderedQuery();
                rendered.Text = query.Render(rendered.Parameters,DbFlavor.DuckDb,false);
                using var table = Result(connector.ExecuteReaderDataTable(rendered));
                var row = table.Rows[0];
                Expect((Guid)row["Token"] == person.Token && ((byte[])row["Payload"]).SequenceEqual(person.Payload) && (decimal)row["Amount"] == person.Amount && (bool)row["Enabled"] && (DateTime)row["Born"] == person.Born,"DataTable changed native types");
            });
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
                var query = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id, 99, SqlOp.GT).OrderBy(x => x.Id, SqlDir.ASC).Page(2,2);
                var page = query.ExecuteWithTotalCount(connector);
                Expect(Result(page).Select(x => x.Id).SequenceEqual([102,103]) && page.TotalResults == 4, "Incorrect page/total");
                var top = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id, 99, SqlOp.GT).Top(1).ExecuteWithTotalCount(connector);
                Expect(Result(top).Count == 1 && top.TotalResults == 4, "Incorrect Top total");
                var empty = new SelectQuery<Person>().SelectRootColumns().Page(10,2).ExecuteWithTotalCount(connector);
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
                var page = new SelectQuery<Person>().SelectAuto().Where(x=>x.Children.First().Name,"a",SqlOp.EQ).Top(1).ExecuteWithTotalCount(connector);
                Expect(Result(page).Single().Id == 100 && page.TotalResults == 1,"Relationship count failed");
                var ordered = Result(new SelectQuery<Person>().SelectRootColumns().OrderBy(x=>x.Children.First().Name,SqlDir.ASC).Execute(connector));
                Expect(ordered.First().Id == 100,"Related ordering failed");
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
                var functions = new SelectQueryInitializer<Person>().BuildQuery(x => new { Name = x.UPPER(p => p.Name), Year = x.YEAR(p => p.Born), Month = x.MONTH(p => p.Born), Day = x.DAY(p => p.Born), Tail = x.RIGHT(p => p.Name, 2) }).Where(x => x.Id,100,SqlOp.EQ);
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
                Expect(Result(nested.Where(x=>x.Id,101,SqlOp.EQ).Execute(connector)).Single().Name == "TWO", "Nested scalar function failed");
            });
            Test("imported named parameters avoid generated placeholder collisions", () =>
            {
                var source = new NamedParameterCollection(); source.AddParameter("$P1", 100);
                var parameters = new ImportedParameters(); parameters.Import(source);
                Expect(parameters.AddParameter(101) == "$P2", "Imported parameter was overwritten");
                var query = new RenderedQuery("SELECT $P1 + $P2") { Parameters = parameters };
                Expect(Result(connector.ExecuteScalar<int>(query)) == 201, "Imported parameters failed to bind");
            });
            Test("all supported aggregate aliases execute in the native engine", () =>
            {
                var column = Table.GetCached<Person>().Columns.TryGet("Id");
                foreach (var function in new[]{SqlFunc.COUNT_BIG,SqlFunc.STDEV,SqlFunc.STDEVP,SqlFunc.VAR,SqlFunc.VARP,SqlFunc.MIN,SqlFunc.MAX})
                {
                    var query = new ConfigurableSelect<Person>();
                    query.Add(new SelectFuncStatement(function,"Value",column));
                    var value = Result(connector.ExecuteReaderJson<NumericSummary>(query.Render(DbFlavor.DuckDb))).Single().Value;
                    Expect(value > 0,"Aggregate failed: "+function);
                }
            });
            Test("nested relationships preserve JSON scalar types", () =>
            {
                var rows = Result(new SelectQuery<Envelope>().SelectAuto().OrderBy(x=>x.Id,SqlDir.ASC).Execute(connector));
                Expect(rows[0].Person.Token == person.Token && rows[0].Person.Payload.SequenceEqual(person.Payload) && rows[0].Person.Children.Count == 3,"Nested type or relationship changed");
                Expect(rows[1].Person == null,"Missing nested object changed");
            });
            Test("parameterized scalar types and binary equality", () =>
            {
                Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x=>x.Token,person.Token,SqlOp.EQ).Where(x=>x.Kind,Kind.Second,SqlOp.EQ).Where(x=>x.Enabled,true,SqlOp.EQ).Where(x=>x.Born,person.Born,SqlOp.EQ).Where(x=>x.Amount,person.Amount,SqlOp.EQ).Execute(connector)).Single().Id == 100, "Typed predicate binding failed");
                Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x=>x.Payload,person.Payload,SqlOp.EQ).Execute(connector)).Single().Id == 100, "Binary predicate failed");
            });
            Test("LIKE escaping preserves literal wildcard and backslash characters", () =>
            {
                string pattern = person.Name.EscapeForSqlLike(DbFlavor.DuckDb);
                Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x=>x.Name,pattern,SqlOp.LIKE).Execute(connector)).Single().Id == 100,"Escaped LIKE failed");
                Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x=>x.Name,pattern,SqlOp.NOTLIKE).Execute(connector)).Count == 3,"Escaped NOT LIKE failed");
            });
            Test("byte arrays remain IN lists for byte-valued columns", () =>
            {
                Sql("CREATE TABLE byte_values (Id UTINYINT); INSERT INTO byte_values VALUES (1),(2),(3)");
                var rows = Result(new SelectQuery<ByteValue>().SelectRootColumns().Where(x=>x.Id,new byte[]{1,2},SqlArrayOp.IN).Execute(connector));
                Expect(rows.Select(x=>x.Id).Order().SequenceEqual(new byte[]{1,2}),"Numeric byte IN list changed");
            });
            Test("date, timestamp with offset, string enum and nullable JSON values", () =>
            {
                Sql("CREATE TABLE typed_values (Id INTEGER, Flag BOOLEAN, Kind VARCHAR, Token UUID, Day DATE, Stamp TIMESTAMPTZ, Value DECIMAL(18,4), Payload BLOB)");
                var stamp = new DateTimeOffset(2024,2,29,12,34,56,TimeSpan.FromHours(-5));
                var command = new RenderedQuery("INSERT INTO typed_values VALUES (1, false, 'Second', '"+person.Token+"', DATE '2024-02-29', TIMESTAMPTZ '2024-02-29 12:34:56-05', -123.4567, from_hex('00FF')), (2,NULL,NULL,NULL,NULL,NULL,NULL,NULL)");
                Result(connector.ExecuteNonQuery(command));
                Expect(Result(new InsertQuery<TypedValues>().InsertAuto(new TypedValues { Id = 3, Stamp = stamp, Day = person.Born.Date, Payload = [42] }).Execute(connector)) == 1, "Nullable/offset parameters failed");
                var rows = Result(new SelectQuery<TypedValues>().SelectRootColumns().OrderBy(x=>x.Id,SqlDir.ASC).Execute(connector));
                Expect(rows[0].Stamp.Value.ToUniversalTime() == stamp.ToUniversalTime() && rows[0].Day == person.Born.Date && rows[0].Flag == false && rows[0].Kind == Kind.Second && rows[0].Token == person.Token && rows[0].Value == -123.4567m && rows[0].Payload.SequenceEqual(new byte[]{0,255}), "Additional JSON types changed");
                Expect(rows[2].Stamp.Value.ToUniversalTime() == stamp.ToUniversalTime() && rows[2].Day == person.Born.Date && rows[2].Payload.Single() == 42, "Offset parameter round trip failed");
                Expect(rows[1].Flag == null && rows[1].Kind == null && rows[1].Token == null && rows[1].Day == null && rows[1].Stamp == null && rows[1].Value == null && rows[1].Payload == null,"Nullable JSON changed");
            });
            Test("update returned rows, arithmetic, columns and affected counts", () =>
            {
                using var table = Result(new UpdateQuery<Person>().Update(x => x.Name, "changed").Where(x => x.Id,101,SqlOp.EQ).Output(x => x.Name).ExecuteWithOutput(connector));
                Expect((string)table.Rows[0][0] == "changed", "Incorrect returned update");
                var keys = new UpdateWithPrimaryKeyQuery<Person,int>().Update(x=>x.Note,"key").Where(x=>x.Id,101,SqlOp.EQ).Execute(connector);
                Expect(Result(keys) == 1 && keys.TouchedPrimaryKeys.Single() == 101,"Updated key wrong");
                Expect(Result(new UpdateQuery<Person>().Update(x => x.Note, x => x.Name).Where(x => x.Id,101,SqlOp.EQ).Execute(connector)) == 1, "Column update count wrong");
                Expect(Result(new UpdateQuery<Person>().Update(x => x.Amount, new SqlLinqer.Components.Update.BasicMathOperation(SqlLinqer.Components.Update.BasicMathOperation.MathOp.Add, 1m)).Where(x => x.Id,100,SqlOp.EQ).Execute(connector)) == 1, "Arithmetic update failed");
                var subquery = new SelectQuery<Person>().Select(x=>x.Id).Where(x=>x.Name,"changed",SqlOp.EQ);
                Expect(Result(new UpdateQuery<Person>().Update(x=>x.Note,"changed").Where(x=>x.Id,subquery,SqlArrayOp.IN).Execute(connector)) == 1,"Subquery write failed");
                using var empty = Result(new UpdateQuery<Person>().Update(x=>x.Note,"absent").Where(x=>x.Id,-1,SqlOp.EQ).Output(x=>x.Id).ExecuteWithOutput(connector));
                Expect(empty.Rows.Count == 0,"Empty returned rows failed");
                Expect(Result(new UpdateQuery<Person>().Update(x => x.Note, "none").Where(x => x.Id,-1,SqlOp.EQ).Execute(connector)) == 0, "Missing update count wrong");
            });
            Test("caller owns transaction, reads/writes/count/output and rollback", () =>
            {
                using var connection = connector.CreateConnection(); connection.Open();
                using var transaction = connection.BeginTransaction(IsolationLevel.Snapshot);
                var added = new InsertWithPrimaryKeyQuery<Person,int>().InsertAuto(new Person { Name = "rollback" }).Execute(connector, transaction);
                Expect(Result(added) == 1, "Insert in transaction failed");
                int id = added.TouchedPrimaryKeys.Single();
                var page = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id,id,SqlOp.EQ).Top(1).ExecuteWithTotalCount(connector,transaction);
                Expect(Result(page).Count == 1 && page.TotalResults == 1, "Read/count ignored caller transaction");
                Expect(Result(new UpdateQuery<Person>().Update(x=>x.Note,"tx").Where(x=>x.Id,id,SqlOp.EQ).Execute(connector,transaction)) == 1, "Update ignored transaction");
                Expect(Result(new DeleteWithPrimaryKeyQuery<Person,int>().Where(x=>x.Id,101,SqlOp.EQ).Execute(connector,transaction)) == 1, "Delete ignored transaction");
                Expect(Result(new UpdateQuery<Person>().Update(x=>x.Note,"rollback").Where(x=>x.Id,100,SqlOp.EQ).Execute(connector,transaction)) == 1,"Existing row update failed");
                Expect(connection.State == ConnectionState.Open && transaction.Connection == connection, "Connector completed caller transaction");
                transaction.Rollback();
                Expect(Scalar($"SELECT count(*) FROM people WHERE Id={id}") == 0 && Scalar("SELECT count(*) FROM people") == 4 && Scalar("SELECT count(*) FROM people WHERE Id=101") == 1 && Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL") == 1, "Rollback failed");
            });
            Test("owned transaction rolls back earlier statements on provider failure", () =>
            {
                var failure = connector.ExecuteNonQuery(new RenderedQuery("UPDATE people SET Note='must rollback' WHERE Id=100; INSERT INTO nonexistent_table VALUES (1)"));
                Expect(failure.State == ResponseState.Error && Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL") == 1, "Owned failure was not rolled back");
            });
            Test("joined source assignments, outputs, keys and LEFT JOIN nulls roll back", () =>
            {
                using var connection = connector.CreateConnection(); connection.Open();
                using var tx = connection.BeginTransaction(IsolationLevel.Snapshot);
                var query = new UpdateQuery<Person>().Update(x=>x.Note,x=>x.Category.Name).Update(x=>x.CategoryId,x=>x.Category.Id).Output(x=>x.Id).Output(x=>x.Note).Output(x=>x.CategoryId);
                var first = query.Render(DbFlavor.DuckDb);
                Expect(query.Render(DbFlavor.SqlServer).Text.Contains("UPDATE"), "Cross-dialect update failed");
                Expect(query.Render(DbFlavor.DuckDb).Text == first.Text, "Joined update render mutated query");
                using var rows = Result(query.ExecuteWithOutput(connector,tx));
                Expect(rows.Rows.Count == 4 && rows.Rows.Cast<DataRow>().Single(x=>(int)x["Id"]==100)["Note"].Equals("group") && rows.Rows.Cast<DataRow>().Where(x=>(int)x["Id"]!=100).All(x=>x.IsNull("Note") && x.IsNull("CategoryId")), "Joined source or LEFT JOIN changed values");
                var keys = new UpdateWithPrimaryKeyQuery<Person,long>().Update(x=>x.Note,"joined").Where(x=>x.Category.Name,"group",SqlOp.EQ).Execute(connector,tx);
                Expect(Result(keys)==1 && keys.TouchedPrimaryKeys.Single()==100L, "Joined update keys failed");
                Expect(Result(new UpdateQuery<Person>().Update(x=>x.Note,"absent").Where(x=>x.Category.Name,"missing",SqlOp.EQ).Execute(connector,tx))==0, "Unmatched update changed data");
                var deleted = new DeleteWithPrimaryKeyQuery<Person,long>().Where(x=>x.Category.Name,(string)null,SqlOp.EQ).Execute(connector,tx);
                Expect(Result(deleted)==3 && deleted.TouchedPrimaryKeys.Order().SequenceEqual(new long[]{101,102,103}), "LEFT JOIN delete keys failed");
                tx.Rollback();
                Expect(Scalar("SELECT count(*) FROM people")==4 && Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL")==1, "Joined writes escaped transaction");
            });
            Test("joined writes combine related-row CTE filters and preserve no-match output", () =>
            {
                using var connection = connector.CreateConnection(); connection.Open();
                using var tx = connection.BeginTransaction(IsolationLevel.Snapshot);
                var update = new UpdateWithPrimaryKeyQuery<Person,int>().Update(x=>x.Note,x=>x.Category.Name).Where(x=>x.Children.First().Name,"a",SqlOp.EQ);
                Expect(update.Render(DbFlavor.DuckDb).Text.TrimStart().StartsWith("WITH"), "Expected related filter CTE");
                var changed = update.Execute(connector,tx);
                Expect(Result(changed)==1 && changed.TouchedPrimaryKeys.Single()==100,"Related CTE update failed");
                using var deleted = Result(new DeleteQuery<Person>().Where(x=>x.Category.Name,"group",SqlOp.EQ).Where(x=>x.Children.First().Name,"a",SqlOp.EQ).Output(x=>x.Id).ExecuteWithOutput(connector,tx));
                Expect(deleted.Rows.Count==1 && (int)deleted.Rows[0][0]==100,"Related CTE joined delete failed");
                using var absent = Result(new DeleteQuery<Person>().Where(x=>x.Category.Name,"absent",SqlOp.EQ).Output(x=>x.Id).ExecuteWithOutput(connector,tx));
                Expect(absent.Rows.Count==0,"Unmatched delete returned rows");
                tx.Rollback();
            });
            Test("duplicate source matches reject ambiguous updates and delete each target once", () =>
            {
                using var connection = connector.CreateConnection(); connection.Open();
                using(var tx = connection.BeginTransaction(IsolationLevel.Snapshot))
                {
                    Result(connector.ExecuteNonQuery(new RenderedQuery("INSERT INTO categories VALUES (1,'other')"),tx));
                    var failure = new UpdateWithPrimaryKeyQuery<Person,int>().Update(x=>x.Note,x=>x.Category.Name).Where(x=>x.Id,100,SqlOp.EQ).Execute(connector,tx);
                    Expect(failure.State==ResponseState.Error && failure.Error.ToString().Contains("at most one source match"),"Ambiguous update did not report precise error");
                    tx.Rollback();
                }
                Expect(Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL")==1,"Ambiguous update escaped rollback");
                using(var tx = connection.BeginTransaction(IsolationLevel.Snapshot))
                {
                    Result(connector.ExecuteNonQuery(new RenderedQuery("INSERT INTO categories VALUES (1,'other')"),tx));
                    var deleted = new DeleteWithPrimaryKeyQuery<Person,int>().Where(x=>x.Category.Name,new[]{"group","other"},SqlArrayOp.IN).Execute(connector,tx);
                    Expect(Result(deleted)==1 && deleted.TouchedPrimaryKeys.Single()==100,"Duplicate delete did not delete target once");
                    tx.Rollback();
                }
            });
            Test("owned joined writes persist", () =>
            {
                Sql("CREATE TABLE write_people (Id INTEGER PRIMARY KEY, CategoryId INTEGER, Note VARCHAR); INSERT INTO write_people VALUES(1,1,NULL),(2,99,NULL)");
                Expect(Result(new UpdateQuery<WritePerson>().Update(x=>x.Note,x=>x.Category.Name).Execute(connector))==2,"Owned joined update failed");
                Expect(Scalar("SELECT count(*) FROM write_people WHERE Note='group'")==1,"Owned joined update did not persist");
                Expect(Result(new DeleteQuery<WritePerson>().Where(x=>x.Category.Name,"group",SqlOp.EQ).Execute(connector))==1,"Owned joined delete failed");
            });
            Test("pure aggregate root FROM, nested numeric conversions and empty aggregates", () =>
            {
                var pure = new SelectQueryInitializer<Person>().BuildQuery(x=>new { Count=x.Count(), Big=x.CountBig() });
                var total=Result(pure.ExecuteWithTotalCount(connector));
                Expect(total.Single().Count==4 && total.Single().Big==4,"Pure aggregate lost root FROM");
                var numeric = new SelectQueryInitializer<Person>().BuildQuery(x=>new { Average=(double?)(double)x.Avg(p=>p.Id), Wide=(long)x.Sum(p=>p.Id) });
                var values=Result(numeric.Execute(connector)).Single();
                Expect(values.Average==101.5 && values.Wide==406,"Nullable numeric conversion failed");
                var empty = new SelectQueryInitializer<Person>().BuildQuery(x=>new { Average=(double?)(double)x.Avg(p=>p.Id), Sum=(long?)x.Sum(p=>p.Id), Count=x.Count() }).Where(x=>x.Id,-1,SqlOp.EQ);
                var none=Result(empty.Execute(connector)).Single();
                Expect(none.Average==null && none.Sum==null && none.Count==0,"Empty aggregate NULL changed");
            });
            Test("grouped and distinct totals count results and preserve parameters", () =>
            {
                var grouped=new SelectQueryInitializer<Person>().BuildQuery(x=>new { Category=x.Select(p=>p.CategoryId), Total=x.Count() });
                var page=grouped.ExecuteWithTotalCount(connector);
                Expect(Result(page).Count==2 && page.TotalResults==2,"Grouped total counted underlying rows");
                var distinct=new SelectQuery<Person>().Select(x=>x.CategoryId).Distinct();
                var result=distinct.ExecuteWithTotalCount(connector);
                Expect(Result(result).Count==2 && result.TotalResults==2,"Distinct total counted underlying rows");
                var table=Table.GetCached<Person>();
                var functions=new ConfigurableSelect<Person>();
                functions.Add(new SelectFuncStatement(SqlFunc.RIGHT,"Tail",table.Columns.TryGet("Name"),2));
                functions.Add(new SelectFuncStatement(SqlFunc.COUNT_BIG,"Total",table.Columns.TryGet("Id")));
                var rendered=new RenderedQuery(); rendered.Text=functions.RenderForCount(rendered.Parameters,DbFlavor.DuckDb);
                Expect(Result(connector.ExecuteScalar<int>(rendered))==4,"Grouped function count lost parameters");
                var nested=new SelectQuery<Person>().SelectAuto().Where(x=>x.Children.First().Name,"a",SqlOp.EQ).ExecuteWithTotalCount(connector);
                Expect(Result(nested).Count==1 && nested.TotalResults==1,"Nested total failed");
            });
            Test("aggregate and distinct paging preserve grouping", () =>
            {
                var pure=new SelectQueryInitializer<Person>().BuildQuery(x=>new { Count=x.Count() }).Page(1,1).ExecuteWithTotalCount(connector);
                Expect(Result(pure).Single().Count==4 && pure.TotalResults==1,"Paging introduced aggregate grouping");
                var grouped=new SelectQueryInitializer<Person>().BuildQuery(x=>new { Category=x.Select(p=>p.CategoryId), Total=x.Count() }).Page(1,1).ExecuteWithTotalCount(connector);
                var row=Result(grouped).Single();
                Expect(grouped.TotalResults==2 && row.Total==(row.Category==1?1:3),"Paging changed result groups");
                var distinct=new SelectQuery<Person>().Select(x=>x.CategoryId).Distinct().Page(1,1).ExecuteWithTotalCount(connector);
                Expect(Result(distinct).Count==1 && distinct.TotalResults==2,"Paging introduced hidden distinct column");
            });
            Test("CASE values and nested functions group by the projected expression", () =>
            {
                var query=new SelectQueryInitializer<Person>().BuildQuery(x=>new { Label=x.Case<int>(c=>c.AddCaseColumn(p=>p.CategoryId,w=>w.Where(p=>p.Id,102,SqlOp.LT)).AddElseValue(0)), Total=x.Count() });
                var groups=Result(query.ExecuteWithTotalCount(connector));
                Expect(groups.Count==2 && groups.Single(x=>x.Label==1).Total==1 && groups.Single(x=>x.Label==0).Total==3,"CASE did not group projected values");
                var ordered=query.OrderBy(x=>x.Label,SqlDir.DESC).Page(1,1).ExecuteWithTotalCount(connector);
                Expect(Result(ordered).Single().Label==1 && ordered.TotalResults==2,"CASE ordering rebound group parameters");
                var table=Table.GetCached<Person>();
                var text=new CaseStatement<Person>(table,"inner");
                text.AddCaseValue("small",w=>w.Where(p=>p.Id,102,SqlOp.LT)).AddElseValue("large");
                var nested=new ConfigurableSelect<Person>();
                nested.Add(new SelectFuncStatement(SqlFunc.UPPER,"Tail",text));
                nested.Add(new SelectFuncStatement(SqlFunc.COUNT,"Total",table.Columns.TryGet("Id")));
                var rows=Result(connector.ExecuteReaderJson<GroupSummary>(nested.Render(DbFlavor.DuckDb)));
                Expect(rows.Count==2 && rows.All(x=>x.Total==2) && rows.Select(x=>x.Tail).Order().SequenceEqual(new[]{"LARGE","SMALL"}),"Function wrapping CASE failed");
                var conditional=new CaseStatement<Person>(table,"Tail");
                conditional.AddCaseValue(new SelectFuncStatement(SqlFunc.UPPER,"inner",table.Columns.TryGet("Name")),w=>w.Where(p=>p.Id,101,SqlOp.EQ)).AddElseValue("other");
                var outer=new ConfigurableSelect<Person>(); outer.Add(conditional);
                outer.Add(new SelectFuncStatement(SqlFunc.COUNT,"Total",table.Columns.TryGet("Id")));
                var nestedRows=Result(connector.ExecuteReaderJson<GroupSummary>(outer.Render(DbFlavor.DuckDb)));
                Expect(nestedRows.Count==2 && nestedRows.Single(x=>x.Tail=="CHANGED").Total==1 && nestedRows.Single(x=>x.Tail=="other").Total==3,"CASE wrapping function failed");
            });
            Test("LEFT projection dispatch and nested functions retain aliases", () =>
            {
                var query=new SelectQueryInitializer<Person>().BuildQuery(x=>new { Prefix=x.LEFT(p=>p.Name,2), Upper=x.UPPER(p=>x.LEFT(q=>q.Name,2)) }).Where(x=>x.Id,101,SqlOp.EQ);
                var row=Result(query.Execute(connector)).Single();
                Expect(row.Prefix=="ch" && row.Upper=="CH","LEFT dispatch or nested scalar failed");
            });
            Test("default-only batch inserts and integer long GUID key conversions", () =>
            {
                Sql("CREATE SEQUENCE default_ids START 2147483648; CREATE TABLE default_keys (Id BIGINT PRIMARY KEY DEFAULT nextval('default_ids')); CREATE TABLE guid_keys (Id UUID PRIMARY KEY DEFAULT uuid())");
                var batch = new InsertWithPrimaryKeyQuery<DefaultKey,long>().InsertAuto(new[]{new DefaultKey(),new DefaultKey()}).Execute(connector);
                Expect(Result(batch)==2 && batch.TouchedPrimaryKeys.Order().SequenceEqual(new long[]{2147483648,2147483649}),"Default batch or BIGINT keys failed");
                var widened=new UpdateWithPrimaryKeyQuery<Person,long>().Update(x=>x.Note,"key conversion").Where(x=>x.Id,-1,SqlOp.EQ).Execute(connector);
                Expect(Result(widened)==0 && widened.TouchedPrimaryKeys.Count==0,"Empty key conversion failed");
                var guid=new InsertWithPrimaryKeyQuery<GuidKey,Guid>().InsertAuto(new GuidKey()).Execute(connector);
                Expect(Result(guid)==1 && guid.TouchedPrimaryKeys.Single()!=Guid.Empty,"Default UUID key failed");
                var key=guid.TouchedPrimaryKeys.Single();
                var deleted=new DeleteWithPrimaryKeyQuery<GuidKey,Guid>().Where(x=>x.Id,key,SqlOp.EQ).Execute(connector);
                Expect(Result(deleted)==1 && deleted.TouchedPrimaryKeys.Single()==key,"UUID delete key failed");
                Expect(Result(connector.ExecuteScalar<byte[]>(new RenderedQuery("SELECT from_hex('00FF')"))).SequenceEqual(new byte[]{0,255}),"Scalar BLOB was not copied before reader disposal");
                Expect(Result(connector.ExecuteScalar<int?>(new RenderedQuery("SELECT NULL::INTEGER")))==null && Result(connector.ExecuteScalar<long?>(new RenderedQuery("SELECT 42::INTEGER")))==42,"Nullable scalar failed");
            });
            Test("custom predicate Finalize overrides retain behavior across dialects", () =>
            {
                var predicate=new CustomPredicate(Table.GetCached<Person>().Columns.TryGet("Id"));
                var query=new SelectQuery<Person>().SelectRootColumns().Where(predicate);
                Expect(Result(query.Execute(connector)).Count==0,"DuckDB bypassed custom Finalize override");
                Expect(query.Render(DbFlavor.SqlServer).Text.Contains("1 = 0"),"Existing dialect bypassed custom Finalize override");
            });
            Test("native word predicates preserve boundaries wildcards escaping NULL and dialect renders", () =>
            {
                Sql("CREATE TABLE words(Id INTEGER, Name VARCHAR); INSERT INTO words VALUES(1,'cat'),(2,'a cat!'),(3,'cats'),(4,'cat_dog'),(5,NULL),(6,'a.c'),(7,'a%c'),(8,'CAT'),(9,'cat-cat')");
                var query=new SelectQuery<Word>().SelectRootColumns().Where(x=>x.Name,"cat",SqlOp.LIKEWORD);
                string first=query.Render(DbFlavor.DuckDb).Text;
                Expect(query.Render(DbFlavor.SqlServer).Text.Contains("LIKE"),"Other dialect render failed");
                Expect(query.Render(DbFlavor.DuckDb).Text==first,"Word render mutated query");
                Expect(Result(query.Execute(connector)).Select(x=>x.Id).Order().SequenceEqual(new[]{1,2,9}),"ASCII boundaries or case changed");
                Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x=>x.Name,"cat",SqlOp.NOTLIKEWORD).Execute(connector)).Select(x=>x.Id).Order().SequenceEqual(new[]{3,4,6,7,8}),"Negative word NULL semantics changed");
                Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x=>x.Name,"cat",SqlOp.NOTLIKEWORDORNULL).Execute(connector)).Count==6,"Word OR NULL failed");
                Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x=>x.Name,"a%c".EscapeForSqlLike(DbFlavor.DuckDb),SqlOp.LIKEWORD).Execute(connector)).Single().Id==7,"Word literal wildcard failed");
                Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x=>x.Name,"a.c",SqlOp.LIKEWORD).Execute(connector)).Single().Id==6,"Regex punctuation was not literal");
                Expect(Result(new SelectQuery<Word>().SelectRootColumns().Where(x=>x.Name,"a_c",SqlOp.LIKEWORD).Execute(connector)).Count==2,"Word LIKE wildcard failed");
                var group=new SelectQuery<Word>().SelectRootColumns(); group.Where(group.NewWhere(SqlWhereOp.OR).Where(x=>x.Name,"cat",SqlOp.LIKEWORD).Where(x=>x.Name,"a.c",SqlOp.LIKEWORD));
                Expect(Result(group.Execute(connector)).Count==4,"Grouped word filter failed");
                Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x=>x.Children.First().Name,"a",SqlOp.LIKEWORD).Execute(connector)).Single().Id==100,"Related CTE word filter failed");
                Expect(Result(new SelectQuery<Person>().SelectRootColumns().Where(x=>x.Children.First().Name,"a",SqlOp.NOTLIKEWORD).Execute(connector)).Count==3,"Related negative word filter failed");
            });
            Test("wide scalar aggregates convert native HUGEINT without losing checked overflow", () =>
            {
                Expect(Result(connector.ExecuteScalar<long>(new RenderedQuery("SELECT sum(Id) FROM people")))==406L,"Native HUGEINT sum conversion failed");
                Expect(Result(connector.ExecuteScalar<decimal>(new RenderedQuery("SELECT 1234567890123456789012345678::HUGEINT")))==1234567890123456789012345678m,"Wide decimal scalar changed");
                var overflow=connector.ExecuteScalar<int>(new RenderedQuery("SELECT 2147483648::BIGINT"));
                Expect(overflow.State==ResponseState.Error && overflow.Error.InnerException is OverflowException,"Key/scalar narrowing silently overflowed");
            });
            Test("JSON and tabular readers honor command deadlines", () =>
            {
                var timed=new ReplicatorConnector(example,DbFlavor.DuckDb,new ConnectorOptions{CommandTimeout=TimeSpan.FromMilliseconds(10)});
                var json=timed.ExecuteReaderJson<NumericSummary>(new RenderedQuery("SELECT json_object('Value', sum(sin(i::DOUBLE))) FROM range(9223372036854775807) t(i)"));
                Expect(json.State==ResponseState.Error && json.Error.InnerException is TimeoutException,"JSON execution did not cancel");
                var table=timed.ExecuteReaderDataTable(new RenderedQuery("SELECT sum(sin(i::DOUBLE)) FROM range(9223372036854775807) t(i)"));
                Expect(table.State==ResponseState.Error && table.Error.InnerException is TimeoutException,"Tabular execution did not cancel");
            });
            Test("command deadline interrupts native work and permits caller rollback and connection reuse", () =>
            {
                using var connection=connector.CreateConnection(); connection.Open();
                using(var tx=connection.BeginTransaction(IsolationLevel.Snapshot))
                {
                    Result(connector.ExecuteNonQuery(new RenderedQuery("UPDATE people SET Note='deadline rollback' WHERE Id=100"),tx));
                    var timed=new ReplicatorConnector(connection,DbFlavor.DuckDb,new ConnectorOptions{CommandTimeout=TimeSpan.FromMilliseconds(10)});
                    var failure=timed.ExecuteScalar<double>(new RenderedQuery("SELECT sum(sin(i::DOUBLE)) FROM range(9223372036854775807) t(i)"),tx);
                    Expect(failure.State==ResponseState.Error && failure.Error.InnerException is TimeoutException,"Native query was not interrupted as timeout");
                    Expect(connection.State==ConnectionState.Open && tx.Connection==connection,"Deadline disposed caller transaction");
                    var aborted=connector.ExecuteScalar<int>(new RenderedQuery("SELECT count(*) FROM people"),tx);
                    Expect(aborted.State==ResponseState.Error && aborted.Error.ToString().Contains("aborted",StringComparison.OrdinalIgnoreCase),"Interrupted native transaction did not report aborted state");
                    tx.Rollback();
                }
                using(var tx=connection.BeginTransaction(IsolationLevel.Snapshot))
                {
                    for(int i=0;i<20;i++)
                        Expect(Result(connector.ExecuteScalar<int>(new RenderedQuery("SELECT 42"),tx))==42,"Late deadline interrupted reused connection");
                    tx.Commit();
                }
                Expect(Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL")==1,"Deadline rollback failed");
                var owned=new ReplicatorConnector(example,DbFlavor.DuckDb,new ConnectorOptions{CommandTimeout=TimeSpan.FromMilliseconds(10)});
                var ownedError=owned.ExecuteNonQuery(new RenderedQuery("UPDATE people SET Note='deadline rollback' WHERE Id=100; SELECT sum(sin(i::DOUBLE)) FROM range(9223372036854775807) t(i)"));
                Expect(ownedError.State==ResponseState.Error && ownedError.Error.InnerException is TimeoutException && Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL")==1,"Owned deadline failed to roll back");
                var unlimited=new ReplicatorConnector(example,DbFlavor.DuckDb,new ConnectorOptions{CommandTimeout=TimeSpan.Zero});
                Expect(Result(unlimited.ExecuteScalar<int>(new RenderedQuery("SELECT 42")))==42,"Zero deadline failed");
            });
            Test("delete keys/rows and affected counts", () =>
            {
                var deleted = new DeleteWithPrimaryKeyQuery<Person,int>().Where(x=>x.Id,103,SqlOp.EQ).Execute(connector);
                Expect(Result(deleted) == 1 && deleted.TouchedPrimaryKeys.Single() == 103, "Deleted key wrong");
                Expect(Result(new DeleteQuery<Person>().Where(x=>x.Id,102,SqlOp.EQ).Execute(connector)) == 1, "Delete count wrong");
                Expect(Result(new DeleteQuery<Person>().Where(x=>x.Id,-1,SqlOp.EQ).Execute(connector)) == 0, "Empty delete wrong");
            });
            Test("stored procedures, TVPs and unsupported functions reject before execution", () =>
            {
                Reject(() => new StoredProcedureQuery<Person>("example").Render(DbFlavor.DuckDb));
                Reject(() => new InsertQuery<Person>().InsertAuto(person).ExecuteTvp(connector));
                Reject(() => new ParameterCollection(DbFlavor.DuckDb).AddTvpParameter(new DataTable(),"unused"));
                Reject(() => new SelectQueryInitializer<Person>().BuildQuery(x=>new { Value=x.SOUNDDEX(p=>p.Name) }).Render(DbFlavor.DuckDb));
            });
            Test("replicated file connections persist with no injected timeout", () =>
            {
                using var connection = connector.CreateConnection();
                Expect(connection.ConnectionString == example.ConnectionString && !connection.ConnectionString.Contains("Timeout"), "Connection string changed");
                connection.Open(); using var command = connection.CreateCommand(); command.CommandText="SELECT count(*) FROM people";
                Expect(Convert.ToInt64(command.ExecuteScalar()) == 2 && File.Exists(path), "File persistence failed");
            });
            Test("TRUNCATE native statement preserves sequence", () =>
            {
                Expect(new TruncateQuery<Person>().Render(DbFlavor.DuckDb).Text.StartsWith("TRUNCATE TABLE"), "Wrong truncate SQL");
                Expect(Result(new TruncateQuery<Person>().Execute(connector)) == 2 && Scalar("SELECT count(*) FROM people") == 0, "TRUNCATE failed");
                var added = new InsertWithPrimaryKeyQuery<Person,int>().InsertAuto(new Person()).Execute(connector);
                Expect(Result(added) == 1 && added.TouchedPrimaryKeys.Single() > 103, "TRUNCATE reset sequence unexpectedly");
            });
            Test("plain memory databases are independent; caller transaction keeps memory alive", () =>
            {
                using var memory = new DuckDBConnection("Data Source=:memory:"); memory.Open();
                using (var command = memory.CreateCommand()) { command.CommandText="CREATE TABLE people (Id INTEGER, Name VARCHAR); INSERT INTO people VALUES (1,'memory');"; command.ExecuteNonQuery(); }
                var memConnector = new ReplicatorConnector(memory,DbFlavor.DuckDb);
                using var transaction = memory.BeginTransaction(IsolationLevel.Snapshot);
                Expect(Result(new SelectQuery<MemoryPerson>().SelectRootColumns().Execute(memConnector,transaction)).Single().Name == "memory", "Memory transaction lost database");
                using var separate = memConnector.CreateConnection(); separate.Open();
                using var probe = separate.CreateCommand(); probe.CommandText="SELECT count(*) FROM information_schema.tables WHERE table_name='people'";
                Expect(Convert.ToInt64(probe.ExecuteScalar()) == 0, "Expected isolated memory connection");
                transaction.Rollback();
            });
            SqlLinqer.Tests.Regression.CompatibleDatabaseTests.Run(connector, Test);
            // Optional diagnostic only: elapsed time depends on the host and is not a regression assertion.
            if (Environment.GetEnvironmentVariable("SQLLINQER_DUCKDB_TIMEOUT_PROBE") == "1")
            {
                using var connection = connector.CreateConnection(); connection.Open();
                using var command = connection.CreateCommand(); command.CommandTimeout=1;
                command.CommandText="SELECT sum(sin(i::DOUBLE)) FROM range(150000000) t(i)";
                var timer=Stopwatch.StartNew(); command.ExecuteScalar(); timer.Stop();
                Console.WriteLine($"CommandTimeout={command.CommandTimeout}; elapsed={timer.Elapsed.TotalSeconds:F3}s");
            }
        }
        finally { File.Delete(path); File.Delete(path+".wal"); }
        SqlLinqer.Tests.Regression.AsyncProviderChecks.Run(DbFlavor.DuckDb, Test);
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;

        static void ConfigureNested(SqlLinqer.Queries.Core.SelectQuery query, int limit, int offset)
        {
            // The existing public typed API exposes no nested paging controls. Configure the core
            // collections here to exercise its nested renderer without expanding the library API.
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var type=typeof(SqlLinqer.Queries.Core.SelectQuery);
            ((PagingControls)type.GetField("PagingControls",flags).GetValue(query)).SetLimit(limit,offset);
            var table=(Table)typeof(SqlLinqer.Queries.Core.Abstract.TableQuery).GetField("Table",flags).GetValue(query);
            var orders=(SqlLinqer.Components.Ordering.OrderByCollection)type.GetField("OrderBys",flags).GetValue(query);
            orders.AddStatement(new SqlLinqer.Components.Ordering.ColumnOrderBy(table.Columns.TryGet("Name"),SqlDir.ASC));
        }
    }
}

public enum Kind { First, Second }
[SqlTable("people"),SqlAutoInsert]
public class Person
{
    [SqlPrimaryKey,SqlAutoInsertExclude] public int Id {get;set;}
    public string Name {get;set;}
    public bool Enabled {get;set;}
    public Kind Kind {get;set;}
    public Guid Token {get;set;}
    public DateTime Born {get;set;}
    public decimal Amount {get;set;}
    public byte[] Payload {get;set;}
    public string Note {get;set;}
    public int CategoryId {get;set;}
    [SqlOneToOne(nameof(CategoryId))] public Category Category {get;set;}
    [SqlOneToMany(nameof(Child.PersonId))] public List<Child> Children {get;set;}
}
[SqlTable("categories")]
public class Category { [SqlPrimaryKey] public int Id {get;set;} public string Name {get;set;} }
[SqlTable("children")]
public class Child { [SqlPrimaryKey] public int Id {get;set;} public int PersonId {get;set;} public string Name {get;set;} }
[SqlTable("people")]
public class MemoryPerson { public int Id {get;set;} public string Name {get;set;} }
public class ConfigurableSelect<T> : SelectQuery<T> where T:class
{
    public void Add(SelectStatement statement) => Selects.AddStatement(statement);
    public SqlLinqer.Queries.Core.SelectQuery Relationship(string alias) => Selects.GetAll<SelectRelationship>().Single(x=>x.Alias==alias).SubQuery;
}

public class GroupSummary { public string Tail {get;set;} public long Total {get;set;} }
public class ImportedParameters : ParameterCollection
{
    public ImportedParameters() : base(DbFlavor.DuckDb) { }
    public void Import(ParameterCollectionBase source) => ImportParametersFrom(source);
}
[SqlTable("typed_values"), SqlAutoInsert]
public class TypedValues
{
    public int Id {get;set;}
    public bool? Flag {get;set;}
    public Kind? Kind {get;set;}
    public Guid? Token {get;set;}
    public DateTime? Day {get;set;}
    public DateTimeOffset? Stamp {get;set;}
    public decimal? Value {get;set;}
    public byte[] Payload {get;set;}
}

public class NumericSummary { public double Value {get;set;} }
[SqlTable("envelopes")]
public class Envelope
{
    [SqlPrimaryKey] public int Id {get;set;}
    public int PersonId {get;set;}
    [SqlOneToOne(nameof(PersonId))] public Person Person {get;set;}
}

[SqlTable("byte_values")]
public class ByteValue { public byte Id {get;set;} }

[SqlTable("write_people")]
public class WritePerson
{
    [SqlPrimaryKey] public int Id {get;set;}
    public int CategoryId {get;set;}
    public string Note {get;set;}
    [SqlOneToOne(nameof(CategoryId))] public Category Category {get;set;}
}
[SqlTable("default_keys"),SqlAutoInsert]
public class DefaultKey { [SqlPrimaryKey,SqlAutoInsertExclude] public long Id {get;set;} }
[SqlTable("guid_keys"),SqlAutoInsert]
public class GuidKey { [SqlPrimaryKey,SqlAutoInsertExclude] public Guid Id {get;set;} }
[SqlTable("words")]
public class Word { public int Id {get;set;} public string Name {get;set;} }

public class CustomPredicate : SqlLinqer.Components.Where.WhereColumnValue
{
    public CustomPredicate(Column column):base(column,100,SqlOp.EQ,SqlSubQueryOp.ANY) { }
    public override SqlLinqer.Components.Where.IWhereComponent Finalize(SqlLinqer.Queries.Core.Abstract.WhereQuery query)
        => new SqlLinqer.Components.Where.WhereColumnValue(Column, Array.Empty<int>(), SqlOp.EQ, SqlSubQueryOp.ANY);
}
