using System;
using System.Data;

namespace SqlLinqer.Modeling
{
    /// <summary>Defines stored procedure parameter metadata on an argument member.</summary>
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
        /// <summary>The database type. When unset, it is inferred from the declared CLR type.</summary>
        public DbType DbType { get => ExplicitDbType ?? DbType.Object; set => ExplicitDbType = value; }
        /// <summary>The SQL Server type. When set, it takes precedence over DbType.</summary>
        public SqlDbType SqlDbType { get => ExplicitSqlDbType ?? SqlDbType.Variant; set => ExplicitSqlDbType = value; }
        /// <summary>The parameter size. Use -1 for MAX types. Variable-length outputs require a size.</summary>
        public int Size { get; set; }
        /// <summary>The numeric precision.</summary>
        public byte Precision { get => ExplicitPrecision ?? 0; set => ExplicitPrecision = value; }
        /// <summary>The numeric or temporal scale.</summary>
        public byte Scale { get => ExplicitScale ?? 0; set => ExplicitScale = value; }
        /// <summary>The existing SQL Server table type for a DataTable argument.</summary>
        public string TypeName { get; set; }

        /// <summary>Uses the member name as the parameter name.</summary>
        public SqlParameterAttribute() { }
        /// <summary>Defines the parameter name.</summary>
        public SqlParameterAttribute(string name) { Name = name; }
        /// <summary>Defines the parameter name and database type.</summary>
        public SqlParameterAttribute(string name, DbType dbType) : this(name) { DbType = dbType; }
    }

    /// <summary>Maps a result member to an output parameter. A matching argument supplies its initial value.</summary>
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
