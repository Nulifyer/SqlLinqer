using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a callable definition to an existing SQL Server or Azure SQL procedure. Supply its object name and schema separately.
    /// An omitted schema uses the execution connector default; if both are omitted, SQL Server resolves the unqualified name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SqlProcedure : Attribute
    {
        /// <summary>The procedure name, without a schema prefix.</summary>
        public string Name { get; }
        /// <summary>
        /// The explicit procedure schema, or null to use the connector default. If both are null, the rendered procedure name remains unqualified.
        /// </summary>
        public string Schema { get; set; }

        /// <summary>
        /// Defines the existing procedure object name without a schema prefix.
        /// </summary>
        /// <param name="name">One procedure identifier. Set Schema separately rather than embedding a qualified SQL name.</param>
        public SqlProcedure(string name) { Name = name; }
    }
}
