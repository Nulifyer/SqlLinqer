using SqlLinqer.Components.Withs;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Core.Abstract
{
    /// <summary>
    /// The base structure of a core query with joins.
    /// </summary>
    public abstract class JoinQuery : TableQuery
    {
        /// <summary>
        /// A collection of the query's joins.
        /// </summary>
        protected internal readonly JoinCollection Joins;

        /// <summary>
        /// A collection of the query's withs.
        /// </summary>
        protected internal WithCollection Withs { get; protected set; }

        /// <summary>
        /// The base structure of a core query with joins.
        /// </summary>
        public JoinQuery(Table table) : base(table)
        {
            Joins = new JoinCollection();
            Withs = new WithCollection();
        }

        protected internal void DisableWiths()
        {
            Withs = null;
        }
    }
}