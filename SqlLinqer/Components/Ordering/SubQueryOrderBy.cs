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
    /// <summary>Orders root rows by a related collection column through a scalar subquery.</summary>
    public class SubQueryOrderBy : IOrderByStatement
    {
        /// <summary>The relationship traversed by the sorting subquery.</summary>
        public readonly Relationship Relationship;
        /// <summary>The related column used for sorting.</summary>
        public readonly Column Column;
        /// <summary>The sorting direction.</summary>
        public readonly SqlDir Dir;

        /// <summary>Creates a sorting subquery for a related column.</summary>
        public SubQueryOrderBy(Relationship relationship, Column column, SqlDir dir)
        {
            Relationship = relationship;
            Column = column;
            Dir = dir;
        }

        /// <summary>Renders this component for the selected database dialect.</summary>
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
            sub_query.IsCompilationCopy = query.IsCompilationCopy;
            sub_query.AddTableAsRoot();
            // setup joins
            var joins = Relationship.GetReversedJoins()
                .Where(x => x.Child.IsSameOrAlsoUnderSameParent(sub_query.Table));
            sub_query.Joins.AddJoinsDirect(joins);
            sub_query.RootWhere.AddComponent(Relationship.ParentTableJoin.Condition);
            // select aggregate column
            sub_query.Selects.AddStatement(new SelectFuncStatement(func, "__sqlinqer_order_value", Column));
            // render and build order by
            string rendered = sub_query.RenderRows(parameters, flavor);
            return $"({rendered}) {Dir.GetDescription()}";
        }
    }
}
