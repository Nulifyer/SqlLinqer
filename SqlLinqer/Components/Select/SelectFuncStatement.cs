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
    /// <summary>A SQL function expression and its output alias.</summary>
    public class SelectFuncStatement : SelectStatement
    {
        /// <summary>The SQL function to render.</summary>
        public readonly SqlFunc Func;
        /// <summary>The column, expression, and value arguments supplied to the function.</summary>
        public readonly object[] Args;
        /// <summary>An argument modifier such as DISTINCT.</summary>
        public string ArgPrefix { get; protected set; }
        /// <summary>
        /// An optional predicate that limits which rows an aggregate reads. PostgreSQL, SQLite, and DuckDB render it as
        /// FILTER (WHERE ...); other databases wrap the argument in CASE WHEN.
        /// </summary>
        public SqlLinqer.Components.Where.WhereCollection Filter { get; protected internal set; }

        private string _alias;
        /// <inheritdoc/>
        public override string Alias => _alias;


        /// <summary>Creates a SQL function expression from its arguments.</summary>
        public SelectFuncStatement(SqlFunc func, string alias, params object[] args)
        {
            if (alias == null)
                throw new System.ArgumentNullException(nameof(alias));

            Func = func;
            _alias = alias;

            Args = args ?? new object[0];
        }

        /// <inheritdoc/>
        public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool include_alias)
        {
            // A filtered aggregate keeps one set of predicate bindings when it is rendered again.
            string val = parameters.FindExpressionOverride(this)
                ?? (Filter != null ? parameters.FindExpression(this) : null)
                ?? RenderValue(query, parameters, flavor);

            if (flavor == DbFlavor.DuckDb || Filter != null)
                parameters.RememberExpression(this, val);

            return $"{val}{(include_alias ? $" AS {Alias.DbWrap(flavor)}" : null)}";
        }

        private string RenderValue(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
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
                        string rendered_sub_stm = sub_statement.Render(query, parameters, flavor, false);
                        args_processed.Add($"({rendered_sub_stm})");
                        break;
                    default:
                        string placeholder = parameters.AddExpressionParameter(this, argumentIndex, arg);
                        args_processed.Add(placeholder);
                        break;
                }
            }

            string filter = Filter?.Render(query, parameters, flavor, include_where: false);
            if (string.IsNullOrEmpty(filter)) filter = null;
            bool filterClause = flavor == DbFlavor.PostgreSql || flavor == DbFlavor.Sqlite || flavor == DbFlavor.DuckDb;
            if (filter != null)
            {
                if (Func.DoGroupBy())
                    throw new NotSupportedException($"A filter applies only to aggregate functions, not {Func}.");
                // CASE without ELSE yields NULL, which aggregates skip. Only the aggregated value is conditional;
                // further arguments, such as a separator, stay as they are.
                if (!filterClause)
                    args_processed[0] = $"CASE WHEN {filter} THEN {args_processed[0]} END";
            }

            string val = RenderFunction(args_processed, flavor);
            if (filter == null) return val;
            if (filterClause) val = $"{val} FILTER (WHERE {filter})";
            // A filtered sum often has no matching rows in a group. Zero keeps non-nullable result members usable.
            return Func == SqlFunc.SUM ? $"COALESCE({val}, 0)" : val;
        }

        private string RenderFunction(List<object> args_processed, DbFlavor flavor)
        {
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
                        case SqlFunc.COUNT_BIG:
                            val = $"COUNT({ArgPrefix}{string.Join(",", args_processed)})";
                            break;
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
                case DbFlavor.MySql when Func == SqlFunc.COUNT_BIG:
                    val = $"COUNT({ArgPrefix}{string.Join(",", args_processed)})";
                    break;
                case DbFlavor.SqlServer when Func == SqlFunc.STRING_AGG && args_processed.Count > 0:
                case DbFlavor.AzureSql when Func == SqlFunc.STRING_AGG && args_processed.Count > 0:
                    // SQL Server rejects a Unicode separator beside a non-Unicode value, and limits a non-MAX result to 8,000 bytes.
                    args_processed[0] = $"CAST({args_processed[0]} AS NVARCHAR(MAX))";
                    val = $"{Func.GetDescription()}({ArgPrefix}{string.Join(",", args_processed)})";
                    break;
                case DbFlavor.MySql when Func == SqlFunc.STRING_AGG:
                    // MySQL has no STRING_AGG. GROUP_CONCAT takes its separator after a keyword.
                    val = args_processed.Count > 1
                        ? $"GROUP_CONCAT({ArgPrefix}{args_processed[0]} SEPARATOR {args_processed[1]})"
                        : $"GROUP_CONCAT({ArgPrefix}{string.Join(",", args_processed)})";
                    break;
                default:
                    val = $"{Func.GetDescription()}({ArgPrefix}{string.Join(",", args_processed)})";
                    break;
            }

            return val;
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
        public override bool DoGroupBy() => Func.DoGroupBy() && Args.OfType<SelectStatement>().All(x => x.DoGroupBy());

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

            if (!DoGroupBy())
                return Args.SelectMany(arg => arg is Column column ? new[] { column.Render(flavor) }
                    : arg is SelectStatement statement ? statement.RenderForGroupBy(query, parameters, flavor) ?? Array.Empty<string>()
                    : arg is IEnumerable<Column> columns ? columns.Select(c => c.Render(flavor)) : Array.Empty<string>()).ToArray();

            if (Func.GroupByOutput())
                return new[] { flavor == DbFlavor.DuckDb
                    ? parameters.FindExpression(this) ?? Render(query, parameters, flavor, false)
                    : Render(query, parameters, flavor, false) };

            var cols = GetTargetColumns();
            return cols.Select(c => c.Render(flavor)).ToArray();
        }
    }
}
