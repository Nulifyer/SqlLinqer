using System;
using System.Data;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a callable argument member to a named SQL parameter and optional provider type metadata. Ordinary null inputs are omitted.
    /// SqlArgument distinguishes omission from explicit SQL NULL and retains its declared CLR type. SqlDbType overrides DbType; enums use their underlying numeric type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class SqlParameterAttribute : Attribute
    {
        internal DbType? ExplicitDbType;
        internal SqlDbType? ExplicitSqlDbType;
        internal byte? ExplicitPrecision;
        internal byte? ExplicitScale;

        /// <summary>The SQL parameter name, with or without @. Defaults to the member name.</summary>
        public string Name { get; }
        /// <summary>The parameter order. SQL Server calls bind by name.</summary>
        public int Order { get; set; }
        /// <summary>
        /// An explicit database parameter type. When unset, binding uses the declared CLR member type, including for explicit SQL NULL.
        /// </summary>
        public DbType DbType { get => ExplicitDbType ?? DbType.Object; set => ExplicitDbType = value; }
        /// <summary>
        /// An explicit SQL Server provider type that takes precedence over DbType. The getter returns Variant while no override is configured.
        /// </summary>
        public SqlDbType SqlDbType { get => ExplicitSqlDbType ?? SqlDbType.Variant; set => ExplicitSqlDbType = value; }
        /// <summary>
        /// The provider parameter size; -1 selects MAX types.
        /// </summary>
        public int Size { get; set; }
        /// <summary>The numeric precision.</summary>
        public byte Precision { get => ExplicitPrecision ?? 0; set => ExplicitPrecision = value; }
        /// <summary>The numeric or temporal scale.</summary>
        public byte Scale { get => ExplicitScale ?? 0; set => ExplicitScale = value; }
        /// <summary>
        /// The existing SQL Server table type used for a mapped row, row collection, or DataTable input. The call does not create or drop this type.
        /// Mapped objects use InsertAuto column selection and order. An empty collection supplies zero rows. Null omits the input.
        /// An unqualified type name uses the procedure schema or the connector default schema.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>Uses the member name as the parameter name.</summary>
        public SqlParameterAttribute() { }
        /// <summary>Defines the parameter name.</summary>
        public SqlParameterAttribute(string name) { Name = name; }
        /// <summary>Defines the parameter name and database type.</summary>
        public SqlParameterAttribute(string name, DbType dbType) : this(name) { DbType = dbType; }
    }

}
