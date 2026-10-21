using System;
using System.Data;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a callable argument member to a named SQL parameter and optional provider type metadata. Null inputs retain their declared CLR type.
    /// SqlArgument distinguishes omission from SQL NULL. SqlDbType overrides DbType; enums use their underlying numeric type. A same-named SqlOutputParameter creates an input/output binding.
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
        /// An explicit database parameter type. When unset, binding uses the declared CLR member type, including for null inputs.
        /// </summary>
        public DbType DbType { get => ExplicitDbType ?? DbType.Object; set => ExplicitDbType = value; }
        /// <summary>
        /// An explicit SQL Server provider type that takes precedence over DbType. The getter returns Variant while no override is configured.
        /// </summary>
        public SqlDbType SqlDbType { get => ExplicitSqlDbType ?? SqlDbType.Variant; set => ExplicitSqlDbType = value; }
        /// <summary>
        /// The provider parameter size. String and binary output parameters require an explicit size; -1 selects MAX types.
        /// </summary>
        public int Size { get; set; }
        /// <summary>The numeric precision.</summary>
        public byte Precision { get => ExplicitPrecision ?? 0; set => ExplicitPrecision = value; }
        /// <summary>The numeric or temporal scale.</summary>
        public byte Scale { get => ExplicitScale ?? 0; set => ExplicitScale = value; }
        /// <summary>
        /// The existing SQL Server table type used for a DataTable input. The call does not create or drop this type.
        /// Requires a non-null DataTable with matching column types and order; an empty table supplies zero rows.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>Uses the member name as the parameter name.</summary>
        public SqlParameterAttribute() { }
        /// <summary>Defines the parameter name.</summary>
        public SqlParameterAttribute(string name) { Name = name; }
        /// <summary>Defines the parameter name and database type.</summary>
        public SqlParameterAttribute(string name, DbType dbType) : this(name) { DbType = dbType; }
    }

    /// <summary>
    /// Maps a writable result member to an output parameter. A same-named argument supplies an input/output parameter's initial value.
    /// Input/output argument and result metadata must agree, and the argument cannot be omitted. Values load into the result after the reader closes. Set Size for variable-length string or binary outputs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SqlOutputParameter : SqlParameterAttribute
    {
        /// <summary>Uses the member name as the parameter name.</summary>
        public SqlOutputParameter() { }
        /// <summary>Defines the output parameter name.</summary>
        public SqlOutputParameter(string name) : base(name) { }
        /// <summary>Defines the output parameter name and database type.</summary>
        public SqlOutputParameter(string name, DbType dbType) : base(name, dbType) { }
    }
}
