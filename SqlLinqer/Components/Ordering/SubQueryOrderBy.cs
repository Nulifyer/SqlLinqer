using System.Linq;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Ordering
{
    public class SubQueryOrderBy : IOrderByStatement
    {
        public readonly Relationship Relationship;
        public readonly Column Column;
        public readonly SqlDir Dir;

        public SubQueryOrderBy(Relationship relationship, Column column, SqlDir dir)
        {
            Relationship = relationship;
            Column = column;
            Dir = dir;
        }
        
        public string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            // decide if min or max
            SqlFunc func;
            switch (Dir)
            {
                case SqlDir.DESC:
                    func = SqlFunc.MAX;
                    break;
                case SqlDir.ASC:                    
                default:
                    func = SqlFunc.MIN;
                    break;
            }
            // build sub query
            var sub_query = new SelectQuery(Relationship.ChildTable, query.Withs);
            sub_query.AddTableAsRoot();
            // setup joins
            var joins = Relationship.GetReversedJoins()
                .Where(x => x.Child.IsSameOrAlsoUnderSameParent(sub_query.Table));
            sub_query.Joins.AddJoinsDirect(joins);
            sub_query.RootWhere.AddComponent(Relationship.ParentTableJoin.Condition);
            // select aggregate column
            sub_query.Selects.AddStatement(new SelectFuncStatement(func, StringExtensions.GetRandomHashString(), Column));
            // render and build order by
            string rendered = sub_query.Render(parameters, flavor, false);
            return $"({rendered}) {Dir.GetDescription()}";
        }
    }
}