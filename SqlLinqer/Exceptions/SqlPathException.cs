using System;
using System.Collections.Generic;
using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Exceptions
{
    /// <summary>
    /// An exception that occurs while executing a query
    /// </summary>
    public sealed class SqlPathNotFoundException : Exception
    {
        /// <summary>
        /// The table the path was applied to
        /// </summary>
        public readonly Table Table;

        /// <summary>
        /// The path that was searched
        /// </summary>
        public readonly string[] Path;

        /// <summary>
        /// An exception that occurs while executing a query
        /// </summary>
        /// <param name="table">The table the path was applied to.</param>
        /// <param name="path">The path that was searched.</param>
        public SqlPathNotFoundException(Table table, IEnumerable<string> path) 
            : base($"The path '{(path != null ? string.Join(".", path) : null)}' cannot be found on table '{table.Name}'{(table.SourceType != null ? $" from type '{table.SourceType.Name}'" : (table.ParentRelationship != null ? $" with parent table '{table.ParentRelationship.ParentTable.Name}'" : null))}.")
        {
            Table = table;
            Path = Path?.ToArray();
        }
    }
}
