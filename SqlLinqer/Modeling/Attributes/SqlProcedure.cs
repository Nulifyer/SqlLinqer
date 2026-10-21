using System;

namespace SqlLinqer.Modeling
{
    /// <summary>Maps a callable definition to a SQL Server or Azure SQL stored procedure.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SqlProcedure : Attribute
    {
        /// <summary>The procedure name, without a schema prefix.</summary>
        public string Name { get; }
        /// <summary>The schema. If omitted, the connector's default schema is used.</summary>
        public string Schema { get; set; }

        /// <summary>Defines the procedure name.</summary>
        public SqlProcedure(string name) { Name = name; }
    }
}
