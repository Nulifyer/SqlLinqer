using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a model class to an existing database table and optional schema. It does not create or migrate the database.
    /// An explicit schema overrides the connector default.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SqlTable : Attribute
    {
        /// <summary>
        /// The database table name
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The explicit database schema, or null to use the schema fallback selected when the query model is built.
        /// </summary>
        public readonly string Schema;

        /// <summary>
        /// Defines the database table name and optional explicit schema for a model.
        /// </summary>
        /// <param name="name">The existing database table name.</param>
        /// <param name="schema">The explicit schema, or null to use the model construction's default schema.</param>
        public SqlTable(string name, string schema = null)
        {
            Name = name;
            Schema = schema;
        }
    }
}
