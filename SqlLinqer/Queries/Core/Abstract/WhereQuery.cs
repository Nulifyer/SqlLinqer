using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;

namespace SqlLinqer.Queries.Core.Abstract
{
    /// <summary>
    /// The base structure of a core query with where components.
    /// </summary>
    public abstract class WhereQuery : JoinQuery
    {
        /// <summary>
        /// The root of the where structure.
        /// </summary>
        protected internal WhereCollection RootWhere;

        /// <summary>
        /// The base structure of a core query with where components.
        /// </summary>
        public WhereQuery(Table table) : base(table)
        {
            RootWhere = new WhereCollection(SqlWhereOp.AND);
        }
    }
}