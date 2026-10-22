using SqlLinqer.Modeling;
namespace SqlLinqer.Tests.Regression;

[SqlProcedure("probe")]
internal sealed class ProcedureProbe : SqlLinqerCallable<ProcedureProbe.Input, ProcedureProbe.Row>
{
    public class Input { }
    public class Row { public int Id { get; set; } public string Name { get; set; } }
}
