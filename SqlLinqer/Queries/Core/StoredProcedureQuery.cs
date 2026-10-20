using System;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Insert;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;
using System.Collections.Generic;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Queries.Core
{
    public class StoredProcedureQuery : BaseQuery
    {
        protected internal string Schema;
        protected internal string Procedure;
        protected internal NamedParameterCollection Parameters;

        public StoredProcedureQuery(string procedure, string schema = null) : base()
        {
            Schema = schema;
            Procedure = procedure;
            Parameters = new NamedParameterCollection();
        }

        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            flavor = SqlDialect.Resolve(flavor);
            if (flavor == DbFlavor.Sqlite)
                throw new NotSupportedException("SQLite does not support stored procedures.");
            if (flavor == DbFlavor.DuckDb)
                throw new NotSupportedException("DuckDB does not support stored procedure queries. Execute macros or table functions with explicit SQL instead.");

            var valueKeys = new List<string>();
            foreach (var parameter in Parameters.GetAll())
            {
                if (parameter is TvpParameter && flavor != DbFlavor.SqlServer)
                    throw new System.NotSupportedException("Stored procedure table-valued parameters require SQL Server or Azure SQL.");
                string placeholder = parameter is TvpParameter tvp
                    ? parameters.AddTvpParameter((System.Data.DataTable)tvp.Value, tvp.TypeName)
                    : parameters.AddParameter(parameter.Value, parameter.DbType);
                switch (flavor)
                {
                    case DbFlavor.SqlServer:
                        valueKeys.Add($"@{parameter.Placehodler} = {placeholder}");
                        break;
                    case DbFlavor.MySql:
                    case DbFlavor.PostgreSql:
                        valueKeys.Add(placeholder);
                        break;
                    default:
                        throw new System.NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported");
                }
            }

            switch (flavor)
            {
                case DbFlavor.SqlServer:
                    return $"EXEC {(Schema != null ? $"{Schema.DbWrap(flavor)}." : null)}{Procedure.DbWrap(flavor)} {string.Join(",", valueKeys)}";
                case DbFlavor.MySql:
                case DbFlavor.PostgreSql:
                    return $"CALL {(Schema != null ? $"{Schema.DbWrap(flavor)}." : null)}{Procedure.DbWrap(flavor)} {string.Join(",", valueKeys)}";
                default:
                        throw new System.NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported");
            }
        }
    }
}
