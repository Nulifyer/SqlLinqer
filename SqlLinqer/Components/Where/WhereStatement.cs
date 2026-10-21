using System.Collections.Generic;
using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Components.Where
{
    /// <summary>A predicate that can be prepared, inverted, and rendered.</summary>
    public abstract class WhereStatement : IWhereComponent
    {
        /// <summary>The preparation order used to sort predicates.</summary>
        public abstract int Order { get; }
        
        /// <summary>Returns the logical inverse of this predicate.</summary>
        public abstract IWhereComponent Invert();
        /// <summary>Prepares the predicate and any relationship subqueries for the owning query.</summary>
        public abstract IWhereComponent Finalize(WhereQuery query);
        /// <summary>Renders this component for the selected database dialect.</summary>
        public abstract void Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder);
        /// <summary>Attempts to merge this predicate into the current group. Returns true if merged.</summary>
        public abstract bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component);

        /// <summary>Enumerates the mapped columns referenced by this predicate.</summary>
        public abstract IEnumerable<Column> GetColumnReferences();
    }
}