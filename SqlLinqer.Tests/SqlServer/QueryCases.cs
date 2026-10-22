using SqlLinqer;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Withs;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.SqlServer;

internal sealed class ParentCteSelect : SelectQuery<AzureParent>
{
    internal ParentCteSelect() : this(new With(new SelectQuery<AzureParent>().SelectRootColumns(), "ParentCte")) { }
    private ParentCteSelect(With cte) : base(cte.OutputTable)
    {
        SelectRootColumns();
        Withs.AddWith(cte);
    }
}

internal static class QueryCases
{
    internal static IEnumerable<(string Name, BaseQuery Query)> All()
    {
        yield return ("root", new SelectQuery<AzureParent>().SelectRootColumns());
        yield return ("nested relationships", new SelectQuery<AzureParent>().SelectRootColumns().Select(x => x.Profile)
            .Select(x => x.Children).Select(new[] { nameof(AzureParent.Children), nameof(AzureChild.Leaves) }).OrderByAuto());
        yield return ("distinct page", new SelectQuery<AzureParent>().Select(x => x.Enabled).Distinct().OrderBy(x => x.Enabled, SqlDir.ASC).Page(2, 1));
        yield return ("grouped", AzureParent.BeginSelectBuilder().BuildQuery(x => new { Enabled = x.Select(y => y.Enabled), Count = x.Count(), Max = x.Max(y => y.Id) })
            .OrderBy(x => x.Enabled, SqlDir.ASC).Page(2, 1));
        yield return ("aggregate", AzureParent.BeginSelectBuilder().BuildQuery(x => new { Count = x.Count() }).Top(1));
        yield return ("CTE", new ParentCteSelect());
        yield return ("CTE distinct page", new ParentCteSelect().Distinct().OrderBy(x => x.Id, SqlDir.ASC).Page(2, 1));
        yield return ("distinct relationship CTE", new SelectQuery<AzureParent>().Select(x => x.Id).Distinct()
            .Where(x => x.Children.First().Name, "child", SqlOp.EQ).OrderBy(x => x.Id, SqlDir.ASC).Page(1, 1));
        yield return ("joined source update", new UpdateQuery<AzureParent>().Update(x => x.Name, x => x.Profile.Name)
            .Where(x => x.Profile.Name, "profile", SqlOp.EQ).Output(x => x.Id).Output(x => x.Name));
        yield return ("joined delete", new DeleteQuery<AzureParent>().Where(x => x.Profile.Name, "profile", SqlOp.EQ).Output(x => x.Id).Output(x => x.Name));
        yield return ("identity insert", new InsertWithPrimaryKeyQuery<AzureParent, int>().InsertAuto(new AzureParent { Name = "雪", Enabled = true }));
        yield return ("type insert", new InsertQuery<AzureTypes>().InsertAuto(LiveSuite.SampleTypes()));
        yield return ("truncate", new TruncateQuery<AzureChild>());
    }
}
