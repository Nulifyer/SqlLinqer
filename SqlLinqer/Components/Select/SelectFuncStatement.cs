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
            for (int argumentIndex = 0; argumentIndex < Args.Length; argumentIndex++)
            {
                var arg = Args[argumentIndex];
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
                        string rendered_sub_stm = sub_statement.Render(query, parameters, flavor, (flavor == DbFlavor.Sqlite || flavor == DbFlavor.DuckDb) ? false : forJson, false);
                        args_processed.Add($"({rendered_sub_stm})");
                        break;
                    default:
                        string placeholder = flavor == DbFlavor.DuckDb
                            ? parameters.AddExpressionParameter(this, argumentIndex, arg)
                            : parameters.AddParameter(arg);
                        args_processed.Add(placeholder);
                        break;
                }
            }

            string val;
            switch (flavor)
            {
                case DbFlavor.Sqlite:
                    val = RenderSqliteFunction(args_processed);
                    break;
                case DbFlavor.DuckDb:
                    string function;
                    switch (Func)
                    {
                        case SqlFunc.COUNT_BIG: function = "COUNT"; break;
                        case SqlFunc.STDEV: function = "STDDEV_SAMP"; break;
                        case SqlFunc.STDEVP: function = "STDDEV_POP"; break;
                        case SqlFunc.VAR: function = "VAR_SAMP"; break;
                        case SqlFunc.VARP: function = "VAR_POP"; break;
                        case SqlFunc.CHECKSUM_AGG:
                        case SqlFunc.SOUNDDEX:
                            throw new NotSupportedException($"DuckDB does not support {Func}.");
                        default: function = Func.GetDescription(); break;
                    }
                    val = $"{function}({ArgPrefix}{string.Join(",", args_processed)})";
                    break;
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

            if (flavor == DbFlavor.DuckDb)
                parameters.RememberExpression(this, val);

            if (forJson)
            {
                switch (flavor)
                {
                    case DbFlavor.Sqlite:
                    case DbFlavor.DuckDb:
                    case DbFlavor.PostgreSql:
                    case DbFlavor.MySql:
                        return $"{Alias.DbWrapString(flavor)}, {val}";
                    case DbFlavor.SqlServer:
                    case DbFlavor.AzureSql:
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

        private string RenderSqliteFunction(List<object> args)
        {
            string joined = string.Join(",", args);
            switch (Func)
            {
                case SqlFunc.DAY:
                case SqlFunc.MONTH:
                case SqlFunc.YEAR:
                    string format = Func == SqlFunc.DAY ? "%d" : Func == SqlFunc.MONTH ? "%m" : "%Y";
                    return $"CAST(strftime('{format}', {joined}) AS INTEGER)";
                case SqlFunc.RIGHT:
                    return $"CASE WHEN {args[1]} = 0 THEN '' ELSE substr({args[0]}, -({args[1]})) END";
                case SqlFunc.SUBSTRING:
                    return $"substr({joined})";
                case SqlFunc.COUNT_BIG:
                    return $"count({ArgPrefix}{joined})";
                case SqlFunc.STRING_AGG:
                    return $"group_concat({ArgPrefix}{joined})";
                case SqlFunc.CONCAT:
                    return args.Count == 0 ? "''" : "(" + string.Join(" || ", args.Select(x => $"coalesce({x}, '')")) + ")";
                case SqlFunc.CONCAT_WS:
                    // Skip NULL arguments and separators, including on SQLite 3.38.
                    if (args.Count == 0)
                        throw new NotSupportedException("SQLite CONCAT_WS requires a separator.");
                    string result = "''";
                    string hasValue = "0";
                    foreach (var arg in args.Skip(1))
                    {
                        result = $"({result} || CASE WHEN {arg} IS NULL THEN '' ELSE (CASE WHEN {hasValue} THEN {args[0]} ELSE '' END || {arg}) END)";
                        hasValue = $"({hasValue} OR {arg} IS NOT NULL)";
                    }
                    return $"CASE WHEN {args[0]} IS NULL THEN NULL ELSE {result} END";
                case SqlFunc.REVERSE:
                case SqlFunc.SOUNDDEX:
                case SqlFunc.CHECKSUM_AGG:
                case SqlFunc.STDEV:
                case SqlFunc.STDEVP:
                case SqlFunc.VAR:
                case SqlFunc.VARP:
                    throw new NotSupportedException($"SQLite does not provide {Func} without extensions.");
                default:
                    return $"{Func.GetDescription()}({ArgPrefix}{joined})";
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
                return new[] { flavor == DbFlavor.DuckDb
                    ? parameters.FindExpression(this) ?? Render(query, parameters, flavor, false, false)
                    : Render(query, parameters, flavor, false, false) };

            var cols = GetTargetColumns();
            return cols.Select(c => c.Render(flavor)).ToArray();
        }
    }
}