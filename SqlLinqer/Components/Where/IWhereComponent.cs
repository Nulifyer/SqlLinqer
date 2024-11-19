using System.Collections.Generic;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Components.Where
{
    /// <summary>
    /// A component of a where collection
    /// </summary>
    public interface IWhereComponent
    {
        bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component);
        IEnumerable<Column> GetColumnReferences();
    }
}