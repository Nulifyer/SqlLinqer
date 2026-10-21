using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using static SqlLinqer.Tests.DuckDb.CheckAssertions;

namespace SqlLinqer.Tests.DuckDb;

internal static class MappingChecks
{
    internal static void Run(BaseConnector connector, Person person, Action<string, Action> Test)
    {
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
    }

    internal static void WideScalars(BaseConnector connector, Action<string, Action> Test)
    {
        Test("wide scalar aggregates convert native HUGEINT without losing checked overflow", () =>
        {
            Expect(Result(connector.ExecuteScalar<long>(new RenderedQuery("SELECT sum(Id) FROM people")))==406L,"Native HUGEINT sum conversion failed");
            Expect(Result(connector.ExecuteScalar<decimal>(new RenderedQuery("SELECT 1234567890123456789012345678::HUGEINT")))==1234567890123456789012345678m,"Wide decimal scalar changed");
            var overflow=connector.ExecuteScalar<int>(new RenderedQuery("SELECT 2147483648::BIGINT"));
            Expect(overflow.State==ResponseState.Error && overflow.Error.InnerException is OverflowException,"Key/scalar narrowing silently overflowed");
        });
    }
}
