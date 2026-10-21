using System.Linq;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Generic;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Components.Withs
{
    /// <summary>Indexes common table expressions and renders them in dependency order.</summary>
    public class WithCollection : KeyedComponentCollectionBase<string, With>
    {
        /// <summary>Creates an empty common table expression collection.</summary>
        public WithCollection() : base()
        {

        }

        /// <summary>Adds a common table expression and its dependencies if absent.</summary>
        public void AddWith(With with)
        {
            AddComponentOverwrite(with.Alias, with);
        }

        internal string NextFilterAlias()
        {
            int index = Count;
            string alias;
            do { alias = "__sqlinqer_filter" + index++; }
            while (HasKey(alias));
            return alias;
        }

        /// <summary>Renders this component for the selected database dialect.</summary>
        public string Render(JoinQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            if (Count == 0)
                return null;

            var strs = GetAll()
                .OrderBy(x => x.Order)
                .Select(x => x.Render(query, parameters, flavor));
            return $"WITH {string.Join(",", strs)}";
        }
    }
}
