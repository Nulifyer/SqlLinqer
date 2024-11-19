using System.Collections.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    public abstract class SelectStatement
    {
        /// <summary>
        /// The alias of the select statement
        /// </summary>
        public abstract string Alias { get; }

        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="query">The query current query</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <param name="for_json">If the inteded output is to be in JSON format</param>
        /// <param name="include_alias">If to include the set alias in the render</param>
        public abstract string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool for_json, bool include_alias);
        /// <summary>
        /// Returns true if query should group by this statement
        /// </summary>
        public abstract bool DoGroupBy();
        /// <summary>
        /// Renders the object into an array of strings for the group by section.
        /// </summary>
        /// <param name="query">The query current query</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        public abstract IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor);
    }
}