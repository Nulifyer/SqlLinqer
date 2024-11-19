using System.Linq;
using SqlLinqer.Queries.Core;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Extensions.EnumExtensions;
using System.Collections.Generic;
using System;

namespace SqlLinqer.Components.Select
{
    public class SelectFuncStatement : SelectStatement
    {
        public readonly SqlFunc Func;
        public readonly object[] Args;
        public string ArgPrefix { get; protected set; }

        private string _alias;
        public override string Alias => _alias;


        public SelectFuncStatement(SqlFunc func, string alias, params object[] args)
        {
            if (alias == null)
                throw new System.ArgumentNullException(nameof(alias));

            Func = func;
            _alias = alias;

            Args = args ?? new object[0];
        }

        /// <inheritdoc/>
        public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool forJson, bool include_alias)
        {
            var args_processed = new List<object>();
            foreach (var arg in Args)
            {
                switch (arg)
                {
                    case Column column:
                        query.Joins.AddTable(column.Table);
                        args_processed.Add(column.Render(flavor));
                        break;
                    case IEnumerable<Column> columns:
                        foreach (var col in columns)
                        {
                            query.Joins.AddTable(col.Table);
                            args_processed.Add(col.Render(flavor));
                        }
                        break;
                    case SelectStatement sub_statement:
                        string rendered_sub_stm = sub_statement.Render(query, parameters, flavor, forJson, false);
                        args_processed.Add($"({rendered_sub_stm})");
                        break;
                    default:
                        string placeholder = parameters.AddParameter(arg);
                        args_processed.Add(placeholder);
                        break;
                }
            }

            string val;
            switch (flavor)
            {
                case DbFlavor.PostgreSql:
                    switch (Func)
                    {
                        case SqlFunc.DAY:
                        case SqlFunc.MONTH:
                        case SqlFunc.YEAR:
                            val = $"EXTRACT({Func.GetDescription()} FROM {string.Join(",", args_processed)})";
                            break;
                        default:
                            val = $"{Func.GetDescription()}({ArgPrefix}{string.Join(",", args_processed)})";
                            break;
                    }
                    break;
                default:
                    val = $"{Func.GetDescription()}({ArgPrefix}{string.Join(",", args_processed)})";
                    break;
            }

            if (forJson)
            {
                switch (flavor)
                {
                    case DbFlavor.PostgreSql:
                    case DbFlavor.MySql:
                        return $"{Alias.DbWrapString(flavor)}, {val}";
                    case DbFlavor.SqlServer:
                        return $"{val}{(include_alias ? $" AS {Alias.DbWrap(flavor)}" : null)}";
                    default:
                        throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported by {nameof(SelectFuncStatement)}.{nameof(Render)}");
                }
            }
            else
            {
                return $"{val}{(include_alias ? $" AS {Alias.DbWrap(flavor)}" : null)}";
            }
        }

        /// <inheritdoc/>
        public override bool DoGroupBy() => Func.DoGroupBy() && Args.OfType<SelectFuncStatement>().All(x => x.DoGroupBy());

        private IEnumerable<Column> GetTargetColumns()
        {
            var columns = new List<Column>();
            if (!Func.GroupByOutput())
            {
                columns.AddRange(Args.OfType<Column>());
                columns.AddRange(Args.OfType<SelectFuncStatement>().SelectMany(x => x.GetTargetColumns()));
            }
            return columns;
        }

        /// <inheritdoc/>
        public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            if (!Func.DoGroupBy())
                return null;

            if (Func.GroupByOutput())
                return new[] { Render(query, parameters, flavor, false, false) };

            var cols = GetTargetColumns();
            return cols.Select(c => c.Render(flavor)).ToArray();
        }
    }
}