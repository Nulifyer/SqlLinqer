using System.Collections.Generic;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Components.Where
{
    /// <summary>
    /// A component of a where collection
    /// </summary>
    public interface IWhereComponent
    {
        /// <summary>Attempts to merge this predicate into the current group. Returns true if merged.</summary>
        bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component);
        /// <summary>Enumerates the mapped columns referenced by this predicate.</summary>
        IEnumerable<Column> GetColumnReferences();
    }
}