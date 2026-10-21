using System.Collections.Generic;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Joins
{
    /// <summary>Indexes explicit joins by child table alias.</summary>
    public class DirectJoinCollection : KeyedComponentCollectionBase<string, Join>
    {
        /// <summary>Adds an explicit join if its child table alias is not already present.</summary>
        public void AddJoin(Join join)
        {
            AddComponent(join.Child.Alias, join);
        }
        /// <summary>Adds the supplied explicit joins.</summary>
        public void AddJoins(IEnumerable<Join> joins)
        {
            foreach (var join in joins)
                AddComponent(join.Child.Alias, join);
        }
    }
}
