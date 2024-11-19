using System.Collections.Generic;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Joins
{
    public class DirectJoinCollection : KeyedComponentCollectionBase<string, Join>
    {
        public void AddJoin(Join join)
        {
            AddComponent(join.Child.Alias, join);
        }
        public void AddJoins(IEnumerable<Join> joins)
        {
            foreach (var join in joins)
                AddComponent(join.Child.Alias, join);
        }
    }
}