using System.Linq;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Ordering
{
    /// <summary>An ordered list of sorting expressions.</summary>
    public class OrderByCollection : ComponentCollectionBase<IOrderByStatement>
    {
        /// <summary>Appends a sorting expression.</summary>
        public void AddStatement(IOrderByStatement statement)
            => base.AddComponent(statement);
        /// <summary>Removes all sorting expressions.</summary>
        public new void Clear()
            => base.Clear();
        internal System.Collections.Generic.IEnumerable<IOrderByStatement> Statements => GetAll();
        /// <summary>Renders this component for the selected database dialect.</summary>
        public string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            if (Count == 0)
                return null;

            var strs = GetAll().Select(x => x.Render(query, parameters, flavor));
            return $"ORDER BY {string.Join(",", strs)}";
        }
    }
}
