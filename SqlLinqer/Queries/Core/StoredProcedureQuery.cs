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
            var valueKeys = new List<string>();
            foreach (var parameter in Parameters.GetAll())
            {
                string placeholder = parameters.AddParameter(parameter.Value);
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